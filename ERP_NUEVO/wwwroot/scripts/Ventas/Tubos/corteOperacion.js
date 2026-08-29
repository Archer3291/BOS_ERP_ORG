// ============================================
// MÓDULO: Operación de Cortes
// ============================================
const CorteOperacionApp = {
    cortesActuales: [],
    gruposPedidos: [],
    _asignacionEnReasignacion: null,
    _reasignarPendiente: null,
    _fechaCorteModo: null,
    _fechaCorteAsignacionId: null,

    // Estado del lector tipo keyboard-wedge (pistola de código de barras / QR)
    _scanBuffer: '',
    _scanLastTime: 0,
    SCAN_PREFIX: 'CO-',       // prefijo que codifica el QR del ticket: CO-{asignacion_id}
    SCAN_GAP_MS: 300,          // gap máximo entre teclas para considerarlo un escaneo

    init() {
        const hoy = new Date().toISOString().slice(0, 10);
        const fDesde = document.getElementById('co-fechaDesde');
        const fHasta = document.getElementById('co-fechaHasta');
        if (fDesde) fDesde.value = hoy;
        if (fHasta) fHasta.value = hoy;

        document.getElementById('co-btn-buscar')?.addEventListener('click', () => this.buscarCortes());
        document.getElementById('co-btn-hoy')?.addEventListener('click', () => {
            const hoy2 = new Date().toISOString().slice(0, 10);
            if (fDesde) fDesde.value = hoy2;
            if (fHasta) fHasta.value = hoy2;
            this.buscarCortes();
        });

        document.getElementById('co-lista-cortes')?.addEventListener('click', (e) => {
            const btnConfirmar = e.target.closest('.co-btn-confirmar');
            if (btnConfirmar) {
                this.abrirModalFechaCorte({ modo: 'confirmar', asignacionId: btnConfirmar.dataset.asignacionId });
                return;
            }
            const btnReasignar = e.target.closest('.co-btn-reasignar');
            if (btnReasignar) { this.abrirModalReasignar(btnReasignar.dataset.asignacionId); return; }

            const btnTicketPedido = e.target.closest('.co-btn-ticket-pedido');
            if (btnTicketPedido) { this.imprimirTicketPedido(btnTicketPedido.dataset.idEncabezado); return; }

            const btnTicketCorte = e.target.closest('.co-btn-ticket-corte');
            if (btnTicketCorte) { this.imprimirTicketCorte(btnTicketCorte.dataset.asignacionId); return; }
        });

        document.getElementById('co-lista-piezas-alt')?.addEventListener('click', (e) => {
            const opcion = e.target.closest('.co-pieza-opcion');
            if (opcion) {
                this._reasignarPendiente = { idCorte: opcion.dataset.idCorte, folio: opcion.dataset.folio };
                bootstrap.Modal.getInstance(document.getElementById('co-modalReasignar'))?.hide();
                this.abrirModalFechaCorte({ modo: 'reasignar' });
            }
        });

        document.getElementById('co-btnConfirmarFechaCorte')?.addEventListener('click', () => this.confirmarConFecha());
        document.getElementById('co-btn-simular-escaneo')?.addEventListener('click', () => this.simularEscaneo());

        this.initScanner(); // ★ lector de código de barras del ticket
        this.buscarCortes(); // ★ carga automática de los cortes de hoy
    },

    // ============================================
    // Lector de QR / código de barras (keyboard-wedge)
    // La pistola "teclea" el contenido del QR (CO-{asignacion_id}) muy rápido y termina
    // con Enter. Detectamos ese patrón y disparamos la misma acción del botón "Respetar",
    // así el operador no tiene que buscar la tarjeta del corte en la lista.
    // ============================================
    initScanner() {
        document.addEventListener('keydown', (e) => {
            const ahora = Date.now();

            // Si pasó demasiado tiempo desde la última tecla, es tecleo humano: reiniciar buffer.
            if (ahora - this._scanLastTime > this.SCAN_GAP_MS) this._scanBuffer = '';
            this._scanLastTime = ahora;

            if (e.key === 'Enter') {
                const code = this._scanBuffer.trim();
                this._scanBuffer = '';
                // Solo actuamos si el contenido tiene la forma exacta del QR del ticket, de modo
                // que un Enter normal (en un input, por ejemplo) nunca se confunde con un escaneo.
                if (new RegExp(`^${this.SCAN_PREFIX}\\d+$`, 'i').test(code)) {
                    e.preventDefault();
                    this.procesarEscaneo(code);
                }
                return;
            }

            // Acumular solo caracteres imprimibles.
            if (e.key.length === 1) this._scanBuffer += e.key;
        });
    },

    async procesarEscaneo(code) {
        const asignacionId = code.replace(new RegExp(`^${this.SCAN_PREFIX}`, 'i'), '');
        let corte = this.cortesActuales.find(c => String(c.asignacion_id) === asignacionId);
        const enVista = !!corte;

        // Si el corte no está en el rango cargado, se resuelve contra el servidor: el ticket
        // puede ser de cualquier día, no solo del rango en pantalla.
        if (!corte) {
            corte = await this.buscarCorteEnServidor(asignacionId);
            if (!corte) {
                toastMixin?.fire({
                    icon: 'warning',
                    title: `El corte escaneado (${code}) no existe o no se pudo cargar.`
                });
                return;
            }
        }

        if (corte.estatus === 'confirmado') {
            if (enVista) this.resaltarCorte(asignacionId);
            toastMixin?.fire({ icon: 'info', title: 'Este corte ya estaba confirmado.' });
            return;
        }

        // Misma acción que el botón "Respetar": localizar (si está en vista), resaltar y abrir
        // el modal de fecha. Si vino del servidor, dar contexto del pedido al operador.
        if (enVista) {
            this.resaltarCorte(asignacionId);
        } else {
            toastMixin?.fire({
                icon: 'info',
                title: `Corte del pedido ${corte.folio_pedido} — confirma la fecha del ticket.`
            });
        }
        this.abrirModalFechaCorte({ modo: 'confirmar', asignacionId });
    },

    async buscarCorteEnServidor(asignacionId) {
        try {
            const resp = await fetch(`/CorteOperacion/ObtenerCortePorAsignacion?asignacionId=${encodeURIComponent(asignacionId)}`);
            const data = await resp.json();
            return (data.success && data.item) ? data.item : null;
        } catch (err) {
            console.error('Error al resolver el corte escaneado', err);
            return null;
        }
    },

    // Simula el escaneo del ticket: pide la asignación y dispara el mismo flujo que la pistola.
    async simularEscaneo() {
        let id = null;
        if (window.Swal) {
            const { value } = await Swal.fire({
                title: 'Simular escaneo de ticket',
                input: 'text',
                inputLabel: 'Asignación del corte (número impreso bajo el código de barras)',
                inputPlaceholder: 'Ej. 123',
                showCancelButton: true,
                confirmButtonText: 'Escanear',
                cancelButtonText: 'Cancelar',
                inputValidator: (v) => (!v || !/^\d+$/.test(v.trim())) ? 'Ingresa un número de asignación válido.' : null
            });
            id = value;
        } else {
            id = window.prompt('Asignación del corte a simular (número):');
        }

        if (!id) return;
        id = String(id).trim().replace(new RegExp(`^${this.SCAN_PREFIX}`, 'i'), '');
        if (!/^\d+$/.test(id)) {
            toastMixin?.fire({ icon: 'warning', title: 'Número de asignación inválido.' });
            return;
        }
        this.procesarEscaneo(`${this.SCAN_PREFIX}${id}`);
    },

    resaltarCorte(asignacionId) {
        const card = document.querySelector(`.co-btn-confirmar[data-asignacion-id="${asignacionId}"], .co-btn-ticket-corte[data-asignacion-id="${asignacionId}"]`)?.closest('.co-card-corte');
        if (!card) return;
        card.scrollIntoView({ behavior: 'smooth', block: 'center' });
        card.classList.remove('co-card-corte--flash');
        void card.offsetWidth; // reinicia la animación
        card.classList.add('co-card-corte--flash');
    },

    async buscarCortes() {
        const fechaDesde = document.getElementById('co-fechaDesde')?.value;
        const fechaHasta = document.getElementById('co-fechaHasta')?.value;
        if (!fechaDesde || !fechaHasta) return;

        this.mostrarSpinner(true);
        document.getElementById('co-vacio')?.classList.add('d-none');
        document.getElementById('co-resultados')?.classList.add('d-none');

        try {
            const params = new URLSearchParams({ fechaDesde, fechaHasta });
            const resp = await fetch(`/CorteOperacion/ObtenerCortesPendientes?${params.toString()}`);
            const data = await resp.json();

            if (!data.success) {
                toastMixin?.fire({ icon: 'error', title: data.message || 'No se pudieron cargar los cortes' });
                document.getElementById('co-vacio')?.classList.remove('d-none');
                return;
            }

            this.cortesActuales = data.items || [];
            this.agruparPorPedido();
            this.renderCortes();
        } catch (err) {
            console.error(err);
            toastMixin?.fire({ icon: 'error', title: 'Error de conexión al buscar cortes' });
        } finally {
            this.mostrarSpinner(false);
        }
    },

    agruparPorPedido() {
        const grupos = {};
        this.cortesActuales.forEach(c => {
            const key = c.id_encabezado;
            if (!grupos[key]) {
                grupos[key] = {
                    idEncabezado: c.id_encabezado,
                    folioPedido: c.folio_pedido,
                    cliente: c.cli_prov,
                    cortes: []
                };
            }
            grupos[key].cortes.push(c);
        });
        this.gruposPedidos = Object.values(grupos);
    },

    mostrarSpinner(show) {
        document.getElementById('co-spinner')?.classList.toggle('d-none', !show);
    },

    renderCortes() {
        const lista = document.getElementById('co-lista-cortes');
        const totalBadge = document.getElementById('co-total-cortes');
        const resultados = document.getElementById('co-resultados');
        const vacio = document.getElementById('co-vacio');

        if (!this.cortesActuales.length) {
            resultados.classList.add('d-none');
            vacio.classList.remove('d-none');
            vacio.querySelector('p').textContent = 'No hay cortes registrados para el rango de fechas seleccionado.';
            return;
        }

        totalBadge.textContent = `${this.cortesActuales.length} corte(s) en ${this.gruposPedidos.length} pedido(s)`;

        lista.innerHTML = this.gruposPedidos.map(grupo => `
        <div class="co-pedido-grupo">
            <div class="d-flex justify-content-between align-items-center flex-wrap gap-2 co-pedido-header">
                <div>
                    <i class="fas fa-file-invoice text-primary me-1"></i>
                    <strong>Pedido ${this.escapeHtml(grupo.folioPedido)}</strong>
                    <span class="text-muted small ms-2">${this.escapeHtml(grupo.cliente || '')}</span>
                    <span class="badge bg-secondary ms-2">${grupo.cortes.length} corte(s)</span>
                </div>
                <button type="button" class="vi-btn vi-btn-outline vi-btn-sm co-btn-ticket-pedido"
                        data-id-encabezado="${grupo.idEncabezado}">
                    <i class="fas fa-print"></i> Imprimir tickets del pedido
                </button>
            </div>
            <div class="co-cards-grid">
                ${grupo.cortes.map(c => this.renderCorteCard(c)).join('')}
            </div>
        </div>`).join('');

        resultados.classList.remove('d-none');
    },

    // Código de barras de la barra física asignada a este corte (srs.corte_piezas.codigo),
    // el mismo Code128 dibujado a mano que usa el Taller 3D de AdminCortes — así el operador
    // ve exactamente la etiqueta que debe buscar en el rack, no solo el folio del lote.
    // c.codigo_pieza viene null en asignaciones anteriores a este enlace o si el módulo de
    // piezas (sql/cortes_piezas.sql) no está instalado; en ese caso no se dibuja nada.
    renderPiezaBarcode(c) {
        if (!c.codigo_pieza || typeof CodigoBarras === 'undefined') return '';
        try {
            const svg = CodigoBarras.svg(c.codigo_pieza, { modulo: 1.6, alto: 40, tamTexto: 11, margen: 6 });
            return `<div class="co-card-corte-pieza">
                <span>Barra a tomar</span>
                ${svg}
            </div>`;
        } catch (e) {
            console.warn('No se pudo dibujar el código de la pieza', c.codigo_pieza, e);
            return '';
        }
    },

    renderCorteCard(c) {
        const confirmado = c.estatus === 'confirmado';
        return `
    <div class="co-card-corte ${confirmado ? 'confirmado' : ''}">
        <div class="co-card-corte-header">
            <div>
                <div class="co-card-corte-producto">
                    <i class="fas fa-box text-primary me-1"></i>${this.escapeHtml(c.cve_prod)}
                </div>
                <div class="co-card-corte-descr">${this.escapeHtml(c.descr_prod)}</div>
            </div>
            <span class="co-badge-estatus ${confirmado ? 'co-badge-confirmado' : 'co-badge-reservado'}">
                ${confirmado ? 'Confirmado' : 'Reservado'}
            </span>
        </div>

        <div class="co-card-corte-body">
            <div class="co-card-corte-fila">
                <span>Solicitado</span>
                <span>${c.cantidad_solicitada} pza(s) × ${c.longitud_solicitada}m</span>
            </div>
            <div class="co-card-corte-fila">
                <span>Folio origen</span>
                <span>${this.escapeHtml(c.folio)}</span>
            </div>
            <div class="co-card-corte-fila">
                <span>Long. pieza</span>
                <span>${c.longitud_origen} m</span>
            </div>
            ${c.sobrante > 0 ? `
            <div class="co-card-corte-fila">
                <span>Sobrante est.</span>
                <span>${c.sobrante} m</span>
            </div>` : ''}
        </div>

        ${this.renderPiezaBarcode(c)}

        ${c.comentario ? `<div class="co-card-corte-comentario">${this.escapeHtml(c.comentario)}</div>` : ''}

        <div class="co-card-corte-footer">
            <div class="co-card-corte-acciones">
                <button type="button" class="vi-btn vi-btn-outline co-btn-ticket-corte"
                        data-asignacion-id="${c.asignacion_id}" title="Imprimir ticket de este corte">
                    <i class="fas fa-print"></i>
                </button>
                ${!confirmado ? `
                <button type="button" class="vi-btn vi-btn-success co-btn-confirmar"
                        data-asignacion-id="${c.asignacion_id}">
                    <i class="fas fa-check"></i> Respetar
                </button>
                <button type="button" class="vi-btn vi-btn-outline co-btn-reasignar"
                        data-asignacion-id="${c.asignacion_id}">
                    <i class="fas fa-exchange-alt"></i> Otra
                </button>` : ''}
            </div>
            ${confirmado ? `<div class="co-card-corte-usuario">${this.escapeHtml(c.usuario_confirmacion || '')}</div>` : ''}
        </div>
    </div>`;
    },

    // ============================================
    // Modal fecha de corte (previo a confirmar)
    // ============================================
    abrirModalFechaCorte({ modo, asignacionId }) {
        this._fechaCorteModo = modo;
        this._fechaCorteAsignacionId = asignacionId;

        const input = document.getElementById('co-fechaCorte-input');
        const ahora = new Date();
        ahora.setMinutes(ahora.getMinutes() - ahora.getTimezoneOffset());
        input.value = ahora.toISOString().slice(0, 16);

        const modalEl = document.getElementById('co-modalFechaCorte');
        // Tras un escaneo, dejar el foco en "Confirmar corte" para que el operador solo
        // presione Enter (la fecha ya viene con la hora actual por defecto).
        modalEl.addEventListener('shown.bs.modal', () => {
            document.getElementById('co-btnConfirmarFechaCorte')?.focus();
        }, { once: true });

        bootstrap.Modal.getOrCreateInstance(modalEl).show();
    },

    async confirmarConFecha() {
        const fechaCorte = document.getElementById('co-fechaCorte-input')?.value;
        if (!fechaCorte) {
            toastMixin?.fire({ icon: 'warning', title: 'Indica la fecha escrita en el ticket' });
            return;
        }

        if (this._fechaCorteModo === 'confirmar') {
            await this.confirmarCorte(this._fechaCorteAsignacionId, fechaCorte);
        } else if (this._fechaCorteModo === 'reasignar' && this._reasignarPendiente) {
            await this.confirmarReasignacion(this._reasignarPendiente.idCorte, this._reasignarPendiente.folio, fechaCorte);
        }

        bootstrap.Modal.getInstance(document.getElementById('co-modalFechaCorte'))?.hide();
    },

    async confirmarCorte(asignacionId, fechaCorte) {
        try {
            const csrfToken = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
            const resp = await fetch('/CorteOperacion/ConfirmarCorte', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': csrfToken || '' },
                body: JSON.stringify({ AsignacionId: parseInt(asignacionId), FechaCorte: fechaCorte })
            });
            const result = await resp.json();

            if (result.success) {
                toastMixin?.fire({ icon: 'success', title: 'Corte confirmado' });
                this.buscarCortes();
            } else {
                toastMixin?.fire({ icon: 'error', title: result.message || 'No se pudo confirmar' });
            }
        } catch (err) {
            console.error(err);
            toastMixin?.fire({ icon: 'error', title: 'Error de conexión' });
        }
    },

    async abrirModalReasignar(asignacionId) {
        const corte = this.cortesActuales.find(c => String(c.asignacion_id) === String(asignacionId));
        if (!corte) return;

        this._asignacionEnReasignacion = corte;

        document.getElementById('co-reasignar-producto').textContent = `${corte.cve_prod} — ${corte.descr_prod}`;
        document.getElementById('co-reasignar-longitud').textContent = corte.longitud_solicitada;
        document.getElementById('co-reasignar-folio-actual').textContent = corte.folio;

        const lista = document.getElementById('co-lista-piezas-alt');
        lista.innerHTML = '<div class="text-center text-muted py-3">Cargando piezas disponibles...</div>';

        bootstrap.Modal.getOrCreateInstance(document.getElementById('co-modalReasignar')).show();

        try {
            const sucursal = document.getElementById('co-sucursal-actual')?.value || 1;
            const resp = await fetch(`/DatosGenerales/ObtenerPiezasDisponibles?productoId=${corte.cve_prod}&sucursal=${sucursal}`);
            const data = await resp.json();

            if (!data.success || !data.piezas.length) {
                lista.innerHTML = '<div class="text-center text-muted py-3">No hay otras piezas disponibles para este producto.</div>';
                return;
            }

            const piezasValidas = data.piezas.filter(p =>
                parseFloat(p.longitud) >= parseFloat(corte.longitud_solicitada) - 0.001
            );

            if (!piezasValidas.length) {
                lista.innerHTML = '<div class="text-center text-muted py-3">Ninguna pieza disponible alcanza la longitud requerida.</div>';
                return;
            }

            lista.innerHTML = piezasValidas.map(p => `
                <div class="co-pieza-opcion" data-id-corte="${p.id_corte}" data-folio="${this.escapeHtml(p.folio)}">
                    <div class="d-flex justify-content-between align-items-center">
                        <span><i class="fas fa-ruler-combined text-primary me-1"></i>
                            Folio ${this.escapeHtml(p.folio)} — ${p.longitud}m
                        </span>
                        <span class="text-muted small">${p.cantidad} disponible(s)</span>
                    </div>
                </div>`).join('');
        } catch (err) {
            console.error(err);
            lista.innerHTML = '<div class="text-center text-danger py-3">Error al cargar piezas disponibles.</div>';
        }
    },

    async confirmarReasignacion(nuevoIdCorte, nuevoFolio, fechaCorte) {
        const corte = this._asignacionEnReasignacion;
        if (!corte) return;

        try {
            const csrfToken = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
            const resp = await fetch('/CorteOperacion/ReasignarCorte', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': csrfToken || '' },
                body: JSON.stringify({
                    AsignacionId: parseInt(corte.asignacion_id),
                    NuevoIdCorte: parseInt(nuevoIdCorte),
                    NuevoFolio: nuevoFolio,
                    FechaCorte: fechaCorte
                })
            });
            const result = await resp.json();

            if (result.success) {
                toastMixin?.fire({ icon: 'success', title: 'Corte reasignado y confirmado' });
                this.buscarCortes();
            } else {
                toastMixin?.fire({ icon: 'error', title: result.message || 'No se pudo reasignar' });
            }
        } catch (err) {
            console.error(err);
            toastMixin?.fire({ icon: 'error', title: 'Error de conexión' });
        }
    },

    // ============================================
    // Tickets PDF
    // ============================================
    imprimirTicketPedido(idEncabezado) {
        const grupo = this.gruposPedidos.find(g => String(g.idEncabezado) === String(idEncabezado));
        if (!grupo || !grupo.cortes.length) return;

        this.iniciarSurtido(idEncabezado);

        const { jsPDF } = window.jspdf;
        const doc = new jsPDF({ unit: 'mm', format: [80, 190] });
        grupo.cortes.forEach((corte, idx) => {
            if (idx > 0) doc.addPage([80, 190]);
            this.dibujarTicket(doc, corte, grupo);
        });
        this.abrirParaImprimir(doc);
    },

    imprimirTicketCorte(asignacionId) {
        const corte = this.cortesActuales.find(c => String(c.asignacion_id) === String(asignacionId));
        if (!corte) return;
        const grupo = this.gruposPedidos.find(g => String(g.idEncabezado) === String(corte.id_encabezado));

        this.iniciarSurtido(corte.id_encabezado);

        const { jsPDF } = window.jspdf;
        const doc = new jsPDF({ unit: 'mm', format: [80, 190] });
        this.dibujarTicket(doc, corte, grupo);
        this.abrirParaImprimir(doc);
    },

    abrirParaImprimir(doc) {
        doc.autoPrint();
        const blobUrl = doc.output('bloburl');
        const ventana = window.open(blobUrl, '_blank');
        if (!ventana) {
            toastMixin?.fire({
                icon: 'warning',
                title: 'El navegador bloqueó la ventana emergente. Habilita popups para este sitio.'
            });
        }
    },

    // Genera un código de barras Code128 como dataURL PNG (JsBarcode sobre un canvas).
    // Codifica CO-{asignacion_id}, que es lo que el lector de la vista interpreta para
    // confirmar el corte automáticamente. displayValue muestra el texto legible debajo.
    generarBarcodeDataUrl(text) {
        try {
            if (typeof JsBarcode !== 'function') return null;
            const canvas = document.createElement('canvas');
            JsBarcode(canvas, text, {
                format: 'CODE128',
                displayValue: true,
                fontSize: 16,
                textMargin: 2,
                height: 45,
                width: 2,
                margin: 6
            });
            return canvas.toDataURL('image/png');
        } catch (e) {
            console.warn('No se pudo generar el código de barras del ticket', e);
            return null;
        }
    },

    dibujarTicket(doc, corte, grupo) {
        const folioPedido = grupo?.folioPedido || corte.folio_pedido || '-';
        const cliente = grupo?.cliente || corte.cli_prov || '';
        const serie = `T-${corte.asignacion_id}`;
        const M = 5;                 // margen izq/der
        const W = 80;                // ancho ticket
        const right = W - M;

        // ── Encabezado con barra oscura ──
        doc.setFillColor(33, 37, 41);
        doc.rect(M, 5, W - M * 2, 10, 'F');
        doc.setTextColor(255, 255, 255);
        doc.setFont(undefined, 'bold');
        doc.setFontSize(12);
        doc.text('TICKET DE CORTE', W / 2, 11.8, { align: 'center' });
        doc.setTextColor(0, 0, 0);

        let y = 20;

        // ── Serie + datos del pedido ──
        doc.setFont(undefined, 'bold');
        doc.setFontSize(9);
        doc.text(serie, M, y);
        doc.setFont(undefined, 'normal');
        doc.setFontSize(7.5);
        doc.text(new Date().toLocaleString('es-MX'), right, y, { align: 'right' });
        y += 5;

        doc.setFontSize(8);
        doc.text(`Pedido: ${folioPedido}`, M, y); y += 4;
        if (cliente) {
            const cliLines = doc.splitTextToSize(`Cliente: ${cliente}`, W - M * 2);
            doc.text(cliLines, M, y); y += cliLines.length * 3.6;
        }
        y += 2;

        doc.setDrawColor(200, 200, 200);
        doc.setLineWidth(0.2);
        doc.line(M, y, right, y); y += 5;

        // ── Producto ──
        doc.setFont(undefined, 'bold');
        doc.setFontSize(10);
        doc.text(`${corte.cve_prod}`, M, y); y += 4.5;
        doc.setFont(undefined, 'normal');
        doc.setFontSize(8);
        const descLines = doc.splitTextToSize(corte.descr_prod || '', W - M * 2);
        doc.text(descLines, M, y); y += descLines.length * 3.6 + 3;

        // ── Recuadro destacado: longitud a cortar × cantidad ──
        const boxH = 15;
        doc.setFillColor(255, 243, 205);
        doc.setDrawColor(255, 193, 7);
        doc.setLineWidth(0.4);
        doc.roundedRect(M, y, W - M * 2, boxH, 2, 2, 'FD');
        doc.setFont(undefined, 'bold');
        doc.setFontSize(15);
        doc.text(`${corte.longitud_solicitada} m`, M + 4, y + 8);
        doc.setFont(undefined, 'normal');
        doc.setFontSize(7);
        doc.text('LONGITUD A CORTAR', M + 4, y + 12.5);
        doc.setFont(undefined, 'bold');
        doc.setFontSize(14);
        doc.text(`× ${corte.cantidad_solicitada}`, right - 4, y + 9.5, { align: 'right' });
        y += boxH + 5;

        // ── Datos de la pieza origen ──
        doc.setFont(undefined, 'normal');
        doc.setFontSize(8);
        doc.text(`Pieza origen: ${corte.folio}`, M, y); y += 4;
        if (corte.codigo_pieza) { doc.text(`Barra: ${corte.codigo_pieza}`, M, y); y += 4; }
        doc.text(`Longitud pieza: ${corte.longitud_origen} m`, M, y); y += 4;
        doc.text(`Sobrante estimado: ${corte.sobrante || 0} m`, M, y); y += 4;

        if (corte.comentario) {
            const comentLines = doc.splitTextToSize(`Comentario: ${corte.comentario}`, W - M * 2);
            doc.text(comentLines, M, y); y += comentLines.length * 3.6 + 1;
        }
        y += 2;

        doc.setDrawColor(200, 200, 200);
        doc.line(M, y, right, y); y += 4;

        // ── Código de barras para confirmar el corte al escanear ──
        const barcodeData = this.generarBarcodeDataUrl(`${this.SCAN_PREFIX}${corte.asignacion_id}`);
        if (barcodeData) {
            doc.setFont(undefined, 'bold');
            doc.setFontSize(7.5);
            doc.text('Escanea para confirmar el corte', W / 2, y, { align: 'center' });
            doc.setFont(undefined, 'normal');
            y += 3;

            // Ancho fijo centrado; la altura respeta la proporción del canvas para no distorsionar las barras.
            const targetW = 64;
            let targetH = 16;
            try {
                const props = doc.getImageProperties(barcodeData);
                targetH = targetW * props.height / props.width;
            } catch { /* jsPDF viejo sin getImageProperties: usar altura por defecto */ }

            doc.addImage(barcodeData, 'PNG', (W - targetW) / 2, y, targetW, targetH);
            y += targetH + 6;
        }

        // ── Firmas ──
        doc.setFontSize(8);
        doc.text('Fecha de finalización:', M, y); y += 9;
        doc.text('_______________________', M, y); y += 7;
        doc.text('Cortó (nombre/firma):', M, y); y += 9;
        doc.text('_______________________', M, y);
    },

    async iniciarSurtido(idEncabezado) {
        try {
            const csrfToken = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
            const formData = new FormData();
            if (csrfToken) formData.append('__RequestVerificationToken', csrfToken);
            formData.append('idEncabezado', idEncabezado);
            await fetch('/CorteOperacion/IniciarSurtido', { method: 'POST', body: formData });
        } catch (err) {
            console.warn('No se pudo iniciar el registro de surtido', err);
        }
    },

    escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text ?? '';
        return div.innerHTML;
    }
};

if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => CorteOperacionApp.init());
} else {
    CorteOperacionApp.init();
}
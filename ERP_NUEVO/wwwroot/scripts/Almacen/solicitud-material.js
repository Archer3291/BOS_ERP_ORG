// ============================================================
// solicitud-material.js  — Verificación de Almacén
// Cambios:
//   • Existencia por almacén mejorada (stock por cada almacén visible)
//   • Columna "Almacén Asignado" eliminada
//   • Modal dedicado para discrepancia física con validaciones
//   • IniciarSurtido corregido: visible en seguimiento aunque no exista RMP
// ============================================================

const VerificacionApp = {
    pedidoActual: null,
    partidas: [],
    idVerificacionGuardada: null,
    surtidoIniciado: false,
    surtidoFinalizado: false,

    // ── Inicialización ──────────────────────────────────────
    init() {
        this.initModalPedidos();
        this.initModalVerificaciones();
        this.initModalDiscrepancia();
        this.bindGuardar();
        this.bindTablaInput();
        this.bindSurtido();
        console.log('VerificacionApp inicializado');
    },

    // ── Modal de búsqueda de pedidos ────────────────────────
    initModalPedidos() {
        const modalEl = document.getElementById('vi-ver-modalBuscarPedido');
        const input = document.getElementById('vi-ver-inputBuscarPedido');
        const lista = document.getElementById('vi-ver-listaResultadosPedidos');
        const btnLimp = document.getElementById('vi-ver-btnLimpiarPedidos');
        const sel = document.getElementById('vi-ver-pageSizePedidos');
        const spinner = document.getElementById('vi-ver-spinnerPedidos');

        let page = 1, pageSize = 25, totalRecords = 0, typing = null;

        const buscar = async (q = '') => {
            spinner.classList.remove('d-none');
            try {
                const r = await fetch(
                    `/VIVerificacionAlmacen/BuscarPedidosParaVerificar?nombre=${encodeURIComponent(q)}&page=${page}&pageSize=${pageSize}`
                );
                const data = await r.json();
                totalRecords = data.total || 0;
                renderLista(data.items || []);
                renderPaginacion(
                    'vi-ver-recordsFromPedidos', 'vi-ver-recordsToPedidos',
                    'vi-ver-totalRecordsPedidos', 'vi-ver-paginationPedidos',
                    page, pageSize, totalRecords,
                    (p) => { page = p; buscar(input.value); }
                );
            } catch (e) {
                lista.innerHTML = '<li class="text-danger p-3">Error al cargar pedidos</li>';
            } finally { spinner.classList.add('d-none'); }
        };

        const renderLista = (items) => {
            if (!items.length) {
                lista.innerHTML = `<li class="text-center text-muted py-4">
            <i class="fas fa-search fa-2x mb-2 d-block opacity-25"></i>Sin resultados</li>`;
                return;
            }
            lista.innerHTML = items.map(item => `
        <div class="vi-search-item" onclick="VerificacionApp.cargarPedido(${item.id_encabezado})">
            <div class="d-flex justify-content-between align-items-start">
                <div>
                    <div class="fw-semibold mb-1">
                        <i class="fas fa-file-invoice text-primary me-1"></i>
                        ${escHtml(item.folio || '')}
                        ${item.es_mixto ? `
                            <span class="vi-ver-badge parcial ms-1" style="font-size:.6rem">
                                <i class="fas fa-layer-group"></i> Mixto
                            </span>` : ''}
                    </div>
                    <div class="small text-muted">
                        <i class="fas fa-user me-1"></i>${escHtml(item.n_cli || item.cli_prov || '')}
                    </div>
                    <div class="d-flex gap-3 mt-1" style="font-size:.75rem;color:var(--vi-gray-400)">
                        <span><i class="fas fa-calendar me-1"></i>${formatFecha(item.fch)}</span>
                        <span><i class="fas fa-money-bill-wave me-1"></i>$${parseFloat(item.imp || 0).toFixed(2)}</span>
                    </div>
                </div>
                <span class="vi-ver-badge ok"><i class="fas fa-check-circle"></i> Aprobado</span>
            </div>
        </div>`).join('');
        };

        modalEl?.addEventListener('show.bs.modal', () => { page = 1; buscar(); });
        input?.addEventListener('input', (e) => {
            clearTimeout(typing);
            typing = setTimeout(() => { page = 1; buscar(e.target.value); }, 300);
        });
        btnLimp?.addEventListener('click', () => { input.value = ''; page = 1; buscar(); });
        sel?.addEventListener('change', (e) => { pageSize = +e.target.value; page = 1; buscar(input.value); });
    },

    // ── Cargar partidas del pedido seleccionado ────────────
    async cargarPedido(idEncabezadoPadre) {
        bootstrap.Modal.getInstance(document.getElementById('vi-ver-modalBuscarPedido'))?.hide();
        Swal.fire({ title: 'Cargando pedido…', allowOutsideClick: false, didOpen: () => Swal.showLoading() });

        try {
            // ← antes: idEncabezado, ahora: idEncabezadoPadre
            const r = await fetch(`/VIVerificacionAlmacen/ObtenerPartidasPedido?idEncabezadoPadre=${idEncabezadoPadre}`);
            const data = await r.json();
            if (!data.success) throw new Error(data.message);

            Swal.close();
            this.pedidoActual = data.encabezado;
            this.partidas = data.partidas || [];
            this.idVerificacionGuardada = null;
            this.surtidoIniciado = false;
            this.surtidoFinalizado = false;

            // ── Guardar referencias a los documentos hijos (stock / modula) ──
            this.idEncabezadoPadre = data.id_encabezado_padre;
            this.idEncabezadoNormal = data.id_encabezado_normal;
            this.idEncabezadoModula = data.id_encabezado_modula;

            // El input hidden sigue mandando el PADRE — es lo que usan
            // Guardar/Iniciar/Finalizar surtido como referencia del pedido completo
            document.getElementById('vi-ver-documentid').value = data.id_encabezado_padre;
            document.getElementById('vi-ver-folio-pedido').value = data.encabezado.folio;
            document.getElementById('vi-ver-cliente').value = data.encabezado.n_cli || data.encabezado.cli_prov;
            document.getElementById('vi-ver-rfc').value = data.encabezado.rfc || '';
            document.getElementById('vi-ver-info-cliente').value = data.encabezado.info_cli || '';

            // ── Aviso si el pedido es mixto ──
            this._mostrarAvisoMixto();

            this.actualizarUIBotonesurtido({ iniciado: false, finalizado: false });
            this.renderTabla();
            this.actualizarResumen();
            this.habilitarBotones(false);
            await this.cargarEstadoSurtidoPorPedido(idEncabezadoPadre);
        } catch (e) {
            Swal.fire({ icon: 'error', title: 'Error', text: e.message });
        }
    },

    // ── Aviso visual arriba de la tabla si el pedido tiene ambos orígenes ──
    _mostrarAvisoMixto() {
        const existente = document.getElementById('vi-ver-aviso-mixto');
        if (existente) existente.remove();

        if (this.idEncabezadoNormal && this.idEncabezadoModula) {
            const tarjeta = document.querySelector('.vi-table-wrap')?.parentElement;
            if (!tarjeta) return;
            const aviso = document.createElement('div');
            aviso.id = 'vi-ver-aviso-mixto';
            aviso.className = 'mb-3 p-2 rounded-2 d-flex align-items-center gap-2';
            aviso.style.cssText = 'background:#f3e8ff;border:1px solid #7c3aed;font-size:.8rem;color:#5b21b6;';
            aviso.innerHTML = `<i class="fas fa-info-circle"></i>
            Este pedido combina partidas de <strong>Almacén Stock</strong> y
            <strong>Máquina Modula</strong>. Se muestran agrupadas abajo.`;
            tarjeta.querySelector('.vi-table-wrap').insertAdjacentElement('beforebegin', aviso);
        }
    },

    // ANTES
    async cargarEstadoSurtidoPorPedido(idPedido) {
        try {
            const r = await fetch(`/VIVerificacionAlmacen/ObtenerEstadoSurtidoPorPedido?idPedido=${idPedido}`);
            const data = await r.json();
            if (!data.success) return;
            this.surtidoIniciado = data.iniciado;
            this.surtidoFinalizado = data.finalizado;
            this.actualizarUIBotonesurtido(data);
            this.habilitarBotones(!!this.idVerificacionGuardada); // ← agregar
        } catch (e) {
            console.error('Error al cargar estado surtido:', e);
        }
    },

    // ── Renderizar tabla de partidas ─────────────────────────
    // COLUMNAS: # | Artículo | Descripción | Existencia por Almacén | Cant. Pendiente | Cant. Verificada | Lote/Serie | Ubicación | Estado | Discrepancia
    renderTabla() {
        const tbody = document.getElementById('vi-ver-tabla-partidas');
        if (!this.partidas.length) {
            tbody.innerHTML = `<tr><td colspan="10" class="vi-table-empty">
            <i class="fas fa-box-open"></i>Sin partidas</td></tr>`;
            return;
        }

        const grupos = [
            { key: 'stock', titulo: 'Almacén Stock', icon: 'fa-warehouse', color: 'var(--primary-blue)' },
            { key: 'modula', titulo: 'Máquina Modula', icon: 'fa-layer-group', color: '#7c3aed' }
        ];

        const filasHtml = [];

        grupos.forEach(grupo => {
            // Filtramos manteniendo el índice ORIGINAL dentro de this.partidas
            const filasDelGrupo = this.partidas
                .map((p, idx) => ({ p, idx }))
                .filter(x => (x.p.origen_almacen || 'stock') === grupo.key);

            if (!filasDelGrupo.length) return;

            filasHtml.push(`
            <tr class="vi-grupo-header-row">
                <td colspan="10" style="background:${grupo.color};color:#fff;font-weight:700;
                    font-size:.75rem;text-transform:uppercase;letter-spacing:.5px;padding:.5rem .8rem;">
                    <i class="fas ${grupo.icon} me-2"></i>${grupo.titulo}
                    <span style="font-weight:400;opacity:.85">
                        (${filasDelGrupo.length} partida${filasDelGrupo.length !== 1 ? 's' : ''})
                    </span>
                </td>
            </tr>`);

            filasDelGrupo.forEach(({ p, idx }) => {
                filasHtml.push(this._renderFilaPartida(p, idx, grupo.key));
            });
        });

        tbody.innerHTML = filasHtml.join('');

        this.partidas.forEach((p, idx) => {
            if (parseFloat(p.cantidad_verificada || 0) > 0) {
                const input = document.getElementById(`vi-ver-qty-${idx}`);
                if (input) this.onCantidadChange(idx, input);
            }
        });
    },

    // ── Renderiza UNA fila de partida (extraído del renderTabla original, sin cambios de lógica) ──
    _renderFilaPartida(p, idx, origen) {
        const estado = calcularEstado(p);
        const rowClass = estado === 'ok' ? 'fila-verificada' : (estado === 'parcial' ? 'fila-parcial' : '');

        const almacenesStock = p.existencia_por_almacen_stock || [];
        const almacenesModula = p.existencia_por_almacen_modula || [];
        const totalStock = almacenesStock.reduce((s, a) => s + parseFloat(a.stock || 0), 0);
        const totalModula = almacenesModula.reduce((s, a) => s + parseFloat(a.stock || 0), 0);
        const totalGeneral = totalStock + totalModula;

        let existHtml;

        if (p.es_servicio) {
            existHtml = `<span class="vi-ver-badge ok">Servicio</span>`;
        } else if (totalGeneral === 0) {
            existHtml = `
    <div class="vi-stock-empty">
        <i class="fas fa-exclamation-triangle"></i>
        <span>Sin stock</span>
    </div>`;
        } else {
            const colorBadge = totalGeneral === 0 ? 'var(--vi-red-600)'
                : totalGeneral <= 5 ? 'var(--vi-amber-600)'
                    : totalGeneral <= 20 ? '#0dcaf0'
                        : 'var(--vi-green-600)';
            const textColor = (totalGeneral > 0 && totalGeneral <= 20) ? '#000' : '#fff';

            const filasStock = almacenesStock.length
                ? `<div class="vi-stock-popup-section-header">
           <i class="fas fa-boxes me-1"></i>Stock
       </div>`
                + almacenesStock.map(a => `
        <div class="vi-stock-alm-row">
            <span class="vi-stock-alm-cve">${escHtml(a.cve_almacen || '')}</span>
            <span class="vi-stock-alm-name">${escHtml(a.n_almacen || '')}</span>
            <span class="vi-stock-alm-qty">${parseFloat(a.stock).toFixed(2)}</span>
        </div>`).join('')
                : `<div class="vi-stock-popup-section-header vi-stock-section-empty">
           <i class="fas fa-boxes me-1"></i>Stock — sin existencia
       </div>`;

            const filasModula = almacenesModula.length
                ? `<div class="vi-stock-popup-section-header vi-stock-section-modula">
           <i class="fas fa-layer-group me-1"></i>Modula
       </div>`
                + almacenesModula.map(a => `
        <div class="vi-stock-alm-row">
            <span class="vi-stock-alm-cve">${escHtml(a.cve_almacen || '')}</span>
            <span class="vi-stock-alm-name">${escHtml(a.n_almacen || '')}</span>
            <span class="vi-stock-alm-qty">${parseFloat(a.stock).toFixed(2)}</span>
        </div>`).join('')
                : `<div class="vi-stock-popup-section-header vi-stock-section-modula vi-stock-section-empty">
           <i class="fas fa-layer-group me-1"></i>Modula — sin existencia
       </div>`;

            existHtml = `
    <div class="vi-stock-wrap" tabindex="0">
        <div class="vi-stock-badge-wrap">
            <span class="vi-stock-total-badge" style="background:${colorBadge};color:${textColor};">
                ${totalGeneral.toFixed(2)}
            </span>
            <span class="vi-stock-expand-icon"><i class="fas fa-chevron-down"></i></span>
        </div>
        <div class="vi-stock-detail-popup">
            <div class="vi-stock-popup-header"><i class="fas fa-warehouse me-1"></i>Existencia por almacén</div>
            ${filasStock}
            ${filasModula}
            <div class="vi-stock-popup-total">
                <span>Total general</span>
                <span style="font-weight:700;color:${colorBadge}">${totalGeneral.toFixed(2)}</span>
            </div>
        </div>
    </div>`;
        }

        const tieneDisc = p.tiene_discrepancia || false;
        const discBadge = tieneDisc
            ? `<div class="vi-disc-tag" title="${escHtml(p.motivo_discrepancia || '')}">
               <i class="fas fa-exclamation-triangle"></i>
               Físico: ${parseFloat(p.cantidad_fisica).toFixed(2)}
           </div>`
            : '';

        // ── Badge de origen junto al SKU, por si el usuario hace scroll y pierde el header de grupo ──
        const origenBadge = origen === 'modula'
            ? `<span class="vi-ver-badge" style="background:#f3e8ff;color:#7c3aed;font-size:.6rem;margin-left:4px">
               <i class="fas fa-layer-group"></i></span>`
            : '';

        // ── Discrepancia solo aplica a Stock (Modula no se puede afectar) ──
        const mostrarBtnDisc = !p.es_servicio && p.tiene_stock && origen === 'stock';

        return `
    <tr data-idx="${idx}" data-origen="${origen}" class="${rowClass}">
        <td class="text-center fw-bold">${idx + 1}</td>
        <td style="font-family:monospace;font-size:.8rem">${escHtml(p.cve_prod || '')}${origenBadge}</td>
        <td style="max-width:180px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis"
            title="${escHtml(p.descripcion || '')}">
            ${escHtml(p.descripcion || '')}
        </td>
        <td class="text-center">${existHtml}</td>
        <td class="text-center fw-semibold">
            ${p.cantidad_pedida}
            ${(p.cantidad_ya_verificada || 0) > 0 ? `
                <div style="font-size:.7rem;color:var(--vi-green-600);
                            font-weight:600;margin-top:2px;white-space:nowrap;">
                    <i class="fas fa-check-circle"></i>
                    ${p.cantidad_ya_verificada} ya verif.
                </div>` : ''}
        </td>
        <td>
            <div>
                <input type="number" class="vi-ver-qty" id="vi-ver-qty-${idx}"
                       value="${p.cantidad_verificada || ''}"
                       min="0" step="0.01" placeholder="0"
                       oninput="VerificacionApp.onCantidadChange(${idx}, this)">
                ${!p.es_servicio && p.tiene_stock ? `
                <div class="vi-progress-wrap" style="margin-top:4px">
                    <div class="vi-progress-bar" id="vi-ver-bar-${idx}"
                         style="width:0%;background:var(--vi-green-600);"></div>
                </div>` : ''}
            </div>
        </td>
        <td>
            <input type="text" class="vi-control" style="font-size:.8rem;padding:.35rem .6rem"
                   id="vi-ver-lote-${idx}"
                   value="${escHtml(p.lote || '')}"
                   placeholder="Lote / Serie">
        </td>
        <td>
            <input type="text" class="vi-control" style="font-size:.8rem;padding:.35rem .6rem"
                   id="vi-ver-ubic-${idx}"
                   value="${escHtml(p.ubicacion || '')}"
                   placeholder="Rack / Nivel">
        </td>
        <td id="vi-ver-estado-${idx}">
            ${badgeEstado(estado)}
        </td>
        <td class="text-center" style="min-width:120px">
            ${discBadge}
            ${mostrarBtnDisc ? `
            <button type="button"
                    class="vi-btn ${tieneDisc ? 'vi-btn-amber' : 'vi-btn-outline'}"
                    style="padding:.28rem .6rem;font-size:.72rem;white-space:nowrap"
                    title="Marcar discrepancia física"
                    onclick="VerificacionApp.abrirModalDiscrepancia(${idx})">
                <i class="fas fa-exclamation-triangle"></i>
                ${tieneDisc ? 'Editar disc.' : 'Discrepancia'}
            </button>` : ''}
            <button type="button"
                id="vi-ver-btn-etiqueta-${idx}"
                class="vi-btn vi-btn-outline"
                style="padding:.28rem .6rem;font-size:.72rem;white-space:nowrap;margin-top:4px"
                title="Imprimir etiqueta de esta partida"
                ${!this.surtidoIniciado ? 'disabled' : ''}
                onclick="EtiquetasVerificacion.imprimirPartida(${idx})">
                <i class="fas fa-tag"></i> Etiqueta
            </button>
            <button type="button"
                id="vi-ver-btn-preview-${idx}"
                class="vi-btn vi-btn-outline"
                style="padding:.28rem .6rem;font-size:.72rem;white-space:nowrap;margin-top:4px"
                title="Vista previa ZPL"
                ${!this.surtidoIniciado ? 'disabled' : ''}
                onclick="EtiquetasVerificacion.imprimirPartida(${idx}, true)">
                <i class="fas fa-eye"></i>
            </button>
        </td>
    </tr>`;
    },
    // ── Cambio de cantidad ──────────────────────────────────
    onCantidadChange(idx, input) {
        const p = this.partidas[idx];
        const val = parseFloat(input.value) || 0;
        const ped = parseFloat(p.cantidad_pedida) || 0;

        this.partidas[idx].cantidad_verificada = val;
        input.classList.toggle('excede', val > ped);

        const bar = document.getElementById(`vi-ver-bar-${idx}`);
        if (bar) {
            const pct = ped > 0 ? Math.min((val / ped) * 100, 100) : 0;
            bar.style.width = pct + '%';
            bar.style.background = val > ped
                ? 'var(--vi-red-600)'
                : (val === ped ? 'var(--vi-green-600)' : 'var(--vi-amber-600)');
        }

        const estado = calcularEstado(this.partidas[idx]);
        const tr = input.closest('tr');
        tr.className = estado === 'ok' ? 'fila-verificada' : (estado === 'parcial' ? 'fila-parcial' : '');
        const estadoEl = document.getElementById(`vi-ver-estado-${idx}`);
        if (estadoEl) estadoEl.innerHTML = badgeEstado(estado);

        this.actualizarResumen();
    },

    // ── Verificar todo ──────────────────────────────────────
    verificarTodo() {
        this.partidas.forEach((p, idx) => {
            const cantMax = p.tiene_discrepancia && p.cantidad_fisica !== null
                ? parseFloat(p.cantidad_fisica)
                : parseFloat(p.cantidad_pedida);

            p.cantidad_verificada = cantMax;
            const input = document.getElementById(`vi-ver-qty-${idx}`);
            if (input) { input.value = cantMax; this.onCantidadChange(idx, input); }
        });
    },

    // ── Leer datos actuales de la tabla ────────────────────
    leerDatosTabla() {
        this.partidas.forEach((p, idx) => {
            p.cantidad_verificada = parseFloat(document.getElementById(`vi-ver-qty-${idx}`)?.value || 0);
            p.lote = document.getElementById(`vi-ver-lote-${idx}`)?.value || '';
            p.ubicacion = document.getElementById(`vi-ver-ubic-${idx}`)?.value || '';
        });
    },

    // ── Actualizar resumen ──────────────────────────────────
    actualizarResumen() {
        const total = this.partidas.length;
        let ok = 0, parcial = 0, pend = 0;

        this.partidas.forEach(p => {
            const e = calcularEstado(p);
            if (e === 'ok') ok++;
            else if (e === 'parcial') parcial++;
            else pend++;
        });

        document.getElementById('vi-ver-cnt-ok').textContent = ok;
        document.getElementById('vi-ver-cnt-parcial').textContent = parcial;
        document.getElementById('vi-ver-cnt-pend').textContent = pend;
        document.getElementById('vi-ver-resumen-total').textContent = total;
        document.getElementById('vi-ver-resumen-ok').textContent = ok;
        document.getElementById('vi-ver-resumen-parcial').textContent = parcial;
        document.getElementById('vi-ver-resumen-pend').textContent = pend;

        const pct = total > 0 ? Math.round(((ok + parcial) / total) * 100) : 0;
        document.getElementById('vi-ver-pct-progreso').textContent = pct + '%';
        const barra = document.getElementById('vi-ver-barra-progreso');
        if (barra) {
            barra.style.width = pct + '%';
            barra.style.background = pct === 100
                ? 'var(--vi-green-600)'
                : (pct > 0 ? 'var(--vi-amber-600)' : 'var(--vi-gray-200)');
        }

        // "Guardar" se habilita con al menos 1 partida con cantidad
        const hayAlguna = this.partidas.some(p => parseFloat(p.cantidad_verificada || 0) > 0);
        document.getElementById('vi-ver-btn-guardar').disabled = !hayAlguna;

        // "Finalizar Surtido": solo habilitado si TODAS las partidas tienen cantidad > 0
        // (se confirma definitivamente al recibir respuesta del servidor al guardar)
        //const todasCapturadas = total > 0 && pend === 0;
        //this._actualizarBotonFinalizar(todasCapturadas);
    },

    // ── Habilitar/deshabilitar botones ──────────────────────
    habilitarBotones(hayVerificacion = false) {
        const hayPartidas = this.partidas.length > 0;
        document.getElementById('vi-ver-btn-verificar-todo').disabled = !hayPartidas;
        document.getElementById('vi-ver-btn-imprimir').disabled = !this.surtidoIniciado;
        document.getElementById('vi-ver-btn-generar-remision').disabled = !hayVerificacion;

        const btnIniciar = document.getElementById('vi-ver-btn-iniciar-surtido');
        if (btnIniciar && !this.surtidoIniciado)
            btnIniciar.disabled = !hayPartidas;

        // ── Botones de etiqueta por fila: activos desde que se inicia el surtido ──
        this.partidas.forEach((_, idx) => {
            const btnEtq = document.getElementById(`vi-ver-btn-etiqueta-${idx}`);
            const btnPrev = document.getElementById(`vi-ver-btn-preview-${idx}`);
            if (btnEtq) btnEtq.disabled = !this.surtidoIniciado;
            if (btnPrev) btnPrev.disabled = !this.surtidoIniciado;
        });
    },

    // ── Binding Guardar ─────────────────────────────────────
    bindGuardar() {
        document.getElementById('vi-ver-btn-guardar')
            ?.addEventListener('click', () => this.guardar());
    },

    // ── Guardar verificación ────────────────────────────────
    async guardar() {
        this.leerDatosTabla();

        const hayPartidas = this.partidas.length > 0;
        if (!hayPartidas) {
            toastMixin.fire({ icon: 'warning', title: 'No hay partidas cargadas' });
            return;
        }

        const btn = document.getElementById('vi-ver-btn-guardar');
        const orig = btn.innerHTML;
        btn.disabled = true;
        btn.innerHTML = '<i class="fas fa-spinner fa-spin"></i> Guardando…';

        try {
            // Solo las partidas con cantidad > 0
            const productosPayload = this.partidas.map(p => ({
                id_partida: String(p.id_partida),
                    productoId: p.cve_prod,
                    descripcion: p.descripcion,
                    cantidad_pedida: String(p.cantidad_pedida),
                    cantidad_verificada: String(p.cantidad_verificada),
                    precio: String(p.precio || 0),
                    descuento: String(p.descuento || 0),
                    costoUnitario: String(p.precio || 0),
                    unidad: p.unidad,
                    id_producto: String(p.id_producto),
                    comentario: p.comentario || '',
                    almacen_seleccionado: '',
                    nombre_almacen: '',
                    lote: p.lote || '',
                    ubicacion: p.ubicacion || '',
                    tiene_discrepancia: String(p.tiene_discrepancia || false),
                    cantidad_fisica: p.tiene_discrepancia ? String(p.cantidad_fisica) : ''
                }));

            document.getElementById('vi-ver-productosJSON').value = JSON.stringify(productosPayload);

            const formData = new FormData(document.getElementById('vi-ver-form'));
            const resp = await fetch('/VIVerificacionAlmacen/GuardarVerificacion',
                { method: 'POST', body: formData });
            const result = await resp.json();

            if (result.success) {
                toastMixin.fire({ icon: 'success', title: 'Verificación guardada correctamente' });

                // Habilitar "Finalizar Surtido" solo si TODAS las partidas ya tienen cantidad
                this._actualizarBotonFinalizar(result.todas_guardadas);
            } else {
                toastMixin.fire({ icon: 'error', title: result.message || 'Error al guardar' });
            }
        } catch (e) {
            toastMixin.fire({ icon: 'error', title: e.message || 'Error de red' });
        } finally {
            btn.disabled = false;
            btn.innerHTML = orig;
        }
    },
    // ── Habilitar/deshabilitar "Finalizar Surtido" ──────────────────────────────
    _actualizarBotonFinalizar(todasGuardadas) {
        const btnFin = document.getElementById('vi-ver-btn-finalizar-surtido');
        if (!btnFin) return;
        if (todasGuardadas) {
            btnFin.classList.remove('d-none');
            btnFin.disabled = false;
        } else {
            // Visible pero deshabilitado mientras falten partidas
            btnFin.classList.remove('d-none');
            btnFin.disabled = true;
            btnFin.title = 'Debe guardar cantidad en todas las partidas antes de finalizar';
        }
    },

    // ── Imprimir etiquetas ──────────────────────────────────
    async imprimirEtiquetas() {
        EtiquetasVerificacion.imprimirTodas();
    },

    // ── Modal verificaciones previas ────────────────────────
    initModalVerificaciones() {
        const modalEl = document.getElementById('vi-ver-modalBuscarVerificaciones');
        const input = document.getElementById('vi-ver-inputBuscarVerif');
        const lista = document.getElementById('vi-ver-listaResultadosVerif');
        const btnLimp = document.getElementById('vi-ver-btnLimpiarVerif');
        const spinner = document.getElementById('vi-ver-spinnerVerif');

        let page = 1, pageSize = 25, totalRecords = 0, typing = null;

        const buscar = async (q = '') => {
            spinner.classList.remove('d-none');
            try {
                const r = await fetch(
                    `/VIVerificacionAlmacen/BuscarDocumentosVerificacion?nombre=${encodeURIComponent(q)}&page=${page}&pageSize=${pageSize}`
                );
                const data = await r.json();
                totalRecords = data.total || 0;

                if (!(data.items || []).length) {
                    lista.innerHTML = `<li class="text-center text-muted py-4">
                        <i class="fas fa-search fa-2x mb-2 d-block opacity-25"></i>Sin resultados</li>`;
                } else {
                    lista.innerHTML = (data.items || []).map(item => `
                        <div class="vi-search-item"
                             onclick="VerificacionApp.cargarVerificacion(${item.id_encabezado})">
                            <div class="d-flex justify-content-between align-items-start">
                                <div>
                                    <div class="fw-semibold mb-1">
                                        <i class="fas fa-warehouse text-primary me-1"></i>
                                        ${escHtml(item.folio || '')}
                                    </div>
                                    <div class="small text-muted">
                                        <i class="fas fa-user me-1"></i>${escHtml(item.n_cli || item.cli_prov || '')}
                                    </div>
                                    <div class="d-flex gap-3 mt-1" style="font-size:.75rem;color:var(--vi-gray-400)">
                                        <span><i class="fas fa-calendar me-1"></i>${formatFecha(item.fch)}</span>
                                        <span><i class="fas fa-money-bill-wave me-1"></i>$${parseFloat(item.imp || 0).toFixed(2)}</span>
                                    </div>
                                </div>
                                <span class="vi-ver-badge ok"><i class="fas fa-check"></i> Verificado</span>
                            </div>
                        </div>`).join('');
                }

                renderPaginacion(
                    'vi-ver-recordsFromVerif', 'vi-ver-recordsToVerif',
                    'vi-ver-totalRecordsVerif', 'vi-ver-paginationVerif',
                    page, pageSize, totalRecords,
                    (p) => { page = p; buscar(input.value); }
                );
            } catch (e) {
                lista.innerHTML = '<li class="text-danger p-3">Error al cargar</li>';
            } finally { spinner.classList.add('d-none'); }
        };

        modalEl?.addEventListener('show.bs.modal', () => { page = 1; buscar(); });
        input?.addEventListener('input', (e) => {
            clearTimeout(typing);
            typing = setTimeout(() => { page = 1; buscar(e.target.value); }, 300);
        });
        btnLimp?.addEventListener('click', () => { input.value = ''; page = 1; buscar(); });
    },

    // ── Cargar verificación previa ──────────────────────────
    async cargarVerificacion(idVerificacion) {
        bootstrap.Modal.getInstance(
            document.getElementById('vi-ver-modalBuscarVerificaciones')
        )?.hide();

        Swal.fire({ title: 'Cargando verificación…', allowOutsideClick: false, didOpen: () => Swal.showLoading() });

        try {
            const r = await fetch(`/VIVerificacionAlmacen/ObtenerVerificacionParaRemision?idVerificacion=${idVerificacion}`);
            const data = await r.json();
            if (!data.success) throw new Error(data.message);

            Swal.close();
            this.idVerificacionGuardada = idVerificacion;
            document.getElementById('vi-ver-id-verificacion').value = idVerificacion;
            document.getElementById('vi-ver-documentid').value = data.pedido_id || '';
            document.getElementById('vi-ver-folio-pedido').value = data.folio || '';
            document.getElementById('vi-ver-cliente').value = data.n_cli || data.cli_prov || '';
            document.getElementById('vi-ver-rfc').value = data.rfc || '';

            this.partidas = (data.productos || []).map(p => ({
                id_partida: p.id_partidas,
                cve_prod: p.producto_id,
                descripcion: p.descripcion,
                cantidad_pedida: parseFloat(p.cantidad_pedida) || 0,
                cantidad_verificada: parseFloat(p.cantidad_verificada) || 0,
                precio: parseFloat(p.precio) || 0,
                descuento: parseFloat(p.descuento) || 0,
                unidad: p.udm || 'PZA',
                comentario: p.comentario || '',
                id_producto: p.id_producto || 0,
                existencia_total: p.existencia,
                existencia_por_almacen: [],
                es_servicio: (p.udm === 'SRV' || p.udm === 'SERVICIO'),
                lote: p.lote || '',
                ubicacion: p.ubicacion || '',
                tiene_discrepancia: p.cantidad_fisica !== null && p.cantidad_fisica !== undefined,
                cantidad_fisica: p.cantidad_fisica || null,
                motivo_discrepancia: p.motivo_discrepancia || ''
            }));

            this.pedidoActual = data;
            this.renderTabla();
            this.actualizarResumen();
            this.habilitarBotones(true);
            await this.cargarEstadoSurtido(idVerificacion);

            toastMixin.fire({ icon: 'success', title: 'Verificación cargada correctamente' });
        } catch (e) {
            Swal.fire({ icon: 'error', title: 'Error', text: e.message });
        }
    },

    // ── Binding de inputs en tabla ──────────────────────────
    bindTablaInput() {
        document.getElementById('vi-ver-tabla-partidas')?.addEventListener('input', (e) => {
            const input = e.target;
            if (input.classList.contains('vi-ver-qty')) {
                const idx = +input.closest('tr').dataset.idx;
                if (!isNaN(idx)) this.onCantidadChange(idx, input);
            }
        });
    },

    // ══════════════════════════════════════════════════════
    // SURTIDO
    // ══════════════════════════════════════════════════════
    bindSurtido() {
        document.getElementById('vi-ver-btn-iniciar-surtido')
            ?.addEventListener('click', () => this.iniciarSurtido());
        document.getElementById('vi-ver-btn-finalizar-surtido')
            ?.addEventListener('click', () => this.finalizarSurtido());
    },

    async cargarEstadoSurtido(idVerificacion) {
        try {
            const r = await fetch(`/VIVerificacionAlmacen/ObtenerEstadoSurtido?idVerificacion=${idVerificacion}`);
            const data = await r.json();
            if (!data.success) return;
            this.surtidoIniciado = data.iniciado;
            this.surtidoFinalizado = data.finalizado;
            this.actualizarUIBotonesurtido(data);
            this.habilitarBotones(!!this.idVerificacionGuardada); // ← agregar
        } catch (e) {
            console.error('Error al cargar estado surtido:', e);
        }
    },

    async iniciarSurtido() {
        const idPedido = document.getElementById('vi-ver-documentid').value;
        if (!idPedido) {
            toastMixin.fire({ icon: 'warning', title: 'Primero debe seleccionar un pedido' });
            return;
        }

        const confirmResult = await Swal.fire({
            icon: 'question',
            title: '¿Iniciar surtido?',
            html: 'Se registrará la <strong>fecha y hora de inicio</strong>. Luego configure cantidades y guarde la verificación.',
            showCancelButton: true,
            confirmButtonText: '<i class="fas fa-play me-1"></i> Sí, iniciar',
            cancelButtonText: 'Cancelar',
            confirmButtonColor: '#059669'
        });

        if (!confirmResult.isConfirmed) return;

        const btn = document.getElementById('vi-ver-btn-iniciar-surtido');
        const orig = btn.innerHTML;
        btn.disabled = true;
        btn.innerHTML = '<i class="fas fa-spinner fa-spin"></i> Iniciando…';

        try {
            const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
            const formData = new FormData();
            formData.append('__RequestVerificationToken', token);
            formData.append('idEncabezadoPedido', idPedido);

            const resp = await fetch('/VIVerificacionAlmacen/IniciarSurtido', { method: 'POST', body: formData });
            const result = await resp.json();

            if (result.success) {
                this.surtidoIniciado = true;
                this._fechaInicioGuardada = result.fecha_inicio; // ← guardar para usarla al finalizar
                this.actualizarUIBotonesurtido({
                    iniciado: true,
                    finalizado: false,
                    fecha_inicio: result.fecha_inicio
                });
                // Mostrar botón Finalizar deshabilitado (esperando que se guarden todas las partidas)
                this._actualizarBotonFinalizar(false);
                this.habilitarBotones(!!this.idVerificacionGuardada);
                toastMixin.fire({ icon: 'success', title: `Surtido iniciado: ${result.fecha_inicio}` });
            } else {
                toastMixin.fire({ icon: 'error', title: result.message || 'Error al iniciar' });
                btn.disabled = false;
                btn.innerHTML = orig;
            }
        } catch (e) {
            toastMixin.fire({ icon: 'error', title: e.message || 'Error de red' });
            btn.disabled = false;
            btn.innerHTML = orig;
        }
    },

    // ── Finalizar surtido → AHORA crea el documento RMP ────────────────────────
    async finalizarSurtido() {
        const idPedido = document.getElementById('vi-ver-documentid').value;
        if (!idPedido) {
            toastMixin.fire({ icon: 'warning', title: 'No hay pedido seleccionado' });
            return;
        }

        // Verificación client-side: todas las partidas deben tener cantidad
        const conCero = this.partidas.filter(p => parseFloat(p.cantidad_verificada || 0) === 0);
        if (conCero.length > 0) {
            const confirm0 = await Swal.fire({
                icon: 'warning',
                title: 'Partidas con cantidad 0',
                html: `<strong>${conCero.length}</strong> partida(s) se guardarán con cantidad <strong>0</strong>.<br>
               ¿Desea continuar de todas formas?`,
                showCancelButton: true,
                confirmButtonText: 'Sí, continuar',
                cancelButtonText: 'Revisar',
                confirmButtonColor: '#d97706'
            });
            if (!confirm0.isConfirmed) return;
        }

        const confirmResult = await Swal.fire({
            icon: 'question',
            title: '¿Finalizar surtido?',
            html: 'Se generará el documento de verificación <strong>RMP</strong> con todas las partidas.<br>' +
                'Esta acción no se puede deshacer.',
            showCancelButton: true,
            confirmButtonText: '<i class="fas fa-flag-checkered me-1"></i> Sí, finalizar y generar',
            cancelButtonText: 'Cancelar',
            confirmButtonColor: '#1e3a5f'
        });
        if (!confirmResult.isConfirmed) return;

        const btn = document.getElementById('vi-ver-btn-finalizar-surtido');
        const orig = btn.innerHTML;
        btn.disabled = true;
        btn.innerHTML = '<i class="fas fa-spinner fa-spin"></i> Generando documento…';

        try {
            const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
            const formData = new FormData();
            formData.append('__RequestVerificationToken', token);
            formData.append('idEncabezadoPedido', idPedido);

            const resp = await fetch('/VIVerificacionAlmacen/FinalizarSurtido',
                { method: 'POST', body: formData });
            const result = await resp.json();

            if (result.success) {
                this.idVerificacionGuardada = result.id_verificacion;
                document.getElementById('vi-ver-id-verificacion').value = result.id_verificacion;

                this.surtidoFinalizado = true;
                this.actualizarUIBotonesurtido({
                    iniciado: true,
                    finalizado: true,
                    fecha_inicio: this._fechaInicioGuardada || '—',
                    fecha_fin: result.fecha_fin
                });

                // Habilitar impresión y generación de remisión
                this.habilitarBotones(true);

                await Swal.fire({
                    icon: 'success',
                    title: 'Surtido finalizado',
                    html: `Documento generado: <strong>${result.folio_generado}</strong><br>
                        Fin: ${result.fecha_fin}`,
                    confirmButtonText: 'Aceptar'
                });
            } else {
                toastMixin.fire({ icon: 'error', title: result.message || 'Error al finalizar' });
                btn.disabled = false;
                btn.innerHTML = orig;
            }
        } catch (e) {
            toastMixin.fire({ icon: 'error', title: e.message || 'Error de red' });
            btn.disabled = false;
            btn.innerHTML = orig;
        }
    },


    actualizarUIBotonesurtido(state) {
        const btnIniciar = document.getElementById('vi-ver-btn-iniciar-surtido');
        const btnFinalizar = document.getElementById('vi-ver-btn-finalizar-surtido');
        const infoSurtido = document.getElementById('vi-ver-info-surtido');
        if (!btnIniciar) return;

        if (state.finalizado) {
            btnIniciar.disabled = true;
            btnIniciar.innerHTML = '<i class="fas fa-check"></i> Surtido Iniciado';
            btnIniciar.className = btnIniciar.className.replace('vi-btn-primary', 'vi-btn-success');

            if (btnFinalizar) {
                btnFinalizar.disabled = true;
                btnFinalizar.innerHTML = '<i class="fas fa-flag-checkered"></i> Surtido Finalizado';
                btnFinalizar.className = btnFinalizar.className
                    .replace('vi-btn-amber', 'vi-btn-success').replace('d-none', '');
            }
            if (infoSurtido) {
                infoSurtido.innerHTML = `
                    <span class="vi-ver-badge ok"><i class="fas fa-clock"></i> Inicio: ${state.fecha_inicio || '—'}</span>
                    <span class="vi-ver-badge ok ms-2"><i class="fas fa-flag-checkered"></i> Fin: ${state.fecha_fin || '—'}</span>`;
                infoSurtido.classList.remove('d-none');
            }
        } else if (state.iniciado) {
            btnIniciar.disabled = true;
            btnIniciar.innerHTML = '<i class="fas fa-check"></i> Surtido Iniciado';
            btnIniciar.className = btnIniciar.className.replace('vi-btn-primary', 'vi-btn-success');
            if (btnFinalizar) { btnFinalizar.disabled = false; btnFinalizar.classList.remove('d-none'); }
            if (infoSurtido) {
                infoSurtido.innerHTML = `
                    <span class="vi-ver-badge parcial"><i class="fas fa-clock"></i> En surtido desde: ${state.fecha_inicio || '—'}</span>`;
                infoSurtido.classList.remove('d-none');
            }
        } else {
            btnIniciar.disabled = !this.pedidoActual && !document.getElementById('vi-ver-documentid').value;
            btnIniciar.className = btnIniciar.className.replace('vi-btn-success', 'vi-btn-primary');
            btnIniciar.innerHTML = '<i class="fas fa-play"></i> Iniciar Surtido';
            if (btnFinalizar) {
                btnFinalizar.classList.add('d-none');
                btnFinalizar.className = btnFinalizar.className.replace('vi-btn-success', 'vi-btn-amber');
                btnFinalizar.innerHTML = '<i class="fas fa-flag-checkered"></i> Finalizar Surtido';
            }
            if (infoSurtido) infoSurtido.classList.add('d-none');
        }
    },

    // ══════════════════════════════════════════════════════
    // DISCREPANCIA FÍSICA
    // ══════════════════════════════════════════════════════
    _idxDiscrepanciaActual: null,

    initModalDiscrepancia() {
        const btnGuardar = document.getElementById('vi-disc-btn-guardar');
        btnGuardar?.addEventListener('click', () => this.guardarDiscrepancia());
    },

    // ── REEMPLAZA el método abrirModalDiscrepancia completo ──────────────────
    abrirModalDiscrepancia(idx) {
        this._idxDiscrepanciaActual = idx;
        const p = this.partidas[idx];

        document.getElementById('vi-disc-producto').textContent = `${p.cve_prod} — ${p.descripcion}`;
        document.getElementById('vi-disc-solicitado').textContent = parseFloat(p.cantidad_pedida).toFixed(2);
        document.getElementById('vi-disc-sistema').textContent = parseFloat(p.existencia_stock || 0).toFixed(2);

        const inputCant = document.getElementById('vi-disc-cantidad-fisica');
        const inputMotiv = document.getElementById('vi-disc-motivo');
        const alerta = document.getElementById('vi-disc-alerta');

        // ── Pre-cargar desde la partida: si ya tiene discrepancia guardada,
        //    usar esa; si no, tomar lo que haya en el input de cantidad verificada;
        //    si tampoco hay nada, poner 0.
        if (p.tiene_discrepancia) {
            inputCant.value = p.cantidad_fisica;
        } else {
            const qtyInput = document.getElementById(`vi-ver-qty-${idx}`);
            const qtyActual = parseFloat(qtyInput?.value || 0);
            inputCant.value = qtyActual > 0 ? qtyActual : 0;
        }
        inputMotiv.value = p.tiene_discrepancia ? (p.motivo_discrepancia || '') : '';
        alerta.classList.add('d-none');

        // Calcular diferencia en tiempo real
        const calcDiff = () => {
            const fis = parseFloat(inputCant.value) || 0;
            const ped = parseFloat(p.cantidad_pedida) || 0;
            const diff = ped - fis;
            if (diff > 0) {
                alerta.classList.remove('d-none');
                document.getElementById('vi-disc-diferencia').textContent = diff.toFixed(2);
            } else {
                alerta.classList.add('d-none');
            }
        };
        inputCant.oninput = calcDiff;
        calcDiff(); // evaluar inmediatamente con el valor pre-cargado

        // ── Al cerrar el modal sin guardar, reflejar la cantidad física
        //    de vuelta en el input de cantidad verificada (solo si no tiene
        //    discrepancia ya guardada, para no pisar datos confirmados)
        const modalEl = document.getElementById('vi-disc-modal');
        const onHide = () => {
            const cantFisicaIngresada = parseFloat(inputCant.value) || 0;
            const qtyInput = document.getElementById(`vi-ver-qty-${idx}`);
            if (qtyInput && !p.tiene_discrepancia) {
                // Solo sincronizar si la cantidad física es distinta de 0
                // y el usuario realmente ingresó algo
                if (cantFisicaIngresada > 0) {
                    qtyInput.value = cantFisicaIngresada;
                    this.onCantidadChange(idx, qtyInput);
                }
            }
            modalEl.removeEventListener('hidden.bs.modal', onHide);
        };
        modalEl.addEventListener('hidden.bs.modal', onHide);

        bootstrap.Modal.getOrCreateInstance(modalEl).show();
    },

    async guardarDiscrepancia() {
        const idx = this._idxDiscrepanciaActual;
        if (idx === null || idx === undefined) return;

        const p = this.partidas[idx];
        const idPartida = p.id_partida;
        const idPedido = document.getElementById('vi-ver-documentid').value;
        const cantFisica = parseFloat(document.getElementById('vi-disc-cantidad-fisica').value);
        const motivo = document.getElementById('vi-disc-motivo').value.trim();

        if (isNaN(cantFisica) || cantFisica < 0) {
            toastMixin.fire({ icon: 'warning', title: 'Ingrese una cantidad física válida (≥ 0)' });
            return;
        }
        if (!motivo) {
            toastMixin.fire({ icon: 'warning', title: 'Debe indicar el motivo de la discrepancia' });
            return;
        }

        const btn = document.getElementById('vi-disc-btn-guardar');
        const orig = btn.innerHTML;
        btn.disabled = true;
        btn.innerHTML = '<i class="fas fa-spinner fa-spin"></i> Guardando…';

        // ── REEMPLAZA solo el bloque try de guardarDiscrepancia ─────────────────
        try {
            const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
            const formData = new FormData();
            formData.append('__RequestVerificationToken', token);
            formData.append('idPartida', idPartida);
            formData.append('idEncabezadoPedido', idPedido);
            formData.append('cantidadFisica', cantFisica);
            formData.append('motivo', motivo);

            // ← ahora apunta al método que también genera el DISM
            const resp = await fetch('/VIVerificacionAlmacen/GuardarDiscrepanciaConDocumento',
                { method: 'POST', body: formData });
            const result = await resp.json();

            if (result.success) {
                this.partidas[idx].tiene_discrepancia = true;
                this.partidas[idx].cantidad_fisica = cantFisica;
                this.partidas[idx].motivo_discrepancia = motivo;

                // Sincronizar el input de cantidad verificada con la cantidad física confirmada
                const qtyInput = document.getElementById(`vi-ver-qty-${idx}`);
                if (qtyInput) {
                    qtyInput.value = cantFisica;
                    this.onCantidadChange(idx, qtyInput);
                }

                bootstrap.Modal.getInstance(document.getElementById('vi-disc-modal'))?.hide();
                this.renderTabla();

                // Mensaje diferenciado según si se generó DISM o no
                const titulo = result.folio_dism
                    ? `Discrepancia registrada — Documento DISM: ${result.folio_dism}`
                    : `Discrepancia registrada. Físico: ${cantFisica} / Sistema: ${result.cantidad_sistema}`;

                toastMixin.fire({ icon: 'success', title: titulo });
            } else {
                toastMixin.fire({ icon: 'error', title: result.message || 'Error al guardar' });
            }
        } catch (e) {
            toastMixin.fire({ icon: 'error', title: e.message || 'Error de red' });
        } finally {
            btn.disabled = false;
            btn.innerHTML = orig;
        }
    }
};

// ============================================================
// FUNCIONES GLOBALES
// ============================================================
function verificarTodoPedido() { VerificacionApp.verificarTodo(); }
function imprimirEtiquetas() { VerificacionApp.imprimirEtiquetas(); }

async function generarRemisionDesdeVerificacion() {
    const idVer = VerificacionApp.idVerificacionGuardada
        || document.getElementById('vi-ver-id-verificacion').value;
    if (!idVer) {
        toastMixin.fire({ icon: 'warning', title: 'Primero debe guardar la verificación' });
        return;
    }
    const confirmResult = await Swal.fire({
        icon: 'question',
        title: '¿Generar remisión?',
        text: 'Se abrirá el módulo de Remisión con los datos de esta verificación precargados.',
        showCancelButton: true,
        confirmButtonText: '<i class="fas fa-truck me-1"></i> Sí, generar remisión',
        cancelButtonText: 'Cancelar'
    });
    if (!confirmResult.isConfirmed) return;
    window.location.href = `/VIRemision/Index?fromVerificacion=${idVer}`;
}

// ============================================================
// HELPERS
// ============================================================
function calcularEstado(p) {
    const ver = parseFloat(p.cantidad_verificada || 0);
    const ped = parseFloat(p.cantidad_pedida || 0);
    if (ver <= 0) return 'pendiente';
    if (ver >= ped) return 'ok';
    return 'parcial';
}

function badgeEstado(estado) {
    const map = {
        ok: `<span class="vi-ver-badge ok"><i class="fas fa-check-circle"></i> Completo</span>`,
        parcial: `<span class="vi-ver-badge parcial"><i class="fas fa-exclamation-circle"></i> Parcial</span>`,
        pendiente: `<span class="vi-ver-badge pendiente"><i class="fas fa-clock"></i> Pendiente</span>`
    };
    return map[estado] || map.pendiente;
}

function renderPaginacion(fromId, toId, totalId, paginationId, page, pageSize, totalRecords, onPage) {
    if (document.getElementById(fromId)) document.getElementById(fromId).textContent = ((page - 1) * pageSize) + 1;
    if (document.getElementById(toId)) document.getElementById(toId).textContent = Math.min(page * pageSize, totalRecords);
    if (document.getElementById(totalId)) document.getElementById(totalId).textContent = totalRecords;

    const pag = document.getElementById(paginationId);
    if (!pag) return;
    const total = Math.ceil(totalRecords / pageSize);
    if (total <= 1) { pag.innerHTML = ''; return; }

    let html = `<li class="page-item${page === 1 ? ' disabled' : ''}">
        <button class="page-link" data-p="${page - 1}">Anterior</button></li>`;
    const start = Math.max(1, page - 2), end = Math.min(total, page + 2);
    for (let i = start; i <= end; i++)
        html += `<li class="page-item${i === page ? ' active' : ''}">
            <button class="page-link" data-p="${i}">${i}</button></li>`;
    html += `<li class="page-item${page === total ? ' disabled' : ''}">
        <button class="page-link" data-p="${page + 1}">Siguiente</button></li>`;

    pag.innerHTML = html;
    pag.querySelectorAll('[data-p]').forEach(btn =>
        btn.addEventListener('click', () => onPage(+btn.dataset.p)));
}

function escHtml(text) {
    const d = document.createElement('div');
    d.textContent = String(text ?? '');
    return d.innerHTML;
}

function formatFecha(val) {
    if (!val) return '—';
    try { return new Date(val).toLocaleDateString('es-MX'); }
    catch { return val; }
}

// ============================================================
// SISTEMA DE ETIQUETAS ZPL — Verificación de Almacén
// ============================================================

const EtiquetasVerificacion = {

    // ── Inicializar impresora Zebra ──────────────────────────
    init() {
        BrowserPrint.getDefaultDevice('printer', function (printer) {
            console.log('Impresora detectada:', printer.name);
        }, function () {
            console.warn('No se encontró impresora Zebra');
        });
    },

    // ── Enviar ZPL a la impresora ────────────────────────────
    imprimir(zpl) {
        BrowserPrint.getDefaultDevice('printer', function (printer) {
            printer.send(
                zpl,
                () => console.log('Impresión enviada'),
                err => {
                    console.error('Error al imprimir:', err);
                    toastMixin.fire({ icon: 'error', title: 'Error al enviar a la impresora Zebra' });
                }
            );
        }, function () {
            toastMixin.fire({ icon: 'error', title: 'No se encontró impresora Zebra conectada' });
        });
    },

    // ============================================================
    // NIVEL 1 — Etiqueta por partida individual
    // ============================================================

    // Genera ZPL para UNA partida
    // p        → objeto partida de VerificacionApp.partidas
    // folio    → string folio del pedido
    // Returns  → string ZPL
    generarEtiquetaPartida(p, folio) {
        const fecha = new Date().toLocaleDateString('es-MX', {
            day: '2-digit', month: '2-digit', year: '2-digit'
        });

        const sku = (p.cve_prod || 'N/A').substring(0, 30);
        const desc = (p.descripcion || 'N/A').substring(0, 45);
        const cantPedida = parseFloat(p.cantidad_pedida || 0).toFixed(2);
        const cantVerif = parseFloat(p.cantidad_verificada || 0).toFixed(2);
        const unidad = p.unidad || 'PZA';
        const folioStr = (folio || 'N/A').substring(0, 30);

        // Estado visual en ZPL
        const esCompleto = parseFloat(cantVerif) >= parseFloat(cantPedida) && parseFloat(cantVerif) > 0;
        const esParcial = parseFloat(cantVerif) > 0 && !esCompleto;
        const estadoLabel = esCompleto ? 'COMPLETO' : (esParcial ? 'PARCIAL' : 'PENDIENTE');

        return `^XA
^CI28
~SD15
^PW800
^LL600

^CF0,22
^FO55,30^FDSELLOS Y RETENES DE SAN LUIS^FS
^CF0,16
^FO55,55^FDAv. Periferico oriente 200, Villa de Pozos^FS

^FO50,78^GB700,2,2^FS

^CF0,38
^FO55,88^FB690,1,0,L^FD${sku}^FS

^FO50,125^GB700,2,2^FS

^CF0,18
^FO55,135^FB690,2,0,L^FD${desc}^FS

^FO50,175^GB700,2,2^FS

^FO55,185^GB215,90,2^FS
^CF0,13
^FO65,197^FDCANTIDAD PEDIDA^FS
^CF0,30
^FO65,218^FD${cantPedida} ${unidad}^FS

^FO280,185^GB215,90,2^FS
^CF0,13
^FO290,197^FDCANT. VERIFICADA^FS

^FO505,185^GB245,90,2^FS
^CF0,13
^FO515,197^FDESTADO^FS
^CF0,22
^FO515,218^FD${estadoLabel}^FS

^FO50,280^GB700,2,2^FS

^CF0,13
^FO55,292^FDFOLIO PEDIDO^FS
^CF0,22
^FO55,310^FD${folioStr}^FS

^CF0,13
^FO450,292^FDFECHA^FS
^CF0,22
^FO450,310^FD${fecha}^FS

^FO50,340^GB700,2,2^FS

^BY2,3,70
^FO255,350^BCN,70,Y,N,N^FD${sku}^FS

^XZ`;
    },

    // Imprimir etiqueta de una partida individual con vista previa opcional
    imprimirPartida(idx, conPreview = false) {
        const p = VerificacionApp.partidas[idx];
        const folio = document.getElementById('vi-ver-folio-pedido')?.value || '';

        if (!p) {
            toastMixin.fire({ icon: 'warning', title: 'Partida no encontrada' });
            return;
        }

        const zpl = this.generarEtiquetaPartida(p, folio);

        if (conPreview) {
            this._mostrarPreview(zpl, `Partida: ${p.cve_prod}`, () => this.imprimir(zpl));
        } else {
            this.imprimir(zpl);
            toastMixin.fire({ icon: 'success', title: `Etiqueta enviada: ${p.cve_prod}`, timer: 1500, showConfirmButton: false });
        }
    },

    // ============================================================
    // NIVEL 2 — Todas las partidas de la verificación actual
    // ============================================================

    async imprimirTodas() {
        const partidas = VerificacionApp.partidas;
        const folio = document.getElementById('vi-ver-folio-pedido')?.value || '';

        if (!partidas.length) {
            toastMixin.fire({ icon: 'warning', title: 'No hay partidas cargadas' });
            return;
        }

        // Preguntar qué partidas incluir
        const { value: filtro } = await Swal.fire({
            title: '🏷️ Imprimir etiquetas',
            html: `
                <p style="margin-bottom:12px">
                    Hay <strong>${partidas.length}</strong> partida(s) cargadas.
                    ¿Cuáles deseas imprimir?
                </p>
                <select id="swal-filtro" class="swal2-input" style="width:80%">
                    <option value="todas">Todas las partidas</option>
                    <option value="completas">Solo completas</option>
                    <option value="pendientes">Solo pendientes / parciales</option>
                </select>`,
            showCancelButton: true,
            confirmButtonText: '🖨️ Imprimir',
            confirmButtonColor: '#1e3a5f',
            cancelButtonText: 'Cancelar',
            preConfirm: () => document.getElementById('swal-filtro').value
        });

        if (!filtro) return;

        const aImprimir = partidas.filter((p, idx) => {
            const estado = calcularEstado(p);
            if (filtro === 'completas') return estado === 'ok';
            if (filtro === 'pendientes') return estado !== 'ok';
            return true;
        });

        if (!aImprimir.length) {
            toastMixin.fire({ icon: 'warning', title: 'No hay partidas que coincidan con el filtro' });
            return;
        }

        const confirmResult = await Swal.fire({
            icon: 'question',
            title: '¿Confirmar impresión?',
            html: `Se imprimirán <strong>${aImprimir.length}</strong> etiqueta(s)`,
            showCancelButton: true,
            confirmButtonText: '🖨️ Sí, imprimir',
            confirmButtonColor: '#059669',
            cancelButtonText: 'Cancelar'
        });

        if (!confirmResult.isConfirmed) return;

        Swal.fire({
            title: '⏳ Imprimiendo...',
            html: 'Enviando etiquetas a la impresora Zebra',
            allowOutsideClick: false,
            didOpen: () => Swal.showLoading()
        });

        let delay = 0;
        aImprimir.forEach(p => {
            setTimeout(() => {
                const zpl = this.generarEtiquetaPartida(p, folio);
                this.imprimir(zpl);
            }, delay);
            delay += 1800;
        });

        setTimeout(() => {
            Swal.fire({
                icon: 'success',
                title: 'Impresión completada',
                text: `${aImprimir.length} etiqueta(s) enviadas`,
                timer: 2500,
                showConfirmButton: false
            });
        }, delay);
    },

    // ── Vista previa del ZPL ────────────────────────────────
    _mostrarPreview(zpl, titulo, onImprimir) {
        Swal.fire({
            title: `👁️ Vista Previa — ${titulo}`,
            html: `
                <div style="text-align:left">
                    <p style="font-size:13px;color:#555;margin-bottom:8px">
                        Código ZPL que se enviará a la impresora:
                    </p>
                    <textarea readonly
                        style="width:100%;height:260px;font-family:'Courier New',monospace;
                               font-size:10px;padding:8px;border:1px solid #ddd;
                               border-radius:4px;background:#fafafa;resize:vertical"
                    >${zpl}</textarea>
                    <div style="margin-top:10px;padding:10px;background:#f0fdf4;
                                border-left:4px solid #22c55e;border-radius:4px">
                        <p style="margin:0;font-size:12px;color:#15803d">
                            💡 Prueba el código en
                            <a href="http://labelary.com/viewer.html" target="_blank"
                               style="color:#16a34a;font-weight:600">Labelary Viewer</a>
                        </p>
                    </div>
                </div>`,
            width: '750px',
            showCancelButton: true,
            showDenyButton: true,
            confirmButtonText: '🖨️ Imprimir Ahora',
            confirmButtonColor: '#0891b2',
            cancelButtonText: '📋 Copiar ZPL',
            cancelButtonColor: '#6b7280',
            denyButtonText: '❌ Cerrar',
            denyButtonColor: '#ef4444'
        }).then(result => {
            if (result.isConfirmed) {
                onImprimir();
            } else if (result.dismiss === Swal.DismissReason.cancel) {
                navigator.clipboard.writeText(zpl).then(() =>
                    toastMixin.fire({ icon: 'success', title: 'ZPL copiado al portapapeles', timer: 1500, showConfirmButton: false })
                );
            }
        });
    }
};

// ============================================================
// ARRANQUE
// ============================================================
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => {
        VerificacionApp.init();
        EtiquetasVerificacion.init(); 
    });
} else {
    VerificacionApp.init();
    EtiquetasVerificacion.init();  
}
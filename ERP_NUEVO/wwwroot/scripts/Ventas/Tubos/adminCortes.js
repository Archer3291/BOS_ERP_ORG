const AdminCortesApp = {
    activeTab: 'taller',
    disp: { page: 1, pageSize: 50, busqueda: '', longitudMin: '', longitudMax: '', productoId: '', totalRecords: 0, items: [] },
    usa: { page: 1, pageSize: 50, busqueda: '', fechaDesde: '', fechaHasta: '', estatus: '', totalRecords: 0, items: [] },
    typingTimerDisp: null,
    typingTimerUsa: null,
    _piezaDividir: null,
    _piezaEditar: null,
    _typingTimerProducto: null,
    _resultadosProducto: [],

    init() {
        // ── Tabs ──
        document.querySelectorAll('#ac-tabs .nav-link').forEach(btn => {
            btn.addEventListener('click', () => this.cambiarTab(btn.dataset.tab));
        });

        // ── Disponibles: filtros ──
        document.getElementById('ac-disp-busqueda')?.addEventListener('input', (e) => this.handleFiltroDisp(e.target.value));
        document.getElementById('ac-disp-longMin')?.addEventListener('input', () => this.handleFiltroDispInmediato());
        document.getElementById('ac-disp-longMax')?.addEventListener('input', () => this.handleFiltroDispInmediato());
        document.getElementById('ac-disp-producto')?.addEventListener('input', () => this.handleFiltroDispInmediato());
        document.getElementById('ac-disp-btnLimpiar')?.addEventListener('click', () => this.limpiarFiltrosDisp());
        document.getElementById('ac-disp-pageSize')?.addEventListener('change', (e) => {
            this.disp.pageSize = parseInt(e.target.value); this.disp.page = 1; this.cargarDisponibles();
        });
        document.getElementById('ac-disp-pagination')?.addEventListener('click', (e) => {
            e.preventDefault();
            const btn = e.target.closest('[data-page]');
            if (btn && !btn.closest('.disabled')) { this.disp.page = parseInt(btn.dataset.page); this.cargarDisponibles(); }
        });
        document.getElementById('ac-disp-tbody')?.addEventListener('click', (e) => {
            const btnExpandir = e.target.closest('.ac-btn-expandir');
            if (btnExpandir) { this.toggleDetallePiezas(btnExpandir); return; }
            const btnEtiquetas = e.target.closest('.ac-btn-etiquetas');
            if (btnEtiquetas) { window.EtiquetasBarras?.imprimir({ idCorte: btnEtiquetas.dataset.idCorte }); return; }
            const btnEditar = e.target.closest('.ac-btn-editar-pieza');
            if (btnEditar) {
                const item = this.disp.items.find(it => String(it.id_corte) === String(btnEditar.dataset.idCorte));
                if (item) this.abrirModalEditar(item);
                return;
            }
            const btnDividir = e.target.closest('.ac-btn-dividir');
            if (btnDividir) { this.abrirModalDividir(btnDividir.dataset); return; }
            const btnDesactivar = e.target.closest('.ac-btn-desactivar');
            if (btnDesactivar) { this.desactivarPieza(btnDesactivar.dataset.idCorte); return; }
        });

        // Etiquetas del producto que se esté filtrando en Disponibles
        document.getElementById('ac-disp-btnEtiquetas')?.addEventListener('click', () => {
            const prod = (document.getElementById('ac-disp-producto')?.value || '').trim();
            if (!prod) {
                toastMixin?.fire({
                    icon: 'info',
                    title: 'Indica una clave de producto',
                    text: 'Se imprimirán las etiquetas de todas sus barras disponibles.',
                });
                document.getElementById('ac-disp-producto')?.focus();
                return;
            }
            window.EtiquetasBarras?.imprimir({ productoId: prod });
        });

        // Etiquetas desde el desglose de un producto
        document.getElementById('ac-desglose-piezas')?.addEventListener('click', (e) => {
            const btn = e.target.closest('.ac-btn-etiquetas');
            if (btn) window.EtiquetasBarras?.imprimir({ idCorte: btn.dataset.idCorte });
        });

        document.getElementById('ac-btnEtiquetasDesglose')?.addEventListener('click', () => {
            const prod = (document.getElementById('ac-crear-productoId')?.value || '').trim();
            if (prod) window.EtiquetasBarras?.imprimir({ productoId: prod });
        });
        document.getElementById('ac-usa-tbody')?.addEventListener('click', (e) => {
            const btn = e.target.closest('.ac-link-pedido');
            if (btn) this.verDetallePedido(btn.dataset.idEncabezado);
        });
        // ── Usados: filtros ──
        document.getElementById('ac-usa-busqueda')?.addEventListener('input', (e) => this.handleFiltroUsa(e.target.value));
        document.getElementById('ac-usa-fechaDesde')?.addEventListener('change', () => this.handleFiltroUsaInmediato());
        document.getElementById('ac-usa-fechaHasta')?.addEventListener('change', () => this.handleFiltroUsaInmediato());
        document.getElementById('ac-usa-estatus')?.addEventListener('change', () => this.handleFiltroUsaInmediato());
        document.getElementById('ac-usa-btnLimpiar')?.addEventListener('click', () => this.limpiarFiltrosUsa());
        document.getElementById('ac-usa-pageSize')?.addEventListener('change', (e) => {
            this.usa.pageSize = parseInt(e.target.value); this.usa.page = 1; this.cargarUsados();
        });
        document.getElementById('ac-usa-pagination')?.addEventListener('click', (e) => {
            e.preventDefault();
            const btn = e.target.closest('[data-page]');
            if (btn && !btn.closest('.disabled')) { this.usa.page = parseInt(btn.dataset.page); this.cargarUsados(); }
        });
        document.getElementById('ac-usa-btnExportarExcel')?.addEventListener('click', () => this.exportarUsadosExcel());
        document.getElementById('ac-usa-btnExportarPdf')?.addEventListener('click', () => this.exportarUsadosPdf());

        // ── Modal Crear (sin cambios de lógica) ──
        document.getElementById('ac-btnVerDesglose')?.addEventListener('click', () => this.verDesglose());
        document.getElementById('ac-btnGuardarCrear')?.addEventListener('click', () => this.guardarCrear());
        ['ac-crear-longitud', 'ac-crear-cantidad'].forEach(id => {
            document.getElementById(id)?.addEventListener('input', () => this.actualizarPreviewMetros());
        });
        document.getElementById('ac-modalCrear')?.addEventListener('hidden.bs.modal', () => this.resetModalCrear());

        // ── Autocompletado de producto (modal Crear) ──
        document.getElementById('ac-crear-productoId')?.addEventListener('input', (e) => this.handleBuscarProducto(e.target.value));
        document.getElementById('ac-crear-producto-resultados')?.addEventListener('click', (e) => {
            const item = e.target.closest('.ac-autocomplete-item');
            if (!item) return;
            const producto = this._resultadosProducto?.[Number(item.dataset.idx)];
            if (producto) this.seleccionarProducto(producto.cve_prod);
        });
        document.addEventListener('click', (e) => {
            if (!e.target.closest('.ac-autocomplete')) this.ocultarResultadosProducto();
        });

        // ── Modal Editar ──
        document.getElementById('ac-btnGuardarEditar')?.addEventListener('click', () => this.guardarEdicion());
        document.getElementById('ac-modalEditarPieza')?.addEventListener('hidden.bs.modal', () => { this._piezaEditar = null; });

        // ── Modal Dividir ──
        document.getElementById('ac-btnAgregarSubpieza')?.addEventListener('click', () => this.agregarSubpiezaRow());
        document.getElementById('ac-btnGuardarDividir')?.addEventListener('click', () => this.guardarDivision());
        document.getElementById('ac-div-lista')?.addEventListener('input', (e) => {
            if (e.target.classList.contains('ac-div-input')) this.recalcularResumenDivision();
        });
        document.getElementById('ac-div-lista')?.addEventListener('click', (e) => {
            const btn = e.target.closest('.ac-div-btn-eliminar');
            if (btn) { btn.closest('.vt-corte-card')?.remove(); this.recalcularResumenDivision(); }
        });
        document.getElementById('ac-modalDividir')?.addEventListener('hidden.bs.modal', () => {
            document.getElementById('ac-div-lista').innerHTML = '';
            this._piezaDividir = null;
        });

        this.cargarDisponibles();
        this.cargarUsados();
    },

    cambiarTab(tab) {
        this.activeTab = tab;
        document.querySelectorAll('#ac-tabs .nav-link').forEach(b => b.classList.toggle('active', b.dataset.tab === tab));
        document.getElementById('ac-panel-taller')?.classList.toggle('d-none', tab !== 'taller');
        document.getElementById('ac-panel-disponibles').classList.toggle('d-none', tab !== 'disponibles');
        document.getElementById('ac-panel-usados').classList.toggle('d-none', tab !== 'usados');

        // El bucle de render se para al salir del taller: con el contenedor
        // oculto mide 0 px y no tiene sentido seguir dibujando.
        if (tab === 'taller') window.Barras3D?.mount();
        else window.Barras3D?.unmount();
    },

    // ============================================================
    // DISPONIBLES
    // ============================================================
    handleFiltroDisp(value) {
        clearTimeout(this.typingTimerDisp);
        this.typingTimerDisp = setTimeout(() => {
            this.disp.busqueda = value; this.disp.page = 1; this.cargarDisponibles();
        }, 300);
    },
    handleFiltroDispInmediato() {
        clearTimeout(this.typingTimerDisp);
        this.typingTimerDisp = setTimeout(() => {
            this.disp.longitudMin = document.getElementById('ac-disp-longMin')?.value || '';
            this.disp.longitudMax = document.getElementById('ac-disp-longMax')?.value || '';
            this.disp.productoId = document.getElementById('ac-disp-producto')?.value || '';
            this.disp.page = 1;
            this.cargarDisponibles();
        }, 300);
    },
    limpiarFiltrosDisp() {
        ['ac-disp-busqueda', 'ac-disp-longMin', 'ac-disp-longMax', 'ac-disp-producto'].forEach(id => {
            const el = document.getElementById(id); if (el) el.value = '';
        });
        Object.assign(this.disp, { busqueda: '', longitudMin: '', longitudMax: '', productoId: '', page: 1 });
        this.cargarDisponibles();
    },

    async cargarDisponibles() {
        const tbody = document.getElementById('ac-disp-tbody');
        try {
            const params = new URLSearchParams({
                busqueda: this.disp.busqueda || '', page: this.disp.page, pageSize: this.disp.pageSize
            });
            if (this.disp.longitudMin) params.append('longitudMin', this.disp.longitudMin);
            if (this.disp.longitudMax) params.append('longitudMax', this.disp.longitudMax);
            if (this.disp.productoId) params.append('productoId', this.disp.productoId);

            const resp = await fetch(`/AdminCortes/ListarDisponibles?${params.toString()}`);
            const data = await resp.json();
            if (data.success === false) {
                toastMixin?.fire({ icon: 'error', title: data.message || 'Error al cargar' });
                return;
            }
            this.disp.totalRecords = data.total || 0;
            this.disp.items = data.items || [];
            this.renderDisponibles(this.disp.items);
            this.renderContadores('disp');
            this.renderPaginacion('disp');
        } catch (err) {
            console.error(err);
            tbody.innerHTML = `<tr><td colspan="9" class="ac-error-state">Error de conexión al listar piezas disponibles</td></tr>`;
        }
    },

    renderDisponibles(items) {
        const tbody = document.getElementById('ac-disp-tbody');
        if (!items.length) {
            tbody.innerHTML = `<tr><td colspan="9" class="ac-empty-state"><i class="fas fa-inbox fa-2x mb-2 d-block"></i>Sin piezas disponibles con estos filtros</td></tr>`;
            return;
        }
        tbody.innerHTML = items.map(it => {
            const producto = `${it.producto_id} - ${it.producto_descripcion}`;
            return `
            <tr>
                <td>
                    <button type="button" class="ac-btn-expandir" data-id-corte="${it.id_corte}" aria-expanded="false"
                            title="Ver las barras físicas de este corte">
                        <i class="fas fa-chevron-right"></i>
                        <span class="fw-semibold">${this.escapeHtml(it.folio)}</span>
                    </button>
                    ${it.es_sobrante ? '<span class="ac-badge-sobrante" title="Remanente sin desglosar de este producto">Sobrante</span>' : ''}
                </td>
                <td>
                    <div class="fw-semibold">${this.escapeHtml(it.producto_id)}</div>
                    <div class="small text-muted">${this.escapeHtml(it.producto_descripcion)}</div>
                </td>
                <td>${parseFloat(it.longitud).toFixed(2)} m</td>
                <td><span class="ac-badge-activo">${it.cantidad}</span></td>
                <td class="text-muted">${it.cantidad_original}</td>
                <td class="small">${this.escapeHtml(it.usuario_creacion || '')}</td>
                <td class="small">${it.fecha_creacion ? new Date(it.fecha_creacion).toLocaleDateString('es-MX') : ''}</td>
                <td class="small">${this.escapeHtml(it.comentario || '')}</td>
                <td class="ac-col-acciones">
                    <button type="button" class="ac-btn-accion ac-btn-etiquetas"
                            data-id-corte="${it.id_corte}"
                            title="Imprimir etiquetas de las barras de este corte">
                        <i class="fas fa-barcode"></i>
                    </button>
                    <button type="button" class="ac-btn-accion ac-btn-editar ac-btn-editar-pieza"
                            data-id-corte="${it.id_corte}"
                            title="Editar cantidad y comentario">
                        <i class="fas fa-pen"></i>
                    </button>
                    <button type="button" class="ac-btn-accion ac-btn-editar ac-btn-dividir"
                            data-id-corte="${it.id_corte}" data-folio="${this.escapeHtml(it.folio)}"
                            data-longitud="${it.longitud}" data-producto="${this.escapeHtml(producto)}"
                            title="Dividir en subpiezas">
                        <i class="fas fa-cut"></i>
                    </button>
                    <button type="button" class="ac-btn-accion ac-btn-eliminar ac-btn-desactivar"
                            data-id-corte="${it.id_corte}" title="Desactivar">
                        <i class="fas fa-ban"></i>
                    </button>
                </td>
            </tr>
            <tr class="ac-fila-detalle d-none" data-detalle-de="${it.id_corte}">
                <td colspan="9">
                    <div class="ac-piezas-detalle" id="ac-piezas-detalle-${it.id_corte}"></div>
                </td>
            </tr>`;
        }).join('');
    },

    // ============================================================
    // DISPONIBLES: fila expandible con las barras físicas de un corte
    // ============================================================
    async toggleDetallePiezas(btn) {
        const idCorte = btn.dataset.idCorte;
        const fila = document.querySelector(`tr[data-detalle-de="${idCorte}"]`);
        const icono = btn.querySelector('i');
        if (!fila) return;

        const abierta = !fila.classList.contains('d-none');
        if (abierta) {
            fila.classList.add('d-none');
            btn.setAttribute('aria-expanded', 'false');
            if (icono) icono.className = 'fas fa-chevron-right';
            return;
        }

        fila.classList.remove('d-none');
        btn.setAttribute('aria-expanded', 'true');
        if (icono) icono.className = 'fas fa-chevron-down';

        const cont = document.getElementById(`ac-piezas-detalle-${idCorte}`);
        if (!cont || cont.dataset.cargado === '1') return;

        cont.innerHTML = `<div class="ac-vacio"><i class="fas fa-spinner fa-spin"></i> Cargando piezas…</div>`;
        try {
            const resp = await fetch(`/AdminCortes/ListarPiezas?idCorte=${idCorte}`);
            const data = await resp.json();
            if (!data.ok) {
                cont.innerHTML = `<div class="ac-error-state">${this.escapeHtml(data.message || 'No se pudieron cargar las piezas')}</div>`;
                return;
            }
            this.renderPiezasDetalle(cont, data.piezas || []);
            cont.dataset.cargado = '1';
        } catch (err) {
            console.error(err);
            cont.innerHTML = `<div class="ac-error-state">Error de conexión al listar las piezas</div>`;
        }
    },

    renderPiezasDetalle(cont, piezas) {
        if (!piezas.length) {
            cont.innerHTML = `<div class="ac-vacio">Sin barras físicas registradas para este corte todavía.</div>`;
            return;
        }
        cont.innerHTML = `
            <table class="ac-desglose-table">
                <thead>
                    <tr>
                        <th>Código</th><th>Estado</th><th>Creada</th><th>Origen</th><th>Comentario</th>
                    </tr>
                </thead>
                <tbody>
                    ${piezas.map(p => `
                        <tr>
                            <td><code>${this.escapeHtml(p.codigo)}</code></td>
                            <td>${p.estado === 'disponible'
                                ? '<span class="ac-badge-activo">Disponible</span>'
                                : `<span class="ac-badge-inactivo">${this.escapeHtml(p.estado)}</span>`}</td>
                            <td class="small">${p.fecha_creacion ? new Date(p.fecha_creacion).toLocaleDateString('es-MX') : '—'}</td>
                            <td class="small">${this.escapeHtml(p.codigo_madre || '—')}</td>
                            <td class="small">${this.escapeHtml(p.comentario || '')}</td>
                        </tr>`).join('')}
                </tbody>
            </table>`;
    },

    // ============================================================
    // MODAL EDITAR PIEZA (cantidad / comentario de un corte existente)
    // ============================================================
    /**
     * @param {{id_corte, folio, producto_id, producto_descripcion, longitud, cantidad, comentario}} item
     * Recibe el objeto ya armado (no un dataset) para poder abrirse tanto
     * desde la fila de Disponibles como desde el panel del Taller 3D, sin
     * tener que reconstruir comentarios de texto libre en atributos HTML.
     */
    abrirModalEditar(item) {
        if (!item) return;

        this._piezaEditar = { idCorte: item.id_corte };
        document.getElementById('ac-edit-folio').textContent = item.folio;
        document.getElementById('ac-edit-producto').textContent = `${item.producto_id} - ${item.producto_descripcion}`;
        document.getElementById('ac-edit-longitud').textContent = (parseFloat(item.longitud) || 0).toFixed(2);
        document.getElementById('ac-edit-cantidad').value = item.cantidad;
        document.getElementById('ac-edit-comentario').value = item.comentario || '';
        bootstrap.Modal.getOrCreateInstance(document.getElementById('ac-modalEditarPieza')).show();
    },

    async guardarEdicion() {
        if (!this._piezaEditar) return;
        const cantidad = document.getElementById('ac-edit-cantidad')?.value;
        const comentario = document.getElementById('ac-edit-comentario')?.value || '';

        if (cantidad === '' || parseFloat(cantidad) < 0) {
            toastMixin?.fire({ icon: 'warning', title: 'Indica una cantidad válida' });
            return;
        }

        try {
            const formData = new FormData();
            const csrfToken = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
            if (csrfToken) formData.append('__RequestVerificationToken', csrfToken);
            formData.append('idCorte', this._piezaEditar.idCorte);
            formData.append('cantidad', cantidad);
            formData.append('comentario', comentario);

            const resp = await fetch('/AdminCortes/Editar', { method: 'POST', body: formData });
            const result = await resp.json();

            if (result.success) {
                toastMixin?.fire({ icon: 'success', title: 'Pieza actualizada' });
                bootstrap.Modal.getInstance(document.getElementById('ac-modalEditarPieza'))?.hide();
                this.cargarDisponibles();
                this.refrescarTaller();
            } else {
                toastMixin?.fire({ icon: 'error', title: result.message || 'No se pudo actualizar' });
            }
        } catch (err) {
            console.error(err);
            toastMixin?.fire({ icon: 'error', title: 'Error de conexión' });
        }
    },

    // ============================================================
    // USADOS
    // ============================================================
    handleFiltroUsa(value) {
        clearTimeout(this.typingTimerUsa);
        this.typingTimerUsa = setTimeout(() => {
            this.usa.busqueda = value; this.usa.page = 1; this.cargarUsados();
        }, 300);
    },
    handleFiltroUsaInmediato() {
        this.usa.fechaDesde = document.getElementById('ac-usa-fechaDesde')?.value || '';
        this.usa.fechaHasta = document.getElementById('ac-usa-fechaHasta')?.value || '';
        this.usa.estatus = document.getElementById('ac-usa-estatus')?.value || '';
        this.usa.page = 1;
        this.cargarUsados();
    },
    limpiarFiltrosUsa() {
        ['ac-usa-busqueda', 'ac-usa-fechaDesde', 'ac-usa-fechaHasta'].forEach(id => {
            const el = document.getElementById(id); if (el) el.value = '';
        });
        document.getElementById('ac-usa-estatus').value = '';
        Object.assign(this.usa, { busqueda: '', fechaDesde: '', fechaHasta: '', estatus: '', page: 1 });
        this.cargarUsados();
    },

    async cargarUsados() {
        const tbody = document.getElementById('ac-usa-tbody');
        try {
            const params = new URLSearchParams({
                busqueda: this.usa.busqueda || '', page: this.usa.page, pageSize: this.usa.pageSize
            });
            if (this.usa.fechaDesde) params.append('fechaDesde', this.usa.fechaDesde);
            if (this.usa.fechaHasta) params.append('fechaHasta', this.usa.fechaHasta);
            if (this.usa.estatus) params.append('estatus', this.usa.estatus);

            const resp = await fetch(`/AdminCortes/ListarUsados?${params.toString()}`);
            const data = await resp.json();
            if (data.success === false) {
                toastMixin?.fire({ icon: 'error', title: data.message || 'Error al cargar' });
                return;
            }
            this.usa.totalRecords = data.total || 0;
            this.usa.items = data.items || [];
            this.renderUsados(this.usa.items);
            this.renderContadores('usa');
            this.renderPaginacion('usa');
        } catch (err) {
            console.error(err);
            tbody.innerHTML = `<tr><td colspan="10" class="ac-error-state">Error de conexión al listar el historial de uso</td></tr>`;
        }
    },

    renderUsados(items) {
        const tbody = document.getElementById('ac-usa-tbody');
        if (!items.length) {
            tbody.innerHTML = `<tr><td colspan="10" class="ac-empty-state"><i class="fas fa-inbox fa-2x mb-2 d-block"></i>Sin registros con estos filtros</td></tr>`;
            return;
        }
        tbody.innerHTML = items.map(it => `
            <tr>
                <td><span class="fw-semibold">${this.escapeHtml(it.folio_pieza)}</span></td>
                <td>
                    <div class="fw-semibold">${this.escapeHtml(it.producto_id)}</div>
                    <div class="small text-muted">${this.escapeHtml(it.producto_descripcion)}</div>
                </td>
                <td>${parseFloat(it.longitud_origen).toFixed(2)} m</td>
                <td>${parseFloat(it.longitud_solicitada).toFixed(2)} m</td>
                <td class="text-muted">${parseFloat(it.sobrante || 0).toFixed(2)} m</td>
                <td>
                    <button type="button" class="ac-link-pedido" data-id-encabezado="${it.id_encabezado}">
                        ${this.escapeHtml(it.folio_pedido)} <i class="fas fa-circle-info fa-xs"></i>
                    </button>
                    <div class="small text-muted">Partida #${it.nro_part}</div>
                </td>
                <td class="small">${this.escapeHtml(it.cli_prov || '')}</td>
                <td>${it.estatus === 'confirmado'
                ? '<span class="ac-badge-activo">Confirmado</span>'
                : '<span class="ac-badge-inactivo">Pendiente</span>'}</td>
                <td class="small">${this.escapeHtml(it.usuario_confirmacion || '—')}</td>
                <td class="small">${it.fecha_confirmacion ? new Date(it.fecha_confirmacion).toLocaleString('es-MX') : '—'}</td>
            </tr>`).join('');
    },

    exportarUsadosExcel() {
        if (!this.usa.items.length) { toastMixin?.fire({ icon: 'warning', title: 'No hay datos para exportar' }); return; }
        const filas = this.usa.items.map(it => ({
            'Folio pieza': it.folio_pieza,
            Producto: `${it.producto_id} - ${it.producto_descripcion}`,
            'Long. origen (m)': parseFloat(it.longitud_origen).toFixed(2),
            'Long. cortada (m)': parseFloat(it.longitud_solicitada).toFixed(2),
            'Sobrante (m)': parseFloat(it.sobrante || 0).toFixed(2),
            Pedido: it.folio_pedido,
            Partida: it.nro_part,
            Cliente: it.cli_prov,
            Estatus: it.estatus,
            'Confirmado por': it.usuario_confirmacion || '',
            Fecha: it.fecha_confirmacion ? new Date(it.fecha_confirmacion).toLocaleString('es-MX') : ''
        }));
        const hoja = XLSX.utils.json_to_sheet(filas);
        const libro = XLSX.utils.book_new();
        XLSX.utils.book_append_sheet(libro, hoja, 'Cortes usados');
        XLSX.writeFile(libro, `cortes_usados_${Date.now()}.xlsx`);
    },

    exportarUsadosPdf() {
        if (!this.usa.items.length) { toastMixin?.fire({ icon: 'warning', title: 'No hay datos para exportar' }); return; }
        const { jsPDF } = window.jspdf;
        const doc = new jsPDF({ orientation: 'landscape' });
        doc.setFontSize(13);
        doc.text('Reporte de cortes usados', 14, 15);
        doc.setFontSize(9);
        doc.text(`Generado: ${new Date().toLocaleString('es-MX')}`, 14, 21);

        doc.autoTable({
            startY: 27,
            head: [['Folio', 'Producto', 'Long. origen', 'Long. cortada', 'Sobrante', 'Pedido', 'Cliente', 'Estatus', 'Fecha']],
            body: this.usa.items.map(it => [
                it.folio_pieza,
                `${it.producto_id}`,
                parseFloat(it.longitud_origen).toFixed(2),
                parseFloat(it.longitud_solicitada).toFixed(2),
                parseFloat(it.sobrante || 0).toFixed(2),
                it.folio_pedido,
                it.cli_prov || '',
                it.estatus,
                it.fecha_confirmacion ? new Date(it.fecha_confirmacion).toLocaleDateString('es-MX') : ''
            ]),
            styles: { fontSize: 7 },
            headStyles: { fillColor: [30, 58, 95] }
        });
        doc.save(`cortes_usados_${Date.now()}.pdf`);
    },

    // ============================================================
    // MODAL DIVIDIR PIEZA
    // ============================================================
    abrirModalDividir(dataset) {
        this._piezaDividir = {
            idCorte: dataset.idCorte,
            folio: dataset.folio,
            longitud: parseFloat(dataset.longitud) || 0,
            producto: dataset.producto
        };
        document.getElementById('ac-div-folio').textContent = dataset.folio;
        document.getElementById('ac-div-producto').textContent = dataset.producto;
        document.getElementById('ac-div-longitud').textContent = this._piezaDividir.longitud.toFixed(2);

        document.getElementById('ac-div-lista').innerHTML = '';
        this.agregarSubpiezaRow();
        this.recalcularResumenDivision();

        bootstrap.Modal.getOrCreateInstance(document.getElementById('ac-modalDividir')).show();
    },

    agregarSubpiezaRow() {
        const numero = document.querySelectorAll('#ac-div-lista .vt-corte-card').length + 1;
        const card = document.createElement('div');
        card.className = 'vt-corte-card';
        card.innerHTML = `
            <div class="vt-corte-card-numero">${numero}</div>
            <div class="vt-corte-card-field">
                <label>Longitud (m)</label>
                <input type="number" min="0" step="0.01" class="vi-control ac-div-input ac-div-longitud" placeholder="Ej. 1.00">
            </div>
            <div class="vt-corte-card-field">
                <label>Cantidad de piezas</label>
                <input type="number" min="1" step="1" class="vi-control ac-div-input ac-div-cantidad" value="1">
            </div>
            <div class="vt-corte-card-status neutro">
                <i class="fas fa-minus-circle"></i>
            </div>
            <button type="button" class="vt-ped-btn-delete ac-div-btn-eliminar" title="Eliminar">
                <i class="fas fa-trash"></i>
            </button>
            <div class="vt-corte-card-comentario">
                <input type="text" class="vi-control ac-div-input ac-div-comentario" placeholder="Comentario (opcional)">
            </div>`;
        document.getElementById('ac-div-lista').appendChild(card);
        this.recalcularResumenDivision();
    },

    recalcularResumenDivision() {
        if (!this._piezaDividir) return;
        let suma = 0;
        document.querySelectorAll('#ac-div-lista .vt-corte-card').forEach(card => {
            const l = parseFloat(card.querySelector('.ac-div-longitud')?.value) || 0;
            const c = parseFloat(card.querySelector('.ac-div-cantidad')?.value) || 0;
            suma += l * c;
        });

        const base = this._piezaDividir.longitud;
        const restante = base - suma;

        document.getElementById('ac-div-suma').textContent = suma.toFixed(2);
        const restanteEl = document.getElementById('ac-div-restante');
        restanteEl.textContent = restante.toFixed(2);

        const resumenCard = document.getElementById('ac-div-resumen');
        const btnGuardar = document.getElementById('ac-btnGuardarDividir');

        if (restante < -0.001) {
            resumenCard.style.background = 'var(--vi-red-50, #fef2f2)';
            resumenCard.style.borderColor = '#fca5a5';
            restanteEl.style.color = '#dc2626';
            btnGuardar.disabled = true;
        } else {
            resumenCard.style.background = restante < 0.5 ? 'var(--vi-green-50, #f0fdf4)' : 'var(--vi-amber-50, #fffbeb)';
            resumenCard.style.borderColor = restante < 0.5 ? '#86efac' : '#fde68a';
            restanteEl.style.color = restante < 0.5 ? '#15803d' : '#d97706';
            btnGuardar.disabled = suma <= 0;
        }
    },

    async guardarDivision() {
        if (!this._piezaDividir) return;
        const subpiezas = [];
        let error = null;

        document.querySelectorAll('#ac-div-lista .vt-corte-card').forEach(card => {
            const longitud = parseFloat(card.querySelector('.ac-div-longitud')?.value) || 0;
            const cantidad = parseFloat(card.querySelector('.ac-div-cantidad')?.value) || 0;
            const comentario = card.querySelector('.ac-div-comentario')?.value || '';
            if (longitud <= 0 || cantidad <= 0) {
                error = 'Todas las subpiezas deben tener longitud y cantidad mayores a 0';
                return;
            }
            subpiezas.push({ Longitud: longitud, Cantidad: cantidad, Comentario: comentario });
        });

        if (error) { toastMixin?.fire({ icon: 'error', title: error }); return; }
        if (!subpiezas.length) { toastMixin?.fire({ icon: 'error', title: 'Agrega al menos una subpieza' }); return; }

        const suma = subpiezas.reduce((s, p) => s + p.Longitud * p.Cantidad, 0);
        if (suma > this._piezaDividir.longitud + 0.001) {
            toastMixin?.fire({ icon: 'error', title: `La suma (${suma.toFixed(2)}m) excede la longitud base (${this._piezaDividir.longitud.toFixed(2)}m)` });
            return;
        }

        try {
            const formData = new FormData();
            const csrfToken = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
            if (csrfToken) formData.append('__RequestVerificationToken', csrfToken);
            formData.append('idCorte', this._piezaDividir.idCorte);
            formData.append('subpiezasJSON', JSON.stringify(subpiezas));

            const resp = await fetch('/AdminCortes/DividirPieza', { method: 'POST', body: formData });
            const result = await resp.json();

            if (result.success) {
                // Se guarda antes de cerrar: el handler de cierre pone
                // _piezaDividir a null.
                const productoDividido = (this._piezaDividir?.producto || '').split(' - ')[0].trim();

                toastMixin?.fire({ icon: 'success', title: `Pieza dividida: ${result.folios.length} folio(s) generado(s)` });
                bootstrap.Modal.getInstance(document.getElementById('ac-modalDividir'))?.hide();
                this.cargarDisponibles();
                this.refrescarTaller();

                // Dividir crea barras nuevas que aún no tienen etiqueta pegada:
                // es el momento exacto en que hace falta imprimirlas.
                this.ofrecerEtiquetasTrasDividir(result.folios || [], productoDividido);
            } else {
                toastMixin?.fire({ icon: 'error', title: result.message || 'No se pudo dividir la pieza' });
            }
        } catch (err) {
            console.error(err);
            toastMixin?.fire({ icon: 'error', title: 'Error de conexión' });
        }
    },

    /**
     * Tras dividir, pregunta si se imprimen las etiquetas de las barras nuevas.
     * El backend devuelve folios, no ids de corte, así que se resuelven por el
     * producto de la pieza que se acababa de dividir.
     */
    async ofrecerEtiquetasTrasDividir(folios, productoId) {
        if (!folios.length || !window.EtiquetasBarras || !productoId) return;

        let imprimir = true;
        if (typeof Swal !== 'undefined') {
            const res = await Swal.fire({
                icon: 'question',
                title: '¿Imprimir las etiquetas?',
                text: `Se generaron ${folios.length} corte(s) nuevo(s). Las barras necesitan su etiqueta pegada.`,
                showCancelButton: true,
                confirmButtonText: 'Imprimir',
                cancelButtonText: 'Ahora no',
            });
            imprimir = res.isConfirmed;
        }

        if (imprimir) window.EtiquetasBarras.imprimir({ productoId });
    },

    // ============================================================
    // COMPARTIDO: contadores / paginación
    // ============================================================
    renderContadores(prefix) {
        const state = prefix === 'disp' ? this.disp : this.usa;
        const from = (state.page - 1) * state.pageSize + 1;
        const to = Math.min(state.page * state.pageSize, state.totalRecords);
        document.getElementById(`ac-${prefix}-recordsFrom`).textContent = state.totalRecords ? from : 0;
        document.getElementById(`ac-${prefix}-recordsTo`).textContent = to;
        document.getElementById(`ac-${prefix}-totalRecords`).textContent = state.totalRecords;
    },

    renderPaginacion(prefix) {
        const state = prefix === 'disp' ? this.disp : this.usa;
        const totalPages = Math.ceil(state.totalRecords / state.pageSize);
        const pagination = document.getElementById(`ac-${prefix}-pagination`);
        if (totalPages <= 1) { pagination.innerHTML = ''; return; }

        const maxPages = 5;
        let startPage = Math.max(state.page - Math.floor(maxPages / 2), 1);
        let endPage = Math.min(startPage + maxPages - 1, totalPages);
        startPage = Math.max(endPage - maxPages + 1, 1);

        let html = `<li class="page-item ${state.page === 1 ? 'disabled' : ''}">
            <button type="button" class="page-link" data-page="${state.page - 1}">Anterior</button></li>`;
        for (let i = startPage; i <= endPage; i++) {
            html += `<li class="page-item ${i === state.page ? 'active' : ''}">
                <button type="button" class="page-link" data-page="${i}">${i}</button></li>`;
        }
        html += `<li class="page-item ${state.page === totalPages ? 'disabled' : ''}">
            <button type="button" class="page-link" data-page="${state.page + 1}">Siguiente</button></li>`;
        pagination.innerHTML = html;
    },

    // ============================================================
    // MODAL CREAR: autocompletado de producto
    // (la sucursal ya no se pide: el servidor usa siempre la de la sesión)
    // ============================================================
    handleBuscarProducto(valor) {
        clearTimeout(this._typingTimerProducto);
        const q = (valor || '').trim();
        if (q.length < 2) { this.ocultarResultadosProducto(); return; }
        this._typingTimerProducto = setTimeout(() => this.buscarProductoInventario(q), 250);
    },

    async buscarProductoInventario(q) {
        const cont = document.getElementById('ac-crear-producto-resultados');
        if (!cont) return;
        try {
            const resp = await fetch(`/AdminCortes/BuscarProductoInventario?q=${encodeURIComponent(q)}`);
            const data = await resp.json();
            if (!data.ok) { this.ocultarResultadosProducto(); return; }
            this.renderResultadosProducto(data.productos || []);
        } catch (err) {
            console.error(err);
            this.ocultarResultadosProducto();
        }
    },

    renderResultadosProducto(productos) {
        // Se guardan en memoria y se referencian por índice (no por atributo
        // data-cve): así no hace falta re-escapar la clave para HTML.
        this._resultadosProducto = productos;
        const cont = document.getElementById('ac-crear-producto-resultados');
        if (!cont) return;

        if (!productos.length) {
            cont.innerHTML = `<div class="ac-autocomplete-vacio">Sin productos con existencia que coincidan.</div>`;
            cont.classList.remove('d-none');
            return;
        }

        cont.innerHTML = productos.map((p, idx) => `
            <button type="button" class="ac-autocomplete-item" data-idx="${idx}">
                <span class="ac-autocomplete-item-txt">
                    <span class="ac-autocomplete-item-cve">${this.escapeHtml(p.cve_prod)}</span>
                    <span class="ac-autocomplete-item-desc">${this.escapeHtml(p.descr_prod || '')}</span>
                </span>
                <span class="ac-autocomplete-item-existencia">${parseFloat(p.existencia_total || 0).toFixed(2)} m</span>
            </button>`).join('');
        cont.classList.remove('d-none');
    },

    ocultarResultadosProducto() {
        const cont = document.getElementById('ac-crear-producto-resultados');
        if (cont) { cont.classList.add('d-none'); cont.innerHTML = ''; }
    },

    seleccionarProducto(cveProd) {
        const input = document.getElementById('ac-crear-productoId');
        if (input) input.value = cveProd;
        this.ocultarResultadosProducto();
        this.verDesglose();
    },

    // ============================================================
    // MODAL CREAR
    // ============================================================
    async verDesglose() {
        const productoId = document.getElementById('ac-crear-productoId')?.value?.trim();
        if (!productoId) { toastMixin?.fire({ icon: 'warning', title: 'Indica la clave del producto primero' }); return; }

        const panel = document.getElementById('ac-desglose-panel');
        panel.classList.remove('d-none');

        try {
            const resp = await fetch(`/AdminCortes/ObtenerDesgloseProducto?productoId=${encodeURIComponent(productoId)}`);
            const data = await resp.json();
            if (!data.success) {
                toastMixin?.fire({ icon: 'error', title: data.message || 'No se pudo obtener el desglose' });
                panel.classList.add('d-none');
                return;
            }
            const total = parseFloat(data.existenciaTotal) || 0;
            const configurado = parseFloat(data.metrosYaConfigurados) || 0;
            const disponible = Math.max(0, total - configurado);
            const porcentaje = total > 0 ? Math.min(100, (configurado / total) * 100) : 0;

            document.getElementById('ac-desglose-total').textContent = total.toFixed(2);
            document.getElementById('ac-desglose-configurado').textContent = configurado.toFixed(2);
            document.getElementById('ac-desglose-disponible').textContent = disponible.toFixed(2);

            const barra = document.getElementById('ac-desglose-barra');
            barra.style.width = `${porcentaje}%`;
            barra.setAttribute('aria-valuenow', porcentaje.toFixed(0));

            const piezasActivas = (data.piezas || []).filter(p => p.activo);
            this.renderDesglosePiezas(piezasActivas);
        } catch (err) {
            console.error(err);
            toastMixin?.fire({ icon: 'error', title: 'Error de conexión al obtener el desglose' });
        }
    },

    renderDesglosePiezas(piezas) {
        const tbody = document.getElementById('ac-desglose-piezas');
        document.getElementById('ac-desglose-count').textContent = piezas.length;
        if (!piezas.length) {
            tbody.innerHTML = `<tr><td colspan="6" class="text-center text-muted py-3">Sin piezas configuradas todavía.</td></tr>`;
            return;
        }
        tbody.innerHTML = piezas.map(p => {
            const longitud = parseFloat(p.longitud) || 0;
            const cantidad = parseInt(p.cantidad) || 0;
            const pendiente = cantidad === 0;
            return `<tr class="${pendiente ? 'ac-fila-pendiente' : ''}">
                <td>${this.escapeHtml(p.folio)}${p.es_sobrante ? '<span class="ac-badge-sobrante">Sobrante</span>' : ''}</td>
                <td class="text-end">${longitud.toFixed(2)}</td>
                <td class="text-end">${cantidad}</td>
                <td class="text-end">${(longitud * cantidad).toFixed(2)}</td>
                <td><span class="ac-desglose-badge ${pendiente ? 'ac-desglose-badge-pendiente' : 'ac-desglose-badge-asignada'}">
                    ${pendiente ? 'Agotada' : 'Disponible'}</span></td>
                <td>
                    ${this.escapeHtml(p.comentario) || '<span class="text-muted">—</span>'}
                    ${pendiente ? '' : `
                    <button type="button" class="ac-btn-accion ac-btn-etiquetas ms-2"
                            data-id-corte="${p.id_corte}" title="Imprimir etiquetas de estas barras">
                        <i class="fas fa-barcode"></i>
                    </button>`}
                </td>
            </tr>`;
        }).join('');
    },

    actualizarPreviewMetros() {
        const longitud = parseFloat(document.getElementById('ac-crear-longitud')?.value) || 0;
        const cantidad = parseFloat(document.getElementById('ac-crear-cantidad')?.value) || 0;
        document.getElementById('ac-crear-metros-preview').value = (longitud * cantidad).toFixed(2);
    },

    async guardarCrear() {
        const productoId = document.getElementById('ac-crear-productoId')?.value?.trim();
        const longitud = document.getElementById('ac-crear-longitud')?.value;
        const cantidad = document.getElementById('ac-crear-cantidad')?.value;
        const comentario = document.getElementById('ac-crear-comentario')?.value || '';

        if (!productoId || !longitud || !cantidad) {
            toastMixin?.fire({ icon: 'warning', title: 'Completa producto, longitud y cantidad' });
            return;
        }

        try {
            const formData = new FormData();
            const csrfToken = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
            if (csrfToken) formData.append('__RequestVerificationToken', csrfToken);
            formData.append('productoId', productoId);
            formData.append('longitud', longitud);
            formData.append('cantidad', cantidad);
            formData.append('comentario', comentario);

            const resp = await fetch('/AdminCortes/Crear', { method: 'POST', body: formData });
            const result = await resp.json();

            if (result.success) {
                const texto = result.folioSobrante
                    ? `El sobrante (${parseFloat(result.sobrante).toFixed(2)} m) quedó como folio ${result.folioSobrante}, con su propio código de barras.`
                    : '';
                toastMixin?.fire({ icon: 'success', title: `Pieza creada: folio ${result.folio}`, text: texto });
                bootstrap.Modal.getInstance(document.getElementById('ac-modalCrear'))?.hide();
                this.cargarDisponibles();
                this.refrescarTaller();
            } else {
                toastMixin?.fire({ icon: 'error', title: result.message || 'No se pudo crear la pieza' });
            }
        } catch (err) {
            console.error(err);
            toastMixin?.fire({ icon: 'error', title: 'Error de conexión' });
        }
    },

    resetModalCrear() {
        document.getElementById('ac-crear-productoId').value = '';
        document.getElementById('ac-crear-longitud').value = '';
        document.getElementById('ac-crear-cantidad').value = '1';
        document.getElementById('ac-crear-comentario').value = '';
        document.getElementById('ac-crear-metros-preview').value = '0.00';
        document.getElementById('ac-desglose-panel').classList.add('d-none');
        this.ocultarResultadosProducto();
    },

    /** Abre "Agregar pieza" con el producto ya elegido (llamado desde el Taller 3D). */
    abrirModalCrearPara(cveProd) {
        const input = document.getElementById('ac-crear-productoId');
        if (input) input.value = cveProd;
        bootstrap.Modal.getOrCreateInstance(document.getElementById('ac-modalCrear')).show();
        this.verDesglose();
    },

    /** El Taller 3D dibuja los mismos cortes: cualquier alta/baja debe reflejarse ahí también. */
    refrescarTaller() {
        window.TallerBarras?.recargar();
    },

    async desactivarPieza(idCorte) {
        const confirmacion = await Swal.fire({
            title: '¿Desactivar esta pieza?',
            text: 'Ya no estará disponible para nuevos cortes.',
            icon: 'warning', showCancelButton: true,
            confirmButtonText: 'Sí, desactivar', cancelButtonText: 'Cancelar'
        });
        if (!confirmacion.isConfirmed) return;

        try {
            const formData = new FormData();
            const csrfToken = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
            if (csrfToken) formData.append('__RequestVerificationToken', csrfToken);
            formData.append('idCorte', idCorte);

            const resp = await fetch('/AdminCortes/Desactivar', { method: 'POST', body: formData });
            const result = await resp.json();

            if (result.success) {
                toastMixin?.fire({ icon: 'success', title: 'Pieza desactivada' });
                this.cargarDisponibles();
                this.refrescarTaller();
            } else {
                toastMixin?.fire({ icon: 'error', title: result.message || 'No se pudo desactivar' });
            }
        } catch (err) {
            console.error(err);
            toastMixin?.fire({ icon: 'error', title: 'Error de conexión' });
        }
    },

    escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text ?? '';
        return div.innerHTML;
    },

    async verDetallePedido(idEncabezado) {
        const modalEl = document.getElementById('ac-modalDetallePedido');
        const modal = bootstrap.Modal.getOrCreateInstance(modalEl);
        document.getElementById('ac-detalle-cuerpo').innerHTML = `
        <div class="text-center py-4 text-muted">
            <div class="spinner-border spinner-border-sm me-2"></div> Cargando pedido...
        </div>`;
        modal.show();

        try {
            const resp = await fetch(`/VTPedido/Detalle?id=${idEncabezado}`);
            const data = await resp.json();
            if (!data.success) {
                document.getElementById('ac-detalle-cuerpo').innerHTML =
                    `<div class="ac-error-state">${this.escapeHtml(data.message || 'No se pudo cargar el pedido')}</div>`;
                return;
            }
            this.renderDetallePedido(data.encabezado, data.partidas);
        } catch (err) {
            console.error(err);
            document.getElementById('ac-detalle-cuerpo').innerHTML =
                `<div class="ac-error-state">Error de conexión al cargar el pedido</div>`;
        }
    },

    renderDetallePedido(enc, partidas) {
        const filasPartidas = (partidas || []).map(p => `
        <tr>
            <td>${this.escapeHtml(p.cve_prod)}</td>
            <td>${this.escapeHtml(p.descr_prod)}</td>
            <td class="text-end">${parseFloat(p.cant_ud).toFixed(2)} ${this.escapeHtml(p.ud || '')}</td>
            <td class="text-end">$${parseFloat(p.pv_prod).toFixed(2)}</td>
            <td class="text-end">$${parseFloat(p.importe).toFixed(2)}</td>
        </tr>`).join('');

        document.getElementById('ac-detalle-cuerpo').innerHTML = `
        <div class="ac-desglose-stats mb-3">
            <div class="ac-desglose-stat">
                <span class="ac-desglose-stat-label">Folio</span>
                <span class="ac-desglose-stat-value">${this.escapeHtml(enc.folio)}</span>
            </div>
            <div class="ac-desglose-stat">
                <span class="ac-desglose-stat-label">Cliente</span>
                <span class="ac-desglose-stat-value">${this.escapeHtml(enc.cliente_nombre || enc.cli_prov)}</span>
            </div>
            <div class="ac-desglose-stat">
                <span class="ac-desglose-stat-label">Total</span>
                <span class="ac-desglose-stat-value">$${parseFloat(enc.imp).toFixed(2)} ${this.escapeHtml(enc.ccy)}</span>
            </div>
        </div>
        <div class="ac-desglose-table-wrap">
            <table class="ac-desglose-table">
                <thead>
                    <tr>
                        <th>Producto</th><th>Descripción</th>
                        <th class="text-end">Cantidad</th><th class="text-end">Precio</th><th class="text-end">Importe</th>
                    </tr>
                </thead>
                <tbody>${filasPartidas || '<tr><td colspan="5" class="text-center text-muted py-3">Sin partidas</td></tr>'}</tbody>
            </table>
        </div>`;
    }
};

// Un `const` de nivel superior no queda como propiedad de `window`: sin esta
// línea, `window.AdminCortesApp` es `undefined` para cualquier OTRO script
// (p. ej. admin-cortes-taller.js), aunque `AdminCortesApp` funcione bien
// dentro de este mismo archivo.
window.AdminCortesApp = AdminCortesApp;

if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => AdminCortesApp.init());
} else {
    AdminCortesApp.init();
}
// ============================================
// 0. ESTADO GLOBAL DE AUTENTICACIÓN (igual que cotizaciones)
// ============================================
const pedidosAuthState = {
    descuentoToken: null,
    precioToken: null
};

// ============================================
// 1. CLASE BASE PARA GESTIÓN DE DATOS
// ============================================
class PedidosDataManager {
    constructor(config) {
        this.endpoint = config.endpoint;
        this.detailEndpoint = config.detailEndpoint;
        this.modalId = config.modalId;
        this.cache = new Map();
        this.cacheTimeout = 5 * 60 * 1000;
        this.abortController = null;
        this.shouldCloseOnSelect = config.shouldCloseOnSelect !== false;
        this.filtros = config.filtros || {};

        this.dom = {
            modal: document.getElementById(this.modalId),
            input: document.getElementById(config.inputId),
            results: document.getElementById(config.resultsId),
            pageSize: document.getElementById(config.pageSizeId),
            btnClear: document.getElementById(config.btnClearId),
            spinner: document.getElementById(config.spinnerId),
            pagination: document.getElementById(config.paginationId),
            recordsFrom: document.getElementById(config.recordsFromId),
            recordsTo: document.getElementById(config.recordsToId),
            totalRecords: document.getElementById(config.totalRecordsId)
        };

        this.state = {
            page: 1,
            pageSize: 50,
            lastSearch: '',
            loading: false,
            totalRecords: 0
        };

        this.typingTimer = null;
        this.initPedidosDataManager();
    }

    initPedidosDataManager() {
        this.dom.modal?.addEventListener('show.bs.modal', () => this.onPedidosModalShow());
        this.dom.input?.addEventListener('input', (e) => this.handlePedidosSearch(e.target.value));
        this.dom.pageSize?.addEventListener('change', (e) => this.handlePedidosPageSizeChange(e.target.value));
        this.dom.btnClear?.addEventListener('click', () => this.handlePedidosClear());

        this.dom.results?.addEventListener('click', (e) => {
            const btn = e.target.closest('[data-select-id]');
            if (btn) {
                const id = btn.dataset.selectId;
                this.handlePedidosSelect(id);
            }
        });

        this.dom.pagination?.addEventListener('click', (e) => {
            e.preventDefault();
            const btn = e.target.closest('[data-page]');
            if (btn && !btn.closest('.disabled')) {
                this.state.page = parseInt(btn.dataset.page);
                this.fetchPedidosData();
            }
        });
    }

    onPedidosModalShow() {
        this.handlePedidosClear();
        this.fetchPedidosData();
    }

    handlePedidosSearch(value) {
        clearTimeout(this.typingTimer);
        this.typingTimer = setTimeout(() => {
            this.state.lastSearch = value;
            this.state.page = 1;
            this.fetchPedidosData();
        }, 300);
    }

    handlePedidosPageSizeChange(value) {
        this.state.pageSize = parseInt(value);
        this.state.page = 1;
        this.fetchPedidosData();
    }

    handlePedidosClear() {
        this.dom.input.value = '';
        this.state.lastSearch = '';
        this.state.page = 1;
        this.fetchPedidosData();
    }

    getPedidosCachedData(key) {
        const cached = this.cache.get(key);
        if (cached && Date.now() - cached.timestamp < this.cacheTimeout) {
            return cached.data;
        }
        return null;
    }

    setPedidosCachedData(key, data) {
        this.cache.set(key, { data, timestamp: Date.now() });
    }

    clearPedidosCache() {
        this.cache.clear();
    }

    buildCacheKey() {
        const filtroEstado = this.filtros.estado || '';
        const filtroFecha = this.filtros.fecha || '';
        return `${this.state.lastSearch}-${this.state.page}-${this.state.pageSize}-${filtroEstado}-${filtroFecha}`;
    }

    buildUrl() {
        const params = new URLSearchParams({
            busqueda: this.state.lastSearch || '',
            nombre: this.state.lastSearch || '',
            page: this.state.page,
            pageSize: this.state.pageSize
        });

        if (this.filtros.estado) params.append('estado', this.filtros.estado);
        if (this.filtros.fecha) params.append('fecha', this.filtros.fecha);

        return `${this.endpoint}?${params.toString()}`;
    }

    async fetchPedidosData() {
        if (this.state.loading) {
            this.abortController?.abort();
        }

        const cacheKey = this.buildCacheKey();
        const cached = this.getPedidosCachedData(cacheKey);

        if (cached) {
            this.renderPedidosResults(cached);
            return;
        }

        this.state.loading = true;
        this.showPedidosSpinner(true);
        this.abortController = new AbortController();

        try {
            const url = this.buildUrl();
            const response = await fetch(url, { signal: this.abortController.signal });

            if (!response.ok) throw new Error('Error en la respuesta');

            const data = await response.json();
            this.state.totalRecords = data.total || 0;

            const results = { items: data.items || data, total: this.state.totalRecords };
            this.setPedidosCachedData(cacheKey, results);
            this.renderPedidosResults(results);

        } catch (error) {
            if (error.name !== 'AbortError') {
                console.error('Error fetching pedidos data:', error);
                this.renderPedidosError();
            }
        } finally {
            this.state.loading = false;
            this.showPedidosSpinner(false);
        }
    }

    showPedidosSpinner(show) {
        this.dom.spinner?.classList.toggle('d-none', !show);
    }

    renderPedidosResults(data) {
        const items = data.items || [];

        if (items.length === 0) {
            this.dom.results.innerHTML = `
                <li class="list-group-item text-center text-muted py-5">
                    <i class="fas fa-search fa-3x mb-3 opacity-25"></i>
                    <p>No se encontraron resultados</p>
                </li>`;
            this.updatePedidosCounters();
            return;
        }

        const fragment = document.createDocumentFragment();

        items.forEach(item => {
            const li = document.createElement('li');
            li.className = 'list-group-item d-flex justify-content-between align-items-center hover-shadow';
            li.innerHTML = `
                <div>
                    <h6 class="mb-1 fw-semibold text-primary">${this.escapePedidosHtml(item.descripcion)}</h6>
                    <small class="text-muted">ID: ${this.escapePedidosHtml(item.id)}</small>
                </div>
                <button type="button" class="btn btn-sm btn-primary" data-select-id="${this.escapePedidosHtml(item.id)}">
                    <i class="fas fa-check me-1"></i>Seleccionar
                </button>`;
            fragment.appendChild(li);
        });

        this.dom.results.innerHTML = '';
        this.dom.results.appendChild(fragment);

        this.updatePedidosCounters();
        this.renderPedidosPagination();
    }

    renderPedidosError() {
        this.dom.results.innerHTML = `
            <li class="list-group-item text-center text-danger py-5">
                <i class="fas fa-exclamation-triangle fa-3x mb-3"></i>
                <p>Error al cargar los datos</p>
            </li>`;
    }

    updatePedidosCounters() {
        const from = (this.state.page - 1) * this.state.pageSize + 1;
        const to = Math.min(this.state.page * this.state.pageSize, this.state.totalRecords);

        if (this.dom.recordsFrom) this.dom.recordsFrom.textContent = from;
        if (this.dom.recordsTo) this.dom.recordsTo.textContent = to;
        if (this.dom.totalRecords) this.dom.totalRecords.textContent = this.state.totalRecords;
    }

    renderPedidosPagination() {
        const totalPages = Math.ceil(this.state.totalRecords / this.state.pageSize);
        if (totalPages <= 1) {
            this.dom.pagination.innerHTML = '';
            return;
        }

        const maxPages = 5;
        let startPage = Math.max(this.state.page - Math.floor(maxPages / 2), 1);
        let endPage = Math.min(startPage + maxPages - 1, totalPages);
        startPage = Math.max(endPage - maxPages + 1, 1);

        const fragment = document.createDocumentFragment();

        const prevLi = this.createPedidosPageButton('Anterior', this.state.page - 1, this.state.page === 1);
        fragment.appendChild(prevLi);

        for (let i = startPage; i <= endPage; i++) {
            const li = this.createPedidosPageButton(i, i, false, i === this.state.page);
            fragment.appendChild(li);
        }

        const nextLi = this.createPedidosPageButton('Siguiente', this.state.page + 1, this.state.page === totalPages);
        fragment.appendChild(nextLi);

        this.dom.pagination.innerHTML = '';
        this.dom.pagination.appendChild(fragment);
    }

    createPedidosPageButton(text, page, disabled = false, active = false) {
        const li = document.createElement('li');
        li.className = `page-item${disabled ? ' disabled' : ''}${active ? ' active' : ''}`;
        li.innerHTML = `<button type="button" class="page-link" data-page="${page}">${text}</button>`;
        return li;
    }

    async handlePedidosSelect(id) {
        try {
            const response = await fetch(`${this.detailEndpoint}?id=${encodeURIComponent(id)}`);
            if (!response.ok) throw new Error('Error al obtener detalles');

            const data = await response.json();
            if (data && data[0]) {
                this.onPedidosSelect(data[0]);
                if (this.shouldCloseOnSelect) {
                    bootstrap.Modal.getInstance(this.dom.modal)?.hide();
                }
            }
        } catch (error) {
            console.error('Error selecting pedidos item:', error);
            alert('Error al seleccionar el elemento');
        }
    }

    onPedidosSelect(item) {
        console.log('Pedidos item selected:', item);
    }

    escapePedidosHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }
}

// ============================================
// 2. CLASE EXTENDIDA PARA DOCUMENTOS
// ============================================
class PedidosDocumentoManager extends PedidosDataManager {
    renderPedidosResults(data) {
        const items = data.items || [];

        if (items.length === 0) {
            this.dom.results.innerHTML = `
                <li class="list-group-item text-center text-muted py-5">
                    <i class="fas fa-search fa-3x mb-3 opacity-25"></i>
                    <p>No se encontraron documentos</p>
                </li>`;
            this.updatePedidosCounters();
            return;
        }

        const fragment = document.createDocumentFragment();

        items.forEach(item => {
            const li = document.createElement('li');
            li.className = 'list-group-item';
            li.dataset.folio = item.folio;

            let badgeClass = 'badge-pending';
            if (item.estado === 'approved' || item.estado === 'aprobado') badgeClass = 'badge-approved';
            if (item.estado === 'cancelled' || item.estado === 'cancelado') badgeClass = 'badge-cancelled';

            li.innerHTML = `
                <div class="d-flex justify-content-between align-items-start">
                    <div class="flex-grow-1">
                        <h6 class="mb-1">
                            <i class="fas fa-file-invoice text-primary me-2"></i>
                            <strong>Folio:</strong> ${this.escapePedidosHtml(item.folio || '')}
                        </h6>
                        <p class="mb-1 text-muted">
                            <i class="fas fa-user me-2"></i>
                            <strong>Cliente:</strong> ${this.escapePedidosHtml(item.cli_prov || '')}
                        </p>
                        <div class="d-flex gap-3 small text-muted">
                            <span><i class="fas fa-calendar me-1"></i> ${this.escapePedidosHtml(item.fecha || '')}</span>
                            <span><i class="fas fa-money-bill-wave me-1"></i> $${parseFloat(item.imp || 0).toFixed(2)}</span>
                            <span><i class="fas fa-user-tie me-1"></i> ${this.escapePedidosHtml(item.usr0 || '')}</span>
                        </div>
                    </div>
                    <div class="d-flex flex-column gap-2 align-items-end">
                        <span></span>
                        <button type="button" class="btn btn-sm btn-primary" data-select-id="${this.escapePedidosHtml(item.id_encabezado)}">
                            <i class="fas fa-check me-1"></i>Cargar
                        </button>
                    </div>
                </div>`;
            fragment.appendChild(li);
        });

        this.dom.results.innerHTML = '';
        this.dom.results.appendChild(fragment);

        this.updatePedidosCounters();
        this.renderPedidosPagination();
    }
}

// ============================================
// 3. AUTENTICACIÓN GLOBAL POR TIPO DE CAMPO (igual que cotizaciones)
// ============================================
async function pedirPedidosAutenticacionGlobal(tipo) {
    if (tipo === 'descuento' && pedidosAuthState.descuentoToken) return;
    if (tipo === 'precio' && pedidosAuthState.precioToken) return;

    const label = tipo === 'descuento' ? 'descuentos' : 'precios';
    const endpoint = tipo === 'descuento'
        ? '/DatosGenerales/ValidarDescuento'
        : '/DatosGenerales/ValidarPrecio';

    const { value: password, isConfirmed } = await Swal.fire({
        title: `Desbloquear ${label}`,
        html: `
            <p class="text-muted mb-3">
                Ingresa tu contraseña para habilitar la edición de
                <strong>${label}</strong> en todas las partidas.
            </p>
            <input id="swal-password"
                   type="password"
                   class="swal2-input"
                   placeholder="Contraseña"
                   autocomplete="current-password">`,
        focusConfirm: false,
        showCancelButton: true,
        confirmButtonText: '<i class="fas fa-unlock me-1"></i> Desbloquear',
        cancelButtonText: 'Cancelar',
        didOpen: () => {
            const input = document.getElementById('swal-password');
            input.focus();
            input.addEventListener('keydown', (e) => {
                if (e.key === 'Enter') Swal.clickConfirm();
            });
        },
        preConfirm: () => {
            const pwd = document.getElementById('swal-password').value;
            if (!pwd) { Swal.showValidationMessage('La contraseña es obligatoria'); return false; }
            return pwd;
        }
    });

    if (!isConfirmed || !password) return;

    try {
        const formData = new FormData();
        formData.append('Password', password);

        const csrfToken = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
        if (csrfToken) formData.append('__RequestVerificationToken', csrfToken);

        const response = await fetch(endpoint, { method: 'POST', body: formData });
        const result = await response.json();

        if (result.success && result.token) {
            if (tipo === 'descuento') pedidosAuthState.descuentoToken = result.token;
            else pedidosAuthState.precioToken = result.token;

            PedidosApp.productManagerInstance?.desbloquearPedidosCampos(tipo);

            toastMixin.fire({
                icon: 'success',
                title: `${tipo === 'descuento' ? 'Descuentos' : 'Precios'} desbloqueados.`
            });
        } else {
            Swal.fire({
                icon: 'error',
                title: 'Contraseña incorrecta',
                text: result.message || 'No fue posible validar las credenciales.'
            });
        }
    } catch (err) {
        console.error(err);
        Swal.fire('Error', 'No se pudo conectar con el servidor.', 'error');
    }
}

// ============================================
// 4. GESTOR DE PRODUCTOS (con protección de precio y descuento)
// ============================================
class PedidosProductManager {
    constructor() {
        this.productos = [];
        this.totales = {
            subtotal1: 0,
            descuento: 0,
            flete: 0,
            subtotal2: 0,
            iva: 0,
            total: 0
        };

        this.dom = {
            table: document.getElementById('vt-ped-productosTable'),
            thead: document.querySelector('#vt-ped-productosTable')?.closest('table')?.querySelector('thead'),
            subtotal1Display: document.getElementById('vt-ped-total-subtotal1-display'),
            descuentoDisplay: document.getElementById('vt-ped-total-descuento-display'),
            fleteDisplay: document.getElementById('vt-ped-total-flete-display'),
            subtotal2Display: document.getElementById('vt-ped-total-subtotal2-display'),
            ivaDisplay: document.getElementById('vt-ped-iva-display'),
            importeDisplay: document.getElementById('vt-ped-importe-display')
        };

        this.corteState = { productoId: null };
        this.corteModalEl = document.getElementById('vt-ped-modalCorte');
        this.corteTableBody = document.getElementById('vt-ped-corteTable');
        this.initPedidosProductManager();
    }

    initPedidosProductManager() {
        this.dom.table?.addEventListener('input', (e) => {
            const input = e.target;
            if (input.classList.contains('vt-ped-cantidad') ||
                input.classList.contains('vt-ped-precio') ||
                input.classList.contains('vt-ped-descuento') ||
                input.classList.contains('vt-ped-comentario')) {
                const tr = input.closest('tr');
                const productoId = tr?.dataset.productoId;
                if (productoId) this.actualizarPedidosFila(productoId);
            }
        });

        // ── Un solo listener de click en la tabla ────────────────────────────
        this.dom.table?.addEventListener('click', (e) => {

            // 1. Eliminar producto
            const btnEliminar = e.target.closest('.vt-ped-btn-eliminar');
            if (btnEliminar) {
                const productoId = btnEliminar.closest('tr')?.dataset.productoId;
                if (productoId) this.eliminarPedidosProducto(productoId);
                return;
            }

            const btnCorte = e.target.closest('.vt-ped-btn-corte');
            if (btnCorte) {
                const productoId = btnCorte.dataset.productoId;
                if (productoId) this.abrirPedidosModalCorte(productoId);
                return;
            }

            // 2. Click en el OVERLAY de campo protegido
            const overlay = e.target.closest('.vt-ped-field-overlay');
            if (overlay) {
                pedirPedidosAutenticacionGlobal(overlay.dataset.tipo);
                return;
            }
        });

        document.getElementById('vt-ped-btnAgregarCorte')?.addEventListener('click', () => this.agregarPedidosCorteRow());
        document.getElementById('vt-ped-btnGuardarCorte')?.addEventListener('click', () => this.guardarPedidosCorte());

        this.corteTableBody?.addEventListener('input', (e) => {
            if (e.target.classList.contains('vt-ped-corte-input')) this.recalcularPedidosResumenCorte();
        });

        this.corteTableBody?.addEventListener('click', (e) => {
            const btn = e.target.closest('.vt-ped-btn-corte-eliminar');
            if (btn) {
                btn.closest('.vt-corte-card')?.remove();
                this.recalcularPedidosResumenCorte();
            }
        });

        document.getElementById('vt-ped-flete-val')?.addEventListener('input', () => {
            this.calcularPedidosTotales();
        });

        document.getElementById('vt-ped-monto-anticipo')?.addEventListener('input', () => {
            this.calcularPedidosTotales();
        });
    }

    agregarPedidosProducto(producto) {
        const productoExistente = this.productos.find(p => p.productoId === producto.id);

        if (productoExistente) {
            productoExistente.cantidad += 1;
            // ... (sin cambios en esta rama)
            const tr = this.dom.table.querySelector(`tr[data-producto-id="${productoExistente.id}"]`);
            if (tr) {
                const inputCantidad = tr.querySelector('.vt-ped-cantidad');
                inputCantidad.value = productoExistente.cantidad;
                const importe = this.calcularPedidosImporte(productoExistente);
                tr.querySelector('.vt-ped-importe').textContent = importe.toFixed(2);
                tr.classList.add('table-warning');
                setTimeout(() => tr.classList.remove('table-warning'), 500);
            }
            this.calcularPedidosTotales();
            if (window.toastMixin) {
                toastMixin.fire({ icon: 'info', title: `Cantidad actualizada: ${this.escapePedidosHtml(producto.descripcion)} (${productoExistente.cantidad})` });
            }
        } else {
            const productoData = {
                id: `prod_${Date.now()}_${Math.random()}`,
                productoId: producto.id,
                descripcion: producto.descripcion,
                existencia: producto.existencia,
                cantidad: 1,
                precio: parseFloat(producto.precio || 0),
                precioOriginal: parseFloat(producto.precio || 0),
                descuento: 0,
                unidad: producto.udm || 'PZA',
                existenciaGeneral: producto.existenciaGeneral || 0,
                existenciaModular: producto.existenciaModular || 0,
                comentario: '',
                cortes: [],
                // ★ NUEVO: desglose de piezas físicas disponibles.
                // Si el backend ya manda esto, se usa tal cual.
                // Si no, se sintetiza una sola "pieza" con el total como fallback temporal.
                piezasDisponibles: this.normalizarPiezasDisponibles(producto)
            };

            this.productos.push(productoData);
            this.renderPedidosTable();
            this.calcularPedidosTotales();

            if (window.toastMixin) {
                toastMixin.fire({ icon: 'success', title: `Producto agregado: ${this.escapePedidosHtml(producto.descripcion)}` });
            }
        }
    }

    normalizarPiezasDisponibles(producto) {
        if (Array.isArray(producto.piezasDisponibles) && producto.piezasDisponibles.length > 0) {
            return producto.piezasDisponibles.map(p => ({
                idCorte: p.id_corte ?? p.idCorte ?? null,   // ★ FIX: faltaba
                folio: p.folio,
                longitud: parseFloat(p.longitud) || 0,
                cantidad: parseFloat(p.cantidad) || 0
            }));
        }
        return [];
    }
    // ★ NUEVO: piezas "efectivas" a usar en simulación / panel cuando aún
    // no existe un desglose real de piezas físicas para el producto.
    // Si hay desglose real (piezasDisponibles con cantidad > 0), se usa tal cual.
    // Si NO hay desglose, se sintetiza UNA sola pieza virtual con el total de
    // producto.existencia (folio: null), para poder seguir cortando/guardando
    // mientras se configura el desglose real en almacén.
    obtenerPiezasEfectivas(producto) {
        if (Array.isArray(producto.piezasDisponibles) && producto.piezasDisponibles.some(p => p.cantidad > 0)) {
            return producto.piezasDisponibles;
        }

        const existenciaTotal = parseFloat(producto.existencia) || 0;
        if (existenciaTotal <= 0) return [];

        return [{ folio: null, longitud: existenciaTotal, cantidad: 1 }];
    }

    // ★ Refresca piezas al abrir el modal de corte (por si cambió el stock)
    // Única versión (folio-aware + refresco desde backend). Se eliminó el
    // duplicado síncrono que sobrescribía este método silenciosamente.
    async abrirPedidosModalCorte(productoId) {
        const producto = this.productos.find(p => p.id === productoId);
        if (!producto) return;

        try {
            const sucursal = document.getElementById('vt-ped-sucursal')?.value || 1;
            const resp = await fetch(`/DatosGenerales/ObtenerPiezasDisponibles?productoId=${producto.productoId}&sucursal=${sucursal}`);
            const data = await resp.json();
            if (data.success) producto.piezasDisponibles = this.normalizarPiezasDisponibles({ piezasDisponibles: data.piezas });
        } catch (e) {
            console.warn('No se pudo refrescar piezas disponibles, usando snapshot local', e);
        }

        this.corteState.productoId = productoId;
        document.getElementById('vt-ped-corte-producto-desc').textContent = producto.descripcion;
        document.getElementById('vt-ped-corte-cantidad-total').textContent = producto.cantidad;
        document.getElementById('vt-ped-corte-unidad').textContent = producto.unidad;

        this.corteTableBody.innerHTML = '';
        // ★ Si ya había cortes guardados (documento cargado), precargarlos
        const cortesExistentes = (producto.cortes && producto.cortes.length)
            ? producto.cortes
            : [{ id: `corte_${Date.now()}`, longitud: '', cantidad: 1, comentario: '' }];

        cortesExistentes.forEach(c => this.agregarPedidosCorteRow(c));
        this.recalcularPedidosResumenCorte();

        bootstrap.Modal.getOrCreateInstance(this.corteModalEl).show();
    }

    /**
     * Busca la pieza más pequeña que alcance para el corte solicitado (best-fit).
     * Muta el array piezas que se le pase (por eso siempre se le pasa una copia
     * cuando es solo "vista previa", y el array real solo al guardar).
     *
     * Única versión (folio-aware). El sobrante NO se reintegra al pool local
     * como pieza anónima — el folio del retazo lo genera el backend al guardar,
     * así el front nunca "inventa" folios que no existen.
     */
    asignarMejorPieza(piezas, longitudRequerida, umbralUtil = 0.5) {
        const candidatas = piezas
            .filter(p => p.cantidad > 0 && p.longitud >= longitudRequerida - 0.001)
            .sort((a, b) => a.longitud - b.longitud);

        if (candidatas.length === 0) {
            return { exito: false, idCorte: null, folio: null, piezaOrigen: null, sobrante: 0, desperdicio: 0 };
        }

        const elegida = candidatas[0];
        const sobrante = Math.max(0, +(elegida.longitud - longitudRequerida).toFixed(3));
        elegida.cantidad -= 1;

        if (sobrante > umbralUtil) {
            piezas.push({
                idCorte: elegida.idCorte,
                folio: elegida.folio,
                longitud: sobrante,
                cantidad: 1,
                _virtual: true
            });
        }

        return {
            exito: true,
            idCorte: elegida.idCorte,
            folio: elegida.folio,
            piezaOrigen: elegida.longitud,
            sobrante,
            desperdicio: sobrante <= umbralUtil ? sobrante : 0
        };
    }

    obtenerPiezasBaseParaSimulacion(producto) {
        const desgloseReal = Array.isArray(producto.piezasDisponibles)
            && producto.piezasDisponibles.some(p => p.cantidad > 0);

        let piezas;
        if (desgloseReal) {
            piezas = producto.piezasDisponibles
                .filter(p => p.cantidad > 0)
                .map(p => ({ ...p }));
        } else {
            const existenciaTotal = parseFloat(producto.existencia) || 0;
            piezas = existenciaTotal > 0
                ? [{ idCorte: 0, folio: null, longitud: existenciaTotal, cantidad: 1 }]
                : [];
        }

        const hermanas = this.productos.filter(
            h => h.productoId === producto.productoId && h.id !== producto.id
        );

        hermanas.forEach(hermana => {
            (hermana.cortes || []).forEach(corte => {
                (corte.piezasUsadas || []).forEach(pu => this.consumirPiezaDelPool(piezas, pu, desgloseReal));
            });
        });

        return piezas;
    }

    consumirPiezaDelPool(piezas, piezaUsada, desgloseReal) {
        const cantidadUsada = piezaUsada.cantidad || 1;

        if (desgloseReal && piezaUsada.idCorte) {
            const idx = piezas.findIndex(p => p.idCorte === piezaUsada.idCorte && p.cantidad > 0);
            if (idx >= 0) piezas[idx].cantidad = Math.max(0, piezas[idx].cantidad - cantidadUsada);

            if (piezaUsada.sobrante > 0.5) {
                piezas.push({
                    idCorte: piezaUsada.idCorte,
                    folio: piezaUsada.folio,
                    longitud: piezaUsada.sobrante,
                    cantidad: cantidadUsada,
                    _virtual: true
                });
            }
            return;
        }

        const bar = piezas.find(p => p.idCorte === 0);
        if (bar) {
            const metrosNetos = (piezaUsada.longitud - (piezaUsada.sobrante || 0)) * cantidadUsada;
            bar.longitud = Math.max(0, bar.longitud - metrosNetos);
        }
    }

    renderPedidosTable() {
        const tbody = this.dom.table;
        if (!tbody) return;

        if (this.productos.length === 0) {
            tbody.innerHTML = `
        <tr class="vt-ped-empty-state">
            <td colspan="12" class="text-center py-5">
                <i class="fas fa-box-open fa-3x mb-3 opacity-25"></i>
                <div>No hay productos agregados</div>
                <small class="text-muted">Haz clic en "Agregar" para comenzar</small>
            </td>
        </tr>`;
            return;
        }

        const fragment = document.createDocumentFragment();
        const descDesbloqueado = !!pedidosAuthState.descuentoToken;
        const precioDesbloqueado = !!pedidosAuthState.precioToken;

        this.productos.forEach((p, index) => {
            try {
                // ★ Partida automática de servicio de corte: fila de solo lectura.
                if (p.esServicioCorte) {
                    fragment.appendChild(this.construirFilaServicioCorte(p, index));
                    return;
                }

                const importe = this.calcularPedidosImporte(p);
                const tr = document.createElement('tr');
                tr.dataset.productoId = p.id;

                // ── Celda precio ─────────────────────────────────────────────────
                const precioCell = document.createElement('td');
                precioCell.style.position = 'relative';

                const precioInput = document.createElement('input');
                precioInput.type = 'number';
                precioInput.className = 'vt-ped-product-input vt-ped-precio';
                precioInput.value = p.precio.toFixed(2);
                precioInput.min = '0';
                precioInput.step = '0.01';
                // ★ precio original guardado como data-attribute
                precioInput.dataset.precioOriginal = p.precioOriginal.toFixed(2);

                if (!precioDesbloqueado) {
                    precioInput.disabled = true;
                    const overlayPrecio = document.createElement('div');
                    overlayPrecio.className = 'vt-ped-field-overlay';
                    overlayPrecio.dataset.tipo = 'precio';
                    overlayPrecio.title = 'Haz clic para desbloquear precios';
                    overlayPrecio.style.cssText =
                        'position:absolute;top:0;left:0;width:100%;height:100%;cursor:pointer;z-index:10;';
                    precioCell.appendChild(precioInput);
                    precioCell.appendChild(overlayPrecio);
                } else {
                    precioCell.appendChild(precioInput);
                }

                // ── Celda descuento ───────────────────────────────────────────────
                const descCell = document.createElement('td');
                descCell.style.position = 'relative';

                const descInput = document.createElement('input');
                descInput.type = 'number';
                descInput.className = 'vt-ped-product-input vt-ped-descuento';
                descInput.value = p.descuento;
                descInput.min = '0';
                descInput.max = '100';
                descInput.step = '0.01';

                if (!descDesbloqueado) {
                    descInput.disabled = true;
                    const overlayDesc = document.createElement('div');
                    overlayDesc.className = 'vt-ped-field-overlay';
                    overlayDesc.dataset.tipo = 'descuento';
                    overlayDesc.title = 'Haz clic para desbloquear descuentos';
                    overlayDesc.style.cssText =
                        'position:absolute;top:0;left:0;width:100%;height:100%;cursor:pointer;z-index:10;';
                    descCell.appendChild(descInput);
                    descCell.appendChild(overlayDesc);
                } else {
                    descCell.appendChild(descInput);
                }

                // ── Filas base ────────────────────────────────────────────────────
                tr.innerHTML = `
            <td class="text-center fw-bold">${index + 1}</td>
            <td>${this.escapePedidosHtml(p.productoId)}</td>
            <td style="max-width:200px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;"
                title="${this.escapePedidosHtml(p.descripcion)}">
                ${this.escapePedidosHtml(p.descripcion)}
            </td>
            <td class="text-center">
                <span style="
                    display:inline-block;
                    padding:4px 12px;
                    border-radius:4px;
                    font-weight:600;
                    ${this.obtenerEstiloExistencia(p.existencia, p.unidad)}
                ">
                    ${(p.unidad === 'SRV' || p.unidad === 'SERVICIO') ? '∞' : (p.existencia || 0)}
                </span>
            </td>
            <td>
                <input type="number" class="vt-ped-product-input vt-ped-cantidad"
                       value="${p.cantidad}" min="0" step="0.01">
            </td>
            <td>${this.escapePedidosHtml(p.unidad)}</td>`;

                const corteCell = this.construirPedidosCorteCell(p);
                tr.appendChild(corteCell);

                // Insertar celdas construidas por JS (precio y descuento)
                tr.appendChild(precioCell);
                tr.appendChild(descCell);

                // Continuar con el resto
                const resto = document.createElement('template');
                resto.innerHTML = `
            <td class="vt-ped-importe fw-bold">${importe.toFixed(2)}</td>
            <td>
                <input type="text" class="vt-ped-product-input vt-ped-comentario"
                       value="${this.escapePedidosHtml(p.comentario)}" placeholder="Comentario...">
            </td>
            <td>
                <button type="button" class="vt-ped-btn-delete vt-ped-btn-eliminar">
                    <i class="fas fa-trash"></i>
                </button>
            </td>`;
                tr.append(...resto.content.childNodes);

                fragment.appendChild(tr);
            } catch (err) {
                console.error(`Error renderizando producto índice ${index}:`, p, err);
                // ★ Fila de error visible en vez de tronar todo el render
                const trError = document.createElement('tr');
                trError.innerHTML = `<td colspan="12" class="text-danger text-center">
                Error al mostrar "${this.escapePedidosHtml(p.descripcion || p.productoId || '')}"
            </td>`;
                fragment.appendChild(trError);
            }
        });

        // ★ Esto ahora SIEMPRE se ejecuta, sin importar qué producto haya fallado
        tbody.innerHTML = '';
        tbody.appendChild(fragment);
    }

    // Fila de solo lectura para la partida automática de SERVICIO DE CORTE.
    // Mantiene las mismas 12 columnas de la tabla; sin inputs editables, sin
    // botón de corte ni de eliminar (se gestiona sola desde los cortes).
    construirFilaServicioCorte(p, index) {
        const tr = document.createElement('tr');
        tr.dataset.productoId = p.id;
        tr.className = 'vt-ped-fila-servicio';
        tr.style.background = '#eff6ff';
        const importe = this.calcularPedidosImporte(p);

        tr.innerHTML = `
            <td class="text-center fw-bold">${index + 1}</td>
            <td>${this.escapePedidosHtml(p.productoId)}</td>
            <td style="max-width:200px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;"
                title="${this.escapePedidosHtml(p.descripcion)}">
                <i class="fas fa-cut me-1 text-primary"></i>${this.escapePedidosHtml(p.descripcion)}
                <span class="badge bg-info text-dark ms-1" style="font-size:.6rem;">AUTO</span>
            </td>
            <td class="text-center"><span class="text-muted">∞</span></td>
            <td class="text-center fw-semibold">${p.cantidad}</td>
            <td>${this.escapePedidosHtml(p.unidad)}</td>
            <td class="text-center"><span class="text-muted">—</span></td>
            <td>
                <input type="number" class="vt-ped-product-input" value="${(p.precio || 0).toFixed(2)}"
                       disabled title="Suma automática de los precios de corte">
            </td>
            <td class="text-center"><span class="text-muted">—</span></td>
            <td class="vt-ped-importe fw-bold">${importe.toFixed(2)}</td>
            <td><small class="text-muted">${this.escapePedidosHtml(p.comentario || '')}</small></td>
            <td class="text-center"><span class="text-muted" title="Partida automática"><i class="fas fa-lock"></i></span></td>
        `;
        return tr;
    }

    construirPedidosCorteCell(p) {
        const td = document.createElement('td');
        td.className = 'text-center';

        if (!this.esProductoTubo(p)) {
            td.innerHTML = '<span class="text-muted">—</span>';
            return td;
        }

        const cortes = Array.isArray(p.cortes) ? p.cortes : [];
        const numCortes = cortes.length;
        const btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'vt-ped-btn-corte' + (numCortes > 0 ? ' con-cortes' : '');
        btn.dataset.productoId = p.id;
        btn.innerHTML = numCortes > 0 ? `<i class="fas fa-cut"></i> ${numCortes}` : `<i class="fas fa-cut"></i>`;
        btn.title = numCortes > 0 ? `${numCortes} pieza(s) configurada(s)` : 'Configurar corte de tubo';
        td.appendChild(btn);

        if (numCortes > 0) {
            const suma = this.calcularPedidosSumaCortes(p.cortes);
            if (Math.abs(suma - p.cantidad) > 0.001) {
                const warn = document.createElement('div');
                warn.className = 'vt-ped-corte-warning';
                warn.title = `Suma de cortes (${suma.toFixed(2)}) no coincide con la cantidad (${p.cantidad})`;
                warn.innerHTML = '<i class="fas fa-exclamation-triangle"></i>';
                td.appendChild(warn);
            }
        }

        return td;
    }

    esProductoTubo(producto) {
        const UNIDADES_TUBO = ['ML', 'MTS', 'MT', 'M', 'MTR']; // ajusta a tus claves reales de unidad
        return UNIDADES_TUBO.includes((producto.unidad || '').toUpperCase());
    }

    calcularPedidosSumaCortes(cortes) {
        if (!Array.isArray(cortes)) return 0;
        return cortes.reduce((sum, c) => sum + (parseFloat(c.longitud) || 0) * (parseFloat(c.cantidad) || 0), 0);
    }

    /**
     * Simula la asignación de TODOS los cortes de un producto contra su stock
     * de piezas, sin mutar el inventario real. Usa una copia profunda.
     * Ordena de mayor a menor longitud para no "robarle" al corte grande
     * la pieza ideal de un corte chico.     */
    simularAsignacionCortes(producto, cortes) {
        const piezasCopia = this.obtenerPiezasBaseParaSimulacion(producto);

        const solicitudes = [];
        cortes.forEach(c => {
            const cant = parseInt(c.cantidad) || 0;
            for (let i = 0; i < cant; i++) {
                solicitudes.push({ corteId: c.id, longitud: parseFloat(c.longitud) || 0 });
            }
        });
        solicitudes.sort((a, b) => b.longitud - a.longitud);

        const resultadosPorCorte = {};

        solicitudes.forEach(sol => {
            if (!resultadosPorCorte[sol.corteId]) {
                resultadosPorCorte[sol.corteId] = { exitos: 0, fallos: 0, desperdicioTotal: 0, piezasUsadas: [] };
            }
            const r = this.asignarMejorPieza(piezasCopia, sol.longitud);
            const bucket = resultadosPorCorte[sol.corteId];
            if (r.exito) {
                bucket.exitos++;
                bucket.desperdicioTotal += r.desperdicio;
                bucket.piezasUsadas.push({ idCorte: r.idCorte, folio: r.folio, longitud: r.piezaOrigen, sobrante: r.sobrante });
            } else {
                bucket.fallos++;
            }
        });

        return {
            porCorte: resultadosPorCorte,
            piezasRestantes: piezasCopia,
            totalFallos: Object.values(resultadosPorCorte).reduce((s, r) => s + r.fallos, 0),
            totalDesperdicio: Object.values(resultadosPorCorte).reduce((s, r) => s + r.desperdicioTotal, 0)
        };
    }


    agregarPedidosCorteRow(corte = null) {
        const id = corte?.id || `corte_${Date.now()}_${Math.random()}`;
        const numero = document.querySelectorAll('.vt-corte-card').length + 1;

        const card = document.createElement('div');
        card.className = 'vt-corte-card';
        card.dataset.corteId = id;
        card.innerHTML = `
        <div class="vt-corte-card-numero">${numero}</div>
        <div class="vt-corte-card-field">
            <label>Longitud por pieza (m)</label>
            <input type="number" min="0" step="0.01" class="vt-ped-corte-input vt-ped-corte-longitud"
                   value="${corte?.longitud ?? ''}" placeholder="Ej. 6.00">
        </div>
        <div class="vt-corte-card-field">
            <label>Cuántas piezas</label>
            <input type="number" min="1" step="1" class="vt-ped-corte-input vt-ped-corte-cantidad"
                   value="${corte?.cantidad ?? 1}">
        </div>
        <div class="vt-corte-card-field">
            <label>Precio del corte ($)</label>
            <input type="number" min="0" step="0.01" class="vt-ped-corte-input vt-ped-corte-precio"
                   value="${corte?.precio ?? ''}" placeholder="0.00">
        </div>
        <div class="vt-corte-card-status neutro vt-ped-corte-asignacion">
            <i class="fas fa-minus-circle"></i> <span>Completa los datos</span>
        </div>
        <button type="button" class="vt-ped-btn-delete vt-ped-btn-corte-eliminar" title="Eliminar">
            <i class="fas fa-trash"></i>
        </button>
        <div class="vt-corte-card-comentario">
            <input type="text" class="vt-ped-corte-input vt-ped-corte-comentario"
                   value="${this.escapePedidosHtml(corte?.comentario ?? '')}" placeholder="Comentario (opcional)">
        </div>
    `;
        document.getElementById('vt-ped-corteTable').appendChild(card);
        this.recalcularPedidosResumenCorte();
    }


    recalcularPedidosResumenCorte() {
        const producto = this.productos.find(p => p.id === this.corteState.productoId);
        if (!producto) return;

        const cortesActuales = [];
        let suma = 0;

        document.querySelectorAll('.vt-corte-card').forEach(card => {
            const longitud = parseFloat(card.querySelector('.vt-ped-corte-longitud')?.value) || 0;
            const cantidad = parseFloat(card.querySelector('.vt-ped-corte-cantidad')?.value) || 0;
            suma += longitud * cantidad;

            // ★ FIX: no incluir filas incompletas — no deben consumir inventario en la simulación
            if (longitud > 0 && cantidad > 0) {
                cortesActuales.push({ id: card.dataset.corteId, longitud, cantidad });
            }
        });

        const simulacion = this.simularAsignacionCortes(producto, cortesActuales);

        // Pintar estado de cada tarjeta
        document.querySelectorAll('.vt-corte-card').forEach(card => {
            const corteId = card.dataset.corteId;
            const resultado = simulacion.porCorte[corteId];
            const statusEl = card.querySelector('.vt-ped-corte-asignacion');
            card.classList.remove('ok', 'error');
            statusEl.classList.remove('ok', 'error', 'neutro');

            const longitud = parseFloat(card.querySelector('.vt-ped-corte-longitud')?.value) || 0;
            if (!longitud || (!resultado || (resultado.exitos === 0 && resultado.fallos === 0))) {
                statusEl.classList.add('neutro');
                statusEl.innerHTML = `<i class="fas fa-minus-circle"></i> <span>Completa los datos</span>`;
                return;
            }

            else {
                card.classList.add('ok');
                statusEl.classList.add('ok');
                const piezasResumen = this.agruparPiezasUsadas(resultado.piezasUsadas);

                // ★ FIX: sumar el sobrante real de cada pieza usada, no solo el "desperdicio"
                const sobranteTotal = resultado.piezasUsadas.reduce((s, p) => s + (p.sobrante || 0), 0);

                let detalle;
                if (sobranteTotal <= 0.01) {
                    detalle = 'Sin sobrante';
                } else if (resultado.desperdicioTotal > 0.01) {
                    detalle = `Desperdicio: ${resultado.desperdicioTotal.toFixed(2)}m (retazo no aprovechable)`;
                } else {
                    detalle = `Sobra ${sobranteTotal.toFixed(2)}m (se generará una pieza nueva reutilizable)`;
                }

                statusEl.innerHTML = `<i class="fas fa-check-circle"></i>
        <span>Se cortará de: ${piezasResumen}<br>
        <small>${detalle}</small></span>`;
            }
        });

        // Resumen general
        const total = parseFloat(producto.cantidad) || 0;
        const restante = total - suma;

        document.getElementById('vt-ped-corte-suma').textContent = suma.toFixed(2);
        const restanteEl = document.getElementById('vt-ped-corte-restante');
        restanteEl.textContent = restante.toFixed(2);

        const resumenCard = document.getElementById('vt-ped-corte-resumen');
        resumenCard.style.background = '';
        resumenCard.style.borderColor = '';
        if (Math.abs(restante) < 0.001) {
            resumenCard.style.background = 'var(--vi-green-50, #f0fdf4)';
            resumenCard.style.borderColor = '#86efac';
            restanteEl.style.color = '#15803d';
        } else if (restante < 0) {
            resumenCard.style.background = 'var(--vi-red-50, #fef2f2)';
            resumenCard.style.borderColor = '#fca5a5';
            restanteEl.style.color = '#dc2626';
        } else {
            resumenCard.style.background = 'var(--vi-amber-50, #fffbeb)';
            resumenCard.style.borderColor = '#fde68a';
            restanteEl.style.color = '#d97706';
        }

        document.getElementById('vt-ped-corte-desperdicio').textContent = simulacion.totalDesperdicio.toFixed(2);

        // ★ Costo del servicio de corte = suma de los precios capturados por corte (en vivo)
        const costoEl = document.getElementById('vt-ped-corte-costo');
        if (costoEl) {
            let costoServicio = 0;
            document.querySelectorAll('.vt-corte-card').forEach(card => {
                costoServicio += parseFloat(card.querySelector('.vt-ped-corte-precio')?.value) || 0;
            });
            costoEl.textContent = `$${costoServicio.toFixed(2)}`;
        }

        const stockWarningEl = document.getElementById('vt-ped-corte-stock-warning');
        if (simulacion.totalFallos > 0) {
            stockWarningEl.style.display = 'flex';
            stockWarningEl.querySelector('span').textContent =
                `No hay material suficiente para ${simulacion.totalFallos} pieza(s) solicitada(s). Ajusta las cantidades o revisa el inventario.`;
        } else {
            stockWarningEl.style.display = 'none';
        }

        this.renderPiezasDisponiblesPanel(producto, simulacion.piezasRestantes);
        this._ultimaSimulacionCorte = simulacion;
    }

    // ★ NUEVO
    agruparPiezasUsadas(piezasUsadas) {
        const conteo = {};
        piezasUsadas.forEach(p => { conteo[p.longitud] = (conteo[p.longitud] || 0) + 1; });
        return Object.entries(conteo)
            .map(([longitud, cant]) => `${cant}×${longitud}m`)
            .join(', ');
    }

    /**
     * ★ MODIFICADO: cuando el producto aún no tiene desglose real de piezas
     * físicas (piezasDisponibles vacío o todo en 0), en vez de mostrar
     * "Sin desglose disponible", se toma producto.existencia como referencia
     * de la existencia total en metros, se avisa que aún no se configuran
     * los cortes/piezas existentes, y se permite continuar operando con
     * ese dato mientras tanto.
     */
    renderPiezasDisponiblesPanel(producto, piezasSimuladas) {
        const panel = document.getElementById('vt-ped-corte-piezas-panel');
        if (!panel) return;

        const tieneDesgloseReal = Array.isArray(producto.piezasDisponibles)
            && producto.piezasDisponibles.some(p => p.cantidad > 0);

        if (!tieneDesgloseReal) {
            const existenciaTotal = parseFloat(producto.existencia) || 0;
            if (existenciaTotal <= 0) {
                panel.innerHTML = '<div class="text-muted small">Sin existencia disponible para este producto.</div>';
                return;
            }
            const cantRestante = piezasSimuladas
                .filter(s => s.idCorte === 0)
                .reduce((sum, s) => sum + (s.cantidad || 0), 0);
            const porcentaje = cantRestante > 0 ? 0 : 100;

            panel.innerHTML = `
            <div class="vt-corte-alerta" style="background:#fffbeb;color:#92400e;border:1px solid #fde68a;">
                <i class="fas fa-exclamation-circle"></i>
                <span>Este producto aún no tiene su inventario dividido en piezas específicas.
                Se usará la existencia total (${existenciaTotal}m) como una sola barra.</span>
            </div>
            <div class="vt-corte-pieza-row">
                <span class="vt-corte-pieza-folio">Existencia total</span>
                <div class="vt-corte-pieza-bar-wrap">
                    <div class="vt-corte-pieza-bar-fill ${porcentaje >= 100 ? 'agotada' : ''}" style="width:${porcentaje}%;"></div>
                </div>
                <span class="vt-corte-pieza-cantidad">
                    <strong>${cantRestante > 0 ? 'Disponible' : 'Usada'}</strong> · ${existenciaTotal}m
                </span>
            </div>`;
            return;
        }

        const piezasOriginales = producto.piezasDisponibles.filter(p => p.cantidad > 0);
        if (!piezasOriginales.length) {
            panel.innerHTML = '<div class="text-muted small">No hay piezas disponibles.</div>';
            return;
        }

        panel.innerHTML = piezasOriginales
            .sort((a, b) => b.longitud - a.longitud)
            .map(p => {
                const restante = piezasSimuladas.find(s => Math.abs(s.longitud - p.longitud) < 0.01);
                const cantRestante = restante ? restante.cantidad : 0;
                const usadas = p.cantidad - cantRestante;
                const porcentajeUsado = p.cantidad > 0 ? Math.min(100, (usadas / p.cantidad) * 100) : 0;

                return `
            <div class="vt-corte-pieza-row">
                <span class="vt-corte-pieza-folio" title="${this.escapePedidosHtml(p.folio || '')}">
                    ${this.escapePedidosHtml(p.folio || 'Sin folio')}
                </span>
                <div class="vt-corte-pieza-bar-wrap">
                    <div class="vt-corte-pieza-bar-fill ${cantRestante === 0 ? 'agotada' : ''}" style="width:${porcentajeUsado}%;"></div>
                </div>
                <span class="vt-corte-pieza-cantidad">
                    <strong>${cantRestante}</strong>/${p.cantidad} pza · ${p.longitud}m c/u
                    ${usadas > 0 ? `<br><small>${usadas} en uso en este pedido</small>` : ''}
                </span>
            </div>`;
            }).join('');
    }

    guardarPedidosCorte() {
        const producto = this.productos.find(p => p.id === this.corteState.productoId);
        if (!producto) return;

        const cortes = [];
        let error = null;

        // ★ FIX: usar .vt-corte-card en vez de 'tr' (ya no es una tabla)
        this.corteTableBody.querySelectorAll('.vt-corte-card').forEach(card => {
            const longitud = parseFloat(card.querySelector('.vt-ped-corte-longitud')?.value) || 0;
            const cantidad = parseFloat(card.querySelector('.vt-ped-corte-cantidad')?.value) || 0;
            const comentario = card.querySelector('.vt-ped-corte-comentario')?.value || '';
            const precio = parseFloat(card.querySelector('.vt-ped-corte-precio')?.value) || 0;

            if (longitud <= 0 || cantidad <= 0) {
                error = 'Todas las piezas deben tener longitud y cantidad mayores a 0';
                return;
            }
            cortes.push({ id: card.dataset.corteId, longitud, cantidad, comentario, precio });
        });

        if (error) {
            toastMixin?.fire({ icon: 'error', title: error });
            return;
        }

        const suma = this.calcularPedidosSumaCortes(cortes);
        const total = parseFloat(producto.cantidad) || 0;

        if (suma > total + 0.001) {
            toastMixin?.fire({
                icon: 'error',
                title: `La suma de los cortes (${suma.toFixed(2)}) excede la cantidad del producto (${total.toFixed(2)})`
            });
            return;
        }

        // ★ Validar contra stock físico real (o contra existencia total si aún
        // no hay desglose real de piezas — ver obtenerPiezasEfectivas)
        const simulacion = this.simularAsignacionCortes(producto, cortes);
        if (simulacion.totalFallos > 0) {
            toastMixin?.fire({
                icon: 'error',
                title: `No hay stock suficiente: ${simulacion.totalFallos} pieza(s) solicitada(s) no caben en ninguna barra disponible.`
            });
            return;
        }

        // ★ Aplicar la asignación real al inventario del producto en memoria
        // (esto se re-valida y aplica en definitiva en el backend al guardar el pedido).
        // Si el producto no tenía desglose real, esto queda como una pieza virtual
        // en memoria; el backend es quien determina el desglose físico final.
        producto.piezasDisponibles = simulacion.piezasRestantes;

        // ★ Guardar de qué pieza salió cada corte, para mandarlo al backend
        cortes.forEach(c => {
            const resultado = simulacion.porCorte[c.id];
            const piezasDetalle = resultado ? resultado.piezasUsadas : [];           
            c.piezasUsadas = this.agruparPiezasParaEnvio(piezasDetalle);
            c.desperdicio = resultado ? +resultado.desperdicioTotal.toFixed(2) : 0;
        });

        producto.cortes = cortes;
        // ★ Recalcular la partida dinámica de SERVICIO DE CORTE (suma de precios de corte)
        this.sincronizarPedidosServicioCorte();
        this.renderPedidosTable();
        this.calcularPedidosTotales();

        bootstrap.Modal.getInstance(this.corteModalEl)?.hide();
        toastMixin?.fire({ icon: 'success', title: 'Configuración de corte guardada' });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Partida dinámica "SERVICIO DE CORTE": una sola partida cuyo precio es la
    // suma de los precios capturados en TODOS los cortes de todos los tubos del
    // pedido. Se agrega/actualiza/elimina automáticamente y es de solo lectura.
    // Al ser una fila más de this.productos fluye sola por totales, payload y
    // backend (producto 'CORTE' udm SRV → partida normal, sin inventario).
    // ─────────────────────────────────────────────────────────────────────────
    sincronizarPedidosServicioCorte() {
        const CVE_SERVICIO = 'CORTE';

        let total = 0;
        this.productos.forEach(p => {
            if (p.esServicioCorte || !Array.isArray(p.cortes)) return;
            p.cortes.forEach(c => { total += parseFloat(c.precio) || 0; });
        });
        total = +total.toFixed(2);

        let servicio = this.productos.find(p => p.esServicioCorte);

        if (total > 0) {
            if (servicio) {
                servicio.precio = total;
                servicio.precioOriginal = total;
                servicio.cantidad = 1;
            } else {
                this.productos.push({
                    id: `servicio_corte_${Date.now()}`,
                    productoId: CVE_SERVICIO,
                    descripcion: 'SERVICIO DE CORTE',
                    existencia: 0,
                    cantidad: 1,
                    precio: total,
                    precioOriginal: total,
                    descuento: 0,
                    unidad: 'SRV',
                    existenciaGeneral: 0,
                    existenciaModular: 0,
                    comentario: 'Servicio de corte (automático)',
                    cortes: [],
                    piezasDisponibles: [],
                    esServicioCorte: true
                });
            }
        } else if (servicio) {
            // Ya no hay cortes con precio → quitar la partida de servicio.
            this.productos = this.productos.filter(p => !p.esServicioCorte);
        }
    }

    agruparPiezasParaEnvio(piezasDetalle) {
        const grupos = new Map();
        piezasDetalle.forEach(p => {
            const key = p.idCorte ?? 'sin_desglose';   // ★ FIX: ya no se descarta
            if (!grupos.has(key)) {
                grupos.set(key, { idCorte: p.idCorte ?? 0, folio: p.folio ?? null, longitud: p.longitud, cantidad: 0, sobrante: p.sobrante });
            }
            grupos.get(key).cantidad += 1;
        });
        return Array.from(grupos.values());
    }

    obtenerEstiloExistencia(existencia, unidad = '') {
        if (unidad === 'SRV' || unidad === 'SERVICIO') {
            return 'background-color: #22c55e; color: white;';
        }
        const cantidad = parseFloat(existencia) || 0;
        if (cantidad === 0) return 'background-color: #dc3545; color: white;';
        if (cantidad <= 5) return 'background-color: #ffc107; color: #000;';
        if (cantidad <= 20) return 'background-color: #0dcaf0; color: #000;';
        return 'background-color: #198754; color: white;';
    }

    actualizarPedidosFila(productoId) {
        const producto = this.productos.find(p => p.id === productoId);
        if (!producto) return;

        const tr = this.dom.table.querySelector(`tr[data-producto-id="${productoId}"]`);
        if (!tr) return;

        producto.cantidad = parseFloat(tr.querySelector('.vt-ped-cantidad').value) || 0;
        producto.comentario = tr.querySelector('.vt-ped-comentario').value || '';

        // ── Validación de precio (igual que cotizaciones) ─────────────────────
        const precioInput = tr.querySelector('.vt-ped-precio');
        if (precioInput && !precioInput.disabled) {
            const nuevoPrecio = parseFloat(precioInput.value) || 0;
            const precioOriginal = parseFloat(precioInput.dataset.precioOriginal) || 0;

            if (!pedidosAuthState.precioToken && Math.abs(nuevoPrecio - precioOriginal) > 0.001) {
                precioInput.value = precioOriginal.toFixed(2);
                producto.precio = precioOriginal;
                toastMixin.fire({
                    icon: 'warning',
                    title: 'Se requiere autorización para modificar el precio'
                });
            } else {
                producto.precio = nuevoPrecio;
            }
        }

        // ── Validación de descuento (igual que cotizaciones) ──────────────────
        const descInput = tr.querySelector('.vt-ped-descuento');
        if (descInput && !descInput.disabled) {
            const nuevoDesc = parseFloat(descInput.value) || 0;

            if (!pedidosAuthState.descuentoToken && nuevoDesc > 0) {
                descInput.value = '0';
                producto.descuento = 0;
                toastMixin.fire({
                    icon: 'warning',
                    title: 'Se requiere autorización para aplicar descuento'
                });
            } else {
                producto.descuento = nuevoDesc;
            }
        }

        tr.querySelector('.vt-ped-importe').textContent = this.calcularPedidosImporte(producto).toFixed(2);
        this.calcularPedidosTotales();
    }

    eliminarPedidosProducto(productoId) {
        // La partida de SERVICIO DE CORTE es automática: no se elimina a mano.
        const objetivo = this.productos.find(p => p.id === productoId);
        if (objetivo?.esServicioCorte) {
            toastMixin?.fire({ icon: 'info', title: 'La partida de servicio de corte se calcula sola desde los cortes.' });
            return;
        }
        this.productos = this.productos.filter(p => p.id !== productoId);
        // Al quitar un tubo pueden cambiar los cortes → recalcular el servicio.
        this.sincronizarPedidosServicioCorte();
        this.renderPedidosTable();
        this.calcularPedidosTotales();
    }

    /**
     * Desbloquea visualmente todos los campos de un tipo.
     * Elimina overlays y habilita los inputs.
     * La autorización real la garantiza el token en el servidor.
     */
    desbloquearPedidosCampos(tipo) {
        const selector = tipo === 'descuento' ? '.vt-ped-descuento' : '.vt-ped-precio';

        this.dom.table?.querySelectorAll(selector).forEach(input => {
            input.removeAttribute('disabled');

            const overlay = input.parentElement?.querySelector('.vt-ped-field-overlay');
            overlay?.remove();
        });
    }

    calcularPedidosImporte(producto) {
        return producto.cantidad * producto.precio * (1 - producto.descuento / 100);
    }

    calcularPedidosTotales() {
        const tipoPago = document.getElementById('vt-ped-toggle-pago')?.getAttribute('data-state') || 'contado';
        const flete = parseFloat(document.getElementById('vt-ped-flete-val')?.value) || 0;
        let subtotal1 = 0, descuento = 0, subtotal2 = 0, iva = 0, total = 0;

        if (tipoPago === 'anticipo') {
            const montoAnticipo = parseFloat(document.getElementById('vt-ped-monto-anticipo')?.value) || 0;
            subtotal1 = montoAnticipo;
            descuento = 0;
            subtotal2 = montoAnticipo;
            iva = montoAnticipo * 0.16;
            total = subtotal2 + iva;
        } else {
            const totales = this.productos.reduce((acc, p) => {
                const importeSinDesc = p.cantidad * p.precio;
                const descuentoMonto = importeSinDesc * (p.descuento / 100);
                acc.subtotal1 += importeSinDesc;
                acc.totalDescuento += descuentoMonto;
                return acc;
            }, { subtotal1: 0, totalDescuento: 0 });

            subtotal1 = totales.subtotal1;
            descuento = totales.totalDescuento;
            subtotal2 = subtotal1 - descuento + flete;
            iva = subtotal2 * 0.16;
            total = subtotal2 + iva;
        }

        this.totales = { subtotal1, descuento, flete, subtotal2, iva, total };

        this.updatePedidosDisplay(this.dom.subtotal1Display, Currency.format(subtotal1));
        this.updatePedidosDisplay(this.dom.descuentoDisplay, Currency.format(descuento), '-$');
        this.updatePedidosDisplay(this.dom.fleteDisplay, Currency.format(flete));
        this.updatePedidosDisplay(this.dom.subtotal2Display, Currency.format(subtotal2));
        this.updatePedidosDisplay(this.dom.ivaDisplay, Currency.format(iva));
        this.updatePedidosDisplay(this.dom.importeDisplay, Currency.format(total));

        this.updatePedidosHiddenField('vt-ped-total-subtotal1', subtotal1);
        this.updatePedidosHiddenField('vt-ped-total-descuento', descuento);
        this.updatePedidosHiddenField('vt-ped-total-flete', flete);
        this.updatePedidosHiddenField('vt-ped-total-subtotal2', subtotal2);
        this.updatePedidosHiddenField('vt-ped-iva', iva);
        this.updatePedidosHiddenField('vt-ped-importe', total);

        _validarCreditoConTotalPedido();
    }

    updatePedidosHiddenField(id, value) {
        const field = document.getElementById(id);
        if (field) field.value = value.toFixed(2);
    }

    updatePedidosDisplay(element, value, prefix = '$') {
        if (element) element.textContent = `${value}`;
    }

    escapePedidosHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    /**
     * ★ Lee el precio del array JS (que actualizarPedidosFila() ya validó),
     *   NO del DOM — así aunque el usuario edite el input desde DevTools,
     *   el valor del array solo cambia si pasó la validación.
     * ★ Incluye precioOriginal para que el servidor haga su propia verificación.
     */
    getPedidosProductosData() {
        return this.productos.map(p => ({
            ...p,
            importe: this.calcularPedidosImporte(p)
        }));
    }
}

// ============================================
// 5. GESTOR DE FORMULARIO
// ============================================
class PedidosFormManager {
    constructor(productManager) {
        this.productManager = productManager;
        this.form = document.getElementById('vt-ped-formCotizacion');
        this.btnSubmit = document.querySelector('.vt-ped-btn-submit');
        this.initPedidosFormManager();
    }

    initPedidosFormManager() {
        this.btnSubmit?.addEventListener('click', (e) => {
            e.preventDefault();
            this.enviarPedidosCotizacion();
        });
    }

    recopilarPedidosDatosFormulario() {
        const formData = new FormData();
        const toggle = document.getElementById('vt-ped-toggle-pago');
        const tipoPago = toggle?.getAttribute('data-state') || 'contado';

        const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
        if (token) formData.append('__RequestVerificationToken', token);

        const camposGenerales = [
            'vt-ped-tipo-docto-mov',
            'vt-ped-folio', 'vt-ped-documentid', 'vt-ped-cliente',
            'vt-ped-rfc', 'vt-ped-vendedor', 'vt-ped-moneda',
            'vt-ped-paridad', 'vt-ped-concepto',
            'vt-ped-metodo-pago', 'vt-ped-forma-pago',
            'vt-ped-uso-cfdi', 'vt-ped-comentarios'
        ];

        camposGenerales.forEach(id => {
            const el = document.getElementById(id);
            if (el) formData.append(id.replace('vt-ped-', ''), el.value || '');
        });

        formData.append('ordenCompra', document.getElementById('vt-ped-orden-compra-val')?.value || '');
        //formData.set('sucursal', suc.getValue());
        //formData.set('almacen', alm.getValue());

        if (tipoPago === 'credito') {
            formData.append('limiteCredito', document.getElementById('vt-ped-limite-credito')?.value || '');
            formData.append('plazo', document.getElementById('vt-ped-plazo')?.value || '');
            formData.append('fechaPago', document.getElementById('vt-ped-fecha-pago')?.value || '');
        }

        if (tipoPago === 'anticipo') {
            formData.append('fechaAnticipo', document.getElementById('vt-ped-fecha-anticipo')?.value || '');
        }

        const productos = this.productManager.getPedidosProductosData();
        if (tipoPago !== 'anticipo') {
            formData.append('productosJSON', JSON.stringify(productos));
        }

        const totales = this.productManager.totales;
        formData.append('subtotal1', totales.subtotal1.toFixed(2));
        formData.append('descuento', totales.descuento.toFixed(2));
        formData.append('flete', totales.flete.toFixed(2));
        formData.append('subtotal2', totales.subtotal2.toFixed(2));
        formData.append('iva', totales.iva.toFixed(2));
        formData.append('total', totales.total.toFixed(2));

        formData.append('tipo', tipoPago);

        // ★ Enviar tokens al servidor para validación
        formData.append('descuentoToken', pedidosAuthState.descuentoToken || '');
        formData.append('precioToken', pedidosAuthState.precioToken || '');

        return formData;
    }

    validarPedidosFormulario() {
        const errores = [];

        const togglePago = document.getElementById('vt-ped-toggle-pago');
        const tipoPago = togglePago ? togglePago.getAttribute('data-state') : 'contado';

        const cliente = document.getElementById('vt-ped-cliente')?.value;
        if (!cliente || cliente.trim() === '') {
            errores.push('Debe seleccionar un cliente');
        }

        const productos = this.productManager.getPedidosProductosData();
        productos.forEach(p => {
            if (this.productManager.esProductoTubo(p)) {
                if (!Array.isArray(p.cortes) || p.cortes.length === 0) {
                    errores.push(`"${p.descripcion}": debes configurar el corte antes de guardar el pedido.`);
                    return;
                }
                const suma = this.productManager.calcularPedidosSumaCortes(p.cortes);
                if (Math.abs(suma - p.cantidad) > 0.001) {
                    errores.push(`"${p.descripcion}": la configuración de corte (${suma.toFixed(2)}) no coincide con la cantidad (${p.cantidad})`);
                }
            }
        });
        const vendedor = document.getElementById('vt-ped-vendedor')?.value;
        if (!vendedor || vendedor === 'Seleccionar vendedor') {
            errores.push('Debe seleccionar un vendedor');
        }

        const rfcInput = document.getElementById('vt-ped-rfc');
        const rfc = rfcInput ? rfcInput.value.trim() : '';
        if (!rfc) {
            errores.push('Debe ingresar el RFC');
        }

        const monedaSelect = tomManager.getInstance('vt-ped-moneda');
        const monedaValue = monedaSelect?.getValue();
        if (!monedaValue) {
            errores.push('Debe seleccionar una Moneda');
        }

        const usoCfdiSelect = tomManager.getInstance('vt-ped-uso-cfdi');
        const usoCfdiValue = usoCfdiSelect?.getValue();
        if (!usoCfdiValue) {
            errores.push('Debe seleccionar un Uso CFDI');
        }

        const formaPagoSelect = tomManager.getInstance('vt-ped-forma-pago');
        const formaPagoValue = formaPagoSelect?.getValue();
        if (!formaPagoValue) {
            errores.push('Debe seleccionar una Forma de Pago');
        }

        const fechaPagoInput = document.getElementById('vt-ped-fecha-pago');
        const fechaPago = fechaPagoInput ? fechaPagoInput.value.trim() : '';
        if (tipoPago === 'credito' && !fechaPago) {
            errores.push('Debe ingresar la Fecha de Pago (solo para crédito)');
        }

        // ★ Validaciones de autorización (igual que cotizaciones)
        //if (!pedidosAuthState.descuentoToken) {
        //    const conDescuento = productos.some(p => p.descuento > 0);
        //    if (conDescuento) errores.push('Hay productos con descuento. Se requiere autorización.');
        //}

        //if (!pedidosAuthState.precioToken) {
        //    const conPrecioModificado = productos.some(p =>
        //        Math.abs(p.precio - p.precioOriginal) > 0.001
        //    );
        //    if (conPrecioModificado) errores.push('Hay productos con precio modificado. Se requiere autorización.');
        //}

        return errores;
    }

    obtenerPedidosConfiguracionPorTipo(tipoMovimiento) {
        const configuraciones = {
            'alta': {
                endpoint: '/VTPedido/Guardar',
                metodo: 'POST',
                mensajeExito: 'Pedido creado exitosamente',
                textoCarga: 'Guardando...',
                validarProductos: true
            },
            'modificacion': {
                endpoint: '/VTPedido/Modificar',
                metodo: 'PUT',
                mensajeExito: 'Pedido modificado exitosamente',
                textoCarga: 'Actualizando...',
                validarProductos: true
            },
            'consulta': {
                endpoint: '/VTPedido/Consultar',
                metodo: 'GET',
                mensajeExito: 'Consulta realizada exitosamente',
                textoCarga: 'Consultando...',
                validarProductos: false
            }
        };

        return configuraciones[tipoMovimiento] || configuraciones['alta'];
    }

    async enviarPedidosCotizacion() {
        const tipoMovimiento = document.getElementById('vt-ped-tipo-docto-mov')?.value || 'alta';
        const config = this.obtenerPedidosConfiguracionPorTipo(tipoMovimiento);

        const errores = this.validarPedidosFormulario();
        if (errores.length > 0) {
            if (window.toastMixin) {
                toastMixin.fire({ icon: 'error', title: errores.join(' | ') });
            }
            return;
        }

        const originalText = this.btnSubmit.innerHTML;
        this.btnSubmit.disabled = true;
        this.btnSubmit.innerHTML = `<i class="fas fa-spinner fa-spin"></i> ${config.textoCarga}`;

        try {
            const formData = this.recopilarPedidosDatosFormulario();
            const response = await fetch(config.endpoint, {
                method: config.metodo.toUpperCase(),
                body: formData
            });
            const result = await response.json();

            if (result.success) {
                if (window.Swal) {
                    await Swal.fire({
                        icon: 'success',
                        title: `${result.message || ''} Folio: ${result.folio_generado || ''}`.trim() || config.mensajeExito,
                        confirmButtonText: 'Aceptar'
                    });
                    location.reload();
                }
            } else {
                if (window.toastMixin) {
                    toastMixin.fire({
                        icon: 'error',
                        title: result.message || 'Ocurrió un error en el servidor'
                    });
                }
            }
        } catch (error) {
            console.error('Error enviando pedido:', error);
            if (window.toastMixin) {
                toastMixin.fire({
                    icon: 'error',
                    title: error.message || 'Ocurrió un error al enviar el pedido'
                });
            }
        } finally {
            this.btnSubmit.disabled = false;
            this.btnSubmit.innerHTML = originalText;
        }
    }

    limpiarPedidosFormulario() {
        this.form?.reset();
        this.productManager.productos = [];
        this.productManager.renderPedidosTable();
        this.productManager.calcularPedidosTotales();
        document.getElementById('vt-ped-cliente').value = '';
        document.getElementById('vt-ped-rfc').value = '';
        document.getElementById('vt-ped-info-proveedor').value = '';
    }

    verPedidosDatosFormulario() {
        const formData = this.recopilarPedidosDatosFormulario();
        const obj = {};
        for (let [key, value] of formData.entries()) obj[key] = value;
        console.log('Datos de pedido a enviar:', obj);
        return obj;
    }
}

// ============================================
// 6. APLICACIÓN PRINCIPAL
// ============================================
const PedidosApp = {
    clienteManager: null,
    productoManager: null,
    documentoManager: null,
    productManagerInstance: null,
    formManager: null,
    form: null,
    btnSubmit: null,

    async initPedidos() {
        this.form = document.getElementById('vt-ped-formCotizacion');
        this.btnSubmit = document.querySelector('.vt-ped-btn-submit');

        this.clienteManager = new PedidosDataManager({
            endpoint: '/DatosGenerales/BuscarC',
            detailEndpoint: '/DatosGenerales/BuscarCliente',
            modalId: 'vt-ped-modalBuscarCliente',
            inputId: 'vt-ped-inputBuscarCliente',
            resultsId: 'vt-ped-listaResultadosClientes',
            pageSizeId: 'vt-ped-pageSizeClientes',
            btnClearId: 'vt-ped-btnLimpiarClientes',
            spinnerId: 'vt-ped-spinnerClientes',
            paginationId: 'vt-ped-paginationClientes',
            recordsFromId: 'vt-ped-recordsFromClientes',
            recordsToId: 'vt-ped-recordsToClientes',
            totalRecordsId: 'vt-ped-totalRecordsClientes',
            shouldCloseOnSelect: true
        });

        this.clienteManager.onPedidosSelect = (cliente) => {
            document.getElementById('vt-ped-cliente').value = cliente.id || '';
            document.getElementById('vt-ped-rfc').value = cliente.rfc || '';
            document.getElementById('vt-ped-info-proveedor').value =
                [cliente.dir, cliente.col, cliente.pob, cliente.cp]
                    .filter(x => x)
                    .join(',\n');

            const vendedorInstance = tomManager.getInstance('vt-ped-vendedor');
            if (vendedorInstance && cliente.cve_vdr) {
                vendedorInstance.setValue(cliente.cve_vdr, true);
            }
            const fpagoInstance = tomManager.getInstance('vt-ped-forma-pago');
            if (fpagoInstance && cliente.forma_pago) {
                fpagoInstance.setValue(cliente.forma_pago, true);
            }
            const cfdiInstance = tomManager.getInstance('vt-ped-uso-cfdi');
            if (cfdiInstance && cliente.uso_sugerido) {
                cfdiInstance.setValue(cliente.uso_sugerido, true);
            }
            const totalActual = PedidosApp.productManagerInstance?.totales?.total || 0;
            mostrarBannerCredito(cliente, totalActual);
        };

        this.productoManager = new PedidosDataManager({
            endpoint: '/DatosGenerales/BuscarP',
            detailEndpoint: '/DatosGenerales/BuscarProducto',
            modalId: 'vt-ped-modalBuscarProducto',
            inputId: 'vt-ped-inputBuscarProducto',
            resultsId: 'vt-ped-listaResultadosProductos',
            pageSizeId: 'vt-ped-pageSizeProductos',
            btnClearId: 'vt-ped-btnLimpiarProductos',
            spinnerId: 'vt-ped-spinnerProductos',
            paginationId: 'vt-ped-paginationProductos',
            recordsFromId: 'vt-ped-recordsFromProductos',
            recordsToId: 'vt-ped-recordsToProductos',
            totalRecordsId: 'vt-ped-totalRecordsProductos',
            shouldCloseOnSelect: false
        });

        this.productManagerInstance = new PedidosProductManager();

        this.productoManager.onPedidosSelect = (producto) => {
            this.productManagerInstance.agregarPedidosProducto(producto);
        };

        this.documentoManager = new PedidosDocumentoManager({
            endpoint: '/DatosGenerales/BuscarVTD',
            detailEndpoint: '/DatosGenerales/BuscarDocumentoConCortes',
            modalId: 'vt-ped-modalBuscarDocumentos',
            inputId: 'vt-ped-inputBuscarDocumento',
            resultsId: 'vt-ped-listaResultadosDocumentos',
            pageSizeId: 'vt-ped-pageSizeDocumentos',
            btnClearId: 'vt-ped-btnLimpiarDocumentos',
            spinnerId: 'vt-ped-spinnerDocumentos',
            paginationId: 'vt-ped-paginationDocumentos',
            recordsFromId: 'vt-ped-recordsFromDocumentos',
            recordsToId: 'vt-ped-recordsToDocumentos',
            totalRecordsId: 'vt-ped-totalRecordsDocumentos',
            shouldCloseOnSelect: true,
            filtros: {}
        });

        const filtroEstado = document.getElementById('vt-ped-filtroEstado');
        const filtroFecha = document.getElementById('vt-ped-filtroFecha');

        filtroEstado?.addEventListener('change', (e) => {
            this.documentoManager.filtros.estado = e.target.value;
            this.documentoManager.state.page = 1;
            this.documentoManager.fetchPedidosData();
        });

        filtroFecha?.addEventListener('change', (e) => {
            this.documentoManager.filtros.fecha = e.target.value;
            this.documentoManager.state.page = 1;
            this.documentoManager.fetchPedidosData();
        });

        this.documentoManager.onPedidosSelect = async (documento) => {
            try {
                if (window.Swal) {
                    Swal.fire({
                        title: 'Cargando documento...',
                        html: 'Por favor espere',
                        allowOutsideClick: false,
                        didOpen: () => { Swal.showLoading(); }
                    });
                }

                await this.cargarDocumentoCompleto(documento);

                if (window.Swal) Swal.close();

                if (window.toastMixin) {
                    toastMixin.fire({
                        icon: 'success',
                        title: `Documento ${documento.folio} cargado correctamente`
                    });
                }
            } catch (error) {
                console.error('Error cargando documento:', error);
                if (window.Swal) {
                    Swal.fire({ icon: 'error', title: 'Error', text: 'No se pudo cargar el documento completo' });
                }
            }
        };
        await this.cargarPermisosUsuario();
        this.formManager = new PedidosFormManager(this.productManagerInstance);

        const tipoMovSelect = document.getElementById('vt-ped-tipo-docto-mov');
        tipoMovSelect?.addEventListener('change', (e) => {
            this.manejarPedidosCambioTipoMovimiento(e.target.value);
        });

        this.manejarPedidosCambioTipoMovimiento(tipoMovSelect?.value || 'alta');

        console.log('Sistema de pedidos inicializado correctamente');

        window.verPedidosDatosFormulario = () => this.formManager.verPedidosDatosFormulario();
    },
    async cargarPermisosUsuario() {
        try {
            const response = await fetch('/DatosGenerales/ObtenerPermisosUsuario');
            if (!response.ok) return;
            const permisos = await response.json();

            if (permisos.tienePrecio && permisos.tokenPrecio) {
                pedidosAuthState.precioToken = permisos.tokenPrecio;  // ★ pedidosAuthState, no authState
                console.log('✓ Precio desbloqueado automáticamente');
            }
            if (permisos.tieneDescuento && permisos.tokenDescuento) {
                pedidosAuthState.descuentoToken = permisos.tokenDescuento;  // ★ pedidosAuthState, no authState
                console.log('✓ Descuento desbloqueado automáticamente');
            }
        } catch (err) {
            console.warn('No se pudieron verificar permisos automáticos:', err);
        }
    },
    async cargarDocumentoCompleto(documento) {
        document.getElementById('vt-ped-sucursal').value = documento.sucursal || '';
        document.getElementById('vt-ped-almacen').value = documento.almacen || '';
        document.getElementById('vt-ped-documentid').value = documento.id_encabezado || '';

        document.getElementById('vt-ped-cliente').value = documento.cli_prov || '';
        document.getElementById('vt-ped-rfc').value = documento.rfc || '';
        document.getElementById('vt-ped-info-proveedor').value = documento.info_cli || '';
        document.getElementById('vt-ped-paridad').value = documento.par || '';

        const vendedorInstance = tomManager.getInstance('vt-ped-vendedor');
        if (vendedorInstance && (documento.vdr_cpr || documento.cve_vdr)) {
            vendedorInstance.setValue(documento.vdr_cpr || documento.cve_vdr, true);
        }

        const monedaInstance = tomManager.getInstance('vt-ped-moneda');
        if (monedaInstance && (documento.ccy || documento.moneda)) {
            monedaInstance.setValue(documento.ccy || documento.moneda, true);
        }

        const cfdiInstance = tomManager.getInstance('vt-ped-uso-cfdi');
        if (cfdiInstance && (documento.usoCfdi || documento.uso_cfdi || documento.uso_sugerido)) {
            cfdiInstance.setValue(documento.usoCfdi || documento.uso_cfdi || documento.uso_sugerido, true);
        }

        const formaPagoInstance = tomManager.getInstance('vt-ped-forma-pago');
        if (formaPagoInstance && (documento.formaPago || documento.forma_pago)) {
            formaPagoInstance.setValue(documento.formaPago || documento.forma_pago, true);
        }

        const metodoPagoInstance = tomManager.getInstance('vt-ped-metodo-pago');
        if (metodoPagoInstance && (documento.metodoPago || documento.metodo_pago)) {
            metodoPagoInstance.setValue(documento.metodoPago || documento.metodo_pago, true);
        }
        if (documento.mdp) {
            // 1. Actualizar el select de método de pago
            const metodoPagoEl = document.getElementById('vt-ped-metodo-pago');
            if (metodoPagoEl) metodoPagoEl.value = documento.mdp;

            // 2. Sincronizar el toggle contado/crédito según el mdp
            const tipoPago = documento.mdp === 'PPD' ? 'credito' : 'contado';
            selectTipoPagoPed(tipoPago);
        }
        document.getElementById('vt-ped-limite-credito').value = documento.lim_crd || '';
        document.getElementById('vt-ped-plazo').value = documento.pl_crd || '';
        calcularFechaPagoPed(); // ← calcula desde hoy + plazo
        document.getElementById('vt-ped-concepto').value = documento.coment1 || '';
        document.getElementById('vt-ped-comentarios').value = documento.coment_aut || '';

        this.productManagerInstance.productos = [];   // ★ también asegúrate de limpiar antes, si no lo tienes ya

        documento.productos.forEach(prod => {
            const precio = parseFloat(prod.precio) || 0;

            let cortesNormalizados = [];
            if (Array.isArray(prod.cortes)) {
                cortesNormalizados = prod.cortes;
            } else if (typeof prod.cortes === 'string' && prod.cortes.trim() !== '') {
                try {
                    const parsed = JSON.parse(prod.cortes);
                    cortesNormalizados = Array.isArray(parsed) ? parsed : [];
                } catch (e) {
                    console.warn('No se pudo parsear cortes:', prod.cortes, e);
                    cortesNormalizados = [];
                }
            }

            const productoData = {
                id: `prod_${Date.now()}_${Math.random()}`,
                productoId: prod.producto_id || prod.id,
                descripcion: prod.descripcion,
                existencia: prod.existencia,
                cantidad: parseFloat(prod.cantidad) || 0,
                precio: precio,
                precioOriginal: precio,
                descuento: parseFloat(prod.descuento) || 0,
                unidad: prod.unidad || prod.udm || 'PZA',
                existenciaGeneral: prod.existenciaGeneral || 0,
                existenciaModular: prod.existenciaModular || 0,
                comentario: prod.comentario || '',
                cortes: cortesNormalizados,
                // ★ FIX: antes faltaba este campo, lo que provocaba
                // "Cannot read properties of undefined (reading 'map')"
                // al abrir el modal de corte para productos cargados
                // desde un documento existente.
                piezasDisponibles: this.productManagerInstance.normalizarPiezasDisponibles(prod)
            };

            // ★ La partida CORTE (SERVICIO DE CORTE) es automática: se marca como
            //   servicio para renderla de solo lectura y que no se duplique al
            //   recalcularla desde los precios de corte.
            if ((productoData.productoId === 'CORTE') && (productoData.unidad || '').toUpperCase() === 'SRV') {
                productoData.esServicioCorte = true;
                productoData.cortes = [];
            }

            this.productManagerInstance.productos.push(productoData);
        });

        // Recalcular/normalizar la partida de servicio con los precios de corte cargados.
        this.productManagerInstance.sincronizarPedidosServicioCorte();
        this.productManagerInstance.renderPedidosTable();

        const fleteInput = document.getElementById('vt-ped-flete-val');
        if (fleteInput && documento.flete) {
            const chkFlete = document.getElementById("vt-ped-flete");
            chkFlete.checked = true;
            chkFlete.dispatchEvent(new Event("change"));
            fleteInput.value = documento.flete;
        }

        this.productManagerInstance.calcularPedidosTotales();

        const ordenCompraInput = document.getElementById('vt-ped-orden-compra-val');
        if (ordenCompraInput && documento.ordencompra) {
            const chkOrdenCompra = document.getElementById("vt-ped-orden-compra-check");
            chkOrdenCompra.checked = true;
            chkOrdenCompra.dispatchEvent(new Event("change"));
            ordenCompraInput.value = documento.ordencompra;
        }

        this.productManagerInstance.calcularPedidosTotales();

        const tipoMovSelect = document.getElementById('vt-ped-tipo-docto-mov');
        if (tipoMovSelect && documento.folio) {
            tipoMovSelect.value = 'modificacion';
            this.manejarPedidosCambioTipoMovimiento('modificacion');
        }

        this.productManagerInstance.calcularPedidosTotales();

        const totalCalculado = this.productManagerInstance.totales?.total || 0;
        if (documento.estatus_credito) {
            mostrarBannerCredito({
                lim_crd: documento.lim_crd || 0,
                credito_usado: documento.credito_usado || 0,
                credito_disponible: documento.credito_disponible || 0,
                porcentaje_uso: documento.porcentaje_credito || 0,
                estatus_credito: documento.estatus_credito,
                estatus_cliente: documento.estatus_cliente,
                clasificacion: documento.clasificacion,
            }, totalCalculado);
        }

        _validarCreditoConTotalPedido();
    },

    manejarPedidosCambioTipoMovimiento(tipoMovimiento) {
        if (tipoMovimiento === 'consulta') {
            this.deshabilitarPedidosFormulario();
        } else {
            this.habilitarPedidosFormulario();
        }
    },

    deshabilitarPedidosFormulario() {
        const pedidoContenedor = document.getElementById('pedido-contenedor');
        if (!pedidoContenedor) return;

        pedidoContenedor.querySelectorAll('input, select, textarea, button').forEach(element => {
            if (element.id !== 'vt-ped-tipo-docto-mov') {
                element.disabled = true;
                element.classList.add('vt-ped-disabled-field');
            }
        });

        const btnBuscarCliente = pedidoContenedor.querySelector('[data-bs-target="#vt-ped-modalBuscarCliente"]');
        const btnBuscarProducto = pedidoContenedor.querySelector('[data-bs-target="#vt-ped-modalBuscarProducto"]');
        if (btnBuscarCliente) btnBuscarCliente.disabled = true;
        if (btnBuscarProducto) btnBuscarProducto.disabled = true;

        pedidoContenedor.querySelectorAll('.vt-ped-btn-eliminar').forEach(btn => btn.disabled = true);

        if (window.tomManager) {
            ['vt-ped-vendedor', 'vt-ped-moneda', 'vt-ped-uso-cfdi', 'vt-ped-forma-pago'].forEach(id => {
                const instance = window.tomManager.instances?.get(id);
                if (instance) instance.disable();
            });
        }

        const btn = pedidoContenedor.querySelector('.vt-ped-btn-submit');
        if (btn) btn.disabled = false;

        const btnDocConsulta = pedidoContenedor.querySelector('.vt-ped-floating-btn');
        if (btnDocConsulta) btnDocConsulta.disabled = false;

        const btnPreview = pedidoContenedor.querySelector('.vt-ped-ticket-btn');
        if (btnPreview) btnPreview.disabled = false;
    },

    habilitarPedidosFormulario() {
        const pedidoContenedor = document.getElementById('pedido-contenedor');
        if (!pedidoContenedor) return;

        pedidoContenedor.querySelectorAll('input, select, textarea, button').forEach(element => {
            element.disabled = false;
            element.classList.remove('vt-ped-disabled-field');
        });

        const btnBuscarCliente = pedidoContenedor.querySelector('[data-bs-target="#vt-ped-modalBuscarCliente"]');
        const btnBuscarProducto = pedidoContenedor.querySelector('[data-bs-target="#vt-ped-modalBuscarProducto"]');
        if (btnBuscarCliente) btnBuscarCliente.disabled = false;
        if (btnBuscarProducto) btnBuscarProducto.disabled = false;

        if (window.tomManager) {
            ['vt-ped-vendedor', 'vt-ped-moneda', 'vt-ped-uso-cfdi', 'vt-ped-forma-pago'].forEach(id => {
                const instance = window.tomManager.instances?.get(id);
                if (instance) instance.enable();
            });
        }

        const tipoMovimiento = document.getElementById('vt-ped-tipo-docto-mov')?.value;
        const btn = pedidoContenedor.querySelector('.vt-ped-btn-submit');
        if (btn) {
            btn.innerHTML = tipoMovimiento === 'modificacion'
                ? '<i class="fas fa-save"></i> Actualizar Pedido'
                : '<i class="fas fa-save"></i> Guardar Pedido';
        }

        // Re-aplicar bloqueo de descuento/precio si los tokens no existen
        this.productManagerInstance?.renderPedidosTable();
    }
};
function calcularFechaPagoPed() {
    const plazo = parseInt(document.getElementById('vt-ped-plazo')?.value) || 0;
    const fechaPagoInput = document.getElementById('vt-ped-fecha-pago');
    if (!fechaPagoInput || plazo <= 0) {
        if (fechaPagoInput) fechaPagoInput.value = '';
        return;
    }

    const fecha = new Date();
    fecha.setDate(fecha.getDate() + plazo);

    const yyyy = fecha.getFullYear();
    const mm = String(fecha.getMonth() + 1).padStart(2, '0');
    const dd = String(fecha.getDate()).padStart(2, '0');
    fechaPagoInput.value = `${yyyy}-${mm}-${dd}`;
}
// ============================================
// 7. FUNCIONES DE CRÉDITO (sin cambios)
// ============================================
function mostrarBannerCredito(cliente, totalPedido = null) {
    const banner = document.getElementById('vt-ped-credit-banner');
    const banner2 = document.getElementById('vt-ped-inactivo-banner');
    const icon = document.getElementById('vt-ped-credit-icon');
    const icon2 = document.getElementById('vt-ped-inactivo-icon');
    const title = document.getElementById('vt-ped-credit-title');
    const title2 = document.getElementById('vt-ped-inactivo-title');
    const sub = document.getElementById('vt-ped-credit-sub');
    const bar = document.getElementById('vt-ped-credit-bar');
    const barWrap = document.getElementById('vt-ped-credit-bar-wrap');
    const pct = document.getElementById('vt-ped-credit-pct');
    const btnSubmit = document.querySelector('.vt-ped-btn-submit');

    const estatus = cliente.estatus_credito || 'SIN_LIMITE';
    const limite = parseFloat(cliente.lim_crd || 0);
    const usado = parseFloat(cliente.credito_usado || 0);
    const disponible = parseFloat(cliente.credito_disponible || 0);
    const porcentaje = parseFloat(cliente.porcentaje_uso || 0);
    const estatus_cliente = cliente.estatus_cliente;

    banner.className = 'vt-ped-credit-banner';
    banner2.className = 'vt-ped-inactivo-banner';

    banner.dataset.limite = limite;
    banner.dataset.usado = usado;
    banner.dataset.disponible = disponible;
    banner.dataset.estatus = estatus;
    banner2.dataset.estatus = estatus_cliente;

    _removePedidoEmailBtn();

    const fmt = n => n.toLocaleString('es-MX', { style: 'currency', currency: 'MXN' });

    if (estatus === 'SIN_LIMITE') {
        banner.classList.add('sin-limite');
        icon.textContent = 'ℹ️';
        title.textContent = 'Cliente sin límite de crédito configurado';
        sub.textContent = 'No se aplicarán restricciones de crédito';
        barWrap.style.display = 'none';
        pct.textContent = '';
        if (btnSubmit) btnSubmit.disabled = false;
        banner.style.display = 'flex';
        return;
    }

    barWrap.style.display = '';
    pct.textContent = porcentaje.toFixed(1) + '%';
    bar.style.width = Math.min(porcentaje, 100) + '%';

    const totalInfo = (totalPedido !== null && totalPedido > 0)
        ? ` · Pedido: ${fmt(totalPedido)}`
        : '';

    sub.textContent = `Límite: ${fmt(limite)} · Usado: ${fmt(usado)} · Disponible: ${fmt(disponible)}${totalInfo}`;

    if (estatus === 'EXCEDIDO') {
        banner.classList.add('excedido');
        icon.textContent = '🚫';
        title.textContent = 'Crédito excedido — no se puede continuar';
        if (btnSubmit) {
            btnSubmit.disabled = true;
            btnSubmit.title = 'El cliente no tiene crédito disponible';
        }
        _insertPedidoEmailBtn(banner, cliente, totalPedido);
    } else if (estatus === 'POR_VENCER') {
        banner.classList.add('por-vencer');
        icon.textContent = '⚠️';
        title.textContent = 'Crédito próximo al límite';
        if (btnSubmit) btnSubmit.disabled = false;
    } else {
        banner.classList.add('disponible');
        icon.textContent = '✅';
        title.textContent = 'Crédito disponible';
        if (btnSubmit) btnSubmit.disabled = false;
    }

    if (estatus_cliente === 'suspendido') {
        banner2.classList.add('excedido');
        icon2.textContent = '🚫';
        title2.textContent = 'Cliente Suspendido — no se puede continuar';
        if (btnSubmit) {
            btnSubmit.disabled = true;
            btnSubmit.title = 'El cliente está suspendido';
        }
        // Insertar botón de email si no existe ya
        if (!document.getElementById('vt-ped-btn-email-gerente')) {
            _insertPedidoEmailBtn(banner2, cliente, totalPedido);
        }
        banner2.style.display = 'flex';
    }

    banner.style.display = 'flex';
}

function _removePedidoEmailBtn() {
    document.getElementById('vt-ped-btn-email-gerente')?.remove();
}

function _insertPedidoEmailBtn(banner, cliente, totalPedido) {
    const btn = document.createElement('button');
    btn.type = 'button';
    btn.id = 'vt-ped-btn-email-gerente';
    btn.title = 'Solicitar autorización al gerente por correo';
    btn.style.cssText = `
        margin-top: 6px;
        padding: 6px 14px;
        border-radius: 8px;
        border: 1.5px solid #dc2626;
        background: white;
        color: #dc2626;
        font-size: 12px;
        font-weight: 600;
        cursor: pointer;
        display: flex;
        align-items: center;
        gap: 6px;
        white-space: nowrap;
        transition: background 0.2s, color 0.2s;
    `;
    btn.innerHTML = '✉️ Solicitar autorización al gerente';

    btn.addEventListener('mouseenter', () => { btn.style.background = '#dc2626'; btn.style.color = 'white'; });
    btn.addEventListener('mouseleave', () => { btn.style.background = 'white'; btn.style.color = '#dc2626'; });
    btn.addEventListener('click', () => _enviarEmailGerente(cliente, totalPedido));

    const subEl = document.getElementById('vt-ped-credit-sub');
    subEl?.insertAdjacentElement('afterend', btn);
}

async function _enviarEmailGerente(cliente, totalPedido) {
    if (!window.Swal) return;

    const fmt = n => n.toLocaleString('es-MX', { style: 'currency', currency: 'MXN' });
    const folio = document.getElementById('vt-ped-folio')?.value || 'Sin folio';
    const clienteId = document.getElementById('vt-ped-cliente')?.value || '';
    const limite = parseFloat(cliente.lim_crd || 0);
    const usado = parseFloat(cliente.credito_usado || 0);
    const disponible = parseFloat(cliente.credito_disponible || 0);

    const { value: correo } = await Swal.fire({
        title: 'Solicitar autorización',
        html: `
            <p style="margin-bottom:12px; font-size:13px; color:#475569; text-align:left;">
                El cliente <strong>${clienteId}</strong> ${cliente.estatus_credito === 'SUSPENDIDO' ? 'está <strong>suspendido</strong>' : 'tiene el crédito excedido'}.<br>
                Se enviará un correo al gerente con los detalles del pedido para que autorice la operación.
            </p>
            <div style="background:#fef2f2; border:1px solid #fecaca; border-radius:8px; padding:10px 14px; margin-bottom:14px; text-align:left; font-size:12px; color:#991b1b;">
                <strong>Límite:</strong> ${fmt(limite)} &nbsp;·&nbsp;
                <strong>Usado:</strong> ${fmt(usado)} &nbsp;·&nbsp;
                <strong>Disponible:</strong> ${fmt(disponible)}<br>
                <strong>Total pedido:</strong> ${totalPedido ? fmt(totalPedido) : 'Sin calcular'}
            </div>
            <input id="swal-gerente-email" class="swal2-input" type="email" placeholder="Correo del gerente" style="margin:0; width:100%;">
        `,
        confirmButtonText: 'Enviar solicitud',
        cancelButtonText: 'Cancelar',
        showCancelButton: true,
        focusConfirm: false,
        preConfirm() {
            const email = document.getElementById('swal-gerente-email')?.value?.trim();
            if (!email || !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) {
                Swal.showValidationMessage('Ingresa un correo válido');
                return false;
            }
            return email;
        }
    });

    if (!correo) return;

    const productos = PedidosApp.productManagerInstance?.getPedidosProductosData() || [];
    const totales = PedidosApp.productManagerInstance?.totales || {};
    const pedido = document.getElementById('vt-ped-documentid')?.value?.trim();
    const formData = new FormData();
    formData.append('gerenteEmail', correo);
    formData.append('folio', folio);
    formData.append('cliente', clienteId);
    formData.append('pedido', pedido);
    formData.append('limiteCredito', limite.toFixed(2));
    formData.append('creditoUsado', usado.toFixed(2));
    formData.append('creditoDisp', disponible.toFixed(2));
    formData.append('totalPedido', (totalPedido || 0).toFixed(2));
    formData.append('productosJSON', JSON.stringify(productos));
    formData.append('totalStr', JSON.stringify(totales));

    try {
        Swal.fire({ title: 'Enviando...', allowOutsideClick: false, didOpen: () => Swal.showLoading() });

        const resp = await fetch('/VIPedido/EnviarSolicitudGerente', { method: 'POST', body: formData });
        const result = await resp.json();

        if (result.success) {
            Swal.fire({ icon: 'success', title: 'Solicitud enviada', text: `El gerente (${correo}) fue notificado.` });
        } else {
            Swal.fire({ icon: 'error', title: 'Error', text: result.message || 'No se pudo enviar el correo.' });
        }
    } catch (err) {
        console.error(err);
        Swal.fire({ icon: 'error', title: 'Error de red', text: 'Revisa la conexión e intenta de nuevo.' });
    }
}

function _validarCreditoConTotalPedido() {
    const bannerEl = document.getElementById('vt-ped-credit-banner');
    if (!bannerEl || bannerEl.style.display === 'none') return;
    if (!bannerEl.dataset.estatus) return;

    const totalPedido = parseFloat(document.getElementById('vt-ped-importe')?.value || 0);
    const limite = parseFloat(bannerEl.dataset.limite || 0);
    const usado = parseFloat(bannerEl.dataset.usado || 0);
    const disponible = parseFloat(bannerEl.dataset.disponible || 0);
    const estatus = bannerEl.dataset.estatus || 'SIN_LIMITE';

    if (estatus === 'SIN_LIMITE') return;

    const bannerEl2 = document.getElementById('vt-ped-inactivo-banner');
    const clienteSuspendido = bannerEl2?.dataset.estatus === 'suspendido';

    const btnSubmit = document.querySelector('.vt-ped-btn-submit');
    const titleEl = document.getElementById('vt-ped-credit-title');
    const subEl = document.getElementById('vt-ped-credit-sub');
    const fmt = n => n.toLocaleString('es-MX', { style: 'currency', currency: 'MXN' });

    subEl.textContent = `Límite: ${fmt(limite)} · Usado: ${fmt(usado)} · Disponible: ${fmt(disponible)} · Pedido: ${fmt(totalPedido)}`;

    const creditoTotal = usado + totalPedido;
    const creditoExcedido = creditoTotal > limite;
    const creditoPorVencer = !creditoExcedido && (creditoTotal / limite >= 0.8);

    if (creditoExcedido) {
        bannerEl.className = 'vi-credit-banner excedido';
        document.getElementById('vt-ped-credit-icon').textContent = '🚫';
        titleEl.textContent = 'Crédito excedido — el pedido supera el límite disponible';

        if (!document.getElementById('vt-ped-btn-email-gerente')) {
            _insertPedidoEmailBtn(bannerEl, {
                lim_crd: limite,
                credito_usado: usado,
                credito_disponible: disponible,
                estatus_credito: 'EXCEDIDO'
            }, totalPedido);
        }
    } else if (creditoPorVencer) {
        bannerEl.className = 'vi-credit-banner por-vencer';
        document.getElementById('vt-ped-credit-icon').textContent = '⚠️';
        titleEl.textContent = 'Crédito próximo al límite con este pedido';
        _removePedidoEmailBtn();
    } else {
        bannerEl.className = 'vi-credit-banner disponible';
        document.getElementById('vt-ped-credit-icon').textContent = '✅';
        titleEl.textContent = 'Crédito disponible';
        _removePedidoEmailBtn();
    }

    if (clienteSuspendido && !document.getElementById('vt-ped-btn-email-gerente')) {
        const bannerSuspendido = document.getElementById('vt-ped-inactivo-banner');
        _insertPedidoEmailBtn(bannerSuspendido, {
            lim_crd: limite,
            credito_usado: usado,
            credito_disponible: disponible,
            estatus_credito: 'SUSPENDIDO'
        }, totalPedido);
    }

    // Si ya no está suspendido ni excedido, limpiar el botón
    if (!clienteSuspendido && !creditoExcedido) {
        _removePedidoEmailBtn();
    }

    if (btnSubmit) {
        btnSubmit.disabled = creditoExcedido || clienteSuspendido;
        btnSubmit.title = creditoExcedido
            ? 'El pedido supera el crédito disponible'
            : clienteSuspendido
                ? 'El cliente está suspendido'
                : '';
    }
}

// ============================================
// 8. ARRANQUE
// ============================================
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => {
        PedidosApp.initPedidos();

        const fleteInput = document.getElementById('vt-ped-flete-val');
        if (fleteInput) {
            fleteInput.addEventListener('input', () => { /* calcularTotales se llama desde el ProductManager */ });
            fleteInput.addEventListener('change', () => { });
        }
    });
} else {
    PedidosApp.initPedidos();
}
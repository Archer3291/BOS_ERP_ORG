// ============================================
// 0. AUTORIZACIÓN DE PRECIOS / DESCUENTOS
// ============================================
// A diferencia de cotización, pedido y remisión, la factura no tiene overlay con
// contraseña sobre los campos: el precio siempre fue editable. Lo que sí puede hacer es
// tomar los permisos directos del usuario (los mismos de los otros documentos), de modo
// que quien esté autorizado pueda bajar del piso y el resto no.
const facturasAuthState = {
    descuentoToken: null,
    precioToken: null
};

async function cargarPermisosFacturas() {
    try {
        const response = await fetch('/DatosGenerales/ObtenerPermisosUsuario');
        if (!response.ok) return;
        const permisos = await response.json();

        if (permisos.tienePrecio && permisos.tokenPrecio)
            facturasAuthState.precioToken = permisos.tokenPrecio;
        if (permisos.tieneDescuento && permisos.tokenDescuento)
            facturasAuthState.descuentoToken = permisos.tokenDescuento;
    } catch (err) {
        console.warn('No se pudieron verificar permisos automáticos:', err);
    }
}

// ============================================
// 1. CLASE BASE PARA GESTIÓN DE DATOS
// ============================================
class FacturasDataManager {
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
        this.initFacturasDataManager();
    }

    initFacturasDataManager() {
        this.dom.modal?.addEventListener('show.bs.modal', () => this.onFacturasModalShow());
        this.dom.input?.addEventListener('input', (e) => this.handleFacturasSearch(e.target.value));
        this.dom.pageSize?.addEventListener('change', (e) => this.handleFacturasPageSizeChange(e.target.value));
        this.dom.btnClear?.addEventListener('click', () => this.handleFacturasClear());

        this.dom.results?.addEventListener('click', (e) => {
            const btn = e.target.closest('[data-select-id]');
            if (btn) {
                const id = btn.dataset.selectId;
                this.handleFacturasSelect(id);
            }
        });

        this.dom.pagination?.addEventListener('click', (e) => {
            e.preventDefault();
            const btn = e.target.closest('[data-page]');
            if (btn && !btn.closest('.disabled')) {
                this.state.page = parseInt(btn.dataset.page);
                this.fetchFacturasData();
            }
        });
    }

    onFacturasModalShow() {
        this.handleFacturasClear();
        this.fetchFacturasData();
    }

    handleFacturasSearch(value) {
        clearTimeout(this.typingTimer);
        this.typingTimer = setTimeout(() => {
            this.state.lastSearch = value;
            this.state.page = 1;
            this.fetchFacturasData();
        }, 300);
    }

    handleFacturasPageSizeChange(value) {
        this.state.pageSize = parseInt(value);
        this.state.page = 1;
        this.fetchFacturasData();
    }

    handleFacturasClear() {
        this.dom.input.value = '';
        this.state.lastSearch = '';
        this.state.page = 1;
        this.fetchFacturasData();
    }

    getFacturasCachedData(key) {
        const cached = this.cache.get(key);
        if (cached && Date.now() - cached.timestamp < this.cacheTimeout) {
            return cached.data;
        }
        return null;
    }

    setFacturasCachedData(key, data) {
        this.cache.set(key, { data, timestamp: Date.now() });
    }

    clearFacturasCache() {
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

    async fetchFacturasData() {
        if (this.state.loading) {
            this.abortController?.abort();
        }

        const cacheKey = this.buildCacheKey();
        const cached = this.getFacturasCachedData(cacheKey);

        if (cached) {
            this.renderFacturasResults(cached);
            return;
        }

        this.state.loading = true;
        this.showFacturasSpinner(true);
        this.abortController = new AbortController();

        try {
            const url = this.buildUrl();
            const response = await fetch(url, { signal: this.abortController.signal });

            if (!response.ok) throw new Error('Error en la respuesta');

            const data = await response.json();
            this.state.totalRecords = data.total || 0;

            const results = { items: data.items || data, total: this.state.totalRecords };
            this.setFacturasCachedData(cacheKey, results);
            this.renderFacturasResults(results);

        } catch (error) {
            if (error.name !== 'AbortError') {
                console.error('Error fetching facturas data:', error);
                this.renderFacturasError();
            }
        } finally {
            this.state.loading = false;
            this.showFacturasSpinner(false);
        }
    }

    showFacturasSpinner(show) {
        this.dom.spinner?.classList.toggle('d-none', !show);
    }

    renderFacturasResults(data) {
        const items = data.items || [];

        if (items.length === 0) {
            this.dom.results.innerHTML = `
                <li class="list-group-item text-center text-muted py-5">
                    <i class="fas fa-search fa-3x mb-3 opacity-25"></i>
                    <p>No se encontraron resultados</p>
                </li>`;
            this.updateFacturasCounters();
            return;
        }

        const fragment = document.createDocumentFragment();

        items.forEach(item => {
            const li = document.createElement('li');
            li.className = 'list-group-item d-flex justify-content-between align-items-center hover-shadow';
            li.innerHTML = `
                <div>
                    <h6 class="mb-1 fw-semibold text-primary">${this.escapeFacturasHtml(item.descripcion)}</h6>
                    <small class="text-muted">ID: ${this.escapeFacturasHtml(item.id)}</small>
                </div>
                <button type="button" class="btn btn-sm btn-primary" data-select-id="${this.escapeFacturasHtml(item.id)}">
                    <i class="fas fa-check me-1"></i>Seleccionar
                </button>`;
            fragment.appendChild(li);
        });

        this.dom.results.innerHTML = '';
        this.dom.results.appendChild(fragment);

        this.updateFacturasCounters();
        this.renderFacturasPagination();
    }

    renderFacturasError() {
        this.dom.results.innerHTML = `
            <li class="list-group-item text-center text-danger py-5">
                <i class="fas fa-exclamation-triangle fa-3x mb-3"></i>
                <p>Error al cargar los datos</p>
            </li>`;
    }

    updateFacturasCounters() {
        const from = (this.state.page - 1) * this.state.pageSize + 1;
        const to = Math.min(this.state.page * this.state.pageSize, this.state.totalRecords);

        if (this.dom.recordsFrom) this.dom.recordsFrom.textContent = from;
        if (this.dom.recordsTo) this.dom.recordsTo.textContent = to;
        if (this.dom.totalRecords) this.dom.totalRecords.textContent = this.state.totalRecords;
    }

    renderFacturasPagination() {
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

        const prevLi = this.createFacturasPageButton('Anterior', this.state.page - 1, this.state.page === 1);
        fragment.appendChild(prevLi);

        for (let i = startPage; i <= endPage; i++) {
            const li = this.createFacturasPageButton(i, i, false, i === this.state.page);
            fragment.appendChild(li);
        }

        const nextLi = this.createFacturasPageButton('Siguiente', this.state.page + 1, this.state.page === totalPages);
        fragment.appendChild(nextLi);

        this.dom.pagination.innerHTML = '';
        this.dom.pagination.appendChild(fragment);
    }

    createFacturasPageButton(text, page, disabled = false, active = false) {
        const li = document.createElement('li');
        li.className = `page-item${disabled ? ' disabled' : ''}${active ? ' active' : ''}`;
        li.innerHTML = `<button type="button" class="page-link" data-page="${page}">${text}</button>`;
        return li;
    }

    async handleFacturasSelect(id) {
        try {
            // El detalle del producto depende del cliente (lista de precios y reglas por
            // cliente); quien use el manager lo aporta con getExtraParams(), que puede ser
            // async porque a veces hay que resolver el id del cliente antes de preguntar.
            const extra = typeof this.getExtraParams === 'function' ? await this.getExtraParams() : null;
            let url = `${this.detailEndpoint}?id=${encodeURIComponent(id)}`;

            Object.entries(extra || {}).forEach(([clave, valor]) => {
                if (valor !== null && valor !== undefined && valor !== '')
                    url += `&${encodeURIComponent(clave)}=${encodeURIComponent(valor)}`;
            });

            const response = await fetch(url);
            if (!response.ok) throw new Error('Error al obtener detalles');

            const data = await response.json();
            if (data && data[0]) {
                this.onFacturasSelect(data[0]);
                if (this.shouldCloseOnSelect) {
                    bootstrap.Modal.getInstance(this.dom.modal)?.hide();
                }
            }
        } catch (error) {
            console.error('Error selecting facturas item:', error);
            alert('Error al seleccionar el elemento');
        }
    }

    onFacturasSelect(item) {
        console.log('Facturas item selected:', item);
    }

    escapeFacturasHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }
}

// ============================================
// 2. CLASE EXTENDIDA PARA DOCUMENTOS
// ============================================
class FacturasDocumentoManager extends FacturasDataManager {

    // ── Override: URL apunta al nuevo endpoint agrupado ──────────
    buildUrl() {
        const params = new URLSearchParams({
            nombre: this.state.lastSearch || '',
            page: this.state.page,
            pageSize: this.state.pageSize
        });
        return `${this.endpoint}?${params.toString()}`;
    }

    // ── Override: render de la lista con badge de grupo ──────────
    renderFacturasResults(data) {
        const items = data.items || [];

        if (items.length === 0) {
            this.dom.results.innerHTML = `
                <li class="list-group-item text-center text-muted py-5">
                    <i class="fas fa-search fa-3x mb-3 opacity-25"></i>
                    <p>No se encontraron documentos</p>
                </li>`;
            this.updateFacturasCounters();
            return;
        }

        const fragment = document.createDocumentFragment();

        items.forEach(item => {
            const esGrupo = parseInt(item.total_remisiones || 1) > 1;
            const remIds = item.remisiones_ids || String(item.grupo_id || item.id_encabezado);
            const folio = item.todos_folios || item.folio || '';
            const fechaStr = item.fch ? dateFormatter(item.fch): '';
            const importe = parseFloat(item.imp || 0).toFixed(2);
            const cliente = item.cli_prov || '';
            const nCli = item.n_cli || '';
            const pendientes = parseInt(item.partidas_pendientes || 0);
            const parciales = parseInt(item.partidas_parciales || 0);
            const impPend = parseFloat(item.importe_pendiente || 0).toFixed(2);

            // Badge de grupo (solo si hay >1 remisión)
            const badgeGrupo = esGrupo
                ? `<span class="badge bg-warning text-dark ms-2"
                          title="Este grupo contiene ${item.total_remisiones} remisiones de distintos almacenes">
                       <i class="fas fa-layer-group me-1"></i>${item.total_remisiones} remisiones
                   </span>`
                : '';

            // Folios extras (cuando hay grupo)
            const foliosExtra = esGrupo
                ? `<div class="mt-1" style="font-size:0.78rem;color:var(--color-text-secondary);">
                       <i class="fas fa-sitemap me-1 opacity-50"></i>
                       ${this.escapeFacturasHtml(folio)}
                   </div>`
                : '';

            const li = document.createElement('li');
            li.className = 'list-group-item';
            li.dataset.remisionesIds = remIds;

            li.innerHTML = `
                <div class="d-flex justify-content-between align-items-start">
                    <div class="flex-grow-1">
                        <h6 class="mb-1">
                            <i class="fas fa-file-invoice text-primary me-2"></i>
                            <strong>${this.escapeFacturasHtml(esGrupo ? folio.split(' | ')[0] : folio)}</strong>
                            ${badgeGrupo}
                        </h6>
                        ${foliosExtra}
                        <p class="mb-1 text-muted" style="font-size:0.85rem;">
                            <i class="fas fa-user me-2"></i>
                            ${this.escapeFacturasHtml(nCli || cliente)}
                        </p>
                        <div class="d-flex gap-3 flex-wrap mt-1" style="font-size:0.8rem;color:var(--color-text-secondary);">
                            <span><i class="fas fa-calendar me-1"></i>${fechaStr}</span>
                            <span><i class="fas fa-money-bill-wave me-1"></i>Total: $${importe}</span>
    
                        </div>
                    </div>
                    <div class="d-flex flex-column gap-2 align-items-end ms-3">
                        <button type="button"
                                class="btn btn-sm btn-primary"
                                data-select-ids="${this.escapeFacturasHtml(remIds)}">
                            <i class="fas fa-file-import me-1"></i>Cargar
                        </button>
                    </div>
                </div>`;
            fragment.appendChild(li);
        });

        this.dom.results.innerHTML = '';
        this.dom.results.appendChild(fragment);

        this.updateFacturasCounters();
        this.renderFacturasPagination();

        // Listener de click sobre el botón "Cargar" (usa data-select-ids)
        this.dom.results.querySelectorAll('[data-select-ids]').forEach(btn => {
            btn.addEventListener('click', () => {
                const idsStr = btn.dataset.selectIds;
                this._cargarPorIds(idsStr);
            });
        });
    }

    // ── Carga un grupo (uno o varios ids) ────────────────────────
    async _cargarPorIds(idsStr) {
        try {
            const resp = await fetch(
                `/DatosGenerales/BuscarDocumentoMultiple?ids=${encodeURIComponent(idsStr)}`
            );
            if (!resp.ok) throw new Error(`HTTP ${resp.status}`);

            const data = await resp.json();
            if (data && data[0]) {
                this.onFacturasSelect(data[0]);
                bootstrap.Modal.getInstance(this.dom.modal)?.hide();
            }
        } catch (error) {
            console.error('Error cargando documento(s):', error);
            Swal.fire({
                icon: 'error',
                title: 'Error',
                text: 'No se pudo cargar el documento: ' + error.message
            });
        }
    }

    // Sobrescribir handleFacturasSelect (el base llama a detailEndpoint genérico)
    // — ya no se usa porque los botones usan _cargarPorIds directamente
    async handleFacturasSelect(id) {
        await this._cargarPorIds(String(id));
    }
}
// ============================================
// 3. CLASE PARA ANTICIPOS - MEJORADA
// ============================================
class FacturasAnticipoManager extends FacturasDataManager {
    constructor(config) {
        super(config);
        this.clienteActual = null;
    }

    buildUrl() {
        const params = new URLSearchParams({
            nombre: this.state.lastSearch || '',
            page: this.state.page,
            pageSize: this.state.pageSize,
            cliente: this.clienteActual || 0
        });

        return `${this.endpoint}?${params.toString()}`;
    }

    buildCacheKey() {
        return `${this.state.lastSearch}-${this.state.page}-${this.state.pageSize}-${this.clienteActual || ''}`;
    }

    renderFacturasResults(data) {
        const items = data.items || [];

        if (items.length === 0) {
            this.dom.results.innerHTML = `
                <li class="list-group-item text-center text-muted py-5">
                    <i class="fas fa-wallet fa-3x mb-3 opacity-25"></i>
                    <p>No se encontraron anticipos</p>
                </li>`;
            this.updateFacturasCounters();
            return;
        }

        const fragment = document.createDocumentFragment();

        items.forEach(item => {
            const li = document.createElement('li');
            li.className = 'list-group-item hover-shadow';
            li.dataset.folio = item.folio;

            li.innerHTML = `
                <div class="d-flex justify-content-between align-items-start">
                    <div class="flex-grow-1">
                        <h6 class="mb-1">
                            <i class="fas fa-wallet text-success me-2"></i>
                            <strong>Folio:</strong> ${this.escapeFacturasHtml(item.folio || '')}
                        </h6>
                        <p class="mb-1 text-muted">
                            <i class="fas fa-user me-2"></i>
                            <strong>Cliente:</strong> ${this.escapeFacturasHtml(item.cli_prov || '')}
                        </p>
                        <div class="d-flex gap-3 small text-muted">
                            <span><i class="fas fa-calendar me-1"></i> ${this.escapeFacturasHtml(item.fch || '')}</span>
                            <span><i class="fas fa-money-bill-wave me-1"></i> $${parseFloat(item.saldo || 0).toFixed(2)}</span>
                            <span><i class="fas fa-user-tie me-1"></i> ${this.escapeFacturasHtml(item.usr0 || '')}</span>
                        </div>
                    </div>
                    <div class="d-flex flex-column gap-2 align-items-end">
                        <button type="button" class="btn btn-sm btn-success" data-select-id="${this.escapeFacturasHtml(item.id_encabezado)}">
                            <i class="fas fa-check me-1"></i> Seleccionar
                        </button>
                    </div>
                </div>`;
            fragment.appendChild(li);
        });

        this.dom.results.innerHTML = '';
        this.dom.results.appendChild(fragment);

        this.updateFacturasCounters();
        this.renderFacturasPagination();
    }

    setClienteActual(clienteId) {
        this.clienteActual = clienteId;
        this.clearFacturasCache();
        this.state.page = 1;
        this.state.lastSearch = '';
        this.dom.input.value = '';
        this.fetchFacturasData();
    }
}

// ============================================
// 4. GESTOR DE PRODUCTOS
// ============================================
class FacturasProductManager {
    constructor() {
        this.productos = [];
        this.anticipos = [];
        this.totales = {
            subtotal1: 0,
            descuento: 0,
            flete: 0,
            subtotal2: 0,
            iva: 0,
            total: 0,
            totalAnticipos: 0,
            totalFinal: 0
        };

        this.dom = {
            table: document.getElementById('vi-fac-productosTable'),
            thead: document.querySelector('#vi-fac-productosTable')?.closest('table')?.querySelector('thead'),
            subtotal1Display: document.getElementById('vi-fac-total-subtotal1-display'),
            descuentoDisplay: document.getElementById('vi-fac-total-descuento-display'),
            fleteDisplay: document.getElementById('vi-fac-total-flete-display'),
            subtotal2Display: document.getElementById('vi-fac-total-subtotal2-display'),
            ivaDisplay: document.getElementById('vi-fac-iva-display'),
            importeDisplay: document.getElementById('vi-fac-importe-display'),
            anticiposTable: document.getElementById('vi-fac-anticiposTable'),
            totalAnticiposDisplay: document.getElementById('vi-fac-total-anticipos-display'),
            totalFinalDisplay: document.getElementById('vi-fac-total-final-display')
        };

        this.initFacturasProductManager();
    }

    initFacturasProductManager() {
        this.dom.table?.addEventListener('input', (e) => {
            const input = e.target;
            if (input.classList.contains('vi-fac-cantidad') ||
                input.classList.contains('vi-fac-precio') ||
                input.classList.contains('vi-fac-descuento') ||
                input.classList.contains('vi-fac-comentario')) {
                const tr = input.closest('tr');
                const productoId = tr?.dataset.productoId;
                if (productoId) this.actualizarFacturasFila(productoId);
            }
        });

        this.dom.table?.addEventListener('click', (e) => {
            const btn = e.target.closest('.vi-fac-btn-eliminar');
            if (btn) {
                const tr = btn.closest('tr');
                const productoId = tr?.dataset.productoId;
                if (productoId) this.eliminarFacturasProducto(productoId);
            }
        });

        this.dom.anticiposTable?.addEventListener('click', (e) => {
            const btn = e.target.closest('.vi-fac-btn-eliminar-anticipo');
            if (btn) {
                const tr = btn.closest('tr');
                const anticipoId = tr?.dataset.anticipoId;
                if (anticipoId) this.eliminarAnticipo(anticipoId);
            }
        });

        document.getElementById('vi-fac-flete-val')?.addEventListener('input', () => {
            this.calcularFacturasTotales();
        });

        document.getElementById('vi-fac-monto-anticipo')?.addEventListener('input', () => {
            this.calcularFacturasTotales();
        });
    }

    agregarFacturasProducto(producto) {
        const productoExistente = this.productos.find(p => p.productoId === producto.id);

        if (productoExistente) {
            productoExistente.cantidad += 1;

            const tr = this.dom.table.querySelector(`tr[data-producto-id="${productoExistente.id}"]`);
            if (tr) {
                const inputCantidad = tr.querySelector('.vi-fac-cantidad');
                inputCantidad.value = productoExistente.cantidad;

                const importe = this.calcularFacturasImporte(productoExistente);
                tr.querySelector('.vi-fac-importe').textContent = importe.toFixed(2);

                tr.classList.add('table-warning');
                setTimeout(() => tr.classList.remove('table-warning'), 500);
            }

            this.calcularFacturasTotales();

            if (window.toastMixin) {
                toastMixin.fire({
                    icon: 'info',
                    title: `Cantidad actualizada: ${this.escapeFacturasHtml(producto.descripcion)} (${productoExistente.cantidad})`
                });
            }
        } else {
            const productoData = Object.assign({
                id: `prod_${Date.now()}_${Math.random()}`,
                productoId: producto.id,
                descripcion: producto.descripcion,
                existencia: producto.existencia,
                cantidad: 1,
                unidad: producto.udm || 'PZA',
                existenciaGeneral: producto.existenciaGeneral || 0,
                existenciaModular: producto.existenciaModular || 0,
                comentario: '',
                pedimentos: producto.pedimentos || []
            },
            // La factura tomaba solo 'precio' y dejaba el descuento en 0, así que una
            // regla que la cotización sí aplicaba aquí se perdía y el CFDI salía con
            // otro importe. El piso y el tope también salen de aquí.
            ReglasPrecio.camposDeRegla(producto));

            this.productos.push(productoData);
            this.renderFacturasTable();
            this.calcularFacturasTotales();

            if (window.toastMixin) {
                toastMixin.fire({
                    icon: 'success',
                    title: `Producto agregado: ${this.escapeFacturasHtml(producto.descripcion)}`
                });
            }
        }
    }

    agregarAnticipo(anticipo) {
        const anticipoExistente = this.anticipos.find(a => a.id_encabezado === anticipo.id_encabezado);

        if (anticipoExistente) {
            if (window.toastMixin) {
                toastMixin.fire({
                    icon: 'warning',
                    title: `Este anticipo ya fue agregado`
                });
            }
            return;
        }

        const anticipoData = {
            id: `ant_${Date.now()}_${Math.random()}`,
            id_encabezado: anticipo.id_encabezado,
            folio: anticipo.folio,
            cli_prov: anticipo.cli_prov,
            imp: parseFloat(anticipo.saldo || 0),
            fch: anticipo.fch
        };

        this.anticipos.push(anticipoData);
        this.renderAnticiposTable();
        this.calcularFacturasTotales();

        if (window.toastMixin) {
            toastMixin.fire({
                icon: 'success',
                title: `Anticipo agregado: ${this.escapeFacturasHtml(anticipo.folio)}`
            });
        }
    }

    renderFacturasTable() {
        const tbody = this.dom.table;
        const thead = this.dom.thead;
        if (!tbody) return;

        const emptyRow = tbody.querySelector('.vi-fac-empty-state');
        if (emptyRow) emptyRow.remove();

        tbody.innerHTML = '';

        if (thead) {
            const descuentoTh = thead.querySelector('.vi-fac-th-descuento');
            if (descuentoTh) descuentoTh.style.display = (typeof rol !== 'undefined' && rol === 'Gerente') ? '' : 'none';
        }

        if (this.productos.length === 0) {
            tbody.innerHTML = `
        <tr class="vi-fac-empty-state">
            <td colspan="12" class="text-center py-5">
                <i class="fas fa-box-open fa-3x mb-3 opacity-25"></i>
                <div>No hay productos agregados</div>
                <small class="text-muted">Haz clic en "Agregar" para comenzar</small>
            </td>
        </tr>`;
            return;
        }

        const fragment = document.createDocumentFragment();

        this.productos.forEach((p, index) => {
            // ✅ MOVIDO AQUÍ: dentro del forEach, 'p' ya existe
            const pedimentos = (() => {
                try {
                    const raw = p.pedimentos;
                    if (!raw) return [];
                    return typeof raw === 'string' ? JSON.parse(raw) : raw;
                } catch { return []; }
            })();

            const pedimentosHtml = pedimentos.length > 0
                ? `<div class="mt-1 d-flex flex-wrap gap-1">
        ${pedimentos
                    .filter(ped => ped.pedimento != null && String(ped.pedimento).trim() !== '' && String(ped.pedimento).toLowerCase() !== 'null')
                    .map(ped => `
            <span style="
                display: inline-flex;
                align-items: center;
                gap: 4px;
                background: #eff6ff;
                border: 1px solid #bfdbfe;
                border-radius: 4px;
                padding: 1px 6px;
                font-size: 0.70rem;
                color: #1d4ed8;
                white-space: nowrap;
            ">
                <i class="fas fa-passport" style="font-size:0.65rem;"></i>
                ${this.escapeFacturasHtml(String(ped.pedimento))}
                <span style="
                    background:#dbeafe;
                    border-radius:3px;
                    padding: 0 4px;
                    font-weight:600;
                    color:#1e40af;
                ">×${ped.cantidad}</span>
            </span>
        `).join('')}
       </div>`
                : '';

            const importe = this.calcularFacturasImporte(p);
            const tr = document.createElement('tr');
            tr.dataset.productoId = p.id;

            const descuentoCell = (typeof rol !== 'undefined' && rol === 'Gerente')
                ? `<td>
                 <input type="number" class="vi-fac-product-input vi-fac-descuento" value="${p.descuento}" min="0" max="100" step="0.01" readonly>
               </td>`
                : '';

            tr.innerHTML = `
        <td class="text-center fw-bold">${index + 1}</td>
        <td>${this.escapeFacturasHtml(p.productoId)}</td>
        <td style="max-width: 220px;">
            <div style="white-space: nowrap; overflow: hidden; text-overflow: ellipsis;">
                ${this.escapeFacturasHtml(p.descripcion)}
            </div>
            ${pedimentosHtml}
        </td>
        <td class="text-center">
            <span style="
                display: inline-block;
                padding: 4px 12px;
                border-radius: 4px;
                font-weight: 600;
                ${this.obtenerEstiloExistencia(p.existencia)}
            ">
                ${p.existencia || 0}
            </span>
        </td>
        <td>
            <input type="number" class="vi-fac-product-input vi-fac-cantidad" value="${p.cantidad}" min="0" step="0.01">
        </td>
        <td>${this.escapeFacturasHtml(p.unidad)}</td>
        <td>
            <input type="number" class="vi-fac-product-input vi-fac-precio" value="${p.precio.toFixed(2)}" min="0" step="0.01">
        </td>
        ${descuentoCell}
        <td class="vi-fac-importe fw-bold">${importe.toFixed(2)}</td>                 
        <td>
            <input type="text" class="vi-fac-product-input vi-fac-comentario" value="${this.escapeFacturasHtml(p.comentario)}" placeholder="Comentario...">
        </td>
        <td>
            <button type="button" class="vi-fac-btn-delete vi-fac-btn-eliminar">
                <i class="fas fa-trash"></i>
            </button>
        </td>`;

            fragment.appendChild(tr);
        });

        tbody.appendChild(fragment);
    }

    renderAnticiposTable() {
        const tbody = this.dom.anticiposTable;
        if (!tbody) return;

        tbody.innerHTML = '';

        if (this.anticipos.length === 0) {
            tbody.innerHTML = `
            <tr class="vi-fac-empty-state-anticipos">
                <td colspan="6" class="text-center py-3">
                    <small class="text-muted">No hay anticipos agregados</small>
                </td>
            </tr>`;
            return;
        }

        const fragment = document.createDocumentFragment();

        this.anticipos.forEach(a => {
            const tr = document.createElement('tr');
            tr.dataset.anticipoId = a.id;
            tr.className = 'table-info';

            tr.innerHTML = `
            <td>${this.escapeFacturasHtml(a.folio)}</td>
            <td>${this.escapeFacturasHtml(a.cli_prov)}</td>
            <td>${this.escapeFacturasHtml(a.fch)}</td>
            <td class="fw-bold text-success">$${a.imp.toFixed(2)}</td>
            <td>
                <input type="hidden" class="vi-fac-anticipo-id" value="${a.id_encabezado}">
            </td>
            <td>
                <button type="button" class="btn btn-sm btn-danger vi-fac-btn-eliminar-anticipo">
                    <i class="fas fa-trash"></i>
                </button>
            </td>`;

            fragment.appendChild(tr);
        });

        tbody.appendChild(fragment);
    }

    obtenerEstiloExistencia(existencia) {
        const cantidad = parseFloat(existencia) || 0;

        if (cantidad === 0) {
            return 'background-color: #dc3545; color: white;';
        } else if (cantidad > 0 && cantidad <= 5) {
            return 'background-color: #ffc107; color: #000;';
        } else if (cantidad > 5 && cantidad <= 20) {
            return 'background-color: #0dcaf0; color: #000;';
        } else {
            return 'background-color: #198754; color: white;';
        }
    }

    actualizarFacturasFila(productoId) {
        const producto = this.productos.find(p => p.id === productoId);
        if (!producto) return;

        const tr = this.dom.table.querySelector(`tr[data-producto-id="${productoId}"]`);
        if (!tr) return;

        producto.cantidad = parseFloat(tr.querySelector('.vi-fac-cantidad').value) || 0;
        producto.comentario = tr.querySelector('.vi-fac-comentario').value || '';

        // ── Precio contra la regla vigente ────────────────────────────────────
        // El input de precio de la factura es libre (no tiene el candado por contraseña
        // de los otros documentos), así que aquí es donde se respeta el piso: sin esto
        // la factura podía emitirse por debajo del mínimo que sí respetaba la remisión.
        const precioInput = tr.querySelector('.vi-fac-precio');
        if (precioInput) {
            const res = ReglasPrecio.validarPrecio(
                producto,
                parseFloat(precioInput.value) || 0,
                !!facturasAuthState.precioToken);

            producto.precio = res.valor;
            if (res.aviso) precioInput.value = res.valor.toFixed(2);
            ReglasPrecio.avisar(res.aviso);
        }

        const descuentoInput = tr.querySelector('.vi-fac-descuento');
        if (descuentoInput) {
            const res = ReglasPrecio.validarDescuento(
                producto,
                parseFloat(descuentoInput.value) || 0,
                !!facturasAuthState.descuentoToken);

            producto.descuento = res.valor;
            if (res.aviso) descuentoInput.value = res.valor.toFixed(2);
            ReglasPrecio.avisar(res.aviso);
        }

        const piso = ReglasPrecio.ajustarPorPisoNeto(producto);
        if (piso.aviso) {
            producto.descuento = piso.descuento;
            if (descuentoInput) descuentoInput.value = piso.descuento.toFixed(2);
            ReglasPrecio.avisar(piso.aviso);
        }

        const importe = this.calcularFacturasImporte(producto);
        tr.querySelector('.vi-fac-importe').textContent = importe.toFixed(2);

        this.calcularFacturasTotales();
    }

    eliminarFacturasProducto(productoId) {
        this.productos = this.productos.filter(p => p.id !== productoId);
        this.renderFacturasTable();
        this.calcularFacturasTotales();
    }

    eliminarAnticipo(anticipoId) {
        this.anticipos = this.anticipos.filter(a => a.id !== anticipoId);
        this.renderAnticiposTable();
        this.calcularFacturasTotales();

        if (window.toastMixin) {
            toastMixin.fire({
                icon: 'info',
                title: 'Anticipo removido'
            });
        }
    }

    calcularFacturasImporte(producto) {
        return producto.cantidad * producto.precio * (1 - producto.descuento / 100);
    }

    calcularFacturasTotales() {
        const tipoPago = document.getElementById('vi-fac-toggle-pago')
            ?.getAttribute('data-state') || 'contado';
        const flete = parseFloat(document.getElementById('vi-fac-flete-val')?.value) || 0;

        let subtotal1 = 0, descuento = 0, subtotal2 = 0, iva = 0, total = 0;

        if (tipoPago === 'anticipo') {
            const montoAnticipo = parseFloat(
                document.getElementById('vi-fac-monto-anticipo')?.value) || 0;
            subtotal1 = montoAnticipo;
            iva = Math.round(montoAnticipo * 0.16 * 100) / 100;
            total = montoAnticipo + iva;
            subtotal2 = montoAnticipo;
        } else {
            const totales = this.productos.reduce((acc, p) => {
                const importeBruto = p.cantidad * p.precio;
                const descuentoMonto = Math.round(importeBruto * (p.descuento / 100) * 100) / 100;
                const importeNeto = Math.round((importeBruto - descuentoMonto) * 100) / 100;

                const ivaConcepto = Math.round(importeNeto * 0.16 * 100) / 100;

                acc.subtotalBruto += Math.round(importeBruto * 100) / 100;
                acc.totalDescuento += descuentoMonto;
                acc.iva += ivaConcepto;

                return acc;
            }, { subtotalBruto: 0, totalDescuento: 0, iva: 0 });

            subtotal1 = totales.subtotalBruto;
            descuento = totales.totalDescuento;
            const subtotalNeto = subtotal1 - descuento;
            subtotal2 = subtotalNeto + flete;

            iva = totales.iva;

            // IVA del flete (si aplica)
            if (flete > 0) {
                iva += Math.round(flete * 0.16 * 100) / 100;
            }

            total = subtotal2 + iva;
        }


        // Calcular total de anticipos
        const totalAnticipos = this.anticipos.reduce((sum, a) => sum + a.imp, 0);
        const totalFinal = total - totalAnticipos;

        this.totales = { subtotal1, descuento, flete, subtotal2, iva, total, totalAnticipos, totalFinal };

        this.updateFacturasDisplay(this.dom.subtotal1Display, subtotal1);
        this.updateFacturasDisplay(this.dom.descuentoDisplay, descuento, '-');
        this.updateFacturasDisplay(this.dom.fleteDisplay, flete);
        this.updateFacturasDisplay(this.dom.subtotal2Display, subtotal2);
        this.updateFacturasDisplay(this.dom.ivaDisplay, iva);
        this.updateFacturasDisplay(this.dom.importeDisplay, totalFinal, '');
        this.updateFacturasDisplay(this.dom.totalAnticiposDisplay, totalAnticipos, '-');
        this.updateFacturasDisplay(this.dom.totalFinalDisplay, totalFinal, '');

        this.updateFacturasHiddenField('vi-fac-total-subtotal1', subtotal1);
        this.updateFacturasHiddenField('vi-fac-total-descuento', descuento);
        this.updateFacturasHiddenField('vi-fac-total-flete', flete);
        this.updateFacturasHiddenField('vi-fac-total-subtotal2', subtotal2);
        this.updateFacturasHiddenField('vi-fac-iva', iva);
        this.updateFacturasHiddenField('vi-fac-importe', total);
        this.updateFacturasHiddenField('vi-fac-total-anticipos', totalAnticipos);
        this.updateFacturasHiddenField('vi-fac-total-final', totalFinal);

        window.CreditoVentas?.evaluar('vi-fac');
    }

    updateFacturasHiddenField(id, value) {
        const field = document.getElementById(id);
        if (field) {
            field.value = value.toFixed(2);
        }
    }

    updateFacturasDisplay(element, value, prefix = '') {
        if (element) {
            element.textContent = `${prefix}${value.toFixed(2)}`;
        }
    }

    escapeFacturasHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    getFacturasProductosData() {
        return this.productos.map(p => ({
            ...p,
            importe: this.calcularFacturasImporte(p)
        }));
    }

    getAnticiposData() {
        return this.anticipos.map(a => ({
            id_encabezado: a.id_encabezado,
            folio: a.folio,
            imp: a.saldo
        }));
    }
}

// ============================================
// 5. GESTOR DE FORMULARIO
// ============================================
class FacturasFormManager {
    constructor(productManager) {
        this.productManager = productManager;
        this.form = document.getElementById('vi-fac-formCotizacion');
        this.btnSubmit = document.querySelector('.vi-fac-btn-submit');
        this.initFacturasFormManager();
    }

    initFacturasFormManager() {
        this.btnSubmit?.addEventListener('click', (e) => {
            e.preventDefault();
            // Llamar a la función global
            enviarFacturasCotizacionSync();
        });
    }

    recopilarFacturasDatosFormulario() {
        const formData = new FormData();
        const toggle = document.getElementById('vi-fac-toggle-pago');
        const tipoPago = toggle?.getAttribute('data-state') || 'contado';

        const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
        if (token) formData.append('__RequestVerificationToken', token);

        const camposGenerales = [
            'vi-fac-tipo-docto-mov',
            'vi-fac-folio', 'vi-fac-documentid', 'vi-fac-cliente',
            'vi-fac-rfc', 'vi-fac-vendedor', 'vi-fac-moneda',
            'vi-fac-paridad', 'vi-fac-concepto',
            'vi-fac-metodo-pago', 'vi-fac-forma-pago',
            'vi-fac-uso-cfdi', 'vi-fac-comentarios'
        ];

        camposGenerales.forEach(id => {
            const el = document.getElementById(id);
            if (el) formData.append(id.replace('vi-fac-', ''), el.value || '');
        });

        //formData.set('sucursal', suc.getValue());
        //formData.set('almacen', alm.getValue());

        if (tipoPago === 'credito') {
            formData.append('limiteCredito', document.getElementById('vi-fac-limite-credito')?.value || '');
            formData.append('plazo', document.getElementById('vi-fac-plazo')?.value || '');
            formData.append('fechaPago', document.getElementById('vi-fac-fecha-pago')?.value || '');
        }

        if (tipoPago === 'anticipo') {
            formData.append('fechaAnticipo', document.getElementById('vi-fac-fecha-anticipo')?.value || '');
        }

        const productos = this.productManager.getFacturasProductosData();
        if (tipoPago !== 'anticipo') {
            formData.append('productosJSON', JSON.stringify(productos));
        }

        formData.append('ordenCompra', document.getElementById('vi-fac-orden-compra-val')?.value || '');

        // Tokens de autorización: el servidor los necesita para saber si este usuario
        // puede facturar por debajo del piso de la regla.
        formData.append('descuentoToken', facturasAuthState.descuentoToken || '');
        formData.append('precioToken', facturasAuthState.precioToken || '');

        // Agregar anticipos aplicados
        const anticipos = this.productManager.getAnticiposData();
        if (anticipos.length > 0) {
            formData.append('anticiposJSON', JSON.stringify(anticipos));
        }
        const adendaSeleccionada = document.getElementById('vi-fac-adenda-seleccionada')?.value;
        if (adendaSeleccionada) {
            formData.append('idAdenda', adendaSeleccionada);
        }

        const totales = this.productManager.totales;
        formData.append('subtotal1', totales.subtotal1.toFixed(2));
        formData.append('descuento', totales.descuento.toFixed(2));
        formData.append('flete', totales.flete.toFixed(2));
        formData.append('subtotal2', totales.subtotal2.toFixed(2));
        formData.append('iva', totales.iva.toFixed(2));
        formData.append('total', totales.total.toFixed(2));
        formData.append('totalAnticipos', totales.totalAnticipos.toFixed(2));
        formData.append('totalFinal', totales.totalFinal.toFixed(2));

        formData.append('tipo', tipoPago);
        // Identifica la autorización de gerencia cuando el documento aún no existe.
        formData.append('creditoToken', window.CreditoVentas?.token('vi-fac') || '');

        return formData;
    }

    validarFacturasFormulario() {
        const errores = [];

        const togglePago = document.getElementById('vi-fac-toggle-pago');
        const tipoPago = togglePago ? togglePago.getAttribute('data-state') : 'contado';

        const cliente = document.getElementById('vi-fac-cliente')?.value;
        if (!cliente || cliente.trim() === '') {
            errores.push('Debe seleccionar un cliente');
        }

        const productos = this.productManager.getFacturasProductosData();
        if (tipoPago !== 'anticipo' && productos.length === 0) {
            errores.push('Debe agregar al menos un producto');
        }

        const vendedor = document.getElementById('vi-fac-vendedor')?.value;
        if (!vendedor || vendedor === 'Seleccionar vendedor') {
            errores.push('Debe seleccionar un vendedor');
        }

        const rfcInput = document.getElementById('vi-fac-rfc');
        const rfc = rfcInput ? rfcInput.value.trim() : '';
        if (!rfc) {
            errores.push('Debe ingresar el RFC');
        }

        const monedaSelect = tomManager.getInstance('vi-fac-moneda');
        const monedaValue = monedaSelect?.getValue?.() ?? ''; 
        if (!monedaValue) {
            errores.push('Debe seleccionar una Moneda');
        }

        const usoCfdiSelect = tomManager.getInstance('vi-fac-uso-cfdi');
        const usoCfdiValue = usoCfdiSelect?.getValue?.() ?? '';
        if (!usoCfdiValue) {
            errores.push('Debe seleccionar un Uso CFDI');
        }

        const formaPagoSelect = tomManager.getInstance('vi-fac-forma-pago');
        const formaPagoValue = formaPagoSelect?.getValue?.() ?? '';
        if (!formaPagoValue) {
            errores.push('Debe seleccionar una Forma de Pago');
        }

        const fechaPagoInput = document.getElementById('vi-fac-fecha-pago');
        const fechaPago = fechaPagoInput ? fechaPagoInput.value.trim() : '';

        if (tipoPago === 'credito' && !fechaPago) {
            errores.push('Debe ingresar la Fecha de Pago (solo para crédito)');
        }

        // Crédito: solo agrega errores cuando la venta es a crédito y excede el límite.
        errores.push(...(window.CreditoVentas?.validarAntesDeGuardar('vi-fac') || []));

        return errores;
    }

    obtenerFacturasConfiguracionPorTipo(tipoMovimiento) {
        const configuraciones = {
            'alta': {
                endpoint: '/VIFactura/Guardar',
                metodo: 'POST',
                mensajeExito: 'Factura creada exitosamente',
                textoCarga: 'Guardando...',
                validarProductos: true
            },
            'modificacion': {
                endpoint: '/VIFactura/Modificar',
                metodo: 'PUT',
                mensajeExito: 'Factura modificada exitosamente',
                textoCarga: 'Actualizando...',
                validarProductos: true
            },
            'consulta': {
                endpoint: '/VIFactura/Consultar',
                metodo: 'GET',
                mensajeExito: 'Consulta realizada exitosamente',
                textoCarga: 'Consultando...',
                validarProductos: false
            }
        };

        return configuraciones[tipoMovimiento] || configuraciones['alta'];
    }

    

    limpiarFacturasFormulario() {
        this.form?.reset();
        this.productManager.productos = [];
        this.productManager.anticipos = [];
        this.productManager.renderFacturasTable();
        this.productManager.renderAnticiposTable();
        this.productManager.calcularFacturasTotales();

        const idsInput = document.getElementById('vi-fac-remisiones-ids');
        if (idsInput) idsInput.value = '';
        const detalleInput = document.getElementById('vi-fac-remisiones-detalle');
        if (detalleInput) detalleInput.value = '';

        document.getElementById('vi-fac-cliente').value = '';
        document.getElementById('vi-fac-rfc').value = '';
        document.getElementById('vi-fac-info-proveedor').value = '';
    }

    verFacturasDatosFormulario() {
        const formData = this.recopilarFacturasDatosFormulario();
        const obj = {};
        for (let [key, value] of formData.entries()) {
            obj[key] = value;
        }
        console.log('Datos de factura a enviar:', obj);
        return obj;
    }
}
// ============================================
// FUNCIÓN PRINCIPAL DE ENVÍO (SINCRÓNICA) - VERSIÓN CORREGIDA
// ============================================
// Esta función debe estar FUERA de la clase FacturasFormManager
async function enviarFacturasCotizacionSync() {
    try {
        // 1. Validaciones básicas usando el método existente
        const errores = FacturasApp.formManager.validarFacturasFormulario();
        if (errores && errores.length > 0) {
            toastMixin?.fire({
                icon: 'error',
                title: errores.join(', ')
            });
            return;
        }

        // 2. Obtener y mostrar selección de centro de costos y cuenta bancaria
        const datosSeleccionados = await mostrarSeleccionCentroCuentaFacturas();
        if (!datosSeleccionados) return; // Usuario canceló

        // 3. Obtener el total de documentos/productos para el modal de progreso
        const productos = FacturasApp.productManagerInstance.getFacturasProductosData();
        const totalDocumentos = productos.length || 1;

        // 4. Preparar FormData con TODOS los datos usando el método existente
        const formData = FacturasApp.formManager.recopilarFacturasDatosFormulario();

        // Agregar centro de costos y cuenta bancaria
        formData.append('CentroCostosId', datosSeleccionados.centro);
        //formData.append('CuentaBancariaId', datosSeleccionados.cuenta);

        // 5. Mostrar modal de progreso
        const modalProgreso = mostrarModalProgresoMejorado(totalDocumentos);

        // 6. Bloquear botón de submit
        const btnSubmit = document.querySelector('.vin-fac-btn-submit');
        const originalText = btnSubmit?.innerHTML || 'Procesar';
        if (btnSubmit) {
            btnSubmit.disabled = true;
            btnSubmit.innerHTML = `<i class="fas fa-spinner fa-spin"></i> Procesando facturación...`;
        }

        try {
            // 7. LLAMADA ÚNICA AL SERVIDOR (proceso completo)
            actualizarProgresoDetallado(modalProgreso, 1, 'Iniciando proceso de facturación...');

            const response = await fetch('/VIFactura/ProcesarDocumentosAsync', {
                method: 'POST',
                body: formData
            });

            // 8. Validar respuesta HTTP
            if (!response.ok) {
                const errorText = await response.text();
                cerrarModalProgreso(modalProgreso);
                mostrarErrorServidor(response.status, errorText);
                return;
            }

            // 9. Parsear respuesta JSON
            const result = await response.json();

            // 10. Simular progreso visual mientras se procesa
            await simularProgresoExitoso(modalProgreso);

            // 11. Cerrar modal de progreso
            cerrarModalProgreso(modalProgreso);

            // 12. Manejar resultado según éxito o error
            if (result.success) {
                // ✅ ÉXITO - Mostrar resumen completo
                mostrarResumenExitoso(result, totalDocumentos);

                // El saldo de las remisiones ya se descontó: si estos renglones
                // siguieran en la tabla, un segundo envío intentaría facturarlos
                // otra vez. Se retiran solo los que vienen de remisiones.
                const pm = FacturasApp.productManagerInstance;
                if (pm?.productos?.some(p => p._partidaRemisionId != null)) {
                    pm.productos = pm.productos.filter(p => p._partidaRemisionId == null);
                    pm.renderFacturasTable();
                    pm.calcularFacturasTotales();
                }
                window.remisionParcialesManager?.limpiarSeleccion();
            } else {
                // ❌ ERROR - Mostrar detalles del error
                if (result.step) {
                    // Error en un paso específico
                    mostrarErrorPorPaso(result, totalDocumentos);
                } else {
                    // Error general
                    Swal.fire({
                        icon: 'error',
                        title: 'Error en el proceso',
                        text: result.message || 'Ocurrió un error desconocido',
                        confirmButtonColor: '#7c3aed'
                    });
                }
            }

        } catch (error) {
            // Error de conexión o procesamiento
            console.error('❌ Error en enviarFacturasCotizacionSync:', error);
            cerrarModalProgreso(modalProgreso);
            mostrarErrorConexion(error);
        } finally {
            // 13. Restaurar botón
            if (btnSubmit) {
                btnSubmit.disabled = false;
                btnSubmit.innerHTML = originalText;
            }
        }

    } catch (error) {
        console.error('❌ Error general en enviarFacturasCotizacionSync:', error);
        Swal.fire({
            icon: 'error',
            title: 'Error',
            text: error.message || 'Ocurrió un error inesperado',
            confirmButtonColor: '#7c3aed'
        });
    }
}

// ============================================
// MODAL PARA SELECCIÓN DE CENTRO Y CUENTA
// ============================================
// Esta función debe estar FUERA de la clase FacturasFormManager
async function mostrarSeleccionCentroCuentaFacturas() {
    const CENTRO_DEFAULT = '13';
    const CUENTA_DEFAULT = '1-1-02-01';
    try {
        // Obtener centros de costos usando GetData (tu función existente)
        const centrosResponse = await GetData({ path: '/DatosGenerales/DatosCentroCostos' });

        if (!centrosResponse.success) {
            Swal.fire({
                icon: 'error',
                title: centrosResponse.message || 'Error al cargar centros de costos',
                confirmButtonColor: '#7c3aed'
            });
            return null;
        }

        const centros = centrosResponse.data || centrosResponse.result || [];
        if (centros.length === 0) {
            Swal.fire({
                icon: 'warning',
                title: 'No hay centros de costos disponibles',
                confirmButtonColor: '#7c3aed'
            });
            return null;
        }

        // Mostrar modal de selección con diseño mejorado
        const { value: datosSeleccionados } = await Swal.fire({
            title: '📋 Selecciona Centro de Costos',
            html: `
                <div style="text-align: left; padding: 1rem;">
                    <!-- Centro de Costos -->
                    <div style="margin-bottom: 1.5rem;">
                        <label style="
                            display: block;
                            color: #374151;
                            font-weight: 600;
                            margin-bottom: 0.5rem;
                            font-size: 0.95rem;
                        ">
                            <i class="fas fa-building"></i> Centro de Costos *
                        </label>
                        <select id="swal-centro-factura" style="
                            width: 100%;
                            padding: 0.75rem;
                            border: 2px solid #e5e7eb;
                            border-radius: 8px;
                            font-size: 1rem;
                        ">
                            <option value="">-- Selecciona un centro de costos --</option>
                            ${centros.map(c => `<option value="${c.areaid}">${c.nombre}</option>`).join('')}
                        </select>
                    </div>

                    <!-- Cuenta Bancaria
                    <div>
                        <label style="
                            display: block;
                            color: #374151;
                            font-weight: 600;
                            margin-bottom: 0.5rem;
                            font-size: 0.95rem;
                        ">
                            <i class="fas fa-university"></i> Cuenta Bancaria *
                        </label>
                        <select id="swal-cuenta-factura" style="
                            width: 100%;
                            padding: 0.75rem;
                            border: 2px solid #e5e7eb;
                            border-radius: 8px;
                            font-size: 1rem;
                        ">
                            <option value="">Cargando cuentas...</option>
                        </select>
                    </div>
                     -->
                    <!-- Nota informativa -->
                    <div style="
                        margin-top: 1.5rem;
                        padding: 1rem;
                        background: linear-gradient(135deg, #dbeafe 0%, #bfdbfe 100%);
                        border-radius: 8px;
                        border-left: 4px solid #3b82f6;
                    ">
                        <p style="
                            margin: 0;
                            color: #1e40af;
                            font-size: 0.85rem;
                            line-height: 1.5;
                        ">
                            <i class="fas fa-info-circle"></i>
                            <strong>Importante:</strong> Estos datos son necesarios para generar 
                            correctamente la factura y las pólizas contables asociadas.
                        </p>
                    </div>
                </div>
            `,
            showCancelButton: true,
            confirmButtonText: '<i class="fas fa-arrow-right"></i> Continuar',
            cancelButtonText: '<i class="fas fa-times"></i> Cancelar',
            confirmButtonColor: '#7c3aed',
            cancelButtonColor: '#6b7280',
            width: '600px',
            focusConfirm: false,

            didOpen: async () => {
                try {
                    const selectCentro = document.getElementById('swal-centro-factura');
                    selectCentro.value = CENTRO_DEFAULT;
                    // Cargar cuentas bancarias usando GetData
                    //const resp = await GetData({ path: '/DatosGenerales/DatosBancos' });
                    //const selectCuenta = document.getElementById('swal-cuenta-factura');
                    //selectCuenta.innerHTML = `<option value="">-- Selecciona una cuenta --</option>`;

                    //if (resp.success && resp.result?.length) {
                    //    resp.result.forEach(c => {
                    //        selectCuenta.innerHTML += `
                    //            <option value="${c.codigo}">
                    //                ${c.codigo} - ${c.nombre}
                    //            </option>`;
                    //    });
                    //    selectCuenta.value = CUENTA_DEFAULT;
                    //} else {
                    //    selectCuenta.innerHTML = `<option value="">No hay cuentas disponibles</option>`;
                    //}
                } catch (err) {
                    console.error('Error cargando cuentas:', err);
                    document.getElementById('swal-cuenta-factura').innerHTML =
                        `<option value="">Error al cargar cuentas</option>`;
                }
            },

            preConfirm: () => {
                const centro = document.getElementById('swal-centro-factura').value;
/*                const cuenta = document.getElementById('swal-cuenta-factura').value;*/

                if (!centro) {
                    Swal.showValidationMessage('Por favor selecciona un centro de costos');
                    return false;
                }

                //if (!cuenta) {
                //    Swal.showValidationMessage('Por favor selecciona una cuenta bancaria');
                //    return false;
                //}

                return { centro };
            }
        });

        return datosSeleccionados;

    } catch (error) {
        console.error('Error en mostrarSeleccionCentroCuentaFacturas:', error);
        Swal.fire({
            icon: 'error',
            title: 'Error al cargar datos',
            text: error.message,
            confirmButtonColor: '#7c3aed'
        });
        return null;
    }
}

// ============================================
// 6. APLICACIÓN PRINCIPAL
// ============================================
const FacturasApp = {
    clienteManager: null,
    productoManager: null,
    documentoManager: null,
    productManagerInstance: null,
    formManager: null,
    anticipoManager: null,
    form: null,
    btnSubmit: null,

    // id_cliente del documento. Se manda explícito en cada consulta de precio en vez de
    // depender de Session["idCliente"], que es global al usuario y se pisa entre pestañas.
    clienteIdActual: 0,
    // Clave con la que se resolvió clienteIdActual, para no repetir la consulta ni
    // quedarse con el id de un cliente que ya se cambió.
    clienteCveResuelta: '',

    /**
     * Devuelve el id del cliente del documento, resolviéndolo si hace falta.
     *
     * En una factura el cliente casi siempre llega al cargar la remisión o el pedido de
     * origen, no por el modal, y por esa vía solo se conoce la clave. Sin esto, agregar
     * un producto después consultaba el precio con clienteId = 0.
     */
    async asegurarClienteId() {
        const cve = document.getElementById('vi-fac-cliente')?.value || '';

        if (!cve) {
            this.clienteIdActual = 0;
            this.clienteCveResuelta = '';
            return 0;
        }

        if (this.clienteIdActual > 0 && this.clienteCveResuelta === cve)
            return this.clienteIdActual;

        this.clienteIdActual = await ReglasPrecio.resolverClienteId(cve);
        this.clienteCveResuelta = cve;
        return this.clienteIdActual;
    },

    /**
     * Recalcula las partidas ya capturadas con la lista de precios del cliente actual.
     */
    async recotizarPartidas(nombreCliente) {
        const pm = this.productManagerInstance;
        if (!pm || !pm.productos || pm.productos.length === 0) return;

        const actualizadas = await ReglasPrecio.recotizar(
            pm.productos, await this.asegurarClienteId(), '/DatosGenerales/BuscarProducto');

        if (actualizadas === 0) return;

        pm.renderFacturasTable();
        pm.calcularFacturasTotales();
        ReglasPrecio.avisar(
            `Precios recalculados con la lista de ${nombreCliente || 'el cliente'} `
            + `(${actualizadas} partida${actualizadas === 1 ? '' : 's'})`,
            'info');
    },

    initFacturas() {
        this.form = document.getElementById('vi-fac-formCotizacion');
        this.btnSubmit = document.querySelector('.vi-fac-btn-submit');

        cargarPermisosFacturas();

        // Crédito: la factura es el último punto de control antes de timbrar; bloquea.
        window.CreditoVentas?.registrar({
            prefix: 'vi-fac',
            modo: 'bloquear',
            documento: 'factura',
            getTipoPago: () =>
                document.getElementById('vi-fac-tipo-pago-hidden-state')?.value || 'contado',
            totalId: 'vi-fac-importe',
            submitSelector: '.vi-fac-btn-submit',
            clienteInputId: 'vi-fac-cliente',
            folioId: 'vi-fac-folio',
            documentIdId: 'vi-fac-documentid',
            getProductos: () => FacturasApp.productManagerInstance?.getFacturasProductosData() || [],
            getTotales: () => FacturasApp.productManagerInstance?.totales || {}
        });

        this.clienteManager = new FacturasDataManager({
            endpoint: '/DatosGenerales/BuscarC',
            detailEndpoint: '/DatosGenerales/BuscarCliente',
            modalId: 'vi-fac-modalBuscarCliente',
            inputId: 'vi-fac-inputBuscarCliente',
            resultsId: 'vi-fac-listaResultadosClientes',
            pageSizeId: 'vi-fac-pageSizeClientes',
            btnClearId: 'vi-fac-btnLimpiarClientes',
            spinnerId: 'vi-fac-spinnerClientes',
            paginationId: 'vi-fac-paginationClientes',
            recordsFromId: 'vi-fac-recordsFromClientes',
            recordsToId: 'vi-fac-recordsToClientes',
            totalRecordsId: 'vi-fac-totalRecordsClientes',
            shouldCloseOnSelect: true
        });

        this.clienteManager.onFacturasSelect = (cliente) => {
            document.getElementById('vi-fac-cliente').value = cliente.id || '';
            document.getElementById('vi-fac-rfc').value = cliente.rfc || '';
            document.getElementById('vi-fac-info-proveedor').value =
                [cliente.dir, cliente.col, cliente.pob, cliente.cp]
                    .filter(x => x)
                    .join(',\n');

            const vendedorInstance = tomManager.getInstance('vi-fac-vendedor');
            if (vendedorInstance && cliente.cve_vdr) {
                vendedorInstance.setValue(cliente.cve_vdr, true);
            }
            const fpagoInstance = tomManager.getInstance('vi-fac-forma-pago');
            if (fpagoInstance && cliente.forma_pago) {
                fpagoInstance.setValue(cliente.forma_pago, true);
            }
            const cfdiInstance = tomManager.getInstance('vi-fac-uso-cfdi');
            if (cfdiInstance && cliente.uso_sugerido) {
                cfdiInstance.setValue(cliente.uso_sugerido, true);
            }
            this.mostrarAdendasCliente(cliente.adendas || []);
            window.CreditoVentas?.setCliente('vi-fac', cliente);

            // El cliente define la lista de precios y las reglas por cliente: se manda
            // explícito al buscar productos y se recalcula lo ya capturado.
            FacturasApp.clienteIdActual = parseInt(cliente.id_cliente, 10) || 0;
            FacturasApp.clienteCveResuelta = cliente.id || '';
            FacturasApp.recotizarPartidas(cliente.descripcion);
        };



        this.productoManager = new FacturasDataManager({
            endpoint: '/DatosGenerales/BuscarP',
            detailEndpoint: '/DatosGenerales/BuscarProducto',
            modalId: 'vi-fac-modalBuscarProducto',
            inputId: 'vi-fac-inputBuscarProducto',
            resultsId: 'vi-fac-listaResultadosProductos',
            pageSizeId: 'vi-fac-pageSizeProductos',
            btnClearId: 'vi-fac-btnLimpiarProductos',
            spinnerId: 'vi-fac-spinnerProductos',
            paginationId: 'vi-fac-paginationProductos',
            recordsFromId: 'vi-fac-recordsFromProductos',
            recordsToId: 'vi-fac-recordsToProductos',
            totalRecordsId: 'vi-fac-totalRecordsProductos',
            shouldCloseOnSelect: false
        });

        this.productManagerInstance = new FacturasProductManager();

        this.productoManager.getExtraParams = async () => ({ clienteId: await this.asegurarClienteId() });
        this.productoManager.onFacturasSelect = (producto) => {
            this.productManagerInstance.agregarFacturasProducto(producto);
        };

        this.documentoManager = new FacturasDocumentoManager({
            endpoint: '/DatosGenerales/BuscarDVIremAgrupadas',
            detailEndpoint: '/DatosGenerales/BuscarDocumentoMultiple',
            //endpoint: '/DatosGenerales/BuscarDVIrem',
            //detailEndpoint: '/DatosGenerales/BuscarDocumento',
            modalId: 'vi-fac-modalBuscarDocumentos',
            inputId: 'vi-fac-inputBuscarDocumento',
            resultsId: 'vi-fac-listaResultadosDocumentos',
            pageSizeId: 'vi-fac-pageSizeDocumentos',
            btnClearId: 'vi-fac-btnLimpiarDocumentos',
            spinnerId: 'vi-fac-spinnerDocumentos',
            paginationId: 'vi-fac-paginationDocumentos',
            recordsFromId: 'vi-fac-recordsFromDocumentos',
            recordsToId: 'vi-fac-recordsToDocumentos',
            totalRecordsId: 'vi-fac-totalRecordsDocumentos',
            shouldCloseOnSelect: true,
            filtros: {}
        });

        const filtroEstado = document.getElementById('vi-fac-filtroEstado');
        const filtroFecha = document.getElementById('vi-fac-filtroFecha');

        filtroEstado?.addEventListener('change', (e) => {
            this.documentoManager.filtros.estado = e.target.value;
            this.documentoManager.state.page = 1;
            this.documentoManager.fetchFacturasData();
        });

        filtroFecha?.addEventListener('change', (e) => {
            this.documentoManager.filtros.fecha = e.target.value;
            this.documentoManager.state.page = 1;
            this.documentoManager.fetchFacturasData();
        });

        this.documentoManager.onFacturasSelect = async (documento) => {
            try {
                if (window.Swal) {
                    Swal.fire({
                        title: 'Cargando documento...',
                        html: 'Por favor espere',
                        allowOutsideClick: false,
                        didOpen: () => {
                            Swal.showLoading();
                        }
                    });
                }

                await this.cargarDocumentoCompleto(documento);

                if (window.Swal) {
                    Swal.close();
                }

                if (window.toastMixin) {
                    toastMixin.fire({
                        icon: 'success',
                        title: `Documento ${documento.folio} cargado correctamente`
                    });
                }

            } catch (error) {
                console.error('Error cargando documento:', error);
                if (window.Swal) {
                    Swal.fire({
                        icon: 'error',
                        title: 'Error',
                        text: 'No se pudo cargar el documento completo'
                    });
                }
            }
        };

        this.anticipoManager = new FacturasAnticipoManager({
            endpoint: '/DatosGenerales/BuscarAnt',
            detailEndpoint: '/DatosGenerales/BuscarAnticipo',
            modalId: 'vi-fac-modalBuscarAnticipos',
            inputId: 'vi-fac-inputBuscarAnticipo',
            resultsId: 'vi-fac-listaResultadosAnticipos',
            pageSizeId: 'vi-fac-pageSizeAnticipos',
            btnClearId: 'vi-fac-btnLimpiarAnticipos',
            spinnerId: 'vi-fac-spinnerAnticipos',
            paginationId: 'vi-fac-paginationAnticipos',
            recordsFromId: 'vi-fac-recordsFromAnticipos',
            recordsToId: 'vi-fac-recordsToAnticipos',
            totalRecordsId: 'vi-fac-totalRecordsAnticipos',
            shouldCloseOnSelect: true
        });

        const modalAnticipos = document.getElementById('vi-fac-modalBuscarAnticipos');
        if (modalAnticipos) {
            modalAnticipos.addEventListener('show.bs.modal', () => {
                const clienteId = document.getElementById('vi-fac-cliente')?.value;
                if (!clienteId || clienteId.trim() === '') {
                    if (window.toastMixin) {
                        toastMixin.fire({
                            icon: 'warning',
                            title: 'Debe seleccionar un cliente primero'
                        });
                    }
                    bootstrap.Modal.getInstance(modalAnticipos)?.hide();
                    return;
                }
                this.anticipoManager.setClienteActual(clienteId);
            });
        }

        this.anticipoManager.onFacturasSelect = async (anticipo) => {
            try {
                if (window.Swal) {
                    Swal.fire({
                        title: 'Agregando anticipo...',
                        html: 'Por favor espere',
                        allowOutsideClick: false,
                        didOpen: () => Swal.showLoading()
                    });
                }

                this.productManagerInstance.agregarAnticipo(anticipo);

                if (window.Swal) Swal.close();

            } catch (error) {
                console.error('Error agregando anticipo:', error);
                if (window.Swal) {
                    Swal.fire({
                        icon: 'error',
                        title: 'Error',
                        text: 'No se pudo agregar el anticipo'
                    });
                }
            }
        };

        this.formManager = new FacturasFormManager(this.productManagerInstance);

        const tipoMovSelect = document.getElementById('vi-fac-tipo-docto-mov');
        tipoMovSelect?.addEventListener('change', (e) => {
            this.manejarFacturasCambioTipoMovimiento(e.target.value);
        });

        this.manejarFacturasCambioTipoMovimiento(tipoMovSelect?.value || 'alta');

        console.log('Sistema de facturas inicializado correctamente');

        window.verFacturasDatosFormulario = () => this.formManager.verFacturasDatosFormulario();

        if (typeof rol !== 'undefined' && rol !== 'Gerente') {
            this.deshabilitarFacturasFormulario()
        } else {
            this.habilitarFacturasFormulario()
        }
    },
    mostrarAdendasCliente(adendas) {
        const seccionAdendas = document.getElementById('vi-fac-seccion-adendas');
        const containerAdendas = document.getElementById('vi-fac-adendas-container');
        const hiddenInput = document.getElementById('vi-fac-adenda-seleccionada');

        if (!seccionAdendas || !containerAdendas) return;

        // Limpiar contenedor
        containerAdendas.innerHTML = '';
        hiddenInput.value = '';

        // Si no hay adendas, ocultar la sección
        if (!adendas || adendas.length === 0) {
            seccionAdendas.style.display = 'none';
            return;
        }

        // Mostrar la sección
        seccionAdendas.style.display = '';

        // Crear opciones de radio
        adendas.forEach((adenda, index) => {
            const optionDiv = document.createElement('div');
            optionDiv.className = 'adenda-option';

            const radioId = `adenda-radio-${adenda.id_addenda}`;
            const accordionId = `accordion-${adenda.id_addenda}`;

            // Formatear el data_template (JSON)
            let templatePreview = '';
            try {
                const templateObj = typeof adenda.data_template === 'string'
                    ? JSON.parse(adenda.data_template)
                    : adenda.data_template;
                templatePreview = JSON.stringify(templateObj, null, 2);
            } catch (e) {
                templatePreview = adenda.data_template || 'Sin template';
            }


            const fechaActualizacion = adenda.updated_at ? new Date(adenda.updated_at).toLocaleString('es-MX', {
                day: '2-digit',
                month: '2-digit',
                year: 'numeric',
                hour: '2-digit',
                minute: '2-digit'
            }) : 'N/A';

            // Determinar si usa conceptos
            const usaConceptos = adenda.usar_conceptos === true || adenda.usar_conceptos === 'true' || adenda.usar_conceptos === 1;

            optionDiv.innerHTML = `
            <input type="radio" 
                   id="${radioId}" 
                   name="adenda-radio" 
                   value="${adenda.id_addenda}"
                   ${index === 0 ? 'checked' : ''}>
            <label class="adenda-label" for="${radioId}">
                <div class="adenda-header">
                    <span class="adenda-nombre">
                        <i class="fas fa-file-contract me-2"></i>
                        ${this.escapeHtml(adenda.nombre || 'Sin nombre')}
                    </span>
                    <div class="adenda-badges">
                        <span class="adenda-badge badge-version">
                            <i class="fas fa-code-branch"></i> v${adenda.version || '1.0'}
                        </span>
                        <span class="adenda-badge badge-prefix">
                            <i class="fas fa-tag"></i> ${this.escapeHtml(adenda.xml_prefix || 'N/A')}
                        </span>
                        ${usaConceptos ? '<span class="adenda-badge badge-conceptos"><i class="fas fa-list"></i> Usa Conceptos</span>' : ''}
                    </div>
                </div>
                
                <div class="adenda-details">
                    <div class="adenda-detail-item">
                        <span class="detail-label"><i class="fas fa-fingerprint"></i> ID</span>
                        <span class="detail-value">${adenda.id_addenda}</span>
                    </div>
                    <div class="adenda-detail-item">
                        <span class="detail-label"><i class="fas fa-link"></i> Namespace</span>
                        <span class="detail-value">${this.escapeHtml(adenda.xml_namespace || 'N/A')}</span>
                    </div>
                </div>
                
                <div class="adenda-template-accordion">
                    <button type="button" class="adenda-template-toggle" data-accordion="${accordionId}">
                        <span><i class="fas fa-code"></i> Ver Template JSON</span>
                        <i class="fas fa-chevron-down"></i>
                    </button>
                    <div class="adenda-template-content" id="${accordionId}">
                        <div class="template-content">${this.escapeHtml(templatePreview)}</div>
                    </div>
                </div>
                
                <div class="adenda-timestamp">
                    <span><i class="fas fa-calendar-plus"></i> ${dateFormatter(adenda.created_at)}</span>
                    <span><i class="fas fa-calendar-check"></i> ${adenda.updated_at ? dateFormatter(adenda.updated_at) : 'N/A'}</span>
                </div>
            </label>
        `;

            // Manejar acordeón
            const toggleBtn = optionDiv.querySelector('.adenda-template-toggle');
            const content = optionDiv.querySelector(`#${accordionId}`);

            toggleBtn.addEventListener('click', (e) => {
                e.preventDefault();
                e.stopPropagation();

                toggleBtn.classList.toggle('expanded');
                content.classList.toggle('show');
            });

            // Manejar selección
            const radio = optionDiv.querySelector('input[type="radio"]');
            radio.addEventListener('change', (e) => {
                // Remover selección visual de todos
                document.querySelectorAll('.adenda-option').forEach(opt => {
                    opt.classList.remove('selected');
                });

                // Agregar selección visual al actual
                if (e.target.checked) {
                    optionDiv.classList.add('selected');
                    hiddenInput.value = e.target.value;

                    if (window.toastMixin) {
                        toastMixin.fire({
                            icon: 'success',
                            title: `Adenda seleccionada: ${adenda.nombre}`,
                            text: `ID: ${adenda.id_addenda} | Versión: ${adenda.version}`
                        });
                    }
                }
            });

            // Seleccionar visualmente el primero por defecto
            if (index === 0) {
                optionDiv.classList.add('selected');
                hiddenInput.value = adenda.id_addenda;
            }

            containerAdendas.appendChild(optionDiv);
        });

        // Mensaje si no hay adendas
        if (containerAdendas.children.length === 0) {
            containerAdendas.innerHTML = `
            <div class="no-adendas-message">
                <i class="fas fa-inbox"></i>
                <p>No hay adendas activas disponibles para este cliente</p>
            </div>
        `;
        }
    },

    escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    },
    async cargarDocumentoCompleto(documento) {
        // ✅ Cargar información básica
        document.getElementById('vi-fac-sucursal').value = documento.sucursal || '';
        //document.getElementById('vi-fac-almacen').value = documento.almacen || '';
        document.getElementById('vi-fac-documentid').value = documento.id_encabezado || '';

        document.getElementById('vi-fac-cliente').value = documento.cli_prov || '';
        document.getElementById('vi-fac-rfc').value = documento.rfc || '';
        document.getElementById('vi-fac-info-proveedor').value = documento.info_cli || '';
        document.getElementById('vi-fac-paridad').value = documento.par || '';

        // ✅ TomSelect instances
        const vendedorInstance = tomManager.getInstance('vi-fac-vendedor');
        if (vendedorInstance && documento.vdr_cpr) {
            vendedorInstance.setValue(documento.vdr_cpr, true);
        }

        const monedaInstance = tomManager.getInstance('vi-fac-moneda');
        if (monedaInstance && documento.ccy) {
            monedaInstance.setValue(documento.ccy, true);
        }

        const cfdiInstance = tomManager.getInstance('vi-fac-uso-cfdi');
        if (cfdiInstance && documento.usoCfdi) {
            cfdiInstance.setValue(documento.usoCfdi, true);
        }

        const formaPagoInstance = tomManager.getInstance('vi-fac-forma-pago');
        if (formaPagoInstance && documento.formaPago) {
            formaPagoInstance.setValue(documento.formaPago, true);
        }

        const formaFP = tomManager.getInstance('vi-fac-forma-pago');
        if (formaFP && documento.f_pago) {
            formaFP.setValue(documento.f_pago, true);
        }

        const formaCDFI = tomManager.getInstance('vi-fac-uso-cfdi');
        if (formaCDFI && documento.cfdi) {
            formaCDFI.setValue(documento.cfdi, true);
        }

        const formaSucursal = tomManager.getInstance('vi-fac-sucursal');
        if (formaSucursal && documento.suc) {
            formaSucursal.setValue(documento.suc, false);
        }

        const formaAlmacen = tomManager.getInstance('vi-fac-almacen');
        if (formaAlmacen && documento.alm) {
            formaAlmacen.setValue(documento.alm, true);
        }
        // ✅ Detectar tipo de pago desde mdp del documento
        if (documento.mdp) {
            const tipoDesdeDoc = documento.mdp.toUpperCase() === 'PPD' ? 'credito' : 'contado';
            selectTipoPagoFac(tipoDesdeDoc);
        }


        // ✅ Información de crédito y pago
        // Vienen del documento cargado (se capturaron en el pedido y viajan pedido → RMP →
        // remisión → factura). pl_crd es el plazo del catálogo del cliente y solo sirve de
        // respaldo: recalcular siempre la fecha como hoy + pl_crd pisaba el vencimiento
        // pactado, que es el que termina en el CFDI.
        const plazoDoc = parseInt(documento.pl_dias) > 0 ? documento.pl_dias : (documento.pl_crd || '');
        document.getElementById('vi-fac-limite-credito').value = documento.lim_crd || '';
        document.getElementById('vi-fac-plazo').value = plazoDoc;
        //document.getElementById('vi-fac-metodo-pago').value = documento.mdp || '';

        // Fecha de pago: la del documento; si no trae, se calcula desde hoy + plazo.
        let fechaISO = documento.fecha_pago || '';
        if (!fechaISO) {
            const hoy = new Date();
            const diasCredito = parseInt(plazoDoc) || 0;
            const fechaPago = new Date(hoy);
            fechaPago.setDate(hoy.getDate() + diasCredito);
            fechaISO = fechaPago.toISOString().split('T')[0];
        }
        document.getElementById('vi-fac-fecha-pago').value = fechaISO;

        document.getElementById('vi-fac-concepto').value = documento.coment1 || '';
        document.getElementById('vi-fac-comentarios').value = documento.coment_aut || '';


        // ✅ Cargar productos
        if (documento.productos && Array.isArray(documento.productos)) {
            this.productManagerInstance.productos = [];

            documento.productos.forEach((prod, idx) => {
                // id único garantizado por índice para evitar colisiones aunque
                // Date.now()/Math.random() coincidan en ejecuciones muy rápidas
                const productoData = {
                    id: `prod_${Date.now()}_${idx}_${Math.random().toString(36).slice(2)}`,
                    productoId: prod.producto_id || prod.id,
                    descripcion: prod.descripcion,
                    existencia: prod.existencia,
                    cantidad: parseFloat(prod.cantidad) || 0,
                    precio: parseFloat(prod.precio) || 0,
                    // Referencia del precio pactado en el documento de origen: sin ella
                    // no hay contra qué comparar para saber si se modificó a mano.
                    precioOriginal: parseFloat(prod.precio) || 0,
                    descuento: parseFloat(prod.descuento) || 0,
                    unidad: prod.unidad || prod.udm || 'PZA',
                    existenciaGeneral: prod.existenciaGeneral || 0,
                    existenciaModular: prod.existenciaModular || 0,
                    comentario: prod.comentario || '',
                    pedimentos: prod.pedimentos || [],
                    origenDocumento: true // marca que viene de carga de documento, no de búsqueda manual
                };
                // push directo intencional: cada remisión/producto del documento es su propia fila
                this.productManagerInstance.productos.push(productoData);
            });

            this.productManagerInstance.renderFacturasTable();

            // El documento trae el precio pactado pero no los límites de la regla. Se
            // consultan aparte para que la factura conozca su piso y su tope sin
            // re-precificar lo que ya se remisionó.
            FacturasApp.asegurarClienteId()
                .then(clienteId => ReglasPrecio.hidratarLimites(
                    this.productManagerInstance.productos,
                    clienteId,
                    '/DatosGenerales/BuscarProducto'))
                .then(n => { if (n > 0) this.productManagerInstance.renderFacturasTable(); });
        }

        // ✅ Cargar flete si existe
        const fleteInput = document.getElementById('vi-fac-flete-val');
        if (fleteInput && documento.flete) {
            const chkFlete = document.getElementById("vi-fac-flete");
            chkFlete.checked = true;
            chkFlete.dispatchEvent(new Event("change"));
            fleteInput.value = documento.flete;
        }
        const ordenCompraInput = document.getElementById('vi-fac-orden-compra-val');
        if (ordenCompraInput && documento.ordencompra) {
            const chkOrdenCompra = document.getElementById("vi-fac-orden-compra-check");
            chkOrdenCompra.checked = true;
            chkOrdenCompra.dispatchEvent(new Event("change"));
            ordenCompraInput.value = documento.ordencompra;
            // Disparar validación OC igual que si el usuario hubiera escrito
            ordenCompraInput.dispatchEvent(new Event("input", { bubbles: true }));
        }
        // ⭐ NUEVA SECCIÓN: CARGAR ADENDAS ⭐
        // Mostrar todas las adendas disponibles del cliente
        if (documento.adendas && Array.isArray(documento.adendas)) {
            console.log('📋 Adendas encontradas:', documento.adendas);
            this.mostrarAdendasCliente(documento.adendas);

            // Si hay una adenda previamente seleccionada en el documento, marcarla
            if (documento.adenda_seleccionada) {
                const hiddenInput = document.getElementById('vi-fac-adenda-seleccionada');
                const radioSeleccionado = document.querySelector(`input[name="adenda-radio"][value="${documento.adenda_seleccionada}"]`);

                if (radioSeleccionado && hiddenInput) {
                    // Esperar a que se rendericen las adendas
                    setTimeout(() => {
                        // Desmarcar todos
                        document.querySelectorAll('.adenda-option').forEach(opt => {
                            opt.classList.remove('selected');
                            const radio = opt.querySelector('input[type="radio"]');
                            if (radio) radio.checked = false;
                        });

                        // Marcar el correcto
                        radioSeleccionado.checked = true;
                        hiddenInput.value = documento.adenda_seleccionada;

                        const optionDiv = radioSeleccionado.closest('.adenda-option');
                        if (optionDiv) {
                            optionDiv.classList.add('selected');
                        }

                        // Hacer scroll hasta la adenda seleccionada
                        if (optionDiv) {
                            optionDiv.scrollIntoView({ behavior: 'smooth', block: 'center' });
                        }

                        console.log('✅ Adenda seleccionada restaurada:', documento.adenda_seleccionada);

                        if (window.toastMixin) {
                            // Buscar el nombre de la adenda seleccionada
                            const adendaInfo = documento.adendas.find(a => a.id_addenda == documento.adenda_seleccionada);
                            toastMixin.fire({
                                icon: 'info',
                                title: `Adenda cargada: ${adendaInfo?.nombre || documento.adenda_seleccionada}`
                            });
                        }
                    }, 100);
                }
            }
        }

        // ✅ Registrar el encabezado como remisión a facturar completamente
        const idsRemisiones = documento.remisiones_ids
            || String(documento.id_encabezado || '');

        if (idsRemisiones) {
            const idsInput = document.getElementById('vi-fac-remisiones-ids');
            if (idsInput) {
                // Reemplazar completamente — cada documento cargado pisa al anterior
                idsInput.value = idsRemisiones;
            }
            const detalleInput = document.getElementById('vi-fac-remisiones-detalle');
            if (detalleInput) detalleInput.value = '';
        }

        // ✅ Calcular totales
        this.productManagerInstance.calcularFacturasTotales();

        // ✅ Cambiar a modo modificación
        const tipoMovSelect = document.getElementById('vi-fac-tipo-docto-mov');
        if (tipoMovSelect && documento.folio) {
            tipoMovSelect.value = 'modificacion';
            this.manejarFacturasCambioTipoMovimiento('modificacion');
        }

        // ✅ Aplicar tipo de pago desde mdp — AL FINAL para no interferir con otros campos
        if (documento.mdp) {
            const tipoDesdeDoc = documento.mdp.toUpperCase() === 'PPD' ? 'credito' : 'contado';
            // Actualizar toggle visualmente
            selectTipoPagoFac(tipoDesdeDoc);
            // Forzar el select con delay para asegurar que el DOM ya procesó todo
            setTimeout(() => {
                const select = document.getElementById('vi-fac-metodo-pago');
                const hidden = document.getElementById('vi-fac-metodo-pago-hidden');
                const valor = documento.mdp.toUpperCase(); // 'PUE' o 'PPD'
                if (hidden) hidden.value = valor;
                if (select) {
                    Array.from(select.options).forEach(opt => {
                        opt.selected = opt.value === valor;
                    });
                }
            }, 50);
        }

        // Crédito del cliente que viene con el documento (solo se usa si es a crédito).
        if (documento.estatus_credito) {
            window.CreditoVentas?.setCliente('vi-fac', {
                id: documento.cli_prov,
                lim_crd: documento.lim_crd || 0,
                credito_usado: documento.credito_usado || 0,
                credito_disponible: documento.credito_disponible || 0,
                estatus_cliente: documento.estatus_cliente
            });
        }

        // Si el pedido de origen ya fue autorizado por gerencia, no se vuelve a bloquear.
        window.CreditoVentas?.refrescarAutorizacion('vi-fac');
    },

    manejarFacturasCambioTipoMovimiento(tipoMovimiento) {
        if (tipoMovimiento === 'consulta') {
            this.deshabilitarFacturasFormulario();
        } else {
            this.habilitarFacturasFormulario();
        }
    },

    deshabilitarFacturasFormulario() {
        const facturasContenedor = document.getElementById('facturas-contenedor');
        if (!facturasContenedor) return;

        const formElements = facturasContenedor.querySelectorAll(
            'input:not([data-no-toggle]), select:not([data-no-toggle]), textarea, button'
        );
        formElements.forEach(element => {
            if (element.id !== 'vi-fac-tipo-docto-mov') {
                element.disabled = true;
                element.classList.add('vi-fac-disabled-field');
            }
        });

        const btnBuscarCliente = facturasContenedor.querySelector('[data-bs-target="#vi-fac-modalBuscarCliente"]');
        const btnBuscarProducto = facturasContenedor.querySelector('[data-bs-target="#vi-fac-modalBuscarProducto"]');
        if (btnBuscarCliente) btnBuscarCliente.disabled = true;
        if (btnBuscarProducto) btnBuscarProducto.disabled = true;

        const botonesEliminar = facturasContenedor.querySelectorAll('.vi-fac-btn-eliminar');
        botonesEliminar.forEach(btn => btn.disabled = true);

        if (window.tomManager) {
            ['vi-fac-vendedor', 'vi-fac-moneda', 'vi-fac-uso-cfdi', 'vi-fac-forma-pago'].forEach(id => {
                const instance = window.tomManager.instances?.get(id);
                if (instance) instance.disable();
            });
        }

        const btn = facturasContenedor.querySelector('.vi-fac-btn-submit');
        if (btn) {
            btn.disabled = false;
        }
        const btnDocConsulta = facturasContenedor.querySelector('.vi-fac-floating-btn');
        if (btnDocConsulta) {
            btnDocConsulta.disabled = false;
        }
        const btnPreview = facturasContenedor.querySelector('.vi-fac-ticket-btn');
        if (btnPreview) {
            btnPreview.disabled = false;
        }
        const estadoActual = document.getElementById('vi-fac-toggle-pago')
            ?.getAttribute('data-state') || 'contado';
        setMetodoPagoSegunTipo(estadoActual);
    },

    habilitarFacturasFormulario() {
        const facturasContenedor = document.getElementById('facturas-contenedor');
        if (!facturasContenedor) return;

        // Excluir elementos marcados con data-no-toggle
        const formElements = facturasContenedor.querySelectorAll(
            'input:not([data-no-toggle]), select:not([data-no-toggle]), textarea, button'
        );
        formElements.forEach(element => {
            element.disabled = false;
            element.classList.remove('vi-fac-disabled-field');
        });

        const btnBuscarCliente = facturasContenedor.querySelector('[data-bs-target="#vi-fac-modalBuscarCliente"]');
        const btnBuscarProducto = facturasContenedor.querySelector('[data-bs-target="#vi-fac-modalBuscarProducto"]');
        if (btnBuscarCliente) btnBuscarCliente.disabled = false;
        if (btnBuscarProducto) btnBuscarProducto.disabled = false;

        if (window.tomManager) {
            ['vi-fac-vendedor', 'vi-fac-moneda', 'vi-fac-uso-cfdi', 'vi-fac-forma-pago'].forEach(id => {
                const instance = window.tomManager.instances?.get(id);
                if (instance) instance.enable();
            });
        }

        const tipoMovimiento = document.getElementById('vi-fac-tipo-docto-mov')?.value;
        const btn = facturasContenedor.querySelector('.vi-fac-btn-submit');
        if (btn) {
            if (tipoMovimiento === 'modificacion') {
                btn.innerHTML = '<i class="fas fa-save"></i> Actualizar Factura';
            } else {
                btn.innerHTML = '<i class="fas fa-save"></i> Guardar Factura';
            }
        }
        const estadoActual = document.getElementById('vi-fac-toggle-pago')
            ?.getAttribute('data-state') || 'contado';
        setMetodoPagoSegunTipo(estadoActual);

        // Habilitar el formulario reactiva el submit: re-evaluar por si el crédito lo bloquea.
        window.CreditoVentas?.evaluar('vi-fac');
    }
};

// ============================================
// 7. FUNCIONES DE AUTENTICACIÓN
// ============================================
async function handleFacturasDescuentoAuth(e) {
    if (!e.target.classList.contains('vi-fac-descuento')) return;
    await pedirFacturasAutenticacion(e.target);
}

async function handleFacturasDescuentoF8(e) {
    if (e.key !== 'F8') return;

    const input = document.activeElement;
    if (!input.classList.contains('vi-fac-descuento')) return;

    e.preventDefault();
    await pedirFacturasAutenticacion(input);
}

async function pedirFacturasAutenticacion(input) {
    if (!input.hasAttribute('readonly')) return;

    if (!window.Swal) return;

    const { value: formValues } = await Swal.fire({
        title: 'Autenticación requerida',
        html:
            '<input id="swal-username" class="swal2-input" placeholder="Usuario">' +
            '<input id="swal-password" type="password" class="swal2-input" placeholder="Contraseña">',
        focusConfirm: false,
        preConfirm: () => ({
            username: document.getElementById('swal-username').value,
            password: document.getElementById('swal-password').value
        }),
        showCancelButton: true
    });

    if (!formValues) return;

    try {
        const formData = new FormData();
        formData.append('Username', formValues.username);
        formData.append('Password', formValues.password);

        const response = await fetch('/DatosGenerales/ValidarDescuento', {
            method: 'POST',
            body: formData
        });

        const result = await response.json();

        if (result.success) {
            if (window.toastMixin) {
                toastMixin.fire({
                    icon: 'success',
                    title: 'Autenticación exitosa. Ahora puede aplicar el descuento.'
                });
            }
            input.removeAttribute('readonly');
            input.focus();
        } else {
            Swal.fire('Error', 'Usuario o contraseña incorrectos', 'error');
        }
    } catch (err) {
        console.error(err);
        Swal.fire('Error', 'No se pudo validar la autenticación', 'error');
    }
}

function deshabilitarInputsSiNoEsGerente(rol) {
    if (rol !== "Gerente") {
        let form = document.getElementById('form-2')
            || document.querySelector('.form-2')
            || document.querySelector('form[id="form-2"]');

        if (!form) {
            console.error('No se pudo encontrar el formulario');
            return;
        }

        const elementos = form.querySelectorAll('input:not([type="hidden"]), select, textarea');
        elementos.forEach(el => el.disabled = true);

        const botones = form.querySelectorAll('button');
        botones.forEach(btn => btn.disabled = true);

        console.log(`Formulario deshabilitado: ${elementos.length} campos, ${botones.length} botones`);
    }
}

// ============================================
// 8. INICIALIZACIÓN GLOBAL
// ============================================
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => {
        FacturasApp.initFacturas();
        document.addEventListener('dblclick', handleFacturasDescuentoAuth);
        document.addEventListener('keydown', handleFacturasDescuentoF8);
        const fleteInput = document.getElementById('vi-fac-flete-val');
        if (!fleteInput) return;

        fleteInput.addEventListener('input', function () {
            //this.calcularTotales();
        });

        fleteInput.addEventListener('change', function () {
            //this.calcularTotales();
        });

    });
} else {
    FacturasApp.initFacturas();
    document.addEventListener('dblclick', handleFacturasDescuentoAuth);
    document.addEventListener('keydown', handleFacturasDescuentoF8);
}
window.enviarFacturasCotizacionSync = enviarFacturasCotizacionSync;
window.mostrarSeleccionCentroCuentaFacturas = mostrarSeleccionCentroCuentaFacturas;

// ── Verificador de Orden de Compra — Modal Bootstrap ─────────────
(function initOCValidator() {
    const chkOC = document.getElementById('vi-fac-orden-compra-check');
    const inputOC = document.getElementById('vi-fac-orden-compra-val');
    const DEBOUNCE = 600;

    if (!chkOC || !inputOC) return;

    // ── Inyectar modal en el body ──────────────────────────────────
    if (!document.getElementById('vi-fac-modal-oc-duplicada')) {
        document.body.insertAdjacentHTML('beforeend', `
        <div class="modal fade" id="vi-fac-modal-oc-duplicada" tabindex="-1"
             aria-labelledby="vi-fac-modal-oc-label" aria-hidden="true">
            <div class="modal-dialog modal-lg modal-dialog-scrollable">
                <div class="modal-content">

                    <div class="modal-header bg-danger bg-opacity-10 border-bottom border-danger border-opacity-25">
                        <div class="d-flex align-items-center gap-2">
                            <div style="
                                width:36px; height:36px; border-radius:50%;
                                background:#dc354520; display:flex;
                                align-items:center; justify-content:center; flex-shrink:0;">
                                <i class="fas fa-exclamation-triangle text-danger" style="font-size:16px;"></i>
                            </div>
                            <div>
                                <h5 class="modal-title mb-0 text-danger fw-semibold"
                                    id="vi-fac-modal-oc-label">
                                    Orden de compra duplicada
                                </h5>
                                <small class="text-muted" id="vi-fac-modal-oc-subtitulo"></small>
                            </div>
                        </div>
                        <button type="button" class="btn-close"
                                data-bs-dismiss="modal" aria-label="Cerrar"></button>
                    </div>

                    <div class="modal-body p-0">
                        <!-- Banner OC buscada -->
                        <div class="px-4 py-3 border-bottom"
                             style="">
                            <div class="d-flex align-items-center gap-2">
                                <span class="text-muted" style="font-size:.82rem;">
                                    Número buscado:
                                </span>
                                <code id="vi-fac-modal-oc-valor"
                                      class="fs-6 fw-bold text-danger px-2 py-1 rounded"
                                      style="background:#fce8e8; letter-spacing:.5px;"></code>
                            </div>
                        </div>

                        <!-- Tabla de usos -->
                        <div class="px-3 py-3">
                            <p class="text-muted mb-2" style="font-size:.82rem;">
                                Esta OC ya aparece en las siguientes facturas:
                            </p>
                            <div class="table-responsive">
                                <table class="table table-hover table-sm align-middle mb-0">
                                    <thead class="table-light">
                                        <tr>
                                            <th style="font-size:.78rem; font-weight:600;
                                                       text-transform:uppercase; letter-spacing:.4px;
                                                       color:#6c757d; width:140px;">
                                                Folio
                                            </th>
                                            <th style="font-size:.78rem; font-weight:600;
                                                       text-transform:uppercase; letter-spacing:.4px;
                                                       color:#6c757d;">
                                                Cliente
                                            </th>
                                            <th style="font-size:.78rem; font-weight:600;
                                                       text-transform:uppercase; letter-spacing:.4px;
                                                       color:#6c757d; width:110px;">
                                                Fecha
                                            </th>
                                            <th class="text-end"
                                                style="font-size:.78rem; font-weight:600;
                                                       text-transform:uppercase; letter-spacing:.4px;
                                                       color:#6c757d; width:110px;">
                                                Total
                                            </th>
                                            <th class="text-center"
                                                style="font-size:.78rem; font-weight:600;
                                                       text-transform:uppercase; letter-spacing:.4px;
                                                       color:#6c757d; width:100px;">
                                                Estatus
                                            </th>
                                        </tr>
                                    </thead>
                                    <tbody id="vi-fac-modal-oc-tbody"></tbody>
                                </table>
                            </div>
                        </div>

                        <!-- Aviso -->
                        <div class="mx-3 mb-3 p-3 rounded-3 border border-warning border-opacity-50"
                             style="">
                            <div class="d-flex gap-2 align-items-start">
                                <i class="fas fa-lightbulb text-warning mt-1" style="font-size:14px;"></i>
                                <p class="mb-0 text-warning-emphasis" style="font-size:.82rem; line-height:1.5;">
                                    Si dos facturas comparten el mismo número de OC puede
                                    causar problemas en la conciliación con el cliente.
                                    Verifique si corresponde a un pedido diferente antes de continuar.
                                </p>
                            </div>
                        </div>
                    </div>

                    <div class="modal-footer justify-content-between">
                        <button type="button"
                                id="vi-fac-modal-oc-ignorar"
                                class="btn btn-outline-warning btn-sm">
                            <i class="fas fa-check me-1"></i>
                            Continuar de todas formas
                        </button>
                        <button type="button"
                                class="btn btn-danger btn-sm"
                                data-bs-dismiss="modal">
                            <i class="fas fa-edit me-1"></i>
                            Cambiar número de OC
                        </button>
                    </div>

                </div>
            </div>
        </div>`);
    }

    // ── Estado interno ─────────────────────────────────────────────
    let timerOC = null;
    let ultimoOC = '';
    let ocIgnorada = false;   // true cuando el usuario acepta duplicado

    const modalEl = document.getElementById('vi-fac-modal-oc-duplicada');
    const bsModal = new bootstrap.Modal(modalEl, { keyboard: true });

    // Botón "ignorar": cierra modal, marca estado y pone borde naranja
    document.getElementById('vi-fac-modal-oc-ignorar')
        ?.addEventListener('click', () => {
            ocIgnorada = true;
            bsModal.hide();
            inputOC.classList.remove('is-invalid');
            inputOC.classList.add('border-warning');
            ocMostrarFeedbackInline('warning',
                'OC duplicada — se usará de todas formas');
        });

    // Al cerrar el modal sin ignorar, restaura el borde rojo
    modalEl.addEventListener('hidden.bs.modal', () => {
        if (!ocIgnorada) {
            inputOC.classList.add('is-invalid');
        }
    });

    // ── Eventos del formulario ─────────────────────────────────────
    chkOC.addEventListener('change', () => {
        if (!chkOC.checked) {
            inputOC.classList.add('d-none');
            inputOC.value = '';
            ocReset();
        } else {
            inputOC.classList.remove('d-none');
        }
    });

    inputOC.addEventListener('input', () => {
        const valor = inputOC.value.trim();
        if (valor === ultimoOC) return;
        ultimoOC = valor;
        ocIgnorada = false;

        ocReset();
        clearTimeout(timerOC);

        if (valor.length < 2) return;

        ocMostrarFeedbackInline('buscando');
        timerOC = setTimeout(() => verificarOC(valor), DEBOUNCE);
    });

    // ── Verificación ───────────────────────────────────────────────
    async function verificarOC(oc) {
        try {
            const resp = await fetch(
                `/DatosGenerales/VerificarOrdenCompra?oc=${encodeURIComponent(oc)}`
            );
            if (!resp.ok) throw new Error(`HTTP ${resp.status}`);

            const data = await resp.json();
            ocLimpiarFeedback();

            if (data.existe) {
                inputOC.classList.add('is-invalid');
                ocMostrarModal(oc, data.usos);
            } else {
                inputOC.classList.remove('is-invalid');
                inputOC.classList.add('is-valid');
                ocMostrarFeedbackInline('ok');
                setTimeout(() => {
                    inputOC.classList.remove('is-valid');
                    ocLimpiarFeedback();
                }, 2500);
            }
        } catch (err) {
            console.error('Error verificando OC:', err);
            ocLimpiarFeedback();
        }
    }

    // ── Modal ──────────────────────────────────────────────────────
    function ocMostrarModal(oc, usos) {
        // Subtítulo
        document.getElementById('vi-fac-modal-oc-subtitulo').textContent =
            `Encontrada en ${usos.length} factura${usos.length !== 1 ? 's' : ''} existente${usos.length !== 1 ? 's' : ''}`;

        // Valor OC
        document.getElementById('vi-fac-modal-oc-valor').textContent = oc;

        // Filas de la tabla
        const tbody = document.getElementById('vi-fac-modal-oc-tbody');
        tbody.innerHTML = usos.map(u => {
            const estatusCfg = ocEstatusConfig(u.estatus);
            return `
            <tr>
                <td>
                    <span class="fw-semibold text-primary" style="font-size:.85rem;">
                        ${esc(u.folio || u.folioInterno)}
                    </span>
                    ${u.folioInterno && u.folio !== u.folioInterno
                    ? `<br><small class="text-muted">${esc(u.folioInterno)}</small>`
                    : ''}
                </td>
                <td>
                    <span style="font-size:.85rem;">${esc(u.cliente)}</span>
                    <br>
                    <small class="text-muted font-monospace">${esc(u.rfc)}</small>
                </td>
                <td style="font-size:.83rem; white-space:nowrap;">
                    ${esc(u.fecha)}
                </td>
                <td class="text-end fw-semibold" style="font-size:.85rem;">
                    $${u.total.toLocaleString('es-MX', { minimumFractionDigits: 2 })}
                </td>
                <td class="text-center">
                    <span class="badge ${estatusCfg.cls}" style="font-size:.72rem;">
                        ${esc(u.estatus)}
                    </span>
                </td>
            </tr>`;
        }).join('');

        bsModal.show();
    }

    // ── Feedback inline (pequeño, bajo el input) ───────────────────
    function ocMostrarFeedbackInline(tipo, texto) {
        ocLimpiarFeedback();
        const cont = ocContenedor();
        if (!cont) return;

        const cfgs = {
            buscando: {
                icon: `<span style="
                    display:inline-block; width:12px; height:12px;
                    border-radius:50%; border:2px solid #dee2e6;
                    border-top-color:#0d6efd;
                    animation:oc-spin .7s linear infinite;"></span>`,
                texto: 'Verificando orden de compra...',
                color: '#6c757d'
            },
            ok: {
                icon: `<svg width="13" height="13" viewBox="0 0 13 13" fill="none">
                    <circle cx="6.5" cy="6.5" r="6" stroke="#198754" stroke-width="1"/>
                    <path d="M3.5 6.5l2 2L9.5 5" stroke="#198754"
                          stroke-width="1.3" stroke-linecap="round" stroke-linejoin="round"/>
                </svg>`,
                texto: 'Orden de compra disponible',
                color: '#198754'
            },
            warning: {
                icon: `<svg width="13" height="13" viewBox="0 0 13 13" fill="none">
                    <path d="M6.5 1.5L12 11.5H1L6.5 1.5Z"
                          stroke="#fd7e14" stroke-width="1.2"
                          stroke-linejoin="round" fill="none"/>
                    <path d="M6.5 5v3M6.5 9.5v.3"
                          stroke="#fd7e14" stroke-width="1.2" stroke-linecap="round"/>
                </svg>`,
                texto: texto || '',
                color: '#fd7e14'
            }
        };

        const cfg = cfgs[tipo];
        if (!cfg) return;

        cont.innerHTML = `
            <div style="
                display:flex; align-items:center; gap:6px;
                margin-top:5px; font-size:.78rem; color:${cfg.color};">
                ${cfg.icon}
                <span>${cfg.texto}</span>
            </div>`;
    }

    function ocLimpiarFeedback() {
        document.getElementById('vi-fac-oc-inline-cont')?.remove();
    }

    function ocContenedor() {
        let cont = document.getElementById('vi-fac-oc-inline-cont');
        if (!cont) {
            cont = document.createElement('div');
            cont.id = 'vi-fac-oc-inline-cont';
            // Insertar después del wrapper del toggle + input de OC
            const wrapper = inputOC.closest('.vn_ped-form-field')
                || inputOC.closest('.vi-fac-form-field')
                || inputOC.parentElement;
            wrapper?.insertAdjacentElement('afterend', cont);
        }
        return cont;
    }

    function ocReset() {
        ocIgnorada = false;
        inputOC.classList.remove('is-invalid', 'is-valid', 'border-warning');
        ocLimpiarFeedback();
    }

    // ── Helpers ────────────────────────────────────────────────────
    function ocEstatusConfig(estatus) {
        const s = (estatus || '').toUpperCase();
        if (s.includes('CANCEL')) return { cls: 'bg-danger' };
        if (s.includes('TIMBR') || s.includes('VALID'))
            return { cls: 'bg-success' };
        if (s.includes('BORRAD')) return { cls: 'bg-secondary' };
        return { cls: 'bg-warning text-dark' };
    }

    function esc(text) {
        const d = document.createElement('div');
        d.textContent = String(text ?? '');
        return d.innerHTML;
    }

    // ── Keyframe del spinner ───────────────────────────────────────
    if (!document.getElementById('vi-fac-oc-spin-kf')) {
        const s = document.createElement('style');
        s.id = 'vi-fac-oc-spin-kf';
        s.textContent = '@keyframes oc-spin{to{transform:rotate(360deg)}}';
        document.head.appendChild(s);
    }

})();
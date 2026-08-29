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
            const response = await fetch(`${this.detailEndpoint}?id=${encodeURIComponent(id)}`);
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
            const li = document.createElement('li');
            li.className = 'list-group-item';
            li.dataset.folio = item.folio;

            li.innerHTML = `
                <div class="d-flex justify-content-between align-items-start">
                    <div class="flex-grow-1">
                        <h6 class="mb-1">
                            <i class="fas fa-file-invoice text-primary me-2"></i>
                            <strong>Folio:</strong> ${this.escapeFacturasHtml(item.folio || '')}
                        </h6>
                        <p class="mb-1 text-muted">
                            <i class="fas fa-user me-2"></i>
                            <strong>Cliente:</strong> ${this.escapeFacturasHtml(item.cli_prov || '')}
                        </p>
                        <div class="d-flex gap-3 small text-muted">
                            <span><i class="fas fa-calendar me-1"></i> ${this.escapeFacturasHtml(item.fecha || '')}</span>
                            <span><i class="fas fa-money-bill-wave me-1"></i> $${parseFloat(item.imp || 0).toFixed(2)}</span>
                            <span><i class="fas fa-user-tie me-1"></i> ${this.escapeFacturasHtml(item.usr0 || '')}</span>
                        </div>
                    </div>
                    <div class="d-flex flex-column gap-2 align-items-end">
                        <button type="button" class="btn btn-sm btn-primary" data-select-id="${this.escapeFacturasHtml(item.id_encabezado)}">
                            <i class="fas fa-check me-1"></i>Cargar
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
            table: document.getElementById('vin-fac-productosTable'),
            thead: document.querySelector('#vin-fac-productosTable')?.closest('table')?.querySelector('thead'),
            subtotal1Display: document.getElementById('vin-fac-total-subtotal1-display'),
            descuentoDisplay: document.getElementById('vin-fac-total-descuento-display'),
            fleteDisplay: document.getElementById('vin-fac-total-flete-display'),
            subtotal2Display: document.getElementById('vin-fac-total-subtotal2-display'),
            ivaDisplay: document.getElementById('vin-fac-iva-display'),
            importeDisplay: document.getElementById('vin-fac-importe-display'),
            anticiposTable: document.getElementById('vin-fac-anticiposTable'),
            totalAnticiposDisplay: document.getElementById('vin-fac-total-anticipos-display'),
            totalFinalDisplay: document.getElementById('vin-fac-total-final-display')
        };

        this.initFacturasProductManager();
    }

    initFacturasProductManager() {
        this.dom.table?.addEventListener('input', (e) => {
            const input = e.target;
            if (input.classList.contains('vin-fac-cli-prov') ||
                input.classList.contains('vin-fac-pedimento') ||  
                input.classList.contains('vin-fac-cantidad') ||
                input.classList.contains('vin-fac-precio') ||
                input.classList.contains('vin-fac-descuento')) {
                const tr = input.closest('tr');
                const productoId = tr?.dataset.productoId;
                if (productoId) this.actualizarFacturasFila(productoId);
            }
        });

        this.dom.table?.addEventListener('click', (e) => {
            const btn = e.target.closest('.vin-fac-btn-eliminar');
            if (btn) {
                const tr = btn.closest('tr');
                const productoId = tr?.dataset.productoId;
                if (productoId) this.eliminarFacturasProducto(productoId);
            }
        });

        this.dom.anticiposTable?.addEventListener('click', (e) => {
            const btn = e.target.closest('.vin-fac-btn-eliminar-anticipo');
            if (btn) {
                const tr = btn.closest('tr');
                const anticipoId = tr?.dataset.anticipoId;
                if (anticipoId) this.eliminarAnticipo(anticipoId);
            }
        });

        document.getElementById('vin-fac-flete-val')?.addEventListener('input', () => {
            this.calcularFacturasTotales();
        });

        document.getElementById('vin-fac-monto-anticipo')?.addEventListener('input', () => {
            this.calcularFacturasTotales();
        });

        const tipoCambioInput = document.getElementById('vin-fac-tipo-cambio-usd');
        tipoCambioInput?.addEventListener('input', () => {
            this.calcularTotalUSD();
        }); 
    }

    agregarFacturasProducto(producto) {
        const productoExistente = this.productos.find(p => p.productoId === producto.id);

        if (productoExistente) {
            productoExistente.cantidad += 1;

        } else {
            const productoData = {
                id: `prod_${Date.now()}_${Math.random()}`,
                productoId: producto.id,
                descripcion: producto.descripcion,
                existencia: producto.existencia,
                claveCliente: '',
                pedimento: '', 
                cantidad: 1,
                precio: parseFloat(producto.precio || 0),
                descuento: 0,
                unidad: producto.udm || 'PZA',
                existenciaGeneral: producto.existenciaGeneral || 0,
                existenciaModular: producto.existenciaModular || 0
            };

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

        const emptyRow = tbody.querySelector('.vin-fac-empty-state');
        if (emptyRow) emptyRow.remove();

        tbody.innerHTML = '';

        if (thead) {
            const descuentoTh = thead.querySelector('.vin-fac-th-descuento');
            if (descuentoTh) descuentoTh.style.display = (typeof rol !== 'undefined' && rol === 'Gerente') ? '' : 'none';
        }

        if (this.productos.length === 0) {
            tbody.innerHTML = `
    <tr class="vin-fac-empty-state">
        <td colspan="12" class="text-center py-5">
            <i class="fas fa-box-open fa-3x mb-3 opacity-25"></i>
            <div>No hay productos agregados</div>
            <small class="text-muted">Haz clic en "Agregar" para comenzar</small>
        </td>
    </tr>`;
            return;
        }

        const fragment = document.createDocumentFragment();

        this.productos.forEach(p => {
            const importe = this.calcularFacturasImporte(p);
            const tr = document.createElement('tr');
            tr.dataset.productoId = p.id;

            const descuentoCell = (typeof rol !== 'undefined' && rol === 'Gerente')
                ? `<td>
             <input type="number" class="vin-fac-product-input vin-fac-descuento" value="${p.descuento}" min="0" max="100" step="0.01" readonly>
           </td>`
                : '';

            tr.innerHTML = `
    <td>${this.escapeFacturasHtml(p.productoId)}</td>
    <td style="max-width: 200px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis;">
        ${this.escapeFacturasHtml(p.descripcion)}
    </td>
    <td>
        <input type="text" class="vin-fac-product-input vin-fac-cli-prov" value="${this.escapeFacturasHtml(p.claveCliente || '')}" placeholder="Clave">
    </td>
    <td>
        <input type="text" class="vin-fac-product-input vin-fac-pedimento" value="${this.escapeFacturasHtml(p.pedimento || '')}" placeholder="Pedimento">
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
        <input type="number" class="vin-fac-product-input vin-fac-cantidad" value="${p.cantidad}" min="0" step="0.01">
    </td>
    <td>${this.escapeFacturasHtml(p.unidad)}</td>
    <td>
        <input type="number" class="vin-fac-product-input vin-fac-precio" value="${p.precio.toFixed(2)}" min="0" step="0.01">
    </td>
    ${descuentoCell}
    <td class="vin-fac-importe fw-bold">${importe.toFixed(2)}</td>                 
    <td>
    <input type="text" class="vin-fac-product-input vin-fac-comentario"
           value="${this.escapeFacturasHtml(p.comentario || '')}" 
           placeholder="Comentario">
    </td>
    <td>
        <button type="button" class="vin-fac-btn-delete vin-fac-btn-eliminar">
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
            <tr class="vin-fac-empty-state-anticipos">
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
                <input type="hidden" class="vin-fac-anticipo-id" value="${a.id_encabezado}">
            </td>
            <td>
                <button type="button" class="btn btn-sm btn-danger vin-fac-btn-eliminar-anticipo">
                    <i class="fas fa-trash"></i>
                </button>
            </td>`;

            fragment.appendChild(tr);
        });

        tbody.appendChild(fragment);
    }

    calcularTotalUSD() {
        const tipoCambio = parseFloat(document.getElementById('vin-fac-tipo-cambio-usd')?.value) || 0;
        const totalMXN = this.totales.total || 0;

        if (tipoCambio > 0) {
            const totalUSD = totalMXN;
            const totalUSDInput = document.getElementById('vin-fac-total-usd');
            if (totalUSDInput) {
                totalUSDInput.value = totalUSD.toFixed(2);
            }
        }
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

        producto.claveCliente = tr.querySelector('.vin-fac-cli-prov').value || '';
        producto.pedimento = tr.querySelector('.vin-fac-pedimento').value || ''; 
        producto.cantidad = parseFloat(tr.querySelector('.vin-fac-cantidad').value) || 0;
        producto.precio = parseFloat(tr.querySelector('.vin-fac-precio').value) || 0;

        const descuentoInput = tr.querySelector('.vin-fac-descuento');
        if (descuentoInput) {
            producto.descuento = parseFloat(descuentoInput.value) || 0;
        }

        const importe = this.calcularFacturasImporte(producto);
        tr.querySelector('.vin-fac-importe').textContent = importe.toFixed(2);

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
        const tipoPago = document.getElementById('vin-fac-toggle-pago')?.getAttribute('data-state') || 'contado';
        const flete = parseFloat(document.getElementById('vin-fac-flete-val')?.value) || 0;
        let subtotal1 = 0, descuento = 0, subtotal2 = 0, iva = 0, total = 0;

        if (tipoPago === 'anticipo') {
            const montoAnticipo = parseFloat(document.getElementById('vin-fac-monto-anticipo')?.value) || 0;

            subtotal1 = montoAnticipo;
            descuento = 0;
            subtotal2 = montoAnticipo;
            iva = montoAnticipo * 0.16;
            total = subtotal2;

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
            total = subtotal2;
        }

        // Calcular total de anticipos
        const totalAnticipos = this.anticipos.reduce((sum, a) => sum + a.imp, 0);
        const totalFinal = total - totalAnticipos;

        this.totales = { subtotal1, descuento, flete, subtotal2, iva, total, totalAnticipos, totalFinal };

        this.updateFacturasDisplay(this.dom.subtotal1Display, Currency.format(subtotal1));
        this.updateFacturasDisplay(this.dom.descuentoDisplay, Currency.format(descuento), '-');
        this.updateFacturasDisplay(this.dom.fleteDisplay, Currency.format(flete));
        this.updateFacturasDisplay(this.dom.subtotal2Display, Currency.format(subtotal2));
        this.updateFacturasDisplay(this.dom.ivaDisplay, Currency.format(iva));
        this.updateFacturasDisplay(this.dom.importeDisplay, Currency.format(totalFinal), '');
        this.updateFacturasDisplay(this.dom.totalAnticiposDisplay, Currency.format(totalAnticipos), '-');
        this.updateFacturasDisplay(this.dom.totalFinalDisplay, Currency.format(totalFinal), '');

        this.updateFacturasHiddenField('vin-fac-total-subtotal1', subtotal1);
        this.updateFacturasHiddenField('vin-fac-total-descuento', descuento);
        this.updateFacturasHiddenField('vin-fac-total-flete', flete);
        this.updateFacturasHiddenField('vin-fac-total-subtotal2', subtotal2);
        this.updateFacturasHiddenField('vin-fac-iva', iva);
        this.updateFacturasHiddenField('vin-fac-importe', total);
        this.updateFacturasHiddenField('vin-fac-total-anticipos', totalAnticipos);
        this.updateFacturasHiddenField('vin-fac-total-final', totalFinal);
        this.calcularTotalUSD();
    }

    updateFacturasHiddenField(id, value) {
        const field = document.getElementById(id);
        if (field) {
            field.value = value.toFixed(2);
        }
    }

    updateFacturasDisplay(element, value, prefix = '') {
        if (element) {
            element.textContent = `${prefix}${value}`;
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
        this.form = document.getElementById('vin-fac-formCotizacion');
        this.btnSubmit = document.querySelector('.vin-fac-btn-submit');
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
        const toggle = document.getElementById('vin-fac-toggle-pago');
        const tipoPago = toggle?.getAttribute('data-state') || 'contado';

        const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
        if (token) formData.append('__RequestVerificationToken', token);

        // ============================================
        // CAMPOS GENERALES
        // ============================================
        const camposGenerales = [
            'vin-fac-tipo-docto-mov',
            'vin-fac-folio', 'vin-fac-documentid', 'vin-fac-cliente',
            'vin-fac-rfc', 'vin-fac-vendedor', 'vin-fac-moneda',
            'vin-fac-paridad', 'vin-fac-concepto', 'vin-fac-incoterm',
            'vin-fac-metodo-pago', 'vin-fac-forma-pago',
            'vin-fac-uso-cfdi', 'vin-fac-comentarios', 'vin-fac-direccion-receptor'
        ];

        camposGenerales.forEach(id => {
            const el = document.getElementById(id);
            if (el) formData.append(id.replace('vin-fac-', ''), el.value || '');
        });

        // ============================================
        // CAMPOS DE CRÉDITO
        // ============================================
        if (tipoPago === 'credito') {
            formData.append('limiteCredito', document.getElementById('vin-fac-limite-credito')?.value || '');
            formData.append('plazo', document.getElementById('vin-fac-plazo')?.value || '');
            formData.append('fechaPago', document.getElementById('vin-fac-fecha-pago')?.value || '');
        }

        // ============================================
        // CAMPOS DE ANTICIPO
        // ============================================
        if (tipoPago === 'anticipo') {
            formData.append('fechaAnticipo', document.getElementById('vin-fac-fecha-anticipo')?.value || '');
            formData.append('montoAnticipo', document.getElementById('vin-fac-monto-anticipo')?.value || '');
        }

        // ============================================
        // PRODUCTOS
        // ============================================
        const productos = this.productManager.getFacturasProductosData();
        if (tipoPago !== 'anticipo') {
            formData.append('productosJSON', JSON.stringify(productos));
        }

        // ============================================
        // ANTICIPOS APLICADOS
        // ============================================
        const anticipos = this.productManager.getAnticiposData();
        if (anticipos.length > 0) {
            formData.append('anticiposJSON', JSON.stringify(anticipos));
        }

        // ============================================
        // TOTALES
        // ============================================
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

        // ============================================
        // 🌎 COMERCIO EXTERIOR 2.0
        // ============================================
        const aplicaCE = document.getElementById('vin-fac-chk-ce')?.checked ?? true;
        formData.append('aplicaComercioExterior', aplicaCE ? '1' : '0');

        if (aplicaCE) {
            const camposComercioExterior = [
                { id: 'vin-fac-clave-pedimento', nombre: 'ce_clavePedimento' },
                { id: 'vin-fac-certificado-origen', nombre: 'ce_certificadoOrigen' },
                { id: 'vin-fac-num-certificado-origen', nombre: 'ce_numCertificadoOrigen' },
                { id: 'vin-fac-num-exportador-confiable', nombre: 'ce_numExportadorConfiable' },
                { id: 'vin-fac-subdivision', nombre: 'ce_subdivision' },
                { id: 'vin-fac-tipo-cambio-usd', nombre: 'ce_tipoCambioUSD' },
                { id: 'vin-fac-total-usd', nombre: 'ce_totalUSD' },
                // Emisor
                { id: 'vin-fac-emisor-calle', nombre: 'ce_emisor_calle' },
                { id: 'vin-fac-emisor-num-ext', nombre: 'ce_emisor_numExterior' },
                { id: 'vin-fac-emisor-num-int', nombre: 'ce_emisor_numInterior' },
                { id: 'vin-fac-emisor-colonia', nombre: 'ce_emisor_colonia' },
                { id: 'vin-fac-emisor-localidad', nombre: 'ce_emisor_localidad' },
                { id: 'vin-fac-emisor-municipio', nombre: 'ce_emisor_municipio' },
                { id: 'vin-fac-emisor-estado', nombre: 'ce_emisor_estado' },
                { id: 'vin-fac-emisor-pais', nombre: 'ce_emisor_pais' },
                { id: 'vin-fac-emisor-cp', nombre: 'ce_emisor_codigoPostal' },
                // Receptor
                { id: 'vin-fac-receptor-calle', nombre: 'ce_receptor_calle' },
                { id: 'vin-fac-receptor-num-ext', nombre: 'ce_receptor_numExterior' },
                { id: 'vin-fac-receptor-num-int', nombre: 'ce_receptor_numInterior' },
                { id: 'vin-fac-receptor-colonia', nombre: 'ce_receptor_colonia' },
                { id: 'vin-fac-receptor-localidad', nombre: 'ce_receptor_localidad' },
                { id: 'vin-fac-receptor-municipio', nombre: 'ce_receptor_municipio' },
                { id: 'vin-fac-receptor-estado', nombre: 'ce_receptor_estado' },
                { id: 'vin-fac-receptor-pais', nombre: 'ce_receptor_pais' },
                { id: 'vin-fac-receptor-cp', nombre: 'ce_receptor_codigoPostal' },
                // Propietario
                { id: 'vin-fac-propietario-numreg', nombre: 'ce_propietario_numRegIdTrib' },
                { id: 'vin-fac-propietario-nombre', nombre: 'ce_propietario_nombre' },
                { id: 'vin-fac-propietario-pais', nombre: 'ce_propietario_residenciaFiscal' }
            ];

            camposComercioExterior.forEach(campo => {
                const el = document.getElementById(campo.id);
                if (el) formData.append(campo.nombre, el.value?.trim() || '');
            });
        }

        // ============================================
        // 🔍 VALIDACIÓN ESPECIAL PARA COMERCIO EXTERIOR
        // ============================================
        // Si algún campo obligatorio de CE está lleno, marcar que se usará CE
        const usaComercioExterior =
            document.getElementById('vin-fac-tipo-cambio-usd')?.value ||
            document.getElementById('vin-fac-emisor-estado')?.value ||
            document.getElementById('vin-fac-receptor-pais')?.value;

        if (usaComercioExterior) {
            formData.append('usaComercioExterior', 'true');
        }

        return formData;
    }

    validarFacturasFormulario() {
        const errores = [];

        const togglePago = document.getElementById('vin-fac-toggle-pago');
        const tipoPago = togglePago ? togglePago.getAttribute('data-state') : 'contado';

        const cliente = document.getElementById('vin-fac-cliente')?.value;
        if (!cliente || cliente.trim() === '') {
            errores.push('Debe seleccionar un cliente');
        }

        const productos = this.productManager.getFacturasProductosData();
        if (tipoPago !== 'anticipo' && productos.length === 0) {
            errores.push('Debe agregar al menos un producto');
        }

        const vendedor = document.getElementById('vin-fac-vendedor')?.value;
        if (!vendedor || vendedor === 'Seleccionar vendedor') {
            errores.push('Debe seleccionar un vendedor');
        }

        const rfcInput = document.getElementById('vin-fac-rfc');
        const rfc = rfcInput ? rfcInput.value.trim() : '';
        if (!rfc) {
            errores.push('Debe ingresar el RFC');
        }

        //const incotermSelect = tomManager.getInstance('vin-fac-incoterm');
        //const incotermValue = incotermSelect?.getValue();
        //if (!incotermValue) {
        //    errores.push('Debe seleccionar un Incoterm');
        //}

        const monedaSelect = tomManager.getInstance('vin-fac-moneda');
        const monedaValue = monedaSelect?.getValue();
        if (!monedaValue) {
            errores.push('Debe seleccionar una Moneda');
        }

        const usoCfdiSelect = tomManager.getInstance('vin-fac-uso-cfdi');
        const usoCfdiValue = usoCfdiSelect?.getValue();
        if (!usoCfdiValue) {
            errores.push('Debe seleccionar un Uso CFDI');
        }

        const formaPagoSelect = tomManager.getInstance('vin-fac-forma-pago');
        const formaPagoValue = formaPagoSelect?.getValue();
        if (!formaPagoValue) {
            errores.push('Debe seleccionar una Forma de Pago');
        }

        const fechaPagoInput = document.getElementById('vin-fac-fecha-pago');
        const fechaPago = fechaPagoInput ? fechaPagoInput.value.trim() : '';

        if (tipoPago === 'credito' && !fechaPago) {
            errores.push('Debe ingresar la Fecha de Pago (solo para crédito)');
        }

        // Validar CE solo si el toggle está activo
        const aplicaCE = document.getElementById('vin-fac-chk-ce')?.checked ?? true;

        if (aplicaCE) {
            const tipoCambioUSD = document.getElementById('vin-fac-tipo-cambio-usd')?.value;
            const emisorEstado = document.getElementById('vin-fac-emisor-estado')?.value;
            const receptorPais = document.getElementById('vin-fac-receptor-pais')?.value;
            const receptorCP = document.getElementById('vin-fac-receptor-cp')?.value;
            const emisorCP = document.getElementById('vin-fac-emisor-cp')?.value;

            if (!tipoCambioUSD || parseFloat(tipoCambioUSD) <= 0)
                errores.push('El Tipo de Cambio USD es obligatorio para Comercio Exterior');

            if (!emisorEstado)
                errores.push('El Estado del Emisor es obligatorio para Comercio Exterior');

            if (!emisorCP || emisorCP.length !== 5)
                errores.push('El Código Postal del Emisor debe ser de 5 dígitos');

            if (!receptorPais)
                errores.push('El País del Receptor es obligatorio para Comercio Exterior');

            if (!receptorCP)
                errores.push('El Código Postal del Receptor es obligatorio para Comercio Exterior');

            // Validar incoterm (obligatorio en CE)
            const incotermSelect = tomManager.getInstance('vin-fac-incoterm');
            if (!incotermSelect?.getValue())
                errores.push('Debe seleccionar un Incoterm para Comercio Exterior');
        }
        // Si CE está inactivo → no se validan esos campos

        return errores;
    }

    obtenerFacturasConfiguracionPorTipo(tipoMovimiento) {
        const configuraciones = {
            'alta': {
                endpoint: '/VINFacturaEspecial/Guardar',
                metodo: 'POST',
                mensajeExito: 'Factura creada exitosamente',
                textoCarga: 'Guardando...',
                validarProductos: true
            },
            'modificacion': {
                endpoint: '/VINFacturaEspecial/Modificar',
                metodo: 'PUT',
                mensajeExito: 'Factura modificada exitosamente',
                textoCarga: 'Actualizando...',
                validarProductos: true
            },
            'consulta': {
                endpoint: '/VINFacturaEspecial/Consultar',
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

        document.getElementById('vin-fac-cliente').value = '';
        document.getElementById('vin-fac-rfc').value = '';
        document.getElementById('vin-fac-info-proveedor').value = '';
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
        formData.append('CuentaBancariaId', datosSeleccionados.cuenta);

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

            const response = await fetch('/VINFacturaEspecial/ProcesarDocumentosAsync', {
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
    const CENTRO_DEFAULT = '17';
    const CUENTA_DEFAULT = '1-1-02-01-0001';
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
            title: '📋 Selecciona Centro de Costos y Cuenta Bancaria',
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

                    <!-- Cuenta Bancaria -->
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
                    const resp = await GetData({ path: '/DatosGenerales/DatosBancos' });
                    const selectCuenta = document.getElementById('swal-cuenta-factura');
                    selectCuenta.innerHTML = `<option value="">-- Selecciona una cuenta --</option>`;

                    if (resp.success && resp.result?.length) {
                        resp.result.forEach(c => {
                            selectCuenta.innerHTML += `
                                <option value="${c.codigo}">
                                    ${c.codigo} - ${c.nombre}
                                </option>`;
                        });
                        selectCuenta.value = CUENTA_DEFAULT;
                    } else {
                        selectCuenta.innerHTML = `<option value="">No hay cuentas disponibles</option>`;
                    }
                } catch (err) {
                    console.error('Error cargando cuentas:', err);
                    document.getElementById('swal-cuenta-factura').innerHTML =
                        `<option value="">Error al cargar cuentas</option>`;
                }
            },

            preConfirm: () => {
                const centro = document.getElementById('swal-centro-factura').value;
                const cuenta = document.getElementById('swal-cuenta-factura').value;

                if (!centro) {
                    Swal.showValidationMessage('Por favor selecciona un centro de costos');
                    return false;
                }

                if (!cuenta) {
                    Swal.showValidationMessage('Por favor selecciona una cuenta bancaria');
                    return false;
                }

                return { centro, cuenta };
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

    initFacturas() {
        this.form = document.getElementById('vin-fac-formCotizacion');
        this.btnSubmit = document.querySelector('.vin-fac-btn-submit');

        this.clienteManager = new FacturasDataManager({
            endpoint: '/DatosGenerales/BuscarC',
            detailEndpoint: '/DatosGenerales/BuscarCliente',
            modalId: 'vin-fac-modalBuscarCliente',
            inputId: 'vin-fac-inputBuscarCliente',
            resultsId: 'vin-fac-listaResultadosClientes',
            pageSizeId: 'vin-fac-pageSizeClientes',
            btnClearId: 'vin-fac-btnLimpiarClientes',
            spinnerId: 'vin-fac-spinnerClientes',
            paginationId: 'vin-fac-paginationClientes',
            recordsFromId: 'vin-fac-recordsFromClientes',
            recordsToId: 'vin-fac-recordsToClientes',
            totalRecordsId: 'vin-fac-totalRecordsClientes',
            shouldCloseOnSelect: true
        });

        this.clienteManager.onFacturasSelect = (cliente) => {
            document.getElementById('vin-fac-cliente').value = cliente.id || '';
            document.getElementById('vin-fac-rfc').value = cliente.rfc || '';
            document.getElementById('vin-fac-propietario-nombre').value = cliente.descripcion || '';
            const direccion = [cliente.dir, cliente.col, cliente.pob, cliente.cp]
                .filter(x => x)
                .join(',\n');

            document.getElementById('vin-fac-info-proveedor').value = direccion;
            document.getElementById('vin-fac-direccion-receptor').value = direccion; 

            // Receptor
            document.getElementById('vin-fac-receptor-calle').value = cliente.calle || '';
            document.getElementById('vin-fac-receptor-num-ext').value = cliente.no_exterior || '';
            document.getElementById('vin-fac-receptor-num-int').value = cliente.no_interior || '';
            document.getElementById('vin-fac-receptor-colonia').value = cliente.colonia || '';
            document.getElementById('vin-fac-receptor-localidad').value = cliente.localidad || '';
            document.getElementById('vin-fac-receptor-municipio').value = cliente.municipio || '';
            document.getElementById('vin-fac-receptor-estado').value = cliente.estado || '';
            document.getElementById('vin-fac-receptor-pais').value = cliente.pais || '';
            document.getElementById('vin-fac-receptor-cp').value = cliente.codigo_postal || '';
            document.getElementById('vin-fac-propietario-numreg').value = cliente.idf || '';
            
            const vendedorInstance = tomManager.getInstance('vin-fac-vendedor');
            if (vendedorInstance && cliente.cve_vdr) {
                vendedorInstance.setValue(cliente.cve_vdr, true);
            }

            const fpagoInstance = tomManager.getInstance('vin-fac-forma-pago');
            if (fpagoInstance && cliente.forma_pago) {
                fpagoInstance.setValue(cliente.forma_pago, true);
            }
            const cfdiInstance = tomManager.getInstance('vin-fac-uso-cfdi');
            if (cfdiInstance && cliente.uso_sugerido) {
                cfdiInstance.setValue(cliente.uso_sugerido, true);
            }
        };

        this.productoManager = new FacturasDataManager({
            endpoint: '/DatosGenerales/BuscarP',
            detailEndpoint: '/DatosGenerales/BuscarProducto',
            modalId: 'vin-fac-modalBuscarProducto',
            inputId: 'vin-fac-inputBuscarProducto',
            resultsId: 'vin-fac-listaResultadosProductos',
            pageSizeId: 'vin-fac-pageSizeProductos',
            btnClearId: 'vin-fac-btnLimpiarProductos',
            spinnerId: 'vin-fac-spinnerProductos',
            paginationId: 'vin-fac-paginationProductos',
            recordsFromId: 'vin-fac-recordsFromProductos',
            recordsToId: 'vin-fac-recordsToProductos',
            totalRecordsId: 'vin-fac-totalRecordsProductos',
            shouldCloseOnSelect: false
        });

        this.productManagerInstance = new FacturasProductManager();

        this.productoManager.onFacturasSelect = (producto) => {
            this.productManagerInstance.agregarFacturasProducto(producto);
        };

        this.documentoManager = new FacturasDocumentoManager({
            endpoint: '/DatosGenerales/BuscarDVINespecial',
            detailEndpoint: '/DatosGenerales/BuscarDocumentoEspecialInternacional',
            modalId: 'vin-fac-modalBuscarDocumentos',
            inputId: 'vin-fac-inputBuscarDocumento',
            resultsId: 'vin-fac-listaResultadosDocumentos',
            pageSizeId: 'vin-fac-pageSizeDocumentos',
            btnClearId: 'vin-fac-btnLimpiarDocumentos',
            spinnerId: 'vin-fac-spinnerDocumentos',
            paginationId: 'vin-fac-paginationDocumentos',
            recordsFromId: 'vin-fac-recordsFromDocumentos',
            recordsToId: 'vin-fac-recordsToDocumentos',
            totalRecordsId: 'vin-fac-totalRecordsDocumentos',
            shouldCloseOnSelect: true,
            filtros: {}
        });

        const filtroEstado = document.getElementById('vin-fac-filtroEstado');
        const filtroFecha = document.getElementById('vin-fac-filtroFecha');

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
            modalId: 'vin-fac-modalBuscarAnticipos',
            inputId: 'vin-fac-inputBuscarAnticipo',
            resultsId: 'vin-fac-listaResultadosAnticipos',
            pageSizeId: 'vin-fac-pageSizeAnticipos',
            btnClearId: 'vin-fac-btnLimpiarAnticipos',
            spinnerId: 'vin-fac-spinnerAnticipos',
            paginationId: 'vin-fac-paginationAnticipos',
            recordsFromId: 'vin-fac-recordsFromAnticipos',
            recordsToId: 'vin-fac-recordsToAnticipos',
            totalRecordsId: 'vin-fac-totalRecordsAnticipos',
            shouldCloseOnSelect: true
        });

        const modalAnticipos = document.getElementById('vin-fac-modalBuscarAnticipos');
        if (modalAnticipos) {
            modalAnticipos.addEventListener('show.bs.modal', () => {
                const clienteId = document.getElementById('vin-fac-cliente')?.value;
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

        const tipoMovSelect = document.getElementById('vin-fac-tipo-docto-mov');
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

    async cargarDocumentoCompleto(documento) {

        if (documento.cli_prov) {
            try {
                // Hacer la consulta del cliente actual usando el endpoint de detalle
                const responseCliente = await fetch(`${this.clienteManager.detailEndpoint}?id=${encodeURIComponent(documento.cli_prov)}`);

                if (responseCliente.ok) {
                    const dataCliente = await responseCliente.json();

                    if (dataCliente && dataCliente[0]) {
                        const clienteActual = dataCliente[0];

                        // Usar la misma lógica que en onFacturasSelect del clienteManager
                        document.getElementById('vin-fac-cliente').value = clienteActual.id || '';
                        document.getElementById('vin-fac-rfc').value = clienteActual.rfc || '';
                        document.getElementById('vin-fac-propietario-nombre').value = clienteActual.descripcion || '';
                        document.getElementById('vin-fac-info-proveedor').value =
                            [clienteActual.dir, clienteActual.col, clienteActual.pob, clienteActual.cp]
                                .filter(x => x)
                                .join(',\n');

                        document.getElementById('vin-fac-direccion-receptor').value =
                            [clienteActual.dir, clienteActual.col, clienteActual.pob, clienteActual.cp]
                                .filter(x => x)
                                .join(',\n');
                        // Receptor (información actualizada del cliente)
                        document.getElementById('vin-fac-receptor-calle').value = clienteActual.calle || '';
                        document.getElementById('vin-fac-propietario-numreg').value = clienteActual.idf || '';
                        document.getElementById('vin-fac-receptor-num-ext').value = clienteActual.no_exterior || '';
                        document.getElementById('vin-fac-receptor-num-int').value = clienteActual.no_interior || '';
                        document.getElementById('vin-fac-receptor-colonia').value = clienteActual.colonia || '';
                        document.getElementById('vin-fac-receptor-localidad').value = clienteActual.localidad || '';
                        document.getElementById('vin-fac-receptor-municipio').value = clienteActual.municipio || '';
                        document.getElementById('vin-fac-receptor-estado').value = clienteActual.estado || '';
                        document.getElementById('vin-fac-receptor-pais').value = clienteActual.pais || '';
                        document.getElementById('vin-fac-receptor-cp').value = clienteActual.codigo_postal || '';

                        // Vendedor actual del cliente
                        const vendedorInstance = tomManager.getInstance('vin-fac-vendedor');
                        if (vendedorInstance && clienteActual.cve_vdr) {
                            vendedorInstance.setValue(clienteActual.cve_vdr, true);
                        }

                        // Forma de pago actual del cliente
                        const fpagoInstance = tomManager.getInstance('vin-fac-forma-pago');
                        if (fpagoInstance && clienteActual.forma_pago) {
                            fpagoInstance.setValue(clienteActual.forma_pago, true);
                        }

                        // CFDI actual del cliente
                        const cfdiInstance = tomManager.getInstance('vin-fac-uso-cfdi');
                        if (cfdiInstance && clienteActual.uso_sugerido) {
                            cfdiInstance.setValue(clienteActual.uso_sugerido, true);
                        }
                    }
                }
            } catch (error) {
                console.warn('No se pudo cargar la información actualizada del cliente:', error);
                // Si falla, se usará la información del documento (fallback)
            }
        }
        document.getElementById('vin-fac-sucursal').value = documento.sucursal || '';
        document.getElementById('vin-fac-almacen').value = documento.almacen || '';
        document.getElementById('vin-fac-documentid').value = documento.id_encabezado || '';

        document.getElementById('vin-fac-cliente').value = documento.cli_prov || '';
        document.getElementById('vin-fac-rfc').value = documento.rfc || '';
        document.getElementById('vin-fac-propietario-nombre').value = documento.n_cli || '';
        document.getElementById('vin-fac-info-proveedor').value = documento.info_cli || '';
        document.getElementById('vin-fac-paridad').value = documento.par || '';
        console.log(documento.par)
        //Receptor
        document.getElementById('vin-fac-receptor-calle').value = documento.calle || '';
        document.getElementById('vin-fac-propietario-numreg').value = documento.idf || '';
        document.getElementById('vin-fac-receptor-num-ext').value = documento.no_exterior || '';
        document.getElementById('vin-fac-receptor-num-int').value = documento.no_interior || '';
        document.getElementById('vin-fac-receptor-colonia').value = documento.colonia || '';
        document.getElementById('vin-fac-receptor-localidad').value = documento.localidad || '';
        document.getElementById('vin-fac-receptor-municipio').value = documento.municipio || '';
        document.getElementById('vin-fac-receptor-estado').value = documento.estado || '';
        document.getElementById('vin-fac-receptor-pais').value = documento.pais || '';
        document.getElementById('vin-fac-receptor-cp').value = documento.codigo_postal || '';

        const vendedorInstance = tomManager.getInstance('vin-fac-vendedor');
        if (vendedorInstance && documento.vdr_cpr) {
            vendedorInstance.setValue(documento.vdr_cpr, true);
        }

        const monedaInstance = tomManager.getInstance('vin-fac-moneda');
        if (monedaInstance && documento.ccy) {
            monedaInstance.setValue(documento.ccy, true);
            document.getElementById('vin-fac-moneda').dispatchEvent(new Event("change"))
        }

        const cfdiInstance = tomManager.getInstance('vin-fac-uso-cfdi');
        if (cfdiInstance && documento.usoCfdi) {
            cfdiInstance.setValue(documento.usoCfdi, true);
        }

        const formaPagoInstance = tomManager.getInstance('vin-fac-forma-pago');
        if (formaPagoInstance && documento.formaPago) {
            formaPagoInstance.setValue(documento.formaPago, true);
        }

        const formaIncoterm = tomManager.getInstance('vin-fac-incoterm');
        if (formaIncoterm && documento.incoterm) {
            formaIncoterm.setValue(documento.incoterm, true);
        }

        const formaFP = tomManager.getInstance('vin-fac-forma-pago');
        if (formaFP && documento.f_pago) {
            formaFP.setValue(documento.f_pago, true);
        }

        const formaCDFI = tomManager.getInstance('vin-fac-uso-cfdi');
        if (formaCDFI && documento.cfdi) {
            formaCDFI.setValue(documento.cfdi, true);
        }

        const formaSucursal = tomManager.getInstance('vin-fac-sucursal');
        if (formaSucursal && documento.suc) {
            formaSucursal.setValue(documento.suc, false);
        }

        const formaAlmacen = tomManager.getInstance('vin-fac-almacen');
        if (formaAlmacen && documento.alm) {
            formaAlmacen.setValue(documento.alm, true);
        }

        
        const cambioDolar = await GetData({ path: '/DatosGenerales/DatosCambioDolar' });

        if (!cambioDolar.success) {
            Swal.fire({
                icon: 'error',
                title: cambioDolar.message || 'Error al cargar el tipo de cambio'
            });
            return;
        }

        document.getElementById('vin-fac-tipo-cambio-usd').value = cambioDolar.valor || '';
/*        document.getElementById('vin-fac-paridad').value = cambioDolar.valor || '';*/
        document.getElementById('vin-fac-limite-credito').value = documento.lim_crd || '';
        document.getElementById('vin-fac-plazo').value = documento.pl_crd || '';
        document.getElementById('vin-fac-metodo-pago').value = documento.mdp || '';
        // Fecha actual
        const hoy = new Date();

        // Sumar los días del plazo de crédito
        const diasCredito = parseInt(documento.pl_crd) || 0;
        const fechaPago = new Date(hoy);
        fechaPago.setDate(hoy.getDate() + diasCredito);

        // Convertir a formato 'YYYY-MM-DD' para el input tipo date
        const fechaISO = fechaPago.toISOString().split('T')[0];

        document.getElementById('vin-fac-fecha-pago').value = fechaISO;
        document.getElementById('vin-fac-concepto').value = documento.coment1 || '';
        document.getElementById('vin-fac-incoterm').value = documento.incoterm || '';
        document.getElementById('vin-fac-comentarios').value = documento.coment_aut || '';

        if (documento.productos && Array.isArray(documento.productos)) {
            this.productManagerInstance.productos = [];

            documento.productos.forEach(prod => {
                const productoData = {
                    id: `prod_${Date.now()}_${Math.random()}`,
                    productoId: prod.producto_id || prod.id,
                    descripcion: prod.descripcion,
                    existencia: prod.existencia,
                    claveCliente: prod.claveCliente || '',
                    pedimento: prod.pedimento || '',      
                    cantidad: parseFloat(prod.cantidad) || 0,
                    precio: parseFloat(prod.precio) || 0,
                    descuento: parseFloat(prod.iva) || 0,
                    unidad: prod.unidad || prod.udm || 'PZA',
                    existenciaGeneral: prod.existenciaGeneral || 0,
                    existenciaModular: prod.existenciaModular || 0
                };
                this.productManagerInstance.productos.push(productoData);
            });

            this.productManagerInstance.renderFacturasTable();
        }

        const fleteInput = document.getElementById('vin-fac-flete-val');
        if (fleteInput && documento.flete) {
            const chkFlete = document.getElementById("vin-fac-flete");

            chkFlete.checked = true;
            chkFlete.dispatchEvent(new Event("change"));

            fleteInput.value = documento.flete;
        }

        this.productManagerInstance.calcularFacturasTotales();

        const tipoMovSelect = document.getElementById('vin-fac-tipo-docto-mov');
        if (tipoMovSelect && documento.folio) {
            tipoMovSelect.value = 'modificacion';
            this.manejarFacturasCambioTipoMovimiento('modificacion');
        }
        //const monedaInstance = tomManager.getInstance('vin-fac-moneda');
        //monedaInstance?.trigger('change');
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

        const formElements = facturasContenedor.querySelectorAll('input, select, textarea, button');
        formElements.forEach(element => {
            if (element.id !== 'vin-fac-tipo-docto-mov') {
                element.disabled = true;
                element.classList.add('vin-fac-disabled-field');
            }
        });

        const btnBuscarCliente = facturasContenedor.querySelector('[data-bs-target="#vin-fac-modalBuscarCliente"]');
        const btnBuscarProducto = facturasContenedor.querySelector('[data-bs-target="#vin-fac-modalBuscarProducto"]');
        if (btnBuscarCliente) btnBuscarCliente.disabled = true;
        if (btnBuscarProducto) btnBuscarProducto.disabled = true;

        const botonesEliminar = facturasContenedor.querySelectorAll('.vin-fac-btn-eliminar');
        botonesEliminar.forEach(btn => btn.disabled = true);

        if (window.tomManager) {
            ['vin-fac-vendedor', 'vin-fac-moneda', 'vin-fac-uso-cfdi', 'vin-fac-forma-pago'].forEach(id => {
                const instance = window.tomManager.instances?.get(id);
                if (instance) instance.disable();
            });
        }

        const btn = facturasContenedor.querySelector('.vin-fac-btn-submit');
        if (btn) {
            btn.disabled = false;
        }
        const btnDocConsulta = facturasContenedor.querySelector('.vin-fac-floating-btn');
        if (btnDocConsulta) {
            btnDocConsulta.disabled = false;
        }
        const btnPreview = facturasContenedor.querySelector('.vin-fac-ticket-btn');
        if (btnPreview) {
            btnPreview.disabled = false;
        }
    },

    habilitarFacturasFormulario() {
        const facturasContenedor = document.getElementById('facturas-contenedor');
        if (!facturasContenedor) return;

        const formElements = facturasContenedor.querySelectorAll('input, select, textarea, button');
        formElements.forEach(element => {
            element.disabled = false;
            element.classList.remove('vin-fac-disabled-field');
        });

        const btnBuscarCliente = facturasContenedor.querySelector('[data-bs-target="#vin-fac-modalBuscarCliente"]');
        const btnBuscarProducto = facturasContenedor.querySelector('[data-bs-target="#vin-fac-modalBuscarProducto"]');
        if (btnBuscarCliente) btnBuscarCliente.disabled = false;
        if (btnBuscarProducto) btnBuscarProducto.disabled = false;

        if (window.tomManager) {
            ['vin-fac-vendedor', 'vin-fac-moneda', 'vin-fac-uso-cfdi', 'vin-fac-forma-pago'].forEach(id => {
                const instance = window.tomManager.instances?.get(id);
                if (instance) instance.enable();
            });
        }

        const tipoMovimiento = document.getElementById('vin-fac-tipo-docto-mov')?.value;
        const btn = facturasContenedor.querySelector('.vin-fac-btn-submit');
        if (btn) {
            if (tipoMovimiento === 'modificacion') {
                btn.innerHTML = '<i class="fas fa-save"></i> Actualizar Factura';
            } else {
                btn.innerHTML = '<i class="fas fa-save"></i> Guardar Factura';
            }
        }
    }
};

// ============================================
// 7. FUNCIONES DE AUTENTICACIÓN
// ============================================
async function handleFacturasDescuentoAuth(e) {
    if (!e.target.classList.contains('vin-fac-descuento')) return;
    await pedirFacturasAutenticacion(e.target);
}

async function handleFacturasDescuentoF8(e) {
    if (e.key !== 'F8') return;

    const input = document.activeElement;
    if (!input.classList.contains('vin-fac-descuento')) return;

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
        const fleteInput = document.getElementById('vin-fac-flete-val');
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
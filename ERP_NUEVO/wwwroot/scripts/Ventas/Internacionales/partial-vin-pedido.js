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
            total: 0
        };

        this.dom = {
            table: document.getElementById('vin-ped-productosTable'),
            thead: document.querySelector('#vin-ped-productosTable')?.closest('table')?.querySelector('thead'),
            subtotal1Display: document.getElementById('vin-ped-total-subtotal1-display'),
            descuentoDisplay: document.getElementById('vin-ped-total-descuento-display'),
            fleteDisplay: document.getElementById('vin-ped-total-flete-display'),
            subtotal2Display: document.getElementById('vin-ped-total-subtotal2-display'),
            importeDisplay: document.getElementById('vin-ped-importe-display')
        };

        this.initPedidosProductManager();
    }

    initPedidosProductManager() {
        this.dom.table?.addEventListener('input', (e) => {
            const input = e.target;
            if (input.classList.contains('vin-ped-cantidad') ||
                input.classList.contains('vin-ped-precio') ||
                input.classList.contains('vin-ped-descuento') ||
                input.classList.contains('vin-ped-comentario')) {
                const tr = input.closest('tr');
                const productoId = tr?.dataset.productoId;
                if (productoId) this.actualizarPedidosFila(productoId);
            }
        });

        // ── Un solo listener de click en la tabla ────────────────────────────
        this.dom.table?.addEventListener('click', (e) => {

            // 1. Eliminar producto
            const btnEliminar = e.target.closest('.vin-ped-btn-eliminar');
            if (btnEliminar) {
                const productoId = btnEliminar.closest('tr')?.dataset.productoId;
                if (productoId) this.eliminarPedidosProducto(productoId);
                return;
            }

            // 2. Click en el OVERLAY de campo protegido
            const overlay = e.target.closest('.vin-ped-field-overlay');
            if (overlay) {
                pedirPedidosAutenticacionGlobal(overlay.dataset.tipo);
                return;
            }
        });

        document.getElementById('vin-ped-flete-val')?.addEventListener('input', () => {
            this.calcularPedidosTotales();
        });

        document.getElementById('vin-ped-monto-anticipo')?.addEventListener('input', () => {
            this.calcularPedidosTotales();
        });
    }

    agregarPedidosProducto(producto) {
        const productoExistente = this.productos.find(p => p.productoId === producto.id);

        if (productoExistente) {
            productoExistente.cantidad += 1;

            const tr = this.dom.table.querySelector(`tr[data-producto-id="${productoExistente.id}"]`);
            if (tr) {
                const inputCantidad = tr.querySelector('.vin-ped-cantidad');
                inputCantidad.value = productoExistente.cantidad;

                const importe = this.calcularPedidosImporte(productoExistente);
                tr.querySelector('.vin-ped-importe').textContent = importe.toFixed(2);

                tr.classList.add('table-warning');
                setTimeout(() => tr.classList.remove('table-warning'), 500);
            }

            this.calcularPedidosTotales();

            if (window.toastMixin) {
                toastMixin.fire({
                    icon: 'info',
                    title: `Cantidad actualizada: ${this.escapePedidosHtml(producto.descripcion)} (${productoExistente.cantidad})`
                });
            }
        } else {
            const productoData = {
                id: `prod_${Date.now()}_${Math.random()}`,
                productoId: producto.id,
                descripcion: producto.descripcion,
                existencia: producto.existencia,
                cantidad: 1,
                precio: parseFloat(producto.precio || 0),
                // ★ precioOriginal: fuente de verdad del catálogo
                precioOriginal: parseFloat(producto.precio || 0),
                descuento: 0,
                unidad: producto.udm || 'PZA',
                existenciaGeneral: producto.existenciaGeneral || 0,
                existenciaModular: producto.existenciaModular || 0,
                comentario: ''
            };

            this.productos.push(productoData);
            this.renderPedidosTable();
            this.calcularPedidosTotales();

            if (window.toastMixin) {
                toastMixin.fire({
                    icon: 'success',
                    title: `Producto agregado: ${this.escapePedidosHtml(producto.descripcion)}`
                });
            }
        }
    }

    renderPedidosTable() {
        const tbody = this.dom.table;
        if (!tbody) return;
        tbody.innerHTML = '';

        if (this.productos.length === 0) {
            tbody.innerHTML = `
            <tr class="vin-ped-empty-state">
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
            const importe = this.calcularPedidosImporte(p);
            const tr = document.createElement('tr');
            tr.dataset.productoId = p.id;

            // ── Celda precio ─────────────────────────────────────────────────
            const precioCell = document.createElement('td');
            precioCell.style.position = 'relative';

            const precioInput = document.createElement('input');
            precioInput.type = 'number';
            precioInput.className = 'vin-ped-product-input vin-ped-precio';
            precioInput.value = p.precio.toFixed(2);
            precioInput.min = '0';
            precioInput.step = '0.01';
            // ★ precio original guardado como data-attribute
            precioInput.dataset.precioOriginal = p.precioOriginal.toFixed(2);

            if (!precioDesbloqueado) {
                precioInput.disabled = true;
                const overlayPrecio = document.createElement('div');
                overlayPrecio.className = 'vin-ped-field-overlay';
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
            descInput.className = 'vin-ped-product-input vin-ped-descuento';
            descInput.value = p.descuento;
            descInput.min = '0';
            descInput.max = '100';
            descInput.step = '0.01';

            if (!descDesbloqueado) {
                descInput.disabled = true;
                const overlayDesc = document.createElement('div');
                overlayDesc.className = 'vin-ped-field-overlay';
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
                <input type="number" class="vin-ped-product-input vin-ped-cantidad"
                       value="${p.cantidad}" min="0" step="0.01">
            </td>
            <td>${this.escapePedidosHtml(p.unidad)}</td>`;

            // Insertar celdas construidas por JS (precio y descuento)
            tr.appendChild(precioCell);
            tr.appendChild(descCell);

            // Continuar con el resto
            const resto = document.createElement('template');
            resto.innerHTML = `
            <td class="vin-ped-importe fw-bold">${importe.toFixed(2)}</td>
            <td>
                <input type="text" class="vin-ped-product-input vin-ped-comentario"
                       value="${this.escapePedidosHtml(p.comentario)}" placeholder="Comentario...">
            </td>
            <td>
                <button type="button" class="vin-ped-btn-delete vin-ped-btn-eliminar">
                    <i class="fas fa-trash"></i>
                </button>
            </td>`;
            tr.append(...resto.content.childNodes);

            fragment.appendChild(tr);
        });

        tbody.appendChild(fragment);
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

        producto.cantidad = parseFloat(tr.querySelector('.vin-ped-cantidad').value) || 0;
        producto.comentario = tr.querySelector('.vin-ped-comentario').value || '';

        // ── Validación de precio (igual que cotizaciones) ─────────────────────
        const precioInput = tr.querySelector('.vin-ped-precio');
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
        const descInput = tr.querySelector('.vin-ped-descuento');
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

        tr.querySelector('.vin-ped-importe').textContent = this.calcularPedidosImporte(producto).toFixed(2);
        this.calcularPedidosTotales();
    }

    eliminarPedidosProducto(productoId) {
        this.productos = this.productos.filter(p => p.id !== productoId);
        this.renderPedidosTable();
        this.calcularPedidosTotales();
    }

    /**
     * Desbloquea visualmente todos los campos de un tipo.
     * Elimina overlays y habilita los inputs.
     * La autorización real la garantiza el token en el servidor.
     */
    desbloquearPedidosCampos(tipo) {
        const selector = tipo === 'descuento' ? '.vin-ped-descuento' : '.vin-ped-precio';

        this.dom.table?.querySelectorAll(selector).forEach(input => {
            input.removeAttribute('disabled');

            const overlay = input.parentElement?.querySelector('.vin-ped-field-overlay');
            overlay?.remove();
        });
    }

    calcularPedidosImporte(producto) {
        return producto.cantidad * producto.precio * (1 - producto.descuento / 100);
    }

    calcularPedidosTotales() {
        const tipoPago = document.getElementById('vin-ped-toggle-pago')?.getAttribute('data-state') || 'contado';
        const flete = parseFloat(document.getElementById('vin-ped-flete-val')?.value) || 0;
        let subtotal1 = 0, descuento = 0, subtotal2 = 0, total = 0;

        if (tipoPago === 'anticipo') {
            const montoAnticipo = parseFloat(document.getElementById('vin-ped-monto-anticipo')?.value) || 0;
            subtotal1 = montoAnticipo;
            descuento = 0;
            subtotal2 = montoAnticipo;
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
            total = subtotal2;
        }

        this.totales = { subtotal1, descuento, flete, subtotal2, total };

        this.updatePedidosDisplay(this.dom.subtotal1Display, Currency.format(subtotal1));
        this.updatePedidosDisplay(this.dom.descuentoDisplay, Currency.format(descuento), '-$');
        this.updatePedidosDisplay(this.dom.fleteDisplay, Currency.format(flete));
        this.updatePedidosDisplay(this.dom.subtotal2Display, Currency.format(subtotal2));
        this.updatePedidosDisplay(this.dom.importeDisplay, Currency.format(total));

        this.updatePedidosHiddenField('vin-ped-total-subtotal1', subtotal1);
        this.updatePedidosHiddenField('vin-ped-total-descuento', descuento);
        this.updatePedidosHiddenField('vin-ped-total-flete', flete);
        this.updatePedidosHiddenField('vin-ped-total-subtotal2', subtotal2);
        this.updatePedidosHiddenField('vin-ped-importe', total);

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
        this.form = document.getElementById('vin-ped-formCotizacion');
        this.btnSubmit = document.querySelector('.vin-ped-btn-submit');
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
        const toggle = document.getElementById('vin-ped-toggle-pago');
        const tipoPago = toggle?.getAttribute('data-state') || 'contado';

        const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
        if (token) formData.append('__RequestVerificationToken', token);

        const camposGenerales = [
            'vin-ped-tipo-docto-mov',
            'vin-ped-folio', 'vin-ped-documentid', 'vin-ped-cliente',
            'vin-ped-rfc', 'vin-ped-vendedor', 'vin-ped-moneda',
            'vin-ped-paridad', 'vin-ped-concepto',
            'vin-ped-metodo-pago', 'vin-ped-forma-pago',
            'vin-ped-uso-cfdi', 'vin-ped-comentarios'
        ];

        camposGenerales.forEach(id => {
            const el = document.getElementById(id);
            if (el) formData.append(id.replace('vin-ped-', ''), el.value || '');
        });

        formData.append('ordenCompra', document.getElementById('vin-ped-orden-compra-val')?.value || '');
        //formData.set('sucursal', suc.getValue());
        //formData.set('almacen', alm.getValue());

        if (tipoPago === 'credito') {
            formData.append('limiteCredito', document.getElementById('vin-ped-limite-credito')?.value || '');
            formData.append('plazo', document.getElementById('vin-ped-plazo')?.value || '');
            formData.append('fechaPago', document.getElementById('vin-ped-fecha-pago')?.value || '');
        }

        if (tipoPago === 'anticipo') {
            formData.append('fechaAnticipo', document.getElementById('vin-ped-fecha-anticipo')?.value || '');
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
        formData.append('total', totales.total.toFixed(2));

        formData.append('tipo', tipoPago);

        // ★ Enviar tokens al servidor para validación
        formData.append('descuentoToken', pedidosAuthState.descuentoToken || '');
        formData.append('precioToken', pedidosAuthState.precioToken || '');

        return formData;
    }

    validarPedidosFormulario() {
        const errores = [];

        const togglePago = document.getElementById('vin-ped-toggle-pago');
        const tipoPago = togglePago ? togglePago.getAttribute('data-state') : 'contado';

        const cliente = document.getElementById('vin-ped-cliente')?.value;
        if (!cliente || cliente.trim() === '') {
            errores.push('Debe seleccionar un cliente');
        }

        const productos = this.productManager.getPedidosProductosData();
        if (tipoPago !== 'anticipo' && productos.length === 0) {
            errores.push('Debe agregar al menos un producto');
        }

        const vendedor = document.getElementById('vin-ped-vendedor')?.value;
        if (!vendedor || vendedor === 'Seleccionar vendedor') {
            errores.push('Debe seleccionar un vendedor');
        }

        const rfcInput = document.getElementById('vin-ped-rfc');
        const rfc = rfcInput ? rfcInput.value.trim() : '';
        if (!rfc) {
            errores.push('Debe ingresar el RFC');
        }

        const monedaSelect = tomManager.getInstance('vin-ped-moneda');
        const monedaValue = monedaSelect?.getValue();
        if (!monedaValue) {
            errores.push('Debe seleccionar una Moneda');
        }

        const usoCfdiSelect = tomManager.getInstance('vin-ped-uso-cfdi');
        const usoCfdiValue = usoCfdiSelect?.getValue();
        if (!usoCfdiValue) {
            errores.push('Debe seleccionar un Uso CFDI');
        }

        const formaPagoSelect = tomManager.getInstance('vin-ped-forma-pago');
        const formaPagoValue = formaPagoSelect?.getValue();
        if (!formaPagoValue) {
            errores.push('Debe seleccionar una Forma de Pago');
        }

        const fechaPagoInput = document.getElementById('vin-ped-fecha-pago');
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
                endpoint: '/VINPedido/Guardar',
                metodo: 'POST',
                mensajeExito: 'Pedido creado exitosamente',
                textoCarga: 'Guardando...',
                validarProductos: true
            },
            'modificacion': {
                endpoint: '/VINPedido/Modificar',
                metodo: 'PUT',
                mensajeExito: 'Pedido modificado exitosamente',
                textoCarga: 'Actualizando...',
                validarProductos: true
            },
            'consulta': {
                endpoint: '/VINPedido/Consultar',
                metodo: 'GET',
                mensajeExito: 'Consulta realizada exitosamente',
                textoCarga: 'Consultando...',
                validarProductos: false
            }
        };

        return configuraciones[tipoMovimiento] || configuraciones['alta'];
    }

    async enviarPedidosCotizacion() {
        const tipoMovimiento = document.getElementById('vin-ped-tipo-docto-mov')?.value || 'alta';
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
        document.getElementById('vin-ped-cliente').value = '';
        document.getElementById('vin-ped-rfc').value = '';
        document.getElementById('vin-ped-info-proveedor').value = '';
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
        this.form = document.getElementById('vin-ped-formCotizacion');
        this.btnSubmit = document.querySelector('.vin-ped-btn-submit');

        this.clienteManager = new PedidosDataManager({
            endpoint: '/DatosGenerales/BuscarC',
            detailEndpoint: '/DatosGenerales/BuscarCliente',
            modalId: 'vin-ped-modalBuscarCliente',
            inputId: 'vin-ped-inputBuscarCliente',
            resultsId: 'vin-ped-listaResultadosClientes',
            pageSizeId: 'vin-ped-pageSizeClientes',
            btnClearId: 'vin-ped-btnLimpiarClientes',
            spinnerId: 'vin-ped-spinnerClientes',
            paginationId: 'vin-ped-paginationClientes',
            recordsFromId: 'vin-ped-recordsFromClientes',
            recordsToId: 'vin-ped-recordsToClientes',
            totalRecordsId: 'vin-ped-totalRecordsClientes',
            shouldCloseOnSelect: true
        });

        this.clienteManager.onPedidosSelect = (cliente) => {
            document.getElementById('vin-ped-cliente').value = cliente.id || '';
            document.getElementById('vin-ped-rfc').value = cliente.rfc || '';
            document.getElementById('vin-ped-info-proveedor').value =
                [cliente.dir, cliente.col, cliente.pob, cliente.cp]
                    .filter(x => x)
                    .join(',\n');

            const vendedorInstance = tomManager.getInstance('vin-ped-vendedor');
            if (vendedorInstance && cliente.cve_vdr) {
                vendedorInstance.setValue(cliente.cve_vdr, true);
            }
            const fpagoInstance = tomManager.getInstance('vin-ped-forma-pago');
            if (fpagoInstance && cliente.forma_pago) {
                fpagoInstance.setValue(cliente.forma_pago, true);
            }
            const cfdiInstance = tomManager.getInstance('vin-ped-uso-cfdi');
            if (cfdiInstance && cliente.uso_sugerido) {
                cfdiInstance.setValue(cliente.uso_sugerido, true);
            }
            const totalActual = PedidosApp.productManagerInstance?.totales?.total || 0;
            mostrarBannerCredito(cliente, totalActual);
        };

        this.productoManager = new PedidosDataManager({
            endpoint: '/DatosGenerales/BuscarP',
            detailEndpoint: '/DatosGenerales/BuscarProducto',
            modalId: 'vin-ped-modalBuscarProducto',
            inputId: 'vin-ped-inputBuscarProducto',
            resultsId: 'vin-ped-listaResultadosProductos',
            pageSizeId: 'vin-ped-pageSizeProductos',
            btnClearId: 'vin-ped-btnLimpiarProductos',
            spinnerId: 'vin-ped-spinnerProductos',
            paginationId: 'vin-ped-paginationProductos',
            recordsFromId: 'vin-ped-recordsFromProductos',
            recordsToId: 'vin-ped-recordsToProductos',
            totalRecordsId: 'vin-ped-totalRecordsProductos',
            shouldCloseOnSelect: false
        });

        this.productManagerInstance = new PedidosProductManager();

        this.productoManager.onPedidosSelect = (producto) => {
            this.productManagerInstance.agregarPedidosProducto(producto);
        };

        this.documentoManager = new PedidosDocumentoManager({
            endpoint: '/DatosGenerales/BuscarVIND',
            detailEndpoint: '/DatosGenerales/BuscarDocumentoEspecialInternacional',
            modalId: 'vin-ped-modalBuscarDocumentos',
            inputId: 'vin-ped-inputBuscarDocumento',
            resultsId: 'vin-ped-listaResultadosDocumentos',
            pageSizeId: 'vin-ped-pageSizeDocumentos',
            btnClearId: 'vin-ped-btnLimpiarDocumentos',
            spinnerId: 'vin-ped-spinnerDocumentos',
            paginationId: 'vin-ped-paginationDocumentos',
            recordsFromId: 'vin-ped-recordsFromDocumentos',
            recordsToId: 'vin-ped-recordsToDocumentos',
            totalRecordsId: 'vin-ped-totalRecordsDocumentos',
            shouldCloseOnSelect: true,
            filtros: {}
        });

        const filtroEstado = document.getElementById('vin-ped-filtroEstado');
        const filtroFecha = document.getElementById('vin-ped-filtroFecha');

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
        await this.cargarPermisosUsuario(); // ★ agregar esta línea
        this.formManager = new PedidosFormManager(this.productManagerInstance);

        const tipoMovSelect = document.getElementById('vin-ped-tipo-docto-mov');
        tipoMovSelect?.addEventListener('change', (e) => {
            this.manejarPedidosCambioTipoMovimiento(e.target.value);
        });

        this.manejarPedidosCambioTipoMovimiento(tipoMovSelect?.value || 'alta');

        console.log('Sistema de pedidos inicializado correctamente');

        window.verPedidosDatosFormulario = () => this.formManager.verPedidosDatosFormulario();
    },

    async cargarDocumentoCompleto(documento) {
        document.getElementById('vin-ped-sucursal').value = documento.sucursal || '';
        document.getElementById('vin-ped-almacen').value = documento.almacen || '';
        document.getElementById('vin-ped-documentid').value = documento.id_encabezado || '';

        document.getElementById('vin-ped-cliente').value = documento.cli_prov || '';
        document.getElementById('vin-ped-rfc').value = documento.rfc || '';
        document.getElementById('vin-ped-info-proveedor').value = documento.info_cli || '';
        document.getElementById('vin-ped-paridad').value = documento.par || '';

        const vendedorInstance = tomManager.getInstance('vin-ped-vendedor');
        if (vendedorInstance && (documento.vdr_cpr || documento.cve_vdr)) {
            vendedorInstance.setValue(documento.vdr_cpr || documento.cve_vdr, true);
        }

        const monedaInstance = tomManager.getInstance('vin-ped-moneda');
        if (monedaInstance && (documento.ccy || documento.moneda)) {
            monedaInstance.setValue(documento.ccy || documento.moneda, true);
        }

        const cfdiInstance = tomManager.getInstance('vin-ped-uso-cfdi');
        if (cfdiInstance && (documento.usoCfdi || documento.uso_cfdi || documento.uso_sugerido)) {
            cfdiInstance.setValue(documento.usoCfdi || documento.uso_cfdi || documento.uso_sugerido, true);
        }

        const formaPagoInstance = tomManager.getInstance('vin-ped-forma-pago');
        if (formaPagoInstance && (documento.formaPago || documento.forma_pago)) {
            formaPagoInstance.setValue(documento.formaPago || documento.forma_pago, true);
        }

        const metodoPagoInstance = tomManager.getInstance('vin-ped-metodo-pago');
        if (metodoPagoInstance && (documento.metodoPago || documento.metodo_pago)) {
            metodoPagoInstance.setValue(documento.metodoPago || documento.metodo_pago, true);
        }

        document.getElementById('vin-ped-limite-credito').value = documento.lim_crd || '';
        document.getElementById('vin-ped-plazo').value = documento.pl_crd || '';
        document.getElementById('vin-ped-fecha-pago').value = documento.fechaPago || '';
        document.getElementById('vin-ped-concepto').value = documento.coment1 || '';
        document.getElementById('vin-ped-comentarios').value = documento.coment_aut || '';

        if (documento.productos && Array.isArray(documento.productos)) {
            this.productManagerInstance.productos = [];
            documento.productos.forEach(prod => {
                const precio = parseFloat(prod.precio) || 0;
                const productoData = {
                    id: `prod_${Date.now()}_${Math.random()}`,
                    productoId: prod.producto_id || prod.id,
                    descripcion: prod.descripcion,
                    existencia: prod.existencia,
                    cantidad: parseFloat(prod.cantidad) || 0,
                    precio: precio,
                    // ★ precioOriginal también al cargar desde documento
                    precioOriginal: precio,
                    descuento: parseFloat(prod.descuento) || 0,
                    unidad: prod.unidad || prod.udm || 'PZA',
                    existenciaGeneral: prod.existenciaGeneral || 0,
                    existenciaModular: prod.existenciaModular || 0,
                    comentario: prod.comentario || ''
                };
                this.productManagerInstance.productos.push(productoData);
            });

            this.productManagerInstance.renderPedidosTable();
        }

        const fleteInput = document.getElementById('vin-ped-flete-val');
        if (fleteInput && documento.flete) {
            const chkFlete = document.getElementById("vin-ped-flete");
            chkFlete.checked = true;
            chkFlete.dispatchEvent(new Event("change"));
            fleteInput.value = documento.flete;
        }

        this.productManagerInstance.calcularPedidosTotales();

        const ordenCompraInput = document.getElementById('vin-ped-orden-compra-val');
        if (ordenCompraInput && documento.ordencompra) {
            const chkOrdenCompra = document.getElementById("vin-ped-orden-compra-check");
            chkOrdenCompra.checked = true;
            chkOrdenCompra.dispatchEvent(new Event("change"));
            ordenCompraInput.value = documento.ordencompra;
        }

        this.productManagerInstance.calcularPedidosTotales();

        const tipoMovSelect = document.getElementById('vin-ped-tipo-docto-mov');
        if (tipoMovSelect && documento.folio) {
            tipoMovSelect.value = 'modificacion';
            this.manejarPedidosCambioTipoMovimiento('modificacion');
        }
        if (documento.mdp) {
            const metodoPagoEl = document.getElementById('vin-ped-metodo-pago');
            if (metodoPagoEl) metodoPagoEl.value = documento.mdp;
            const tipoPago = documento.mdp === 'PPD' ? 'credito' : 'contado';
            selectTipoPagoVin(tipoPago);
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
    async cargarPermisosUsuario() {
        try {
            const response = await fetch('/DatosGenerales/ObtenerPermisosUsuario');
            if (!response.ok) return;
            const permisos = await response.json();

            if (permisos.tienePrecio && permisos.tokenPrecio) {
                pedidosAuthState.precioToken = permisos.tokenPrecio;
            }
            if (permisos.tieneDescuento && permisos.tokenDescuento) {
                pedidosAuthState.descuentoToken = permisos.tokenDescuento;
            }
        } catch (err) {
            console.warn('No se pudieron verificar permisos automáticos:', err);
        }
    },
    deshabilitarPedidosFormulario() {
        const pedidoContenedor = document.getElementById('pedido-contenedor');
        if (!pedidoContenedor) return;

        pedidoContenedor.querySelectorAll('input, select, textarea, button').forEach(element => {
            if (element.id !== 'vin-ped-tipo-docto-mov') {
                element.disabled = true;
                element.classList.add('vin-ped-disabled-field');
            }
        });

        const btnBuscarCliente = pedidoContenedor.querySelector('[data-bs-target="#vin-ped-modalBuscarCliente"]');
        const btnBuscarProducto = pedidoContenedor.querySelector('[data-bs-target="#vin-ped-modalBuscarProducto"]');
        if (btnBuscarCliente) btnBuscarCliente.disabled = true;
        if (btnBuscarProducto) btnBuscarProducto.disabled = true;

        pedidoContenedor.querySelectorAll('.vin-ped-btn-eliminar').forEach(btn => btn.disabled = true);

        if (window.tomManager) {
            ['vin-ped-vendedor', 'vin-ped-moneda', 'vin-ped-uso-cfdi', 'vin-ped-forma-pago'].forEach(id => {
                const instance = window.tomManager.instances?.get(id);
                if (instance) instance.disable();
            });
        }

        const btn = pedidoContenedor.querySelector('.vin-ped-btn-submit');
        if (btn) btn.disabled = false;

        const btnDocConsulta = pedidoContenedor.querySelector('.vin-ped-floating-btn');
        if (btnDocConsulta) btnDocConsulta.disabled = false;

        const btnPreview = pedidoContenedor.querySelector('.vin-ped-ticket-btn');
        if (btnPreview) btnPreview.disabled = false;
    },

    habilitarPedidosFormulario() {
        const pedidoContenedor = document.getElementById('pedido-contenedor');
        if (!pedidoContenedor) return;

        pedidoContenedor.querySelectorAll('input, select, textarea, button').forEach(element => {
            element.disabled = false;
            element.classList.remove('vin-ped-disabled-field');
        });

        const btnBuscarCliente = pedidoContenedor.querySelector('[data-bs-target="#vin-ped-modalBuscarCliente"]');
        const btnBuscarProducto = pedidoContenedor.querySelector('[data-bs-target="#vin-ped-modalBuscarProducto"]');
        if (btnBuscarCliente) btnBuscarCliente.disabled = false;
        if (btnBuscarProducto) btnBuscarProducto.disabled = false;

        if (window.tomManager) {
            ['vin-ped-vendedor', 'vin-ped-moneda', 'vin-ped-uso-cfdi', 'vin-ped-forma-pago'].forEach(id => {
                const instance = window.tomManager.instances?.get(id);
                if (instance) instance.enable();
            });
        }

        const tipoMovimiento = document.getElementById('vin-ped-tipo-docto-mov')?.value;
        const btn = pedidoContenedor.querySelector('.vin-ped-btn-submit');
        if (btn) {
            btn.innerHTML = tipoMovimiento === 'modificacion'
                ? '<i class="fas fa-save"></i> Actualizar Pedido'
                : '<i class="fas fa-save"></i> Guardar Pedido';
        }

        // Re-aplicar bloqueo de descuento/precio si los tokens no existen
        this.productManagerInstance?.renderPedidosTable();
    }
};

// ============================================
// 7. FUNCIONES DE CRÉDITO (sin cambios)
// ============================================
function mostrarBannerCredito(cliente, totalPedido = null) {
    const banner = document.getElementById('vin-ped-credit-banner');
    const banner2 = document.getElementById('vin-ped-inactivo-banner');
    const icon = document.getElementById('vin-ped-credit-icon');
    const icon2 = document.getElementById('vin-ped-inactivo-icon');
    const title = document.getElementById('vin-ped-credit-title');
    const title2 = document.getElementById('vin-ped-inactivo-title');
    const sub = document.getElementById('vin-ped-credit-sub');
    const bar = document.getElementById('vin-ped-credit-bar');
    const barWrap = document.getElementById('vin-ped-credit-bar-wrap');
    const pct = document.getElementById('vin-ped-credit-pct');
    const btnSubmit = document.querySelector('.vin-ped-btn-submit');

    const estatus = cliente.estatus_credito || 'SIN_LIMITE';
    const limite = parseFloat(cliente.lim_crd || 0);
    const usado = parseFloat(cliente.credito_usado || 0);
    const disponible = parseFloat(cliente.credito_disponible || 0);
    const porcentaje = parseFloat(cliente.porcentaje_uso || 0);
    const estatus_cliente = cliente.estatus_cliente;

    banner.className = 'vin-ped-credit-banner';
    banner2.className = 'vin-ped-inactivo-banner';

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
        title2.textContent = 'Cliente Suspendido';
        if (btnSubmit) btnSubmit.disabled = true;
        banner2.style.display = 'flex';
    }

    banner.style.display = 'flex';
}

function _removePedidoEmailBtn() {
    document.getElementById('vin-ped-btn-email-gerente')?.remove();
}

function _insertPedidoEmailBtn(banner, cliente, totalPedido) {
    const btn = document.createElement('button');
    btn.type = 'button';
    btn.id = 'vin-ped-btn-email-gerente';
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

    const subEl = document.getElementById('vin-ped-credit-sub');
    subEl?.insertAdjacentElement('afterend', btn);
}

async function _enviarEmailGerente(cliente, totalPedido) {
    if (!window.Swal) return;

    const fmt = n => n.toLocaleString('es-MX', { style: 'currency', currency: 'MXN' });
    const folio = document.getElementById('vin-ped-folio')?.value || 'Sin folio';
    const clienteId = document.getElementById('vin-ped-cliente')?.value || '';
    const limite = parseFloat(cliente.lim_crd || 0);
    const usado = parseFloat(cliente.credito_usado || 0);
    const disponible = parseFloat(cliente.credito_disponible || 0);

    const { value: correo } = await Swal.fire({
        title: 'Solicitar autorización',
        html: `
            <p style="margin-bottom:12px; font-size:13px; color:#475569; text-align:left;">
                El cliente <strong>${clienteId}</strong> tiene el crédito excedido.<br>
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
    const pedido = document.getElementById('vin-ped-documentid')?.value?.trim();
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

        const resp = await fetch('/VINPedido/EnviarSolicitudGerente', { method: 'POST', body: formData });
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
    const bannerEl = document.getElementById('vin-ped-credit-banner');
    if (!bannerEl || bannerEl.style.display === 'none') return;
    if (!bannerEl.dataset.estatus) return;

    const totalPedido = parseFloat(document.getElementById('vin-ped-importe')?.value || 0);
    const limite = parseFloat(bannerEl.dataset.limite || 0);
    const usado = parseFloat(bannerEl.dataset.usado || 0);
    const disponible = parseFloat(bannerEl.dataset.disponible || 0);
    const estatus = bannerEl.dataset.estatus || 'SIN_LIMITE';

    if (estatus === 'SIN_LIMITE') return;

    const bannerEl2 = document.getElementById('vin-ped-inactivo-banner');
    const clienteSuspendido = bannerEl2?.dataset.estatus === 'suspendido';

    const btnSubmit = document.querySelector('.vin-ped-btn-submit');
    const titleEl = document.getElementById('vin-ped-credit-title');
    const subEl = document.getElementById('vin-ped-credit-sub');
    const fmt = n => n.toLocaleString('es-MX', { style: 'currency', currency: 'MXN' });

    subEl.textContent = `Límite: ${fmt(limite)} · Usado: ${fmt(usado)} · Disponible: ${fmt(disponible)} · Pedido: ${fmt(totalPedido)}`;

    const creditoTotal = usado + totalPedido;
    const creditoExcedido = creditoTotal > limite;
    const creditoPorVencer = !creditoExcedido && (creditoTotal / limite >= 0.8);

    if (creditoExcedido) {
        bannerEl.className = 'vin-ped-credit-banner excedido';
        document.getElementById('vin-ped-credit-icon').textContent = '🚫';
        titleEl.textContent = 'Crédito excedido — el pedido supera el límite disponible';

        if (!document.getElementById('vin-ped-btn-email-gerente')) {
            _insertPedidoEmailBtn(bannerEl, {
                lim_crd: limite,
                credito_usado: usado,
                credito_disponible: disponible,
                estatus_credito: 'EXCEDIDO'
            }, totalPedido);
        }
    } else if (creditoPorVencer) {
        bannerEl.className = 'vin-ped-credit-banner por-vencer';
        document.getElementById('vin-ped-credit-icon').textContent = '⚠️';
        titleEl.textContent = 'Crédito próximo al límite con este pedido';
        _removePedidoEmailBtn();
    } else {
        bannerEl.className = 'vin-ped-credit-banner disponible';
        document.getElementById('vin-ped-credit-icon').textContent = '✅';
        titleEl.textContent = 'Crédito disponible';
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

        const fleteInput = document.getElementById('vin-ped-flete-val');
        if (fleteInput) {
            fleteInput.addEventListener('input', () => { /* calcularTotales se llama desde el ProductManager */ });
            fleteInput.addEventListener('change', () => { });
        }
    });
} else {
    PedidosApp.initPedidos();
}
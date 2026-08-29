// ============================================
// 0. ESTADO GLOBAL DE AUTENTICACIÓN
// ============================================
const remisionesAuthState = {
    descuentoToken: null,
    precioToken: null
};

// ============================================
// 1. CLASE BASE PARA GESTIÓN DE DATOS
// ============================================
class RemisionesDataManager {
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
        this.initRemisionesDataManager();
    }

    initRemisionesDataManager() {
        this.dom.modal?.addEventListener('show.bs.modal', () => this.onRemisionesModalShow());
        this.dom.input?.addEventListener('input', (e) => this.handleRemisionesSearch(e.target.value));
        this.dom.pageSize?.addEventListener('change', (e) => this.handleRemisionesPageSizeChange(e.target.value));
        this.dom.btnClear?.addEventListener('click', () => this.handleRemisionesClear());

        this.dom.results?.addEventListener('click', (e) => {
            const btn = e.target.closest('[data-select-id]');
            if (btn) {
                const id = btn.dataset.selectId;
                this.handleRemisionesSelect(id);
            }
        });

        this.dom.pagination?.addEventListener('click', (e) => {
            e.preventDefault();
            const btn = e.target.closest('[data-page]');
            if (btn && !btn.closest('.disabled')) {
                this.state.page = parseInt(btn.dataset.page);
                this.fetchRemisionesData();
            }
        });
    }

    onRemisionesModalShow() {
        this.handleRemisionesClear();
        this.fetchRemisionesData();
    }

    handleRemisionesSearch(value) {
        clearTimeout(this.typingTimer);
        this.typingTimer = setTimeout(() => {
            this.state.lastSearch = value;
            this.state.page = 1;
            this.fetchRemisionesData();
        }, 300);
    }

    handleRemisionesPageSizeChange(value) {
        this.state.pageSize = parseInt(value);
        this.state.page = 1;
        this.fetchRemisionesData();
    }

    handleRemisionesClear() {
        this.dom.input.value = '';
        this.state.lastSearch = '';
        this.state.page = 1;
        this.fetchRemisionesData();
    }

    getRemisionesCachedData(key) {
        const cached = this.cache.get(key);
        if (cached && Date.now() - cached.timestamp < this.cacheTimeout) {
            return cached.data;
        }
        return null;
    }

    setRemisionesCachedData(key, data) {
        this.cache.set(key, { data, timestamp: Date.now() });
    }

    clearRemisionesCache() {
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

    async fetchRemisionesData() {
        if (this.state.loading) {
            this.abortController?.abort();
        }

        const cacheKey = this.buildCacheKey();
        const cached = this.getRemisionesCachedData(cacheKey);

        if (cached) {
            this.renderRemisionesResults(cached);
            return;
        }

        this.state.loading = true;
        this.showRemisionesSpinner(true);
        this.abortController = new AbortController();

        try {
            const url = this.buildUrl();
            const response = await fetch(url, { signal: this.abortController.signal });

            if (!response.ok) throw new Error('Error en la respuesta');

            const data = await response.json();
            this.state.totalRecords = data.total || 0;

            const results = { items: data.items || data, total: this.state.totalRecords };
            this.setRemisionesCachedData(cacheKey, results);
            this.renderRemisionesResults(results);

        } catch (error) {
            if (error.name !== 'AbortError') {
                console.error('Error fetching remisiones data:', error);
                this.renderRemisionesError();
            }
        } finally {
            this.state.loading = false;
            this.showRemisionesSpinner(false);
        }
    }

    showRemisionesSpinner(show) {
        this.dom.spinner?.classList.toggle('d-none', !show);
    }

    renderRemisionesResults(data) {
        const items = data.items || [];

        if (items.length === 0) {
            this.dom.results.innerHTML = `
                <li class="list-group-item text-center text-muted py-5">
                    <i class="fas fa-search fa-3x mb-3 opacity-25"></i>
                    <p>No se encontraron resultados</p>
                </li>`;
            this.updateRemisionesCounters();
            return;
        }

        const fragment = document.createDocumentFragment();

        items.forEach(item => {
            const li = document.createElement('li');
            li.className = 'list-group-item d-flex justify-content-between align-items-center hover-shadow';
            li.innerHTML = `
                <div>
                    <h6 class="mb-1 fw-semibold text-primary">${this.escapeRemisionesHtml(item.descripcion)}</h6>
                    <small class="text-muted">ID: ${this.escapeRemisionesHtml(item.id)}</small>
                </div>
                <button type="button" class="btn btn-sm btn-primary" data-select-id="${this.escapeRemisionesHtml(item.id)}">
                    <i class="fas fa-check me-1"></i>Seleccionar
                </button>`;
            fragment.appendChild(li);
        });

        this.dom.results.innerHTML = '';
        this.dom.results.appendChild(fragment);

        this.updateRemisionesCounters();
        this.renderRemisionesPagination();
    }

    renderRemisionesError() {
        this.dom.results.innerHTML = `
            <li class="list-group-item text-center text-danger py-5">
                <i class="fas fa-exclamation-triangle fa-3x mb-3"></i>
                <p>Error al cargar los datos</p>
            </li>`;
    }

    updateRemisionesCounters() {
        const from = (this.state.page - 1) * this.state.pageSize + 1;
        const to = Math.min(this.state.page * this.state.pageSize, this.state.totalRecords);

        if (this.dom.recordsFrom) this.dom.recordsFrom.textContent = from;
        if (this.dom.recordsTo) this.dom.recordsTo.textContent = to;
        if (this.dom.totalRecords) this.dom.totalRecords.textContent = this.state.totalRecords;
    }

    renderRemisionesPagination() {
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

        const prevLi = this.createRemisionesPageButton('Anterior', this.state.page - 1, this.state.page === 1);
        fragment.appendChild(prevLi);

        for (let i = startPage; i <= endPage; i++) {
            const li = this.createRemisionesPageButton(i, i, false, i === this.state.page);
            fragment.appendChild(li);
        }

        const nextLi = this.createRemisionesPageButton('Siguiente', this.state.page + 1, this.state.page === totalPages);
        fragment.appendChild(nextLi);

        this.dom.pagination.innerHTML = '';
        this.dom.pagination.appendChild(fragment);
    }

    createRemisionesPageButton(text, page, disabled = false, active = false) {
        const li = document.createElement('li');
        li.className = `page-item${disabled ? ' disabled' : ''}${active ? ' active' : ''}`;
        li.innerHTML = `<button type="button" class="page-link" data-page="${page}">${text}</button>`;
        return li;
    }

    async handleRemisionesSelect(id) {
        try {
            const response = await fetch(`${this.detailEndpoint}?id=${encodeURIComponent(id)}`);
            if (!response.ok) throw new Error('Error al obtener detalles');

            const data = await response.json();
            if (data && data[0]) {
                this.onRemisionesSelect(data[0]);
                if (this.shouldCloseOnSelect) {
                    bootstrap.Modal.getInstance(this.dom.modal)?.hide();
                }
            }
        } catch (error) {
            console.error('Error selecting remisiones item:', error);
            alert('Error al seleccionar el elemento');
        }
    }

    onRemisionesSelect(item) {
        console.log('Remisiones item selected:', item);
    }

    escapeRemisionesHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }
}

// ============================================
// 2. CLASE EXTENDIDA PARA DOCUMENTOS
// ============================================
class RemisionesDocumentoManager extends RemisionesDataManager {
    renderRemisionesResults(data) {
        const items = data.items || [];

        if (items.length === 0) {
            this.dom.results.innerHTML = `
                <li class="list-group-item text-center text-muted py-5">
                    <i class="fas fa-search fa-3x mb-3 opacity-25"></i>
                    <p>No se encontraron documentos</p>
                </li>`;
            this.updateRemisionesCounters();
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
                            <strong>Folio:</strong> ${this.escapeRemisionesHtml(item.folio || '')}
                        </h6>
                        <p class="mb-1 text-muted">
                            <i class="fas fa-user me-2"></i>
                            <strong>Cliente:</strong> ${this.escapeRemisionesHtml(item.cli_prov || '')}
                        </p>
                        <div class="d-flex gap-3 small text-muted">
                            <span><i class="fas fa-calendar me-1"></i> ${this.escapeRemisionesHtml(item.fecha || '')}</span>
                            <span><i class="fas fa-money-bill-wave me-1"></i> $${parseFloat(item.imp || 0).toFixed(2)}</span>
                            <span><i class="fas fa-user-tie me-1"></i> ${this.escapeRemisionesHtml(item.usr0 || '')}</span>
                        </div>
                    </div>
                    <div class="d-flex flex-column gap-2 align-items-end">
                        <span></span>
                        <button type="button" class="btn btn-sm btn-primary" data-select-id="${this.escapeRemisionesHtml(item.id_encabezado)}">
                            <i class="fas fa-check me-1"></i>Cargar
                        </button>
                    </div>
                </div>`;
            fragment.appendChild(li);
        });

        this.dom.results.innerHTML = '';
        this.dom.results.appendChild(fragment);

        this.updateRemisionesCounters();
        this.renderRemisionesPagination();
    }
}

// ============================================
// 3. AUTENTICACIÓN GLOBAL POR TIPO DE CAMPO
// ============================================
async function pedirRemisionesAutenticacionGlobal(tipo) {
    // Si ya tiene token, no repetir
    if (tipo === 'descuento' && remisionesAuthState.descuentoToken) return;
    if (tipo === 'precio' && remisionesAuthState.precioToken) return;

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
            if (tipo === 'descuento') remisionesAuthState.descuentoToken = result.token;
            else remisionesAuthState.precioToken = result.token;

            RemisionesApp.productManagerInstance?.desbloquearRemisionesCampos(tipo);

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
class RemisionesProductManager {
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
            table: document.getElementById('vin-rem-productosTable'),
            thead: document.querySelector('#vin-rem-productosTable')?.closest('table')?.querySelector('thead'),
            subtotal1Display: document.getElementById('vin-rem-total-subtotal1-display'),
            descuentoDisplay: document.getElementById('vin-rem-total-descuento-display'),
            fleteDisplay: document.getElementById('vin-rem-total-flete-display'),
            subtotal2Display: document.getElementById('vin-rem-total-subtotal2-display'),
            importeDisplay: document.getElementById('vin-rem-importe-display')
        };

        this.initRemisionesProductManager();
    }

    initRemisionesProductManager() {
        this.dom.table?.addEventListener('input', (e) => {
            const input = e.target;
            if (input.classList.contains('vin-rem-cantidad') ||
                input.classList.contains('vin-rem-precio') ||
                input.classList.contains('vin-rem-descuento') ||
                input.classList.contains('vin-rem-comentario')) {
                const tr = input.closest('tr');
                const productoId = tr?.dataset.productoId;
                if (productoId) this.actualizarRemisionesFila(productoId);
            }
        });

        this.dom.table?.addEventListener('click', (e) => {
            // 1. Eliminar producto
            const btnEliminar = e.target.closest('.vin-rem-btn-eliminar');
            if (btnEliminar) {
                const productoId = btnEliminar.closest('tr')?.dataset.productoId;
                if (productoId) this.eliminarRemisionesProducto(productoId);
                return;
            }

            // 2. Click en OVERLAY de campo protegido
            const overlay = e.target.closest('.vin-rem-field-overlay');
            if (overlay) {
                pedirRemisionesAutenticacionGlobal(overlay.dataset.tipo);
                return;
            }
        });

        document.getElementById('vin-rem-flete-val')?.addEventListener('input', () => {
            this.calcularRemisionesTotales();
        });

        document.getElementById('vin-rem-monto-anticipo')?.addEventListener('input', () => {
            this.calcularRemisionesTotales();
        });
    }

    agregarRemisionesProducto(producto) {
        const productoExistente = this.productos.find(p => p.productoId === producto.id);

        if (productoExistente) {
            productoExistente.cantidad += 1;

            const tr = this.dom.table.querySelector(`tr[data-producto-id="${productoExistente.id}"]`);
            if (tr) {
                const inputCantidad = tr.querySelector('.vin-rem-cantidad');
                inputCantidad.value = productoExistente.cantidad;

                const importe = this.calcularRemisionesImporte(productoExistente);
                tr.querySelector('.vin-rem-importe').textContent = importe.toFixed(2);

                tr.classList.add('table-warning');
                setTimeout(() => tr.classList.remove('table-warning'), 500);
            }

            this.calcularRemisionesTotales();

            if (window.toastMixin) {
                toastMixin.fire({
                    icon: 'info',
                    title: `Cantidad actualizada: ${this.escapeRemisionesHtml(producto.descripcion)} (${productoExistente.cantidad})`
                });
            }
        } else {
            const precio = parseFloat(producto.precio || 0);
            const productoData = {
                id: `prod_${Date.now()}_${Math.random()}`,
                productoId: producto.id,
                descripcion: producto.descripcion,
                existencia: producto.existencia,
                cantidad: 1,
                precio: precio,
                precioOriginal: precio,
                descuento: 0,
                unidad: producto.udm || 'PZA',
                existenciaGeneral: producto.existenciaGeneral || 0,
                existenciaModular: producto.existenciaModular || 0,
                comentario: ''
            };

            this.productos.push(productoData);
            this.renderRemisionesTable();
            this.calcularRemisionesTotales();

            if (window.toastMixin) {
                toastMixin.fire({
                    icon: 'success',
                    title: `Producto agregado: ${this.escapeRemisionesHtml(producto.descripcion)}`
                });
            }
        }
    }

    renderRemisionesTable() {
        const tbody = this.dom.table;
        if (!tbody) return;
        tbody.innerHTML = '';

        if (this.productos.length === 0) {
            tbody.innerHTML = `
            <tr class="vin-rem-empty-state">
                <td colspan="12" class="text-center py-5">
                    <i class="fas fa-box-open fa-3x mb-3 opacity-25"></i>
                    <div>No hay productos agregados</div>
                    <small class="text-muted">Haz clic en "Agregar" para comenzar</small>
                </td>
            </tr>`;
            return;
        }

        const fragment = document.createDocumentFragment();

        const descDesbloqueado = !!remisionesAuthState.descuentoToken;
        const precioDesbloqueado = !!remisionesAuthState.precioToken;

        this.productos.forEach((p, index) => {
            const importe = this.calcularRemisionesImporte(p);
            const tr = document.createElement('tr');
            tr.dataset.productoId = p.id;

            // Celda precio
            const precioCell = document.createElement('td');
            precioCell.style.cssText = 'position:relative; display:none;';
            precioCell.style.position = 'relative';

            const precioInput = document.createElement('input');
            precioInput.type = 'number';
            precioInput.className = 'vin-rem-product-input vin-rem-precio';
            precioInput.value = p.precio.toFixed(2);
            precioInput.min = '0';
            precioInput.step = '0.01';
            precioInput.dataset.precioOriginal = p.precioOriginal.toFixed(2);

            if (!precioDesbloqueado) {
                precioInput.disabled = true;
                const overlayPrecio = document.createElement('div');
                overlayPrecio.className = 'vin-rem-field-overlay';
                overlayPrecio.dataset.tipo = 'precio';
                overlayPrecio.title = 'Haz clic para desbloquear precios';
                overlayPrecio.style.cssText =
                    'position:absolute;top:0;left:0;width:100%;height:100%;cursor:pointer;z-index:10;';
                precioCell.appendChild(precioInput);
                precioCell.appendChild(overlayPrecio);
            } else {
                precioCell.appendChild(precioInput);
            }

            // Celda descuento
            const descCell = document.createElement('td');
            descCell.style.cssText = 'position:relative; display:none;';
            descCell.style.position = 'relative';

            const descInput = document.createElement('input');
            descInput.type = 'number';
            descInput.className = 'vin-rem-product-input vin-rem-descuento';
            descInput.value = p.descuento;
            descInput.min = '0';
            descInput.max = '100';
            descInput.step = '0.01';

            if (!descDesbloqueado) {
                descInput.disabled = true;
                const overlayDesc = document.createElement('div');
                overlayDesc.className = 'vin-rem-field-overlay';
                overlayDesc.dataset.tipo = 'descuento';
                overlayDesc.title = 'Haz clic para desbloquear descuentos';
                overlayDesc.style.cssText =
                    'position:absolute;top:0;left:0;width:100%;height:100%;cursor:pointer;z-index:10;';
                descCell.appendChild(descInput);
                descCell.appendChild(overlayDesc);
            } else {
                descCell.appendChild(descInput);
            }

            tr.innerHTML = `
            <td class="text-center fw-bold">${index + 1}</td>
            <td>${this.escapeRemisionesHtml(p.productoId)}</td>
            <td style="max-width:200px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;"
                title="${this.escapeRemisionesHtml(p.descripcion)}">
                ${this.escapeRemisionesHtml(p.descripcion)}
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
                <input type="number" class="vin-rem-product-input vin-rem-cantidad"
                       value="${p.cantidad}" min="0" step="0.01">
            </td>
            <td>${this.escapeRemisionesHtml(p.unidad)}</td>`;

            tr.appendChild(precioCell);
            tr.appendChild(descCell);

            const resto = document.createElement('template');
            resto.innerHTML = `
            <td class="vin-rem-importe fw-bold" style="display:none;">${importe.toFixed(2)}</td>
            <td>
                <input type="text" class="vin-rem-product-input vin-rem-comentario"
                       value="${this.escapeRemisionesHtml(p.comentario)}" placeholder="Comentario...">
            </td>
            <td>
                <button type="button" class="vin-rem-btn-delete vin-rem-btn-eliminar">
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

    actualizarRemisionesFila(productoId) {
        const producto = this.productos.find(p => p.id === productoId);
        if (!producto) return;

        const tr = this.dom.table.querySelector(`tr[data-producto-id="${productoId}"]`);
        if (!tr) return;

        producto.cantidad = parseFloat(tr.querySelector('.vin-rem-cantidad').value) || 0;
        producto.comentario = tr.querySelector('.vin-rem-comentario')?.value || '';

        // Validación de precio
        const precioInput = tr.querySelector('.vin-rem-precio');
        if (precioInput && !precioInput.disabled) {
            const nuevoPrecio = parseFloat(precioInput.value) || 0;
            const precioOriginal = parseFloat(precioInput.dataset.precioOriginal) || 0;

            if (!remisionesAuthState.precioToken && Math.abs(nuevoPrecio - precioOriginal) > 0.001) {
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

        // Validación de descuento
        const descInput = tr.querySelector('.vin-rem-descuento');
        if (descInput && !descInput.disabled) {
            const nuevoDesc = parseFloat(descInput.value) || 0;

            if (!remisionesAuthState.descuentoToken && nuevoDesc > 0) {
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

        tr.querySelector('.vin-rem-importe').textContent = this.calcularRemisionesImporte(producto).toFixed(2);
        this.calcularRemisionesTotales();
    }

    eliminarRemisionesProducto(productoId) {
        this.productos = this.productos.filter(p => p.id !== productoId);
        this.renderRemisionesTable();
        this.calcularRemisionesTotales();
    }

    desbloquearRemisionesCampos(tipo) {
        const selector = tipo === 'descuento' ? '.vin-rem-descuento' : '.vin-rem-precio';

        this.dom.table?.querySelectorAll(selector).forEach(input => {
            input.removeAttribute('disabled');

            const overlay = input.parentElement?.querySelector('.vin-rem-field-overlay');
            overlay?.remove();
        });
    }

    calcularRemisionesImporte(producto) {
        return producto.cantidad * producto.precio * (1 - producto.descuento / 100);
    }

    calcularRemisionesTotales() {
        const tipoPago = document.getElementById('vin-rem-toggle-pago')?.getAttribute('data-state') || 'contado';
        const flete = parseFloat(document.getElementById('vin-rem-flete-val')?.value) || 0;
        let subtotal1 = 0, descuento = 0, subtotal2 = 0, total = 0;

        if (tipoPago === 'anticipo') {
            const montoAnticipo = parseFloat(document.getElementById('vin-rem-monto-anticipo')?.value) || 0;
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

        this.updateRemisionesDisplay(this.dom.subtotal1Display, subtotal1);
        this.updateRemisionesDisplay(this.dom.descuentoDisplay, descuento, '-$');
        this.updateRemisionesDisplay(this.dom.fleteDisplay, flete);
        this.updateRemisionesDisplay(this.dom.subtotal2Display, subtotal2);
        this.updateRemisionesDisplay(this.dom.importeDisplay, total);

        this.updateRemisionesHiddenField('vin-rem-total-subtotal1', subtotal1);
        this.updateRemisionesHiddenField('vin-rem-total-descuento', descuento);
        this.updateRemisionesHiddenField('vin-rem-total-flete', flete);
        this.updateRemisionesHiddenField('vin-rem-total-subtotal2', subtotal2);
        this.updateRemisionesHiddenField('vin-rem-importe', total);
    }

    updateRemisionesHiddenField(id, value) {
        const field = document.getElementById(id);
        if (field) field.value = value.toFixed(2);
    }

    updateRemisionesDisplay(element, value, prefix = '$') {
        if (element) element.textContent = `${prefix}${value.toFixed(2)}`;
    }

    escapeRemisionesHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    getRemisionesProductosData() {
        return this.productos.map(p => ({
            ...p,
            importe: this.calcularRemisionesImporte(p)
        }));
    }
}

// ============================================
// 5. GESTOR DE FORMULARIO
// ============================================
class RemisionesFormManager {
    constructor(productManager) {
        this.productManager = productManager;
        this.form = document.getElementById('vin-rem-formCotizacion');
        this.btnSubmit = document.querySelector('.vin-rem-btn-submit');
        this.initRemisionesFormManager();
    }

    initRemisionesFormManager() {
        this.btnSubmit?.addEventListener('click', (e) => {
            e.preventDefault();
            this.enviarRemisionesCotizacion();
        });
    }

    recopilarRemisionesDatosFormulario() {
        const formData = new FormData();
        const toggle = document.getElementById('vin-rem-toggle-pago');
        const tipoPago = toggle?.getAttribute('data-state') || 'contado';

        const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
        if (token) formData.append('__RequestVerificationToken', token);

        const camposGenerales = [
            'vin-rem-tipo-docto-mov',
            'vin-rem-folio', 'vin-rem-documentid', 'vin-rem-cliente',
            'vin-rem-rfc', 'vin-rem-vendedor', 'vin-rem-moneda',
            'vin-rem-paridad', 'vin-rem-concepto',
            'vin-rem-metodo-pago', 'vin-rem-forma-pago',
            'vin-rem-uso-cfdi', 'vin-rem-comentarios'
        ];

        camposGenerales.forEach(id => {
            const el = document.getElementById(id);
            if (el) formData.append(id.replace('vin-rem-', ''), el.value || '');
        });

        formData.set('sucursal', tomManager.getInstance('vin-rem-sucursal')?.getValue() || '');
        formData.set('almacen', tomManager.getInstance('vin-rem-almacen')?.getValue() || '');

        if (tipoPago === 'credito') {
            formData.append('limiteCredito', document.getElementById('vin-rem-limite-credito')?.value || '');
            formData.append('plazo', document.getElementById('vin-rem-plazo')?.value || '');
            formData.append('fechaPago', document.getElementById('vin-rem-fecha-pago')?.value || '');
        }

        if (tipoPago === 'anticipo') {
            formData.append('fechaAnticipo', document.getElementById('vin-rem-fecha-anticipo')?.value || '');
        }

        formData.append('ordenCompra', document.getElementById('vin-rem-orden-compra-val')?.value || '');

        const productos = this.productManager.getRemisionesProductosData();
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

        // Enviar tokens al servidor para validación
        formData.append('descuentoToken', remisionesAuthState.descuentoToken || '');
        formData.append('precioToken', remisionesAuthState.precioToken || '');

        return formData;
    }

    validarRemisionesFormulario() {
        const errores = [];

        const togglePago = document.getElementById('vin-rem-toggle-pago');
        const tipoPago = togglePago ? togglePago.getAttribute('data-state') : 'contado';

        const cliente = document.getElementById('vin-rem-cliente')?.value;
        if (!cliente || cliente.trim() === '') {
            errores.push('Debe seleccionar un cliente');
        }

        const productos = this.productManager.getRemisionesProductosData();
        if (tipoPago !== 'anticipo' && productos.length === 0) {
            errores.push('Debe agregar al menos un producto');
        }

        const vendedor = document.getElementById('vin-rem-vendedor')?.value;
        if (!vendedor || vendedor === 'Seleccionar vendedor') {
            errores.push('Debe seleccionar un vendedor');
        }

        const rfcInput = document.getElementById('vin-rem-rfc');
        const rfc = rfcInput ? rfcInput.value.trim() : '';
        if (!rfc) {
            errores.push('Debe ingresar el RFC');
        }

        const monedaSelect = tomManager.getInstance('vin-rem-moneda');
        const monedaValue = monedaSelect?.getValue();
        if (!monedaValue) {
            errores.push('Debe seleccionar una Moneda');
        }

        //const usoCfdiSelect = tomManager.getInstance('vin-rem-uso-cfdi');
        //const usoCfdiValue = usoCfdiSelect?.getValue();
        //if (!usoCfdiValue) {
        //    errores.push('Debe seleccionar un Uso CFDI');
        //}

        //const formaPagoSelect = tomManager.getInstance('vin-rem-forma-pago');
        //const formaPagoValue = formaPagoSelect?.getValue();
        //if (!formaPagoValue) {
        //    errores.push('Debe seleccionar una Forma de Pago');
        //}

        //const fechaPagoInput = document.getElementById('vin-rem-fecha-pago');
        //const fechaPago = fechaPagoInput ? fechaPagoInput.value.trim() : '';
        //if (tipoPago === 'credito' && !fechaPago) {
        //    errores.push('Debe ingresar la Fecha de Pago (solo para crédito)');
        //}

        // Validaciones de autorización — aplican a todos los usuarios
        //if (!remisionesAuthState.descuentoToken) {
        //    const conDescuento = productos.some(p => p.descuento > 0);
        //    if (conDescuento) errores.push('Hay productos con descuento. Se requiere autorización.');
        //}

        //if (!remisionesAuthState.precioToken) {
        //    const conPrecioModificado = productos.some(p =>
        //        Math.abs(p.precio - p.precioOriginal) > 0.001
        //    );
        //    if (conPrecioModificado) errores.push('Hay productos con precio modificado. Se requiere autorización.');
        //}

        return errores;
    }

    obtenerRemisionesConfiguracionPorTipo(tipoMovimiento) {
        const configuraciones = {
            'alta': {
                endpoint: '/VINRemision/Guardar',
                metodo: 'POST',
                mensajeExito: 'Remisión creada exitosamente',
                textoCarga: 'Guardando...',
                validarProductos: true
            },
            'modificacion': {
                endpoint: '/VINRemision/Modificar',
                metodo: 'PUT',
                mensajeExito: 'Remisión modificada exitosamente',
                textoCarga: 'Actualizando...',
                validarProductos: true
            },
            'consulta': {
                endpoint: '/VINRemision/Consultar',
                metodo: 'GET',
                mensajeExito: 'Consulta realizada exitosamente',
                textoCarga: 'Consultando...',
                validarProductos: false
            }
        };

        return configuraciones[tipoMovimiento] || configuraciones['alta'];
    }

    async enviarRemisionesCotizacion() {
        const tipoMovimiento = document.getElementById('vin-rem-tipo-docto-mov')?.value || 'alta';
        const config = this.obtenerRemisionesConfiguracionPorTipo(tipoMovimiento);

        const errores = this.validarRemisionesFormulario();
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
            const formData = this.recopilarRemisionesDatosFormulario();
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
                        confirmButtonText: 'Aceptar',
                        allowOutsideClick: false,
                        allowEscapeKey: false
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
            console.error('Error enviando remisión:', error);
            if (window.toastMixin) {
                toastMixin.fire({
                    icon: 'error',
                    title: error.message || 'Ocurrió un error al enviar la remisión'
                });
            }
        } finally {
            this.btnSubmit.disabled = false;
            this.btnSubmit.innerHTML = originalText;
        }
    }

    limpiarRemisionesFormulario() {
        this.form?.reset();
        this.productManager.productos = [];
        this.productManager.renderRemisionesTable();
        this.productManager.calcularRemisionesTotales();
        document.getElementById('vin-rem-cliente').value = '';
        document.getElementById('vin-rem-rfc').value = '';
        document.getElementById('vin-rem-info-proveedor').value = '';
    }

    verRemisionesDatosFormulario() {
        const formData = this.recopilarRemisionesDatosFormulario();
        const obj = {};
        for (let [key, value] of formData.entries()) obj[key] = value;
        console.log('Datos de remisión a enviar:', obj);
        return obj;
    }
}

// ============================================
// 6. APLICACIÓN PRINCIPAL
// ============================================
const RemisionesApp = {
    clienteManager: null,
    productoManager: null,
    documentoManager: null,
    productManagerInstance: null,
    formManager: null,
    form: null,
    btnSubmit: null,

    async initRemisiones() {
        this.form = document.getElementById('vin-rem-formCotizacion');
        this.btnSubmit = document.querySelector('.vin-rem-btn-submit');

        this.clienteManager = new RemisionesDataManager({
            endpoint: '/DatosGenerales/BuscarC',
            detailEndpoint: '/DatosGenerales/BuscarCliente',
            modalId: 'vin-rem-modalBuscarCliente',
            inputId: 'vin-rem-inputBuscarCliente',
            resultsId: 'vin-rem-listaResultadosClientes',
            pageSizeId: 'vin-rem-pageSizeClientes',
            btnClearId: 'vin-rem-btnLimpiarClientes',
            spinnerId: 'vin-rem-spinnerClientes',
            paginationId: 'vin-rem-paginationClientes',
            recordsFromId: 'vin-rem-recordsFromClientes',
            recordsToId: 'vin-rem-recordsToClientes',
            totalRecordsId: 'vin-rem-totalRecordsClientes',
            shouldCloseOnSelect: true
        });

        this.clienteManager.onRemisionesSelect = (cliente) => {
            document.getElementById('vin-rem-cliente').value = cliente.id || '';
            document.getElementById('vin-rem-rfc').value = cliente.rfc || '';
            document.getElementById('vin-rem-info-proveedor').value =
                [cliente.dir, cliente.col, cliente.pob, cliente.cp]
                    .filter(x => x)
                    .join(',\n');

            const vendedorInstance = tomManager.getInstance('vin-rem-vendedor');
            if (vendedorInstance && cliente.cve_vdr) {
                vendedorInstance.setValue(cliente.cve_vdr, true);
            }
            const fpagoInstance = tomManager.getInstance('vin-rem-forma-pago');
            if (fpagoInstance && cliente.forma_pago) {
                fpagoInstance.setValue(cliente.forma_pago, true);
            }
            const cfdiInstance = tomManager.getInstance('vin-rem-uso-cfdi');
            if (cfdiInstance && cliente.uso_sugerido) {
                cfdiInstance.setValue(cliente.uso_sugerido, true);
            }
        };

        this.productoManager = new RemisionesDataManager({
            endpoint: '/DatosGenerales/BuscarP',
            detailEndpoint: '/DatosGenerales/BuscarProducto',
            modalId: 'vin-rem-modalBuscarProducto',
            inputId: 'vin-rem-inputBuscarProducto',
            resultsId: 'vin-rem-listaResultadosProductos',
            pageSizeId: 'vin-rem-pageSizeProductos',
            btnClearId: 'vin-rem-btnLimpiarProductos',
            spinnerId: 'vin-rem-spinnerProductos',
            paginationId: 'vin-rem-paginationProductos',
            recordsFromId: 'vin-rem-recordsFromProductos',
            recordsToId: 'vin-rem-recordsToProductos',
            totalRecordsId: 'vin-rem-totalRecordsProductos',
            shouldCloseOnSelect: false
        });

        await this.cargarPermisosUsuario();
        this.productManagerInstance = new RemisionesProductManager();

        this.productoManager.onRemisionesSelect = (producto) => {
            this.productManagerInstance.agregarRemisionesProducto(producto);
        };

        this.documentoManager = new RemisionesDocumentoManager({
            endpoint: '/DatosGenerales/BuscarDVINped',
            detailEndpoint: '/DatosGenerales/BuscarDocumentoEspecialInternacional',
            modalId: 'vin-rem-modalBuscarDocumentos',
            inputId: 'vin-rem-inputBuscarDocumento',
            resultsId: 'vin-rem-listaResultadosDocumentos',
            pageSizeId: 'vin-rem-pageSizeDocumentos',
            btnClearId: 'vin-rem-btnLimpiarDocumentos',
            spinnerId: 'vin-rem-spinnerDocumentos',
            paginationId: 'vin-rem-paginationDocumentos',
            recordsFromId: 'vin-rem-recordsFromDocumentos',
            recordsToId: 'vin-rem-recordsToDocumentos',
            totalRecordsId: 'vin-rem-totalRecordsDocumentos',
            shouldCloseOnSelect: true,
            filtros: {}
        });

        const filtroEstado = document.getElementById('vin-rem-filtroEstado');
        const filtroFecha = document.getElementById('vin-rem-filtroFecha');

        filtroEstado?.addEventListener('change', (e) => {
            this.documentoManager.filtros.estado = e.target.value;
            this.documentoManager.state.page = 1;
            this.documentoManager.fetchRemisionesData();
        });

        filtroFecha?.addEventListener('change', (e) => {
            this.documentoManager.filtros.fecha = e.target.value;
            this.documentoManager.state.page = 1;
            this.documentoManager.fetchRemisionesData();
        });

        this.documentoManager.onRemisionesSelect = async (documento) => {
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

        this.formManager = new RemisionesFormManager(this.productManagerInstance);

        const tipoMovSelect = document.getElementById('vin-rem-tipo-docto-mov');
        tipoMovSelect?.addEventListener('change', (e) => {
            this.manejarRemisionesCambioTipoMovimiento(e.target.value);
        });

        this.manejarRemisionesCambioTipoMovimiento(tipoMovSelect?.value || 'alta');

        console.log('Sistema de remisiones inicializado correctamente');
        window.verRemisionesDatosFormulario = () => this.formManager.verRemisionesDatosFormulario();
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
        document.getElementById('vin-rem-sucursal').value = documento.sucursal || '';
        document.getElementById('vin-rem-almacen').value = documento.almacen || '';
        document.getElementById('vin-rem-documentid').value = documento.id_encabezado || '';

        document.getElementById('vin-rem-cliente').value = documento.cli_prov || '';
        document.getElementById('vin-rem-rfc').value = documento.rfc || '';
        document.getElementById('vin-rem-info-proveedor').value = documento.info_cli || '';
        document.getElementById('vin-rem-paridad').value = documento.par || '';
        document.getElementById('vin-rem-metodo-pago').value = documento.mdp || '';

        const vendedorInstance = tomManager.getInstance('vin-rem-vendedor');
        if (vendedorInstance && (documento.vdr_cpr || documento.cve_vdr)) {
            vendedorInstance.setValue(documento.vdr_cpr || documento.cve_vdr, true);
        }

        const monedaInstance = tomManager.getInstance('vin-rem-moneda');
        if (monedaInstance && (documento.ccy || documento.moneda)) {
            monedaInstance.setValue(documento.ccy || documento.moneda, true);
        }

        const cfdiInstance = tomManager.getInstance('vin-rem-uso-cfdi');
        if (cfdiInstance && (documento.usoCfdi || documento.uso_cfdi || documento.cfdi)) {
            cfdiInstance.setValue(documento.usoCfdi || documento.uso_cfdi || documento.cfdi, true);
        }

        const formaPagoInstance = tomManager.getInstance('vin-rem-forma-pago');
        if (formaPagoInstance && (documento.formaPago || documento.forma_pago || documento.f_pago)) {
            formaPagoInstance.setValue(documento.f_pago, true);
        }

        //const metodoPagoInstance = tomManager.getInstance('vin-rem-metodo-pago');
        //if (metodoPagoInstance && (documento.metodoPago || documento.metodo_pago || documento.mdp)) {
        //    metodoPagoInstance.setValue(documento.metodoPago || documento.metodo_pago || documento.mdp, true);
        //}

        const sucursalInstance = tomManager.getInstance('vin-rem-sucursal');
        if (sucursalInstance && documento.suc) {
            sucursalInstance.setValue(documento.suc, false);
        }

        const almacenInstance = tomManager.getInstance('vin-rem-almacen');
        if (almacenInstance && documento.alm) {
            almacenInstance.setValue(documento.alm, true);
        }
        document.getElementById('vin-rem-metodo-pago').value = documento.mdp || '';
        const tipoPagoDesdeDoc = documento.mdp === 'PPD' ? 'credito' : 'contado';
        selectTipoPagoVinRem(tipoPagoDesdeDoc);

        document.getElementById('vin-rem-limite-credito').value = documento.lim_crd || '';
        document.getElementById('vin-rem-plazo').value = documento.pl_crd || '';
        document.getElementById('vin-rem-fecha-pago').value = documento.fechaPago || '';
        document.getElementById('vin-rem-concepto').value = documento.coment1 || '';
        document.getElementById('vin-rem-comentarios').value = documento.coment_aut || '';

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
                    precioOriginal: precio,
                    descuento: parseFloat(prod.descuento) || 0,
                    unidad: prod.unidad || prod.udm || 'PZA',
                    existenciaGeneral: prod.existenciaGeneral || 0,
                    existenciaModular: prod.existenciaModular || 0,
                    comentario: prod.comentario || ''
                };
                this.productManagerInstance.productos.push(productoData);
            });

            this.productManagerInstance.renderRemisionesTable();
        }

        const fleteInput = document.getElementById('vin-rem-flete-val');
        if (fleteInput && documento.flete) {
            const chkFlete = document.getElementById('vin-rem-flete');
            if (chkFlete) {
                chkFlete.checked = true;
                chkFlete.dispatchEvent(new Event('change'));
            }
            fleteInput.value = documento.flete;
        }

        const ordenCompraInput = document.getElementById('vin-rem-orden-compra-val');
        if (ordenCompraInput && documento.ordencompra) {
            const chkOrdenCompra = document.getElementById('vin-rem-orden-compra-check');
            if (chkOrdenCompra) {
                chkOrdenCompra.checked = true;
                chkOrdenCompra.dispatchEvent(new Event('change'));
            }
            ordenCompraInput.value = documento.ordencompra;
        }

        this.productManagerInstance.calcularRemisionesTotales();

        const tipoMovSelect = document.getElementById('vin-rem-tipo-docto-mov');
        if (tipoMovSelect && documento.folio) {
            tipoMovSelect.value = 'modificacion';
            this.manejarRemisionesCambioTipoMovimiento('modificacion');
        }

        this.productManagerInstance.calcularRemisionesTotales();
    },

    manejarRemisionesCambioTipoMovimiento(tipoMovimiento) {
        if (tipoMovimiento === 'consulta') {
            this.deshabilitarRemisionesFormulario();
        } else {
            this.habilitarRemisionesFormulario();
        }
    },

    deshabilitarRemisionesFormulario() {
        const remisionContenedor = document.getElementById('remision-contenedor');
        if (!remisionContenedor) return;

        remisionContenedor.querySelectorAll('input, select, textarea, button').forEach(element => {
            if (element.id !== 'vin-rem-tipo-docto-mov') {
                element.disabled = true;
                element.classList.add('vin-rem-disabled-field');
            }
        });

        const btnBuscarCliente = remisionContenedor.querySelector('[data-bs-target="#vin-rem-modalBuscarCliente"]');
        const btnBuscarProducto = remisionContenedor.querySelector('[data-bs-target="#vin-rem-modalBuscarProducto"]');
        if (btnBuscarCliente) btnBuscarCliente.disabled = true;
        if (btnBuscarProducto) btnBuscarProducto.disabled = true;

        remisionContenedor.querySelectorAll('.vin-rem-btn-eliminar').forEach(btn => btn.disabled = true);

        if (window.tomManager) {
            ['vin-rem-vendedor', 'vin-rem-moneda', 'vin-rem-uso-cfdi', 'vin-rem-forma-pago'].forEach(id => {
                const instance = window.tomManager.instances?.get(id);
                if (instance) instance.disable();
            });
        }

        const btn = remisionContenedor.querySelector('.vin-rem-btn-submit');
        if (btn) btn.disabled = false;

        const btnDocConsulta = remisionContenedor.querySelector('.vin-rem-floating-btn');
        if (btnDocConsulta) btnDocConsulta.disabled = false;

        const btnPreview = remisionContenedor.querySelector('.vin-rem-ticket-btn');
        if (btnPreview) btnPreview.disabled = false;
    },

    habilitarRemisionesFormulario() {
        const remisionContenedor = document.getElementById('remision-contenedor');
        if (!remisionContenedor) return;

        remisionContenedor.querySelectorAll('input, select, textarea, button').forEach(element => {
            element.disabled = false;
            element.classList.remove('vin-rem-disabled-field');
        });

        const btnBuscarCliente = remisionContenedor.querySelector('[data-bs-target="#vin-rem-modalBuscarCliente"]');
        const btnBuscarProducto = remisionContenedor.querySelector('[data-bs-target="#vin-rem-modalBuscarProducto"]');
        if (btnBuscarCliente) btnBuscarCliente.disabled = false;
        if (btnBuscarProducto) btnBuscarProducto.disabled = false;

        if (window.tomManager) {
            ['vin-rem-vendedor', 'vin-rem-moneda', 'vin-rem-uso-cfdi', 'vin-rem-forma-pago'].forEach(id => {
                const instance = window.tomManager.instances?.get(id);
                if (instance) instance.enable();
            });
        }

        const tipoMovimiento = document.getElementById('vin-rem-tipo-docto-mov')?.value;
        const btn = remisionContenedor.querySelector('.vin-rem-btn-submit');
        if (btn) {
            btn.innerHTML = tipoMovimiento === 'modificacion'
                ? '<i class="fas fa-save"></i> Actualizar Remisión'
                : '<i class="fas fa-save"></i> Guardar Remisión';
        }

        this.productManagerInstance?.renderRemisionesTable();
    },


    // ============================================
    // VALIDACIÓN DE FRACCIONES ARANCELARIAS
    // ============================================
    async validarFraccionesArancelarias() {
        const productos = this.productManagerInstance?.getRemisionesProductosData() || [];

        if (productos.length === 0) {
            if (window.toastMixin) {
                toastMixin.fire({ icon: 'warning', title: 'Agrega al menos un producto antes de validar' });
            }
            return;
        }

        const btn = document.getElementById('vin-rem-btn-validar-fracciones');
        const originalHtml = btn.innerHTML;
        btn.disabled = true;
        btn.innerHTML = `<i class="fas fa-spinner fa-spin"></i> Validando...`;

        try {
            const ids = productos.map(p => p.productoId);
            const formData = new FormData();
            formData.append('productosJSON', JSON.stringify(ids));

            const csrfToken = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
            if (csrfToken) formData.append('__RequestVerificationToken', csrfToken);

            const response = await fetch('/VINRemision/ValidarFraccionesArancelarias', {
                method: 'POST',
                body: formData
            });

            if (!response.ok) throw new Error('Error en la respuesta del servidor');

            const result = await response.json();

            const todoOk = result.resultados.every(r => r.tieneFraccion);
            const sinFraccion = result.resultados.filter(r => !r.tieneFraccion);

            // ── Tabla de resultados ────────────────────────────────────────────
            const filas = result.resultados.map(r => {
                const icono = r.tieneFraccion
                    ? `<i class="fas fa-check-circle" style="color:var(--vi-green-600)"></i>`
                    : `<i class="fas fa-times-circle" style="color:var(--vi-red-600)"></i>`;

                const fraccion = r.tieneFraccion
                    ? `<code style="font-size:.8rem;background:var(--vi-gray-100);
                          padding:2px 6px;border-radius:4px;">${r.fraccion}</code>`
                    : `<span style="color:var(--vi-red-600);font-weight:600;font-size:.8rem;">
                       Sin fracción
                   </span>`;

                const rowBg = r.tieneFraccion ? '' : 'style="background:var(--vi-red-50);"';

                return `
            <tr ${rowBg}>
                <td style="padding:.5rem .75rem;font-weight:600;font-size:.82rem;
                           color:var(--vi-gray-800);">${r.productoId}</td>
                <td style="padding:.5rem .75rem;font-size:.82rem;color:var(--vi-gray-600);
                           max-width:220px;white-space:nowrap;overflow:hidden;
                           text-overflow:ellipsis;" title="${r.descripcion}">${r.descripcion}</td>
                <td style="padding:.5rem .75rem;text-align:center;">${fraccion}</td>
                <td style="padding:.5rem .75rem;text-align:center;font-size:1.1rem;">${icono}</td>
            </tr>`;
            }).join('');

            // ── Banner resumen ─────────────────────────────────────────────────
            const resumenHtml = todoOk
                ? `<div style="background:var(--vi-green-50);border:1px solid var(--vi-green-600);
                       border-radius:8px;padding:.75rem 1rem;margin-bottom:1rem;
                       display:flex;align-items:center;gap:.5rem;color:var(--vi-green-700);">
                   <i class="fas fa-check-circle"></i>
                   <strong>Todos los productos tienen fracción arancelaria asignada.</strong>
               </div>`
                : `<div style="background:var(--vi-red-50);border:1px solid var(--vi-red-600);
                       border-radius:8px;padding:.75rem 1rem;margin-bottom:1rem;
                       display:flex;align-items:center;gap:.5rem;color:var(--vi-red-600);">
                   <i class="fas fa-exclamation-triangle"></i>
                   <strong>${sinFraccion.length} producto(s) sin fracción arancelaria asignada.</strong>
               </div>`;

            // ── Sección de alerta por correo (solo si hay productos sin fracción) ──
            const alertaEmailHtml = todoOk ? '' : `
            <hr style="margin:.75rem 0;border:none;border-top:1px dashed var(--vi-gray-200);">
            <div id="swal-fraccion-email-section"
                 style="background:var(--vi-amber-50);border:1px solid var(--vi-amber-600);
                        border-radius:8px;padding:.85rem 1rem;">

                <!-- Toggle para mostrar/ocultar el formulario de correo -->
                <div style="display:flex;align-items:center;justify-content:space-between;
                            cursor:pointer;" onclick="toggleFraccionEmailForm()">
                    <div style="display:flex;align-items:center;gap:.5rem;
                                color:var(--vi-amber-600);font-weight:600;font-size:.88rem;">
                        <i class="fas fa-envelope"></i>
                        Enviar alerta por correo a los responsables
                    </div>
                    <i id="swal-fraccion-chevron" class="fas fa-chevron-down"
                       style="color:var(--vi-amber-600);transition:transform .25s;"></i>
                </div>

                <!-- Formulario colapsable -->
                <div id="swal-fraccion-email-form"
                     style="display:none;margin-top:.75rem;">

                    <label style="font-size:.78rem;font-weight:600;
                                  color:var(--vi-gray-600);margin-bottom:.3rem;display:block;">
                        <i class="fas fa-at" style="color:#3b82f6;margin-right:.25rem;"></i>
                        Destinatarios <small style="font-weight:400;">(separados por coma o punto y coma)</small>
                    </label>
                    <textarea id="swal-fraccion-emails"
                              rows="2"
                              placeholder="ej: almacen@empresa.com; compras@empresa.com"
                              style="width:100%;border:1.5px solid var(--vi-gray-200);
                                     border-radius:8px;padding:.55rem .75rem;
                                     font-size:.85rem;resize:vertical;
                                     background:var(--bg-light,#fff);
                                     color:var(--vi-gray-800);
                                     box-sizing:border-box;margin-bottom:.5rem;"></textarea>

                    <!-- Lista de productos que se incluirán en el correo -->
                    <div style="background:var(--bg-light,#fff);border:1px solid var(--vi-gray-200);
                                border-radius:6px;padding:.5rem .75rem;margin-bottom:.65rem;
                                font-size:.78rem;color:var(--vi-gray-600);">
                        <div style="font-weight:700;margin-bottom:.3rem;color:var(--vi-gray-800);">
                            <i class="fas fa-list-ul me-1"></i>
                            Productos que se reportarán (${sinFraccion.length}):
                        </div>
                        ${sinFraccion.map(p => `
                            <div style="display:flex;align-items:center;gap:.4rem;
                                        padding:.2rem 0;border-bottom:1px solid var(--vi-gray-200);">
                                <i class="fas fa-times-circle" style="color:var(--vi-red-600);
                                          font-size:.75rem;flex-shrink:0;"></i>
                                <strong>${p.productoId}</strong>
                                <span style="color:var(--vi-gray-400);">—</span>
                                <span style="white-space:nowrap;overflow:hidden;
                                             text-overflow:ellipsis;">${p.descripcion}</span>
                            </div>`).join('')}
                    </div>

                    <button type="button"
                            id="swal-fraccion-btn-enviar"
                            onclick="enviarAlertaFraccionEmail()"
                            style="background:var(--vi-amber-600);color:#fff;border:none;
                                   border-radius:8px;padding:.5rem 1.25rem;font-weight:600;
                                   font-size:.85rem;cursor:pointer;display:inline-flex;
                                   align-items:center;gap:.4rem;transition:background .2s;">
                        <i class="fas fa-paper-plane"></i> Enviar alerta
                    </button>
                    <span id="swal-fraccion-email-feedback"
                          style="font-size:.8rem;margin-left:.75rem;"></span>
                </div>
            </div>`;

            // ── Guardar resultados en window para que los helpers los accedan ──
            window._fraccionResultados = result.resultados;
            window._fraccionSinFraccion = sinFraccion;
            window._fraccionFolioActual = document.getElementById('vin-rem-folio')?.value || 'Sin folio';
            window._fraccionClienteActual = document.getElementById('vin-rem-cliente')?.value || 'Sin cliente';

            await Swal.fire({
                title: '<i class="fas fa-shield-alt me-2"></i>Validación de Fracciones Arancelarias',
                html: `
                ${resumenHtml}
                <div style="overflow-x:auto;border-radius:8px;
                            border:1px solid var(--vi-gray-200);
                            max-height:320px;overflow-y:auto;margin-bottom:.5rem;">
                    <table style="width:100%;border-collapse:collapse;font-family:inherit;">
                        <thead>
                            <tr style="background:var(--vi-blue-600);color:#fff;
                                       position:sticky;top:0;z-index:1;">
                                <th style="padding:.55rem .75rem;text-align:left;
                                           font-size:.78rem;white-space:nowrap;">Artículo</th>
                                <th style="padding:.55rem .75rem;text-align:left;
                                           font-size:.78rem;">Descripción</th>
                                <th style="padding:.55rem .75rem;text-align:center;
                                           font-size:.78rem;white-space:nowrap;">Fracción</th>
                                <th style="padding:.55rem .75rem;text-align:center;
                                           font-size:.78rem;">Estado</th>
                            </tr>
                        </thead>
                        <tbody>${filas}</tbody>
                    </table>
                </div>
                ${alertaEmailHtml}`,
                width: 700,
                confirmButtonText: todoOk
                    ? '<i class="fas fa-check me-1"></i> Aceptar'
                    : '<i class="fas fa-times me-1"></i> Cerrar',
                confirmButtonColor: todoOk ? '#059669' : '#dc2626',
                customClass: { htmlContainer: 'text-start' }
            });

        } catch (error) {
            console.error('Error validando fracciones arancelarias:', error);
            if (window.toastMixin) {
                toastMixin.fire({ icon: 'error', title: error.message || 'Error al validar fracciones arancelarias' });
            }
        } finally {
            btn.disabled = false;
            btn.innerHTML = originalHtml;
        }
    },
};
// ============================================
// HELPERS PARA ALERTA DE FRACCIÓN (fuera de RemisionesApp)
// ============================================

/** Despliega / colapsa el formulario de correo dentro del SweetAlert */
function toggleFraccionEmailForm() {
    const form = document.getElementById('swal-fraccion-email-form');
    const chevron = document.getElementById('swal-fraccion-chevron');
    const visible = form.style.display !== 'none';

    form.style.display = visible ? 'none' : 'block';
    chevron.style.transform = visible ? '' : 'rotate(180deg)';
}

/** Envía la alerta al endpoint EnviarAlertaFraccion */
async function enviarAlertaFraccionEmail() {
    const emailsInput = document.getElementById('swal-fraccion-emails');
    const btnEnviar = document.getElementById('swal-fraccion-btn-enviar');
    const feedback = document.getElementById('swal-fraccion-email-feedback');

    const emails = emailsInput?.value?.trim();
    if (!emails) {
        feedback.style.color = 'var(--vi-red-600)';
        feedback.textContent = 'Ingresa al menos un correo destinatario.';
        emailsInput.focus();
        return;
    }

    // Estado de carga
    const originalHtml = btnEnviar.innerHTML;
    btnEnviar.disabled = true;
    btnEnviar.innerHTML = `<i class="fas fa-spinner fa-spin"></i> Enviando...`;
    feedback.textContent = '';

    try {
        const formData = new FormData();
        formData.append('emails', emails);
        formData.append('resultadosJson', JSON.stringify(window._fraccionSinFraccion));
        formData.append('folio', window._fraccionFolioActual || 'Sin folio');
        formData.append('cliente', window._fraccionClienteActual || 'Sin cliente');

        const csrfToken = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
        if (csrfToken) formData.append('__RequestVerificationToken', csrfToken);

        const response = await fetch('/VINRemision/EnviarAlertaFraccion', {
            method: 'POST',
            body: formData
        });

        const result = await response.json();

        if (result.success) {
            feedback.style.color = 'var(--vi-green-600)';
            feedback.innerHTML = `<i class="fas fa-check-circle me-1"></i>${result.message}`;
            emailsInput.value = '';

            // Toast de confirmación adicional
            if (window.toastMixin) {
                toastMixin.fire({ icon: 'success', title: result.message });
            }
        } else {
            feedback.style.color = 'var(--vi-red-600)';
            feedback.innerHTML = `<i class="fas fa-exclamation-circle me-1"></i>${result.message}`;
        }
    } catch (error) {
        console.error('Error enviando alerta de fracción:', error);
        feedback.style.color = 'var(--vi-red-600)';
        feedback.textContent = 'Error de conexión. Intenta de nuevo.';
    } finally {
        btnEnviar.disabled = false;
        btnEnviar.innerHTML = originalHtml;
    }
}
// ============================================
// 7. ARRANQUE
// ============================================
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => {
        RemisionesApp.initRemisiones();
    });
} else {
    RemisionesApp.initRemisiones();
}
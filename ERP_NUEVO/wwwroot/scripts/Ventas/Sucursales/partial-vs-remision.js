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
            iva: 0,
            total: 0
        };

        this.dom = {
            table: document.getElementById('vs-rem-productosTable'),
            thead: document.querySelector('#vs-rem-productosTable')?.closest('table')?.querySelector('thead'),
            subtotal1Display: document.getElementById('vs-rem-total-subtotal1-display'),
            descuentoDisplay: document.getElementById('vs-rem-total-descuento-display'),
            fleteDisplay: document.getElementById('vs-rem-total-flete-display'),
            subtotal2Display: document.getElementById('vs-rem-total-subtotal2-display'),
            ivaDisplay: document.getElementById('vs-rem-iva-display'),
            importeDisplay: document.getElementById('vs-rem-importe-display')
        };

        this.initRemisionesProductManager();
    }

    initRemisionesProductManager() {
        this.dom.table?.addEventListener('input', (e) => {
            const input = e.target;
            if (input.classList.contains('vs-rem-cantidad') ||
                input.classList.contains('vs-rem-precio') ||
                input.classList.contains('vs-rem-descuento') ||
                input.classList.contains('vs-rem-comentario')) {
                const tr = input.closest('tr');
                const productoId = tr?.dataset.productoId;
                if (productoId) this.actualizarRemisionesFila(productoId);
            }
        });

        this.dom.table?.addEventListener('click', (e) => {
            // 1. Eliminar producto
            const btnEliminar = e.target.closest('.vs-rem-btn-eliminar');
            if (btnEliminar) {
                const productoId = btnEliminar.closest('tr')?.dataset.productoId;
                if (productoId) this.eliminarRemisionesProducto(productoId);
                return;
            }

            // 2. Click en OVERLAY de campo protegido
            const overlay = e.target.closest('.vs-rem-field-overlay');
            if (overlay) {
                pedirRemisionesAutenticacionGlobal(overlay.dataset.tipo);
                return;
            }
        });

        document.getElementById('vs-rem-flete-val')?.addEventListener('input', () => {
            this.calcularRemisionesTotales();
        });

        document.getElementById('vs-rem-monto-anticipo')?.addEventListener('input', () => {
            this.calcularRemisionesTotales();
        });
    }

    agregarRemisionesProducto(producto) {
        const productoExistente = this.productos.find(p => p.productoId === producto.id);

        if (productoExistente) {
            productoExistente.cantidad += 1;

            const tr = this.dom.table.querySelector(`tr[data-producto-id="${productoExistente.id}"]`);
            if (tr) {
                const inputCantidad = tr.querySelector('.vs-rem-cantidad');
                inputCantidad.value = productoExistente.cantidad;

                const importe = this.calcularRemisionesImporte(productoExistente);
                tr.querySelector('.vs-rem-importe').textContent = importe.toFixed(2);

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
            const productoData = Object.assign({
                id: `prod_${Date.now()}_${Math.random()}`,
                productoId: producto.id,
                descripcion: producto.descripcion,
                existencia: producto.existencia,
                cantidad: 1,
                unidad: producto.udm || 'PZA',
                existenciaGeneral: producto.existenciaGeneral || 0,
                existenciaModular: producto.existenciaModular || 0,
                comentario: ''
            },
            // precio, precioMinimo, descuento sugerido y tope salen de la regla vigente:
            // antes solo se leía 'precio' y el descuento arrancaba siempre en 0.
            ReglasPrecio.camposDeRegla(producto));

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
            <tr class="vs-rem-empty-state">
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
            precioInput.className = 'vs-rem-product-input vs-rem-precio';
            precioInput.value = p.precio.toFixed(2);
            // El piso viene de la regla; 0 = sin piso (precio libre, solo no negativo).
            precioInput.min = (p.precioMinimo || 0).toFixed(2);
            precioInput.step = '0.01';
            precioInput.dataset.precioOriginal = p.precioOriginal.toFixed(2);
            precioInput.dataset.precioMinimo = (p.precioMinimo || 0).toFixed(2);

            if (!precioDesbloqueado) {
                precioInput.disabled = true;
                const overlayPrecio = document.createElement('div');
                overlayPrecio.className = 'vs-rem-field-overlay';
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
            descInput.className = 'vs-rem-product-input vs-rem-descuento';
            descInput.value = p.descuento;
            descInput.min = '0';
            // Tope de la regla: en PRECIO_FIJO es 0 y el campo queda efectivamente fijo.
            descInput.max = (p.descuentoMaximo != null ? p.descuentoMaximo : 100).toFixed(2);
            descInput.step = '0.01';
            descInput.dataset.descMaximo = descInput.max;

            if (!descDesbloqueado) {
                descInput.disabled = true;
                const overlayDesc = document.createElement('div');
                overlayDesc.className = 'vs-rem-field-overlay';
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
                <input type="number" class="vs-rem-product-input vs-rem-cantidad"
                       value="${p.cantidad}" min="0" step="0.01">
            </td>
            <td>${this.escapeRemisionesHtml(p.unidad)}</td>`;

            tr.appendChild(precioCell);
            tr.appendChild(descCell);

            const resto = document.createElement('template');
            resto.innerHTML = `
            <td class="vs-rem-importe fw-bold" style="display:none;">${importe.toFixed(2)}</td>
            <td>
                <input type="text" class="vs-rem-product-input vs-rem-comentario"
                       value="${this.escapeRemisionesHtml(p.comentario)}" placeholder="Comentario...">
            </td>
            <td>
                <button type="button" class="vs-rem-btn-delete vs-rem-btn-eliminar">
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

        producto.cantidad = parseFloat(tr.querySelector('.vs-rem-cantidad').value) || 0;
        producto.comentario = tr.querySelector('.vs-rem-comentario')?.value || '';

        // ── Precio y descuento contra la regla vigente ────────────────────────
        // Mismas comprobaciones que la cotización, vía el módulo compartido: piso solo
        // cuando la regla lo define, nunca negativos y el tope de descuento de la regla.
        const precioInput = tr.querySelector('.vs-rem-precio');
        if (precioInput && !precioInput.disabled) {
            const res = ReglasPrecio.validarPrecio(
                producto,
                parseFloat(precioInput.value) || 0,
                !!remisionesAuthState.precioToken);

            producto.precio = res.valor;
            if (res.aviso) precioInput.value = res.valor.toFixed(2);
            ReglasPrecio.avisar(res.aviso);
        }

        const descInput = tr.querySelector('.vs-rem-descuento');
        if (descInput && !descInput.disabled) {
            const res = ReglasPrecio.validarDescuento(
                producto,
                parseFloat(descInput.value) || 0,
                !!remisionesAuthState.descuentoToken);

            producto.descuento = res.valor;
            if (res.aviso) descInput.value = res.valor.toFixed(2);
            ReglasPrecio.avisar(res.aviso);
        }

        // PRECIO_FIJO: el piso es sobre el neto, no sobre el unitario.
        const piso = ReglasPrecio.ajustarPorPisoNeto(producto);
        if (piso.aviso) {
            producto.descuento = piso.descuento;
            if (descInput) descInput.value = piso.descuento.toFixed(2);
            ReglasPrecio.avisar(piso.aviso);
        }

        tr.querySelector('.vs-rem-importe').textContent = this.calcularRemisionesImporte(producto).toFixed(2);
        this.calcularRemisionesTotales();
    }

    eliminarRemisionesProducto(productoId) {
        this.productos = this.productos.filter(p => p.id !== productoId);
        this.renderRemisionesTable();
        this.calcularRemisionesTotales();
    }

    desbloquearRemisionesCampos(tipo) {
        const selector = tipo === 'descuento' ? '.vs-rem-descuento' : '.vs-rem-precio';

        this.dom.table?.querySelectorAll(selector).forEach(input => {
            input.removeAttribute('disabled');

            const overlay = input.parentElement?.querySelector('.vs-rem-field-overlay');
            overlay?.remove();
        });
    }

    calcularRemisionesImporte(producto) {
        return producto.cantidad * producto.precio * (1 - producto.descuento / 100);
    }

    calcularRemisionesTotales() {
        const tipoPago = document.getElementById('vs-rem-toggle-pago')?.getAttribute('data-state') || 'contado';
        const flete = parseFloat(document.getElementById('vs-rem-flete-val')?.value) || 0;
        let subtotal1 = 0, descuento = 0, subtotal2 = 0, iva = 0, total = 0;

        if (tipoPago === 'anticipo') {
            const montoAnticipo = parseFloat(document.getElementById('vs-rem-monto-anticipo')?.value) || 0;
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
            iva = subtotal1 * 0.16;
            total = subtotal2 + iva;
        }

        this.totales = { subtotal1, descuento, flete, subtotal2, iva, total };

        this.updateRemisionesDisplay(this.dom.subtotal1Display, subtotal1);
        this.updateRemisionesDisplay(this.dom.descuentoDisplay, descuento, '-$');
        this.updateRemisionesDisplay(this.dom.fleteDisplay, flete);
        this.updateRemisionesDisplay(this.dom.subtotal2Display, subtotal2);
        this.updateRemisionesDisplay(this.dom.ivaDisplay, iva);
        this.updateRemisionesDisplay(this.dom.importeDisplay, total);

        this.updateRemisionesHiddenField('vs-rem-total-subtotal1', subtotal1);
        this.updateRemisionesHiddenField('vs-rem-total-descuento', descuento);
        this.updateRemisionesHiddenField('vs-rem-total-flete', flete);
        this.updateRemisionesHiddenField('vs-rem-total-subtotal2', subtotal2);
        this.updateRemisionesHiddenField('vs-rem-iva', iva);
        this.updateRemisionesHiddenField('vs-rem-importe', total);

        window.CreditoVentas?.evaluar('vs-rem');
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
        this.form = document.getElementById('vs-rem-formCotizacion');
        this.btnSubmit = document.querySelector('.vs-rem-btn-submit');
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
        const toggle = document.getElementById('vs-rem-toggle-pago');
        const tipoPago = toggle?.getAttribute('data-state') || 'contado';

        const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
        if (token) formData.append('__RequestVerificationToken', token);

        const camposGenerales = [
            'vs-rem-tipo-docto-mov',
            'vs-rem-folio', 'vs-rem-documentid', 'vs-rem-cliente',
            'vs-rem-rfc', 'vs-rem-vendedor', 'vs-rem-moneda',
            'vs-rem-paridad', 'vs-rem-concepto',
            'vs-rem-metodo-pago', 'vs-rem-forma-pago',
            'vs-rem-uso-cfdi', 'vs-rem-comentarios'
        ];

        camposGenerales.forEach(id => {
            const el = document.getElementById(id);
            if (el) formData.append(id.replace('vs-rem-', ''), el.value || '');
        });

        formData.set('sucursal', tomManager.getInstance('vs-rem-sucursal')?.getValue() || '');
        formData.set('almacen', tomManager.getInstance('vs-rem-almacen')?.getValue() || '');

        // Las condiciones de pago se envían siempre, aunque la tarjeta esté oculta por ser
        // de contado: la factura las hereda de la remisión y si no viajan aquí se pierden.
        formData.append('limiteCredito', document.getElementById('vs-rem-limite-credito')?.value || '');
        formData.append('plazo', document.getElementById('vs-rem-plazo')?.value || '');
        formData.append('fechaPago', document.getElementById('vs-rem-fecha-pago')?.value || '');

        if (tipoPago === 'anticipo') {
            formData.append('fechaAnticipo', document.getElementById('vs-rem-fecha-anticipo')?.value || '');
        }

        formData.append('ordenCompra', document.getElementById('vs-rem-orden-compra-val')?.value || '');

        const productos = this.productManager.getRemisionesProductosData();
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
        // Identifica la autorización de gerencia cuando el documento aún no existe.
        formData.append('creditoToken', window.CreditoVentas?.token('vs-rem') || '');

        // Enviar tokens al servidor para validación
        formData.append('descuentoToken', remisionesAuthState.descuentoToken || '');
        formData.append('precioToken', remisionesAuthState.precioToken || '');

        return formData;
    }

    validarRemisionesFormulario() {
        const errores = [];

        const togglePago = document.getElementById('vs-rem-toggle-pago');
        const tipoPago = togglePago ? togglePago.getAttribute('data-state') : 'contado';

        const cliente = document.getElementById('vs-rem-cliente')?.value;
        if (!cliente || cliente.trim() === '') {
            errores.push('Debe seleccionar un cliente');
        }

        const productos = this.productManager.getRemisionesProductosData();
        if (tipoPago !== 'anticipo' && productos.length === 0) {
            errores.push('Debe agregar al menos un producto');
        }

        const vendedor = document.getElementById('vs-rem-vendedor')?.value;
        if (!vendedor || vendedor === 'Seleccionar vendedor') {
            errores.push('Debe seleccionar un vendedor');
        }

        const rfcInput = document.getElementById('vs-rem-rfc');
        const rfc = rfcInput ? rfcInput.value.trim() : '';
        if (!rfc) {
            errores.push('Debe ingresar el RFC');
        }

        const monedaSelect = tomManager.getInstance('vs-rem-moneda');
        const monedaValue = monedaSelect?.getValue();
        if (!monedaValue) {
            errores.push('Debe seleccionar una Moneda');
        }

        // Crédito: solo agrega errores cuando la venta es a crédito y excede el límite.
        errores.push(...(window.CreditoVentas?.validarAntesDeGuardar('vs-rem') || []));

        //const usoCfdiSelect = tomManager.getInstance('vs-rem-uso-cfdi');
        //const usoCfdiValue = usoCfdiSelect?.getValue();
        //if (!usoCfdiValue) {
        //    errores.push('Debe seleccionar un Uso CFDI');
        //}

        //const formaPagoSelect = tomManager.getInstance('vs-rem-forma-pago');
        //const formaPagoValue = formaPagoSelect?.getValue();
        //if (!formaPagoValue) {
        //    errores.push('Debe seleccionar una Forma de Pago');
        //}

        //const fechaPagoInput = document.getElementById('vs-rem-fecha-pago');
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
                endpoint: '/VSRemision/Guardar',
                metodo: 'POST',
                mensajeExito: 'Remisión creada exitosamente',
                textoCarga: 'Guardando...',
                validarProductos: true
            },
            'modificacion': {
                endpoint: '/VSRemision/Modificar',
                metodo: 'PUT',
                mensajeExito: 'Remisión modificada exitosamente',
                textoCarga: 'Actualizando...',
                validarProductos: true
            },
            'consulta': {
                endpoint: '/VSRemision/Consultar',
                metodo: 'GET',
                mensajeExito: 'Consulta realizada exitosamente',
                textoCarga: 'Consultando...',
                validarProductos: false
            }
        };

        return configuraciones[tipoMovimiento] || configuraciones['alta'];
    }

    async enviarRemisionesCotizacion() {
        const tipoMovimiento = document.getElementById('vs-rem-tipo-docto-mov')?.value || 'alta';
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
        document.getElementById('vs-rem-cliente').value = '';
        document.getElementById('vs-rem-rfc').value = '';
        document.getElementById('vs-rem-info-proveedor').value = '';
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
    // Impresión de etiquetas Zebra: no existe en Industriales, es del mostrador.
    etiquetaManager: null,
    clienteManager: null,
    productoManager: null,
    documentoManager: null,
    productManagerInstance: null,
    formManager: null,
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
     * En una remisión el cliente casi nunca llega por el modal: viene al cargar el
     * pedido de origen, y por esa vía solo se conoce la clave. Sin esto, agregar un
     * producto después consultaba el precio con clienteId = 0.
     */
    async asegurarClienteId() {
        const cve = document.getElementById('vs-rem-cliente')?.value || '';

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

        pm.renderRemisionesTable();
        pm.calcularRemisionesTotales();
        ReglasPrecio.avisar(
            `Precios recalculados con la lista de ${nombreCliente || 'el cliente'} `
            + `(${actualizadas} partida${actualizadas === 1 ? '' : 's'})`,
            'info');
    },

    async initRemisiones() {
        this.form = document.getElementById('vs-rem-formCotizacion');
        this.btnSubmit = document.querySelector('.vs-rem-btn-submit');

        // Crédito: la remisión ya entrega mercancía a crédito, así que bloquea.
        window.CreditoVentas?.registrar({
            prefix: 'vs-rem',
            modo: 'bloquear',
            documento: 'remision',
            // La solicitud va al controlador de sucursal: el de Industriales, al
            // aprobar, reconstruye un pedido VIPED desde la cotizacion de origen.
            endpointSolicitud: '/VSPedido/EnviarSolicitudGerente',
            getTipoPago: () =>
                document.getElementById('vs-rem-toggle-pago')?.getAttribute('data-state') || 'contado',
            totalId: 'vs-rem-importe',
            submitSelector: '.vs-rem-btn-submit',
            clienteInputId: 'vs-rem-cliente',
            folioId: 'vs-rem-folio',
            documentIdId: 'vs-rem-documentid',
            getProductos: () => RemisionesApp.productManagerInstance?.getRemisionesProductosData() || [],
            getTotales: () => RemisionesApp.productManagerInstance?.totales || {}
        });

        this.clienteManager = new RemisionesDataManager({
            endpoint: '/DatosGenerales/BuscarC',
            detailEndpoint: '/DatosGenerales/BuscarCliente',
            modalId: 'vs-rem-modalBuscarCliente',
            inputId: 'vs-rem-inputBuscarCliente',
            resultsId: 'vs-rem-listaResultadosClientes',
            pageSizeId: 'vs-rem-pageSizeClientes',
            btnClearId: 'vs-rem-btnLimpiarClientes',
            spinnerId: 'vs-rem-spinnerClientes',
            paginationId: 'vs-rem-paginationClientes',
            recordsFromId: 'vs-rem-recordsFromClientes',
            recordsToId: 'vs-rem-recordsToClientes',
            totalRecordsId: 'vs-rem-totalRecordsClientes',
            shouldCloseOnSelect: true
        });

        this.clienteManager.onRemisionesSelect = (cliente) => {
            document.getElementById('vs-rem-cliente').value = cliente.id || '';
            document.getElementById('vs-rem-rfc').value = cliente.rfc || '';
            document.getElementById('vs-rem-info-proveedor').value =
                [cliente.dir, cliente.col, cliente.pob, cliente.cp]
                    .filter(x => x)
                    .join(',\n');

            asignarVendedorSucursal('vs-rem-vendedor', cliente);
            const fpagoInstance = tomManager.getInstance('vs-rem-forma-pago');
            if (fpagoInstance && cliente.forma_pago) {
                fpagoInstance.setValue(cliente.forma_pago, true);
            }
            const cfdiInstance = tomManager.getInstance('vs-rem-uso-cfdi');
            if (cfdiInstance && cliente.uso_sugerido) {
                cfdiInstance.setValue(cliente.uso_sugerido, true);
            }
            window.CreditoVentas?.setCliente('vs-rem', cliente);

            // El cliente define la lista de precios y las reglas por cliente: se manda
            // explícito al buscar productos y se recalcula lo ya capturado.
            RemisionesApp.clienteIdActual = parseInt(cliente.id_cliente, 10) || 0;
            RemisionesApp.clienteCveResuelta = cliente.id || '';
            RemisionesApp.recotizarPartidas(cliente.descripcion);
        };

        this.productoManager = new RemisionesDataManager({
            endpoint: '/DatosGenerales/BuscarP',
            detailEndpoint: '/DatosGenerales/BuscarProducto',
            modalId: 'vs-rem-modalBuscarProducto',
            inputId: 'vs-rem-inputBuscarProducto',
            resultsId: 'vs-rem-listaResultadosProductos',
            pageSizeId: 'vs-rem-pageSizeProductos',
            btnClearId: 'vs-rem-btnLimpiarProductos',
            spinnerId: 'vs-rem-spinnerProductos',
            paginationId: 'vs-rem-paginationProductos',
            recordsFromId: 'vs-rem-recordsFromProductos',
            recordsToId: 'vs-rem-recordsToProductos',
            totalRecordsId: 'vs-rem-totalRecordsProductos',
            shouldCloseOnSelect: false
        });

        await this.cargarPermisosUsuario();
        this.productManagerInstance = new RemisionesProductManager();

        this.productoManager.getExtraParams = async () => ({ clienteId: await this.asegurarClienteId() });
        this.productoManager.onRemisionesSelect = (producto) => {
            this.productManagerInstance.agregarRemisionesProducto(producto);
        };

        this.documentoManager = new RemisionesDocumentoManager({
            endpoint: '/DatosGenerales/BuscarDVSped',
            detailEndpoint: '/DatosGenerales/BuscarDocumento',
            modalId: 'vs-rem-modalBuscarDocumentos',
            inputId: 'vs-rem-inputBuscarDocumento',
            resultsId: 'vs-rem-listaResultadosDocumentos',
            pageSizeId: 'vs-rem-pageSizeDocumentos',
            btnClearId: 'vs-rem-btnLimpiarDocumentos',
            spinnerId: 'vs-rem-spinnerDocumentos',
            paginationId: 'vs-rem-paginationDocumentos',
            recordsFromId: 'vs-rem-recordsFromDocumentos',
            recordsToId: 'vs-rem-recordsToDocumentos',
            totalRecordsId: 'vs-rem-totalRecordsDocumentos',
            shouldCloseOnSelect: true,
            filtros: {}
        });

        const filtroEstado = document.getElementById('vs-rem-filtroEstado');
        const filtroFecha = document.getElementById('vs-rem-filtroFecha');

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

        const tipoMovSelect = document.getElementById('vs-rem-tipo-docto-mov');
        tipoMovSelect?.addEventListener('change', (e) => {
            this.manejarRemisionesCambioTipoMovimiento(e.target.value);
        });

        this.manejarRemisionesCambioTipoMovimiento(tipoMovSelect?.value || 'alta');

        this.etiquetaManager = new RemisionesEtiquetaManager();

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
        document.getElementById('vs-rem-sucursal').value = documento.sucursal || '';
        document.getElementById('vs-rem-almacen').value = documento.almacen || '';
        document.getElementById('vs-rem-documentid').value = documento.id_encabezado || '';

        document.getElementById('vs-rem-cliente').value = documento.cli_prov || '';
        document.getElementById('vs-rem-rfc').value = documento.rfc || '';
        document.getElementById('vs-rem-info-proveedor').value = documento.info_cli || '';
        document.getElementById('vs-rem-paridad').value = documento.par || '';
        document.getElementById('vs-rem-metodo-pago').value = documento.mdp || '';
        // El método de pago manda (PPD = crédito). Si el documento viene sin mdp se usa
        // tipo_proceso, donde el pedido guarda "pedido_credito" / "pedido_contado".
        const mdpDoc = (documento.mdp || '').toString().trim().toUpperCase();
        const procesoDoc = (documento.tipo_proceso || '').toString().toLowerCase();
        const tipoPagoDesdeDoc = mdpDoc
            ? (mdpDoc === 'PPD' ? 'credito' : 'contado')
            : (procesoDoc.includes('credito') ? 'credito' : 'contado');
        setTipoPagoRemision(tipoPagoDesdeDoc);

        const vendedorInstance = tomManager.getInstance('vs-rem-vendedor');
        if (vendedorInstance && (documento.vdr_cpr || documento.cve_vdr)) {
            vendedorInstance.setValue(documento.vdr_cpr || documento.cve_vdr, true);
        }

        const monedaInstance = tomManager.getInstance('vs-rem-moneda');
        if (monedaInstance && (documento.ccy || documento.moneda)) {
            monedaInstance.setValue(documento.ccy || documento.moneda, true);
        }

        // Solo el uso de CFDI del propio documento. NO se cae al sugerido del cliente:
        // es un dato fiscal y es preferible dejarlo vacío (el formulario lo exige) a
        // rellenarlo con uno distinto al que se capturó en el pedido.
        const cfdiInstance = tomManager.getInstance('vs-rem-uso-cfdi');
        const cfdiDoc = documento.usoCfdi || documento.uso_cfdi || documento.cfdi;
        if (cfdiInstance && cfdiDoc) {
            cfdiInstance.setValue(cfdiDoc, true);
        }

        // Solo la forma de pago del propio documento (f_pago). NO se cae a la sugerida del
        // cliente (df.forma_pago): acaba en el CFDI, y rellenarla con "efectivo" cuando en
        // el pedido se eligió "99 - Por definir" es peor que dejarla vacía y que se elija.
        const formaPagoInstance = tomManager.getInstance('vs-rem-forma-pago');
        if (formaPagoInstance && documento.f_pago) {
            formaPagoInstance.setValue(documento.f_pago, true);
        }

        //const metodoPagoInstance = tomManager.getInstance('vs-rem-metodo-pago');
        //if (metodoPagoInstance && (documento.metodoPago || documento.metodo_pago || documento.mdp)) {
        //    metodoPagoInstance.setValue(documento.metodoPago || documento.metodo_pago || documento.mdp, true);
        //}

        const sucursalInstance = tomManager.getInstance('vs-rem-sucursal');
        if (sucursalInstance && documento.suc) {
            sucursalInstance.setValue(documento.suc, false);
        }

        const almacenInstance = tomManager.getInstance('vs-rem-almacen');
        if (almacenInstance && documento.alm) {
            almacenInstance.setValue(documento.alm, true);
        }

        // Mandan las condiciones de pago que trae el documento (se capturaron en el pedido y
        // el RMP las hereda); pl_crd, el plazo del catálogo del cliente, es solo el respaldo.
        // Recalcular siempre la fecha como hoy + plazo pisaba el vencimiento pactado.
        document.getElementById('vs-rem-limite-credito').value = documento.lim_crd || '';
        document.getElementById('vs-rem-plazo').value =
            parseInt(documento.pl_dias) > 0 ? documento.pl_dias : (documento.pl_crd || '');
        if (documento.fecha_pago) {
            document.getElementById('vs-rem-fecha-pago').value = documento.fecha_pago;
        } else {
            calcularFechaPagoRem();
        }
        document.getElementById('vs-rem-concepto').value = documento.coment1 || '';
        document.getElementById('vs-rem-comentarios').value = documento.coment_aut || '';

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

            // El documento trae el precio pactado pero no los límites de la regla. Se
            // consultan aparte para que la remisión conozca su piso y su tope sin
            // re-precificar lo que ya se cotizó.
            RemisionesApp.asegurarClienteId()
                .then(clienteId => ReglasPrecio.hidratarLimites(
                    this.productManagerInstance.productos,
                    clienteId,
                    '/DatosGenerales/BuscarProducto'))
                .then(n => { if (n > 0) this.productManagerInstance.renderRemisionesTable(); });
        }

        const fleteInput = document.getElementById('vs-rem-flete-val');
        if (fleteInput && documento.flete) {
            const chkFlete = document.getElementById('vs-rem-flete');
            if (chkFlete) {
                chkFlete.checked = true;
                chkFlete.dispatchEvent(new Event('change'));
            }
            fleteInput.value = documento.flete;
        }

        const ordenCompraInput = document.getElementById('vs-rem-orden-compra-val');
        if (ordenCompraInput && documento.ordencompra) {
            const chkOrdenCompra = document.getElementById('vs-rem-orden-compra-check');
            if (chkOrdenCompra) {
                chkOrdenCompra.checked = true;
                chkOrdenCompra.dispatchEvent(new Event('change'));
            }
            ordenCompraInput.value = documento.ordencompra;
        }

        this.productManagerInstance.calcularRemisionesTotales();

        const tipoMovSelect = document.getElementById('vs-rem-tipo-docto-mov');
        if (tipoMovSelect && documento.folio) {
            tipoMovSelect.value = 'modificacion';
            this.manejarRemisionesCambioTipoMovimiento('modificacion');
        }

        this.productManagerInstance.calcularRemisionesTotales();

        // Crédito del cliente que viene con el documento (solo se usa si es a crédito).
        if (documento.estatus_credito) {
            window.CreditoVentas?.setCliente('vs-rem', {
                id: documento.cli_prov,
                lim_crd: documento.lim_crd || 0,
                credito_usado: documento.credito_usado || 0,
                credito_disponible: documento.credito_disponible || 0,
                estatus_cliente: documento.estatus_cliente
            });
        }

        // Si el pedido de origen ya fue autorizado por gerencia, no se vuelve a bloquear.
        window.CreditoVentas?.refrescarAutorizacion('vs-rem');
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
            if (element.id !== 'vs-rem-tipo-docto-mov') {
                element.disabled = true;
                element.classList.add('vs-rem-disabled-field');
            }
        });

        const btnBuscarCliente = remisionContenedor.querySelector('[data-bs-target="#vs-rem-modalBuscarCliente"]');
        const btnBuscarProducto = remisionContenedor.querySelector('[data-bs-target="#vs-rem-modalBuscarProducto"]');
        if (btnBuscarCliente) btnBuscarCliente.disabled = true;
        if (btnBuscarProducto) btnBuscarProducto.disabled = true;

        remisionContenedor.querySelectorAll('.vs-rem-btn-eliminar').forEach(btn => btn.disabled = true);

        if (window.tomManager) {
            ['vs-rem-vendedor', 'vs-rem-moneda', 'vs-rem-uso-cfdi', 'vs-rem-forma-pago'].forEach(id => {
                const instance = window.tomManager.instances?.get(id);
                if (instance) instance.disable();
            });
        }

        const btn = remisionContenedor.querySelector('.vs-rem-btn-submit');
        if (btn) btn.disabled = false;

        const btnDocConsulta = remisionContenedor.querySelector('.vs-rem-floating-btn');
        if (btnDocConsulta) btnDocConsulta.disabled = false;

        const btnPreview = remisionContenedor.querySelector('.vs-rem-ticket-btn');
        if (btnPreview) btnPreview.disabled = false;
    },

    habilitarRemisionesFormulario() {
        const remisionContenedor = document.getElementById('remision-contenedor');
        if (!remisionContenedor) return;

        remisionContenedor.querySelectorAll('input, select, textarea, button').forEach(element => {
            element.disabled = false;
            element.classList.remove('vs-rem-disabled-field');
        });

        const btnBuscarCliente = remisionContenedor.querySelector('[data-bs-target="#vs-rem-modalBuscarCliente"]');
        const btnBuscarProducto = remisionContenedor.querySelector('[data-bs-target="#vs-rem-modalBuscarProducto"]');
        if (btnBuscarCliente) btnBuscarCliente.disabled = false;
        if (btnBuscarProducto) btnBuscarProducto.disabled = false;

        if (window.tomManager) {
            ['vs-rem-vendedor', 'vs-rem-moneda', 'vs-rem-uso-cfdi', 'vs-rem-forma-pago'].forEach(id => {
                const instance = window.tomManager.instances?.get(id);
                if (instance) instance.enable();
            });
        }

        const tipoMovimiento = document.getElementById('vs-rem-tipo-docto-mov')?.value;
        const btn = remisionContenedor.querySelector('.vs-rem-btn-submit');
        if (btn) {
            btn.innerHTML = tipoMovimiento === 'modificacion'
                ? '<i class="fas fa-save"></i> Actualizar Remisión'
                : '<i class="fas fa-save"></i> Guardar Remisión';
        }

        this.productManagerInstance?.renderRemisionesTable();

        // Habilitar el formulario reactiva el submit: re-evaluar por si el crédito lo bloquea.
        window.CreditoVentas?.evaluar('vs-rem');
    }

};
// Nombre propio de la remisión: partial-vs-pedido.js declara su propia calcularFechaPagoPed
// global y las cuatro parciales se cargan en la misma página (Sucursales.cshtml), así que la
// última en cargar (ésta) pisaba a la del pedido y le escribía la fecha en los campos vs-rem-*.
function calcularFechaPagoRem() {
    const plazo = parseInt(document.getElementById('vs-rem-plazo')?.value) || 0;
    const fechaPagoInput = document.getElementById('vs-rem-fecha-pago');
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
// GESTOR DE ETIQUETAS DE REMISIÓN
// ============================================
class RemisionesEtiquetaManager extends RemisionesDocumentoManager {
    constructor() {
        super({
            endpoint: '/DatosGenerales/BuscarDVSetiquetaRem',
            detailEndpoint: '/DatosGenerales/BuscarDocumento',
            modalId: 'vs-rem-modalEtiquetas',
            inputId: 'vs-rem-inputBuscarEtiqueta',
            resultsId: 'vs-rem-listaResultadosEtiquetas',
            pageSizeId: 'vs-rem-pageSizeEtiquetas',
            btnClearId: 'vs-rem-btnLimpiarEtiquetas',
            spinnerId: 'vs-rem-spinnerEtiquetas',
            paginationId: 'vs-rem-paginationEtiquetas',
            recordsFromId: 'vs-rem-recordsFromEtiquetas',
            recordsToId: 'vs-rem-recordsToEtiquetas',
            totalRecordsId: 'vs-rem-totalRecordsEtiquetas',
            shouldCloseOnSelect: true,
            filtros: {}
        });
    }

    renderRemisionesResults(data) {
        const items = data.items || [];

        if (items.length === 0) {
            this.dom.results.innerHTML = `
                <li class="list-group-item text-center text-muted py-5">
                    <i class="fas fa-search fa-3x mb-3 opacity-25"></i>
                    <p>No se encontraron remisiones</p>
                </li>`;
            this.updateRemisionesCounters();
            return;
        }

        const fragment = document.createDocumentFragment();

        items.forEach(item => {
            const li = document.createElement('li');
            li.className = 'list-group-item';

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
                        </div>
                    </div>
                    <div class="d-flex flex-column gap-2 align-items-end">
                        <button type="button" class="btn btn-sm btn-success" data-select-id="${this.escapeRemisionesHtml(item.id_encabezado)}">
                            <i class="fas fa-tag me-1"></i>Generar Etiqueta
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

    async handleRemisionesSelect(id) {
        try {
            const response = await fetch(`${this.detailEndpoint}?id=${encodeURIComponent(id)}`);
            if (!response.ok) throw new Error('Error al obtener detalles');

            const data = await response.json();
            if (data && data[0]) {
                // Llamar directamente a la nueva función
                await generarEtiquetaRemision(data[0]);

                if (this.shouldCloseOnSelect) {
                    bootstrap.Modal.getInstance(this.dom.modal)?.hide();
                }
            }
        } catch (error) {
            console.error('Error seleccionando remisión para etiqueta:', error);
            Swal.fire({
                icon: 'error',
                title: 'Error',
                text: 'No se pudo cargar la información de la remisión'
            });
        }
    }
}

// ============================================
// FUNCIÓN PARA GENERAR ETIQUETA
// ============================================
async function generarEtiquetaRemision(documento) {
    try {
        // 1. Obtener productos de la remisión
        const productos = documento.productos || [];

        if (!productos || productos.length === 0) {
            await Swal.fire({
                icon: 'warning',
                title: 'Sin productos',
                text: 'Esta remisión no tiene productos para imprimir etiquetas'
            });
            return;
        }

        // 2. Mostrar selector de productos
        const { value: productosSeleccionados } = await Swal.fire({
            title: '🏷️ Selecciona los productos para etiquetar',
            html: generarListaProductosHTML(productos),
            width: '800px',
            showCancelButton: true,
            confirmButtonText: '🖨️ Imprimir Etiquetas',
            cancelButtonText: 'Cancelar',
            preConfirm: () => {
                const checkboxes = document.querySelectorAll('.producto-checkbox:checked');
                const seleccionados = Array.from(checkboxes).map(cb => ({
                    index: parseInt(cb.dataset.index),
                    cantidad: parseInt(document.getElementById(`cant-${cb.dataset.index}`).value) || 1
                }));

                if (seleccionados.length === 0) {
                    Swal.showValidationMessage('Debes seleccionar al menos un producto');
                }

                return seleccionados;
            },
            didOpen: () => {
                // Event listeners para checkboxes "seleccionar todos"
                document.getElementById('select-all')?.addEventListener('change', (e) => {
                    document.querySelectorAll('.producto-checkbox').forEach(cb => {
                        cb.checked = e.target.checked;
                    });
                });
            }
        });

        if (!productosSeleccionados) return;

        // 3. Imprimir etiquetas
        await imprimirEtiquetasProductosZebra(documento, productos, productosSeleccionados);

    } catch (error) {
        console.error('❌ Error generando etiquetas:', error);
        Swal.fire({
            icon: 'error',
            title: 'Error',
            text: 'No se pudieron generar las etiquetas: ' + error.message
        });
    }
}
// ============================================
// GENERAR HTML DE LISTA DE PRODUCTOS
// ============================================
function generarListaProductosHTML(productos) {
    return `
        <div style="text-align: left; max-height: 400px; overflow-y: auto;">
            <div style="margin-bottom: 15px; padding: 10px; background: #f0f9ff; border-radius: 8px;">
                <label style="display: flex; align-items: center; gap: 10px; cursor: pointer;">
                    <input type="checkbox" id="select-all" style="width: 18px; height: 18px;">
                    <strong style="color: #0066cc;">Seleccionar todos</strong>
                </label>
            </div>
            
            <table style="width: 100%; border-collapse: collapse;">
                <thead>
                    <tr style="background: #0066cc; color: white;">
                        <th style="padding: 10px; text-align: center;">✓</th>
                        <th style="padding: 10px; text-align: left;">Artículo</th>
                        <th style="padding: 10px; text-align: left;">Descripción</th>
                        <th style="padding: 10px; text-align: center;">Cantidad</th>
                        <th style="padding: 10px; text-align: center;">Etiquetas</th>
                    </tr>
                </thead>
                <tbody>
                    ${productos.map((prod, index) => `
                        <tr style="border-bottom: 1px solid #e0e0e0; ${index % 2 === 0 ? 'background: #f8f8f8;' : ''}">
                            <td style="padding: 10px; text-align: center;">
                                <input type="checkbox" class="producto-checkbox" data-index="${index}" style="width: 18px; height: 18px;">
                            </td>
                            <td style="padding: 10px; font-weight: bold; color: #0066cc;">
                                ${prod.producto_id || prod.id || 'N/A'}
                            </td>
                            <td style="padding: 10px;">
                                ${(prod.descripcion || 'Sin descripción').substring(0, 40)}${(prod.descripcion || '').length > 40 ? '...' : ''}
                            </td>
                            <td style="padding: 10px; text-align: center;">
                                <strong>${prod.cantidad || 0}</strong>
                            </td>
                            <td style="padding: 10px; text-align: center;">
                                <input type="number" id="cant-${index}" min="1" max="99" value="1" 
                                       style="width: 60px; padding: 5px; text-align: center; border: 2px solid #0066cc; border-radius: 4px;">
                            </td>
                        </tr>
                    `).join('')}
                </tbody>
            </table>
        </div>
    `;
}

// ============================================
// IMPRIMIR ETIQUETAS ZEBRA POR PRODUCTO
// ============================================
async function imprimirEtiquetasProductosZebra(documento, productos, seleccionados) {
    try {
        // Mostrar loading
        Swal.fire({
            title: 'Conectando con impresora...',
            html: 'Por favor espere',
            allowOutsideClick: false,
            didOpen: () => {
                Swal.showLoading();
            }
        });

        // Seleccionar impresora
        if (!impresoraSeleccionada) {
            await seleccionarImpresoraZebra();
        }

        // Generar y enviar etiquetas
        let totalEtiquetas = 0;

        for (const sel of seleccionados) {
            const producto = productos[sel.index];
            const cantidadEtiquetas = sel.cantidad;

            for (let i = 0; i < cantidadEtiquetas; i++) {
                const zpl = generarZPLEtiquetaProducto(documento, producto, i + 1, cantidadEtiquetas);

                await new Promise((resolve, reject) => {
                    impresoraSeleccionada.send(zpl,
                        () => {
                            totalEtiquetas++;
                            console.log(`✅ Etiqueta ${totalEtiquetas} enviada`);
                            resolve();
                        },
                        (error) => {
                            console.error('❌ Error:', error);
                            reject(error);
                        }
                    );
                });

                // Pequeña pausa entre etiquetas
                await new Promise(resolve => setTimeout(resolve, 500));
            }
        }

        Swal.fire({
            icon: 'success',
            title: 'Impresión completada',
            text: `Se enviaron ${totalEtiquetas} etiqueta(s) a la impresora`,
            timer: 3000,
            showConfirmButton: true
        });

    } catch (error) {
        console.error('❌ Error:', error);
        Swal.fire({
            icon: 'error',
            title: 'Error de impresión',
            html: `
                <p>${error.message || 'No se pudo conectar con la impresora Zebra'}</p>
                <p style="font-size: 12px; color: #666; margin-top: 10px;">
                    Verifica que:
                    <ul style="text-align: left; margin-top: 10px;">
                        <li>La impresora Zebra esté encendida</li>
                        <li>Esté conectada por USB o red</li>
                        <li>El servicio BrowserPrint esté ejecutándose</li>
                    </ul>
                </p>
            `,
            footer: '<a href="https://www.zebra.com/us/en/support-downloads/software/developer-tools/browser-print.html" target="_blank">Descargar BrowserPrint</a>'
        });
    }
}

// ============================================
// FUNCIÓN PARA IMPRIMIR ETIQUETA
// ============================================
function imprimirEtiqueta(htmlContent) {
    // Crear un contenedor temporal
    const printContainer = document.createElement('div');
    printContainer.id = 'etiqueta-print-container';
    printContainer.innerHTML = htmlContent;
    printContainer.style.display = 'none';

    document.body.appendChild(printContainer);

    // Usar timeout para asegurar que el contenido se renderiza
    setTimeout(() => {
        window.print();

        // Remover el contenedor después de imprimir
        setTimeout(() => {
            document.body.removeChild(printContainer);
        }, 1000);
    }, 100);
}

// ============================================
// SISTEMA DE IMPRESIÓN ZEBRA PARA ETIQUETAS
// ============================================

let impresoraSeleccionada = null;

// Función para seleccionar impresora Zebra
async function seleccionarImpresoraZebra() {
    return new Promise((resolve, reject) => {
        BrowserPrint.getDefaultDevice("printer", function (device) {
            if (device && device.name) {
                impresoraSeleccionada = device;
                console.log('✅ Impresora detectada:', device.name);
                resolve(device);
            } else {
                BrowserPrint.getLocalDevices(function (devices) {
                    if (devices && devices.length > 0) {
                        const zebraDevice = devices.find(d =>
                            d.name.toLowerCase().includes('zebra')
                        ) || devices[0];

                        impresoraSeleccionada = zebraDevice;
                        console.log('✅ Impresora seleccionada:', zebraDevice.name);
                        resolve(zebraDevice);
                    } else {
                        reject(new Error('No se encontraron impresoras'));
                    }
                }, reject);
            }
        }, reject);
    });
}

// ============================================
// GENERAR CÓDIGO ZPL PARA ETIQUETA DE PRODUCTO
// ============================================
function generarZPLEtiquetaProducto(documento, producto, numEtiqueta, totalEtiquetas) {
    const fecha = new Date().toLocaleDateString('es-MX', {
        day: '2-digit',
        month: '2-digit',
        year: '2-digit'
    });

    // Truncar textos largos
    const clienteCorto = (documento.cli_prov || 'N/A').substring(0, 28);
    const descripcionLinea1 = (producto.descripcion || 'Sin descripción').substring(0, 35);
    const descripcionLinea2 = (producto.descripcion || '').substring(35, 70);


    const zpl = `^XA
^CI28
~SD15
^PW800
^LL400

^FO20,01^GFA,2037,2037,21,,:::Q038V0E,P03CW03C,P0FY078,O03CY01E,O0FgG078,N078gG01E,M01FgI078,M03CgI03E,M0FgK0F,L03EgK03C,L078gK01F,K01FgM078,K03EgM03C,K078gM01F,K0FgO0F8,J03EgO07C,J07CgO03E,J0F8gO01F,I01FK0FFK0C0IFEM03FJ0F8,I03EJ0NFE1KFK01KFC7C,I07EI07NFC3KFCJ0LFC3E,I07CI0OF07LFI01LFC1F,I0F8003NFE0MF8007LFC0F8,001FI07NFC1MFC00MFC0F8,003FI0OF83MFE01MFC07C,003EI0OF07MFE03MF803E,007C001NFE0OF03MF803E,00FC001NFC1OF07MF801F,00F8003NF83OF87MF801F8,01F8003NF07OF87MFC00F8,01F8003MFE0PF87MF800F8,03FI03FFEK01IFI07IF8IFCM0FC,03FI07FFCK03IFI01IF8IF8M07C,03FI07FFCK03IFI01IF8IF8M07E,07EI07FFCK01IFJ0IF8IF8M07E,07EI03FFEK03IFJ0IF8IFCM03E,07EI03IFK03IFJ0IF8IFCM03F,07CI03KFI03IFJ0IF87FFEM03F,0FCI03KFE003IFI01IF87JFEK03F,0FCI01LF003IFI01IF87KFEJ03F,0FCI01LFC03IFI03IF03LF8I01F8,0FCI01LFE03IFI0IFE03LFCI01F8,0FCJ07LF01IF0KFE01LFCI01F8,0FCgU01F8,:0FCK0LFE1IF01IFEI01LFC001F8,0FCK07KFE1IF00JFJ0LF8001F8,0FCK03KFE3IF00JF8I07KFC003F,0FCL07JFE3IF007IFCI01KFC003F,07CN0IFE1IF003IFEK01IFC003F,07EN07IF3IF001JFL0IFC003F,07EN03IF3IFI07IF8K07FFC003F,07EN03FFE3IFI03IF8K07FFC003E,03FN03FFE3IFI01IF8K07FFC007E,03FN07FFE3IFJ0IF8J01IFC007E,03FI03MFE3IFJ07FF8NFC007C,01F8007MFE3IFJ03FF8NFC00FC,01F8007MFC3IFJ01FF8NF800F8,00F8007MFC3IFK0FF8NF800F8,00FC007MF83IFK07F8NF801F8,00FC007MF83IFK03F8NF001F,007C007MF03IFK01F8MFE003E,003E007MF03IFL0F8MFE003E,003F007MF03IFL078MFC007C,001F007MF03IFL038MFI0F8,I0F807LFE03IFL018LFE001F,I07C07LFC03IFL08LF8003E,I07E07KFC003IFQ07C,I03EgQ07C,I01FgQ0F8,J0F8gO01F,J07CgO03E,J03EgO07C,K0FgO0F8,K078gM01F,K03EgM03C,K01FgM078,L078gK01F,L03EgK03C,M0FgK0F8,M03CgI01EJ03C,N0FgI078J076,N03CgG01EL06,N01FgG078,O07CY01E,P0FY07M042,P01CW03C,Q038V0E,R04,,::::^FS

^FO190,15^A0N,35,35^FDREMISION^FS
^FO190,52^A0N,26,26^FD${documento.folio || 'N/A'}^FS

^FO20,97^GB540,1,2^FS

^FO25,110^A0N,20,20^FDARTICULO:^FS
^FO140,110^A0N,24,24^FD${producto.producto_id || producto.id || 'N/A'}^FS

^FO25,135^A0N,16,16^FDDESCRIPCION:^FS
^FO25,155^A0N,18,18^FB510,3,0,L^FD${producto.descripcion || 'N/A'}^FS

^FO20,215^GB540,1,2^FS

^FO25,225^A0N,20,20^FDCantidad:^FS
^FO150,225^A0N,28,28^FD${producto.cantidad || 0} ${producto.unidad || 'PZA'}^FS

^FO20,265^GB540,1,2^FS

^FO25,275^A0N,16,16^FDCliente:^FS
^FO25,295^A0N,18,18^FB510,2,0,L^FD${clienteCorto}^FS

^FO225,275^A0N,14,14^FD${(documento.suc || 'CEDIS')} - SRS INDUSTRIAL^FS
^FO225,295^A0N,14,14^FD${fecha}^FS

^FO20,340^GB540,1,2^FS

^FO580,15^BQN,2,7^FDQA,REM:${documento.folio}|ART:${producto.producto_id || producto.id}|CANT:${producto.cantidad}^FS

^FO600,280^A0N,50,50^FD#${numEtiqueta}^FS
^FO730,295^A0N,26,26^FD/${totalEtiquetas}^FS

^XZ`;
    return zpl;
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

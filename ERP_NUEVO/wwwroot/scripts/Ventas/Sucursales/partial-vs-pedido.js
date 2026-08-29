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
            table: document.getElementById('vs-ped-productosTable'),
            thead: document.querySelector('#vs-ped-productosTable')?.closest('table')?.querySelector('thead'),
            subtotal1Display: document.getElementById('vs-ped-total-subtotal1-display'),
            descuentoDisplay: document.getElementById('vs-ped-total-descuento-display'),
            fleteDisplay: document.getElementById('vs-ped-total-flete-display'),
            subtotal2Display: document.getElementById('vs-ped-total-subtotal2-display'),
            ivaDisplay: document.getElementById('vs-ped-iva-display'),
            importeDisplay: document.getElementById('vs-ped-importe-display')
        };

        this.initPedidosProductManager();
    }

    initPedidosProductManager() {
        this.dom.table?.addEventListener('input', (e) => {
            const input = e.target;
            if (input.classList.contains('vs-ped-cantidad') ||
                input.classList.contains('vs-ped-precio') ||
                input.classList.contains('vs-ped-descuento') ||
                input.classList.contains('vs-ped-comentario')) {
                const tr = input.closest('tr');
                const productoId = tr?.dataset.productoId;
                if (productoId) this.actualizarPedidosFila(productoId);
            }
        });

        // ── Un solo listener de click en la tabla ────────────────────────────
        this.dom.table?.addEventListener('click', (e) => {

            // 1. Eliminar producto
            const btnEliminar = e.target.closest('.vs-ped-btn-eliminar');
            if (btnEliminar) {
                const productoId = btnEliminar.closest('tr')?.dataset.productoId;
                if (productoId) this.eliminarPedidosProducto(productoId);
                return;
            }

            // 2. Click en el OVERLAY de campo protegido
            const overlay = e.target.closest('.vs-ped-field-overlay');
            if (overlay) {
                pedirPedidosAutenticacionGlobal(overlay.dataset.tipo);
                return;
            }
        });

        document.getElementById('vs-ped-flete-val')?.addEventListener('input', () => {
            this.calcularPedidosTotales();
        });

        document.getElementById('vs-ped-monto-anticipo')?.addEventListener('input', () => {
            this.calcularPedidosTotales();
        });
    }

    agregarPedidosProducto(producto) {
        const productoExistente = this.productos.find(p => p.productoId === producto.id);

        if (productoExistente) {
            productoExistente.cantidad += 1;

            const tr = this.dom.table.querySelector(`tr[data-producto-id="${productoExistente.id}"]`);
            if (tr) {
                const inputCantidad = tr.querySelector('.vs-ped-cantidad');
                inputCantidad.value = productoExistente.cantidad;

                const importe = this.calcularPedidosImporte(productoExistente);
                tr.querySelector('.vs-ped-importe').textContent = importe.toFixed(2);

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
            // precio, precioOriginal, precioMinimo, descuento, descuentoMaximo,
            // aplicarAutomatico y tipoRegla salen de la regla vigente. Antes el pedido
            // solo leía 'precio' y descuento arrancaba en 0, así que una regla automática
            // que la cotización sí aplicaba aquí se perdía.
            ReglasPrecio.camposDeRegla(producto));

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
            <tr class="vs-ped-empty-state">
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
            precioInput.className = 'vs-ped-product-input vs-ped-precio';
            precioInput.value = p.precio.toFixed(2);
            // El piso viene de la regla; 0 = sin piso (precio libre, solo no negativo).
            precioInput.min = (p.precioMinimo || 0).toFixed(2);
            precioInput.step = '0.01';
            // ★ precio original guardado como data-attribute
            precioInput.dataset.precioOriginal = p.precioOriginal.toFixed(2);
            precioInput.dataset.precioMinimo = (p.precioMinimo || 0).toFixed(2);

            if (!precioDesbloqueado) {
                precioInput.disabled = true;
                const overlayPrecio = document.createElement('div');
                overlayPrecio.className = 'vs-ped-field-overlay';
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
            descInput.className = 'vs-ped-product-input vs-ped-descuento';
            descInput.value = p.descuento;
            descInput.min = '0';
            // Tope de la regla: en PRECIO_FIJO es 0 y el campo queda efectivamente fijo.
            descInput.max = (p.descuentoMaximo != null ? p.descuentoMaximo : 100).toFixed(2);
            descInput.step = '0.01';
            descInput.dataset.descMaximo = descInput.max;

            if (!descDesbloqueado) {
                descInput.disabled = true;
                const overlayDesc = document.createElement('div');
                overlayDesc.className = 'vs-ped-field-overlay';
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
                <input type="number" class="vs-ped-product-input vs-ped-cantidad"
                       value="${p.cantidad}" min="0" step="0.01">
            </td>
            <td>${this.escapePedidosHtml(p.unidad)}</td>`;

            // Insertar celdas construidas por JS (precio y descuento)
            tr.appendChild(precioCell);
            tr.appendChild(descCell);

            // Continuar con el resto
            const resto = document.createElement('template');
            resto.innerHTML = `
            <td class="vs-ped-importe fw-bold">${importe.toFixed(2)}</td>
            <td>
                <input type="text" class="vs-ped-product-input vs-ped-comentario"
                       value="${this.escapePedidosHtml(p.comentario)}" placeholder="Comentario...">
            </td>
            <td>
                <button type="button" class="vs-ped-btn-delete vs-ped-btn-eliminar">
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

        producto.cantidad = parseFloat(tr.querySelector('.vs-ped-cantidad').value) || 0;
        producto.comentario = tr.querySelector('.vs-ped-comentario').value || '';

        // ── Precio y descuento contra la regla vigente ────────────────────────
        // Mismas comprobaciones que la cotización, vía el módulo compartido: piso solo
        // cuando la regla lo define, nunca negativos y el tope de descuento de la regla.
        const precioInput = tr.querySelector('.vs-ped-precio');
        if (precioInput && !precioInput.disabled) {
            const res = ReglasPrecio.validarPrecio(
                producto,
                parseFloat(precioInput.value) || 0,
                !!pedidosAuthState.precioToken);

            producto.precio = res.valor;
            if (res.aviso) precioInput.value = res.valor.toFixed(2);
            ReglasPrecio.avisar(res.aviso);
        }

        const descInput = tr.querySelector('.vs-ped-descuento');
        if (descInput && !descInput.disabled) {
            const res = ReglasPrecio.validarDescuento(
                producto,
                parseFloat(descInput.value) || 0,
                !!pedidosAuthState.descuentoToken);

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

        tr.querySelector('.vs-ped-importe').textContent = this.calcularPedidosImporte(producto).toFixed(2);
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
        const selector = tipo === 'descuento' ? '.vs-ped-descuento' : '.vs-ped-precio';

        this.dom.table?.querySelectorAll(selector).forEach(input => {
            input.removeAttribute('disabled');

            const overlay = input.parentElement?.querySelector('.vs-ped-field-overlay');
            overlay?.remove();
        });
    }

    calcularPedidosImporte(producto) {
        return producto.cantidad * producto.precio * (1 - producto.descuento / 100);
    }

    calcularPedidosTotales() {
        const tipoPago = document.getElementById('vs-ped-toggle-pago')?.getAttribute('data-state') || 'contado';
        const flete = parseFloat(document.getElementById('vs-ped-flete-val')?.value) || 0;
        let subtotal1 = 0, descuento = 0, subtotal2 = 0, iva = 0, total = 0;

        if (tipoPago === 'anticipo') {
            const montoAnticipo = parseFloat(document.getElementById('vs-ped-monto-anticipo')?.value) || 0;
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

        this.updatePedidosHiddenField('vs-ped-total-subtotal1', subtotal1);
        this.updatePedidosHiddenField('vs-ped-total-descuento', descuento);
        this.updatePedidosHiddenField('vs-ped-total-flete', flete);
        this.updatePedidosHiddenField('vs-ped-total-subtotal2', subtotal2);
        this.updatePedidosHiddenField('vs-ped-iva', iva);
        this.updatePedidosHiddenField('vs-ped-importe', total);

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
        this.form = document.getElementById('vs-ped-formCotizacion');
        this.btnSubmit = document.querySelector('.vs-ped-btn-submit');
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
        const toggle = document.getElementById('vs-ped-toggle-pago');
        const tipoPago = toggle?.getAttribute('data-state') || 'contado';

        const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
        if (token) formData.append('__RequestVerificationToken', token);

        const camposGenerales = [
            'vs-ped-tipo-docto-mov',
            'vs-ped-folio', 'vs-ped-documentid', 'vs-ped-cliente',
            'vs-ped-rfc', 'vs-ped-vendedor', 'vs-ped-moneda',
            'vs-ped-paridad', 'vs-ped-concepto',
            'vs-ped-metodo-pago', 'vs-ped-forma-pago',
            'vs-ped-uso-cfdi', 'vs-ped-comentarios'
        ];

        camposGenerales.forEach(id => {
            const el = document.getElementById(id);
            if (el) formData.append(id.replace('vs-ped-', ''), el.value || '');
        });

        formData.append('ordenCompra', document.getElementById('vs-ped-orden-compra-val')?.value || '');
        //formData.set('sucursal', suc.getValue());
        //formData.set('almacen', alm.getValue());

        if (tipoPago === 'credito') {
            formData.append('limiteCredito', document.getElementById('vs-ped-limite-credito')?.value || '');
            formData.append('plazo', document.getElementById('vs-ped-plazo')?.value || '');
            formData.append('fechaPago', document.getElementById('vs-ped-fecha-pago')?.value || '');
        }

        if (tipoPago === 'anticipo') {
            formData.append('fechaAnticipo', document.getElementById('vs-ped-fecha-anticipo')?.value || '');
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
        // Identifica la autorización de gerencia cuando el documento aún no existe.
        formData.append('creditoToken', window.CreditoVentas?.token('vs-ped') || '');

        // ★ Enviar tokens al servidor para validación
        formData.append('descuentoToken', pedidosAuthState.descuentoToken || '');
        formData.append('precioToken', pedidosAuthState.precioToken || '');

        return formData;
    }

    validarPedidosFormulario() {
        const errores = [];

        const togglePago = document.getElementById('vs-ped-toggle-pago');
        const tipoPago = togglePago ? togglePago.getAttribute('data-state') : 'contado';

        const cliente = document.getElementById('vs-ped-cliente')?.value;
        if (!cliente || cliente.trim() === '') {
            errores.push('Debe seleccionar un cliente');
        }

        const productos = this.productManager.getPedidosProductosData();
        if (tipoPago !== 'anticipo' && productos.length === 0) {
            errores.push('Debe agregar al menos un producto');
        }

        const vendedor = document.getElementById('vs-ped-vendedor')?.value;
        if (!vendedor || vendedor === 'Seleccionar vendedor') {
            errores.push('Debe seleccionar un vendedor');
        }

        const rfcInput = document.getElementById('vs-ped-rfc');
        const rfc = rfcInput ? rfcInput.value.trim() : '';
        if (!rfc) {
            errores.push('Debe ingresar el RFC');
        }

        const monedaSelect = tomManager.getInstance('vs-ped-moneda');
        const monedaValue = monedaSelect?.getValue();
        if (!monedaValue) {
            errores.push('Debe seleccionar una Moneda');
        }

        const usoCfdiSelect = tomManager.getInstance('vs-ped-uso-cfdi');
        const usoCfdiValue = usoCfdiSelect?.getValue();
        if (!usoCfdiValue) {
            errores.push('Debe seleccionar un Uso CFDI');
        }

        const formaPagoSelect = tomManager.getInstance('vs-ped-forma-pago');
        const formaPagoValue = formaPagoSelect?.getValue();
        if (!formaPagoValue) {
            errores.push('Debe seleccionar una Forma de Pago');
        }

        const fechaPagoInput = document.getElementById('vs-ped-fecha-pago');
        const fechaPago = fechaPagoInput ? fechaPagoInput.value.trim() : '';
        if (tipoPago === 'credito' && !fechaPago) {
            errores.push('Debe ingresar la Fecha de Pago (solo para crédito)');
        }

        // Crédito: solo agrega errores cuando la venta es a crédito y excede el límite.
        errores.push(...(window.CreditoVentas?.validarAntesDeGuardar('vs-ped') || []));

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
                endpoint: '/VSPedido/Guardar',
                metodo: 'POST',
                mensajeExito: 'Pedido creado exitosamente',
                textoCarga: 'Guardando...',
                validarProductos: true
            },
            'modificacion': {
                endpoint: '/VSPedido/Modificar',
                metodo: 'PUT',
                mensajeExito: 'Pedido modificado exitosamente',
                textoCarga: 'Actualizando...',
                validarProductos: true
            },
            'consulta': {
                endpoint: '/VSPedido/Consultar',
                metodo: 'GET',
                mensajeExito: 'Consulta realizada exitosamente',
                textoCarga: 'Consultando...',
                validarProductos: false
            }
        };

        return configuraciones[tipoMovimiento] || configuraciones['alta'];
    }

    async enviarPedidosCotizacion() {
        const tipoMovimiento = document.getElementById('vs-ped-tipo-docto-mov')?.value || 'alta';
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
        document.getElementById('vs-ped-cliente').value = '';
        document.getElementById('vs-ped-rfc').value = '';
        document.getElementById('vs-ped-info-proveedor').value = '';
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
/**
 * Valor real de un campo del pedido. Los selects están montados sobre TomSelect, que
 * mantiene su propio estado: leer solo el `.value` del elemento puede devolver el
 * valor anterior o vacío justo después de un cambio.
 */
function _valorCampoPed(id) {
    const instancia = window.tomManager?.getInstance?.(id);
    const valorTom = instancia?.getValue?.();
    if (valorTom !== undefined && valorTom !== null && valorTom !== '') {
        return Array.isArray(valorTom) ? valorTom.join(',') : String(valorTom);
    }
    return document.getElementById(id)?.value || '';
}

const PedidosApp = {
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
     * El cliente casi nunca llega por el modal en un pedido: lo normal es que venga al
     * cargar la cotización de origen, y por esa vía solo se conoce la clave. Sin esto,
     * agregar un producto después de cargar el documento consultaba el precio con
     * clienteId = 0 y la regla del cliente no se aplicaba.
     */
    async asegurarClienteId() {
        const cve = document.getElementById('vs-ped-cliente')?.value || '';

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

        pm.renderPedidosTable();
        pm.calcularPedidosTotales();
        ReglasPrecio.avisar(
            `Precios recalculados con la lista de ${nombreCliente || 'el cliente'} `
            + `(${actualizadas} partida${actualizadas === 1 ? '' : 's'})`,
            'info');
    },

    async initPedidos() {
        this.form = document.getElementById('vs-ped-formCotizacion');
        this.btnSubmit = document.querySelector('.vs-ped-btn-submit');

        // Crédito: en el pedido ya se compromete la línea, así que bloquea.
        window.CreditoVentas?.registrar({
            prefix: 'vs-ped',
            modo: 'bloquear',
            documento: 'pedido',
            // La solicitud va al controlador de sucursal: el de Industriales, al
            // aprobar, reconstruye un pedido VIPED desde la cotizacion de origen.
            endpointSolicitud: '/VSPedido/EnviarSolicitudGerente',
            getTipoPago: () => document.getElementById('vs-ped-tipo-pago')?.value || 'contado',
            totalId: 'vs-ped-importe',
            submitSelector: '.vs-ped-btn-submit',
            clienteInputId: 'vs-ped-cliente',
            folioId: 'vs-ped-folio',
            documentIdId: 'vs-ped-documentid',
            getProductos: () => PedidosApp.productManagerInstance?.getPedidosProductosData() || [],
            getTotales: () => PedidosApp.productManagerInstance?.totales || {},
            // Lo capturado aquí (forma de pago, uso CFDI…) no existe en la cotización, así
            // que viaja con la solicitud para que el pedido autorizado lo conserve.
            getConfiguracion: () => ({
                formaPago: _valorCampoPed('vs-ped-forma-pago'),
                usoCfdi: _valorCampoPed('vs-ped-uso-cfdi'),
                metodoPago: _valorCampoPed('vs-ped-metodo-pago'),
                plazo: _valorCampoPed('vs-ped-plazo'),
                fechaPago: _valorCampoPed('vs-ped-fecha-pago'),
                moneda: _valorCampoPed('vs-ped-moneda'),
                paridad: _valorCampoPed('vs-ped-paridad'),
                vendedor: _valorCampoPed('vs-ped-vendedor'),
                concepto: _valorCampoPed('vs-ped-concepto'),
                comentarios: _valorCampoPed('vs-ped-comentarios'),
                ordenCompra: _valorCampoPed('vs-ped-orden-compra-val')
            })
        });

        this.clienteManager = new PedidosDataManager({
            endpoint: '/DatosGenerales/BuscarC',
            detailEndpoint: '/DatosGenerales/BuscarCliente',
            modalId: 'vs-ped-modalBuscarCliente',
            inputId: 'vs-ped-inputBuscarCliente',
            resultsId: 'vs-ped-listaResultadosClientes',
            pageSizeId: 'vs-ped-pageSizeClientes',
            btnClearId: 'vs-ped-btnLimpiarClientes',
            spinnerId: 'vs-ped-spinnerClientes',
            paginationId: 'vs-ped-paginationClientes',
            recordsFromId: 'vs-ped-recordsFromClientes',
            recordsToId: 'vs-ped-recordsToClientes',
            totalRecordsId: 'vs-ped-totalRecordsClientes',
            shouldCloseOnSelect: true
        });

        this.clienteManager.onPedidosSelect = (cliente) => {
            document.getElementById('vs-ped-cliente').value = cliente.id || '';
            document.getElementById('vs-ped-rfc').value = cliente.rfc || '';
            document.getElementById('vs-ped-info-proveedor').value =
                [cliente.dir, cliente.col, cliente.pob, cliente.cp]
                    .filter(x => x)
                    .join(',\n');

            asignarVendedorSucursal('vs-ped-vendedor', cliente);
            const fpagoInstance = tomManager.getInstance('vs-ped-forma-pago');
            if (fpagoInstance && cliente.forma_pago) {
                fpagoInstance.setValue(cliente.forma_pago, true);
            }
            const cfdiInstance = tomManager.getInstance('vs-ped-uso-cfdi');
            if (cfdiInstance && cliente.uso_sugerido) {
                cfdiInstance.setValue(cliente.uso_sugerido, true);
            }
            const totalActual = PedidosApp.productManagerInstance?.totales?.total || 0;
            mostrarBannerCredito(cliente, totalActual);

            // El cliente define la lista de precios y las reglas por cliente: se manda
            // explícito al buscar productos y se recalcula lo ya capturado, que antes se
            // quedaba con los precios del cliente anterior.
            PedidosApp.clienteIdActual = parseInt(cliente.id_cliente, 10) || 0;
            PedidosApp.clienteCveResuelta = cliente.id || '';
            PedidosApp.recotizarPartidas(cliente.descripcion);
        };

        this.productoManager = new PedidosDataManager({
            endpoint: '/DatosGenerales/BuscarP',
            detailEndpoint: '/DatosGenerales/BuscarProducto',
            modalId: 'vs-ped-modalBuscarProducto',
            inputId: 'vs-ped-inputBuscarProducto',
            resultsId: 'vs-ped-listaResultadosProductos',
            pageSizeId: 'vs-ped-pageSizeProductos',
            btnClearId: 'vs-ped-btnLimpiarProductos',
            spinnerId: 'vs-ped-spinnerProductos',
            paginationId: 'vs-ped-paginationProductos',
            recordsFromId: 'vs-ped-recordsFromProductos',
            recordsToId: 'vs-ped-recordsToProductos',
            totalRecordsId: 'vs-ped-totalRecordsProductos',
            shouldCloseOnSelect: false
        });

        this.productManagerInstance = new PedidosProductManager();

        this.productoManager.getExtraParams = async () => ({ clienteId: await this.asegurarClienteId() });
        this.productoManager.onPedidosSelect = (producto) => {
            this.productManagerInstance.agregarPedidosProducto(producto);
        };

        this.documentoManager = new PedidosDocumentoManager({
            endpoint: '/DatosGenerales/BuscarVSD',
            detailEndpoint: '/DatosGenerales/BuscarDocumento',
            modalId: 'vs-ped-modalBuscarDocumentos',
            inputId: 'vs-ped-inputBuscarDocumento',
            resultsId: 'vs-ped-listaResultadosDocumentos',
            pageSizeId: 'vs-ped-pageSizeDocumentos',
            btnClearId: 'vs-ped-btnLimpiarDocumentos',
            spinnerId: 'vs-ped-spinnerDocumentos',
            paginationId: 'vs-ped-paginationDocumentos',
            recordsFromId: 'vs-ped-recordsFromDocumentos',
            recordsToId: 'vs-ped-recordsToDocumentos',
            totalRecordsId: 'vs-ped-totalRecordsDocumentos',
            shouldCloseOnSelect: true,
            filtros: {}
        });

        const filtroEstado = document.getElementById('vs-ped-filtroEstado');
        const filtroFecha = document.getElementById('vs-ped-filtroFecha');

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

        const tipoMovSelect = document.getElementById('vs-ped-tipo-docto-mov');
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
        document.getElementById('vs-ped-sucursal').value = documento.sucursal || '';
        document.getElementById('vs-ped-almacen').value = documento.almacen || '';
        document.getElementById('vs-ped-documentid').value = documento.id_encabezado || '';

        document.getElementById('vs-ped-cliente').value = documento.cli_prov || '';
        document.getElementById('vs-ped-rfc').value = documento.rfc || '';
        document.getElementById('vs-ped-info-proveedor').value = documento.info_cli || '';
        document.getElementById('vs-ped-paridad').value = documento.par || '';

        const vendedorInstance = tomManager.getInstance('vs-ped-vendedor');
        if (vendedorInstance && (documento.vdr_cpr || documento.cve_vdr)) {
            vendedorInstance.setValue(documento.vdr_cpr || documento.cve_vdr, true);
        }

        const monedaInstance = tomManager.getInstance('vs-ped-moneda');
        if (monedaInstance && (documento.ccy || documento.moneda)) {
            monedaInstance.setValue(documento.ccy || documento.moneda, true);
        }

        const cfdiInstance = tomManager.getInstance('vs-ped-uso-cfdi');
        if (cfdiInstance && (documento.usoCfdi || documento.uso_cfdi || documento.uso_sugerido)) {
            cfdiInstance.setValue(documento.usoCfdi || documento.uso_cfdi || documento.uso_sugerido, true);
        }

        // Al cargar un documento manda su propia forma de pago (f_pago). La sugerida del
        // cliente solo se aplica al seleccionarlo en el buscador, no aquí: si el documento
        // no la trae es mejor dejarla vacía que heredar una distinta a la capturada.
        const formaPagoInstance = tomManager.getInstance('vs-ped-forma-pago');
        if (formaPagoInstance && documento.f_pago) {
            formaPagoInstance.setValue(documento.f_pago, true);
        }

        const metodoPagoInstance = tomManager.getInstance('vs-ped-metodo-pago');
        if (metodoPagoInstance && (documento.metodoPago || documento.metodo_pago)) {
            metodoPagoInstance.setValue(documento.metodoPago || documento.metodo_pago, true);
        }
        // El método de pago manda (PPD = crédito). Si el documento viene sin mdp se usa
        // tipo_proceso, donde se guarda "…_credito" / "…_contado".
        const mdpDoc = (documento.mdp || '').toString().trim().toUpperCase();
        const procesoDoc = (documento.tipo_proceso || '').toString().toLowerCase();
        if (mdpDoc || procesoDoc) {
            const metodoPagoEl = document.getElementById('vs-ped-metodo-pago');
            const tipoPago = mdpDoc
                ? (mdpDoc === 'PPD' ? 'credito' : 'contado')
                : (procesoDoc.includes('credito') ? 'credito' : 'contado');

            if (metodoPagoEl) metodoPagoEl.value = mdpDoc || (tipoPago === 'credito' ? 'PPD' : 'PUE');
            selectTipoPagoPed(tipoPago);
        }
        // Una cotización no trae condiciones de pago (se capturan a partir del pedido), pero
        // al recargar un pedido ya guardado sí: en ese caso mandan las suyas sobre el plazo
        // del catálogo del cliente y no se recalcula la fecha, que ya está pactada.
        document.getElementById('vs-ped-limite-credito').value = documento.lim_crd || '';
        document.getElementById('vs-ped-plazo').value =
            parseInt(documento.pl_dias) > 0 ? documento.pl_dias : (documento.pl_crd || '');
        if (documento.fecha_pago) {
            document.getElementById('vs-ped-fecha-pago').value = documento.fecha_pago;
        } else {
            calcularFechaPagoPed(); // ← calcula desde hoy + plazo
        }
        document.getElementById('vs-ped-concepto').value = documento.coment1 || '';
        document.getElementById('vs-ped-comentarios').value = documento.coment_aut || '';

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

            // El documento trae el precio pactado pero no los límites de la regla. Se
            // consultan aparte para que el pedido conozca su piso y su tope sin
            // re-precificar lo que ya se cotizó. El cliente viene del documento, así que
            // hay que resolverlo antes: es la vía por la que se llena en la práctica.
            PedidosApp.asegurarClienteId()
                .then(clienteId => ReglasPrecio.hidratarLimites(
                    this.productManagerInstance.productos,
                    clienteId,
                    '/DatosGenerales/BuscarProducto'))
                .then(n => { if (n > 0) this.productManagerInstance.renderPedidosTable(); });
        }

        const fleteInput = document.getElementById('vs-ped-flete-val');
        if (fleteInput && documento.flete) {
            const chkFlete = document.getElementById("vs-ped-flete");
            chkFlete.checked = true;
            chkFlete.dispatchEvent(new Event("change"));
            fleteInput.value = documento.flete;
        }

        this.productManagerInstance.calcularPedidosTotales();

        const ordenCompraInput = document.getElementById('vs-ped-orden-compra-val');
        if (ordenCompraInput && documento.ordencompra) {
            const chkOrdenCompra = document.getElementById("vs-ped-orden-compra-check");
            chkOrdenCompra.checked = true;
            chkOrdenCompra.dispatchEvent(new Event("change"));
            ordenCompraInput.value = documento.ordencompra;
        }

        this.productManagerInstance.calcularPedidosTotales();

        const tipoMovSelect = document.getElementById('vs-ped-tipo-docto-mov');
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

        // Si este documento ya fue autorizado por gerencia, no se vuelve a bloquear.
        window.CreditoVentas?.refrescarAutorizacion('vs-ped');
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
            if (element.id !== 'vs-ped-tipo-docto-mov') {
                element.disabled = true;
                element.classList.add('vs-ped-disabled-field');
            }
        });

        const btnBuscarCliente = pedidoContenedor.querySelector('[data-bs-target="#vs-ped-modalBuscarCliente"]');
        const btnBuscarProducto = pedidoContenedor.querySelector('[data-bs-target="#vs-ped-modalBuscarProducto"]');
        if (btnBuscarCliente) btnBuscarCliente.disabled = true;
        if (btnBuscarProducto) btnBuscarProducto.disabled = true;

        pedidoContenedor.querySelectorAll('.vs-ped-btn-eliminar').forEach(btn => btn.disabled = true);

        if (window.tomManager) {
            ['vs-ped-vendedor', 'vs-ped-moneda', 'vs-ped-uso-cfdi', 'vs-ped-forma-pago'].forEach(id => {
                const instance = window.tomManager.instances?.get(id);
                if (instance) instance.disable();
            });
        }

        const btn = pedidoContenedor.querySelector('.vs-ped-btn-submit');
        if (btn) btn.disabled = false;

        const btnDocConsulta = pedidoContenedor.querySelector('.vs-ped-floating-btn');
        if (btnDocConsulta) btnDocConsulta.disabled = false;

        const btnPreview = pedidoContenedor.querySelector('.vs-ped-ticket-btn');
        if (btnPreview) btnPreview.disabled = false;
    },

    habilitarPedidosFormulario() {
        const pedidoContenedor = document.getElementById('pedido-contenedor');
        if (!pedidoContenedor) return;

        pedidoContenedor.querySelectorAll('input, select, textarea, button').forEach(element => {
            element.disabled = false;
            element.classList.remove('vs-ped-disabled-field');
        });

        const btnBuscarCliente = pedidoContenedor.querySelector('[data-bs-target="#vs-ped-modalBuscarCliente"]');
        const btnBuscarProducto = pedidoContenedor.querySelector('[data-bs-target="#vs-ped-modalBuscarProducto"]');
        if (btnBuscarCliente) btnBuscarCliente.disabled = false;
        if (btnBuscarProducto) btnBuscarProducto.disabled = false;

        if (window.tomManager) {
            ['vs-ped-vendedor', 'vs-ped-moneda', 'vs-ped-uso-cfdi', 'vs-ped-forma-pago'].forEach(id => {
                const instance = window.tomManager.instances?.get(id);
                if (instance) instance.enable();
            });
        }

        const tipoMovimiento = document.getElementById('vs-ped-tipo-docto-mov')?.value;
        const btn = pedidoContenedor.querySelector('.vs-ped-btn-submit');
        if (btn) {
            btn.innerHTML = tipoMovimiento === 'modificacion'
                ? '<i class="fas fa-save"></i> Actualizar Pedido'
                : '<i class="fas fa-save"></i> Guardar Pedido';
        }

        // Re-aplicar bloqueo de descuento/precio si los tokens no existen
        this.productManagerInstance?.renderPedidosTable();

        // Habilitar el formulario reactiva el submit: re-evaluar por si el crédito lo bloquea.
        window.CreditoVentas?.evaluar('vs-ped');
    }
};
function calcularFechaPagoPed() {
    const plazo = parseInt(document.getElementById('vs-ped-plazo')?.value) || 0;
    const fechaPagoInput = document.getElementById('vs-ped-fecha-pago');
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
// 7. CRÉDITO
// ============================================
// La lógica vive en Scripts/Ventas/credito-ventas.js (CreditoVentas), compartida por
// cotización, pedido, remisión y factura. Estos wrappers conservan los nombres que ya
// usaba el resto del archivo.

function mostrarBannerCredito(cliente) {
    window.CreditoVentas?.setCliente('vs-ped', cliente);
}

function _validarCreditoConTotalPedido() {
    window.CreditoVentas?.evaluar('vs-ped');
}

// ============================================
// 8. ARRANQUE
// ============================================
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => {
        PedidosApp.initPedidos();

        const fleteInput = document.getElementById('vs-ped-flete-val');
        if (fleteInput) {
            fleteInput.addEventListener('input', () => { /* calcularTotales se llama desde el ProductManager */ });
            fleteInput.addEventListener('change', () => { });
        }
    });
} else {
    PedidosApp.initPedidos();
}

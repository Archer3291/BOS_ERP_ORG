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
            console.log("documentos: " + item)
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
// 3. GESTOR DE PRODUCTOS
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
            table: document.getElementById('vn-rem-productosTable'),
            thead: document.querySelector('#vn-rem-productosTable')?.closest('table')?.querySelector('thead'),
            subtotal1Display: document.getElementById('vn-rem-total-subtotal1-display'),
            descuentoDisplay: document.getElementById('vn-rem-total-descuento-display'),
            fleteDisplay: document.getElementById('vn-rem-total-flete-display'),
            subtotal2Display: document.getElementById('vn-rem-total-subtotal2-display'),
            ivaDisplay: document.getElementById('vn-rem-iva-display'),
            importeDisplay: document.getElementById('vn-rem-importe-display')
        };

        this.initRemisionesProductManager();
    }

    initRemisionesProductManager() {
        this.dom.table?.addEventListener('input', (e) => {
            const input = e.target;
            if (input.classList.contains('vn-rem-cantidad') ||
                input.classList.contains('vn-rem-precio') ||
                input.classList.contains('vn-rem-descuento') ||        
                input.classList.contains('vi-ped-comentario')) {
                const tr = input.closest('tr');
                const productoId = tr?.dataset.productoId;
                if (productoId) this.actualizarRemisionesFila(productoId);
            }
        });

        this.dom.table?.addEventListener('click', (e) => {
            const btn = e.target.closest('.vn-rem-btn-eliminar');
            if (btn) {
                const tr = btn.closest('tr');
                const productoId = tr?.dataset.productoId;
                if (productoId) this.eliminarRemisionesProducto(productoId);
            }
        });

        // Event listener para flete
        document.getElementById('vn-rem-flete-val')?.addEventListener('input', () => {
            this.calcularRemisionesTotales();
        });

        // Event listener para anticipo
        document.getElementById('vn-rem-monto-anticipo')?.addEventListener('input', () => {
            this.calcularRemisionesTotales();
        });
    }

    agregarRemisionesProducto(producto) {
        const productoExistente = this.productos.find(p => p.productoId === producto.id);

        if (productoExistente) {
            productoExistente.cantidad += 1;

            const tr = this.dom.table.querySelector(`tr[data-producto-id="${productoExistente.id}"]`);
            if (tr) {
                const inputCantidad = tr.querySelector('.vn-rem-cantidad');
                inputCantidad.value = productoExistente.cantidad;

                const importe = this.calcularRemisionesImporte(productoExistente);
                tr.querySelector('.vn-rem-importe').textContent = importe.toFixed(2);

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
            // precio, precioMinimo, descuento sugerido y tope salen de la regla vigente.
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
        const thead = this.dom.thead;
        if (!tbody) return;

        // Limpiar mensaje vacío si existe
        const emptyRow = tbody.querySelector('.vn-rem-empty-state');
        if (emptyRow) emptyRow.remove();

        // Limpiar tabla
        tbody.innerHTML = '';

        // Opcional: ocultar columna de descuento en <thead>
        if (thead) {
            const descuentoTh = thead.querySelector('.vn-rem-th-descuento');
            if (descuentoTh) descuentoTh.style.display = (rol === 'Gerente') ? '' : 'none';
        }

        if (this.productos.length === 0) {
            tbody.innerHTML = `
            <tr class="vn-rem-empty-state">
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
            const importe = this.calcularRemisionesImporte(p);
            const tr = document.createElement('tr');
            tr.dataset.productoId = p.id;

            // Precio y descuento se mantienen en el DOM (para calcular importe y guardar) pero
            // ocultos: el usuario de remisión no debe ver ni editar precios/descuentos.
            const descuentoCell = `<td class="d-none">
         <input type="number" class="vn-rem-product-input vn-rem-descuento" value="${p.descuento}" min="0" max="${(p.descuentoMaximo != null ? p.descuentoMaximo : 100).toFixed(2)}" step="0.01" readonly>
       </td>`;

            tr.innerHTML = `
            <td class="text-center fw-bold">${index + 1}</td>
            <td>${this.escapeRemisionesHtml(p.productoId)}</td>
            <td style="max-width: 200px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis;">
                ${this.escapeRemisionesHtml(p.descripcion)}
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
                <input type="number" class="vn-rem-product-input vn-rem-cantidad" value="${p.cantidad}" min="0" step="0.01">
            </td>
            <td>${this.escapeRemisionesHtml(p.unidad)}</td>
            <td class="d-none">
                <input type="number" class="vn-rem-product-input vn-rem-precio" value="${p.precio.toFixed(2)}" min="${(p.precioMinimo || 0).toFixed(2)}" step="0.01">
            </td>
            ${descuentoCell}
            <td class="vn-rem-importe fw-bold d-none">${importe.toFixed(2)}</td>
            <td>
                <input type="text" class="vi-ped-product-input vi-ped-comentario" value="${this.escapeRemisionesHtml(p.comentario)}" placeholder="Comentario...">
            </td>
            <td>
                <button type="button" class="vn-rem-btn-delete vn-rem-btn-eliminar">
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

    actualizarRemisionesFila(productoId) {
        const producto = this.productos.find(p => p.id === productoId);
        if (!producto) return;

        const tr = this.dom.table.querySelector(`tr[data-producto-id="${productoId}"]`);
        if (!tr) return;

        producto.cantidad = parseFloat(tr.querySelector('.vn-rem-cantidad').value) || 0;
        producto.comentario = tr.querySelector('.vi-ped-comentario').value || '';

        // ── Precio y descuento contra la regla vigente ────────────────────────
        const precioInput = tr.querySelector('.vn-rem-precio');
        if (precioInput) {
            const res = ReglasPrecio.validarPrecio(
                producto,
                parseFloat(precioInput.value) || 0,
                !!vnRemAuth.precioToken);

            producto.precio = res.valor;
            if (res.aviso) precioInput.value = res.valor.toFixed(2);
            ReglasPrecio.avisar(res.aviso);
        }

        const descuentoInput = tr.querySelector('.vn-rem-descuento');
        if (descuentoInput) {
            const res = ReglasPrecio.validarDescuento(
                producto,
                parseFloat(descuentoInput.value) || 0,
                !!vnRemAuth.descuentoToken);

            producto.descuento = res.valor;
            if (res.aviso) descuentoInput.value = res.valor.toFixed(2);
            ReglasPrecio.avisar(res.aviso);
        }

        // PRECIO_FIJO: el piso es sobre el neto, no sobre el unitario.
        const piso = ReglasPrecio.ajustarPorPisoNeto(producto);
        if (piso.aviso) {
            producto.descuento = piso.descuento;
            if (descuentoInput) descuentoInput.value = piso.descuento.toFixed(2);
            ReglasPrecio.avisar(piso.aviso);
        }

        const importe = this.calcularRemisionesImporte(producto);
        tr.querySelector('.vn-rem-importe').textContent = importe.toFixed(2);

        this.calcularRemisionesTotales();
    }

    eliminarRemisionesProducto(productoId) {
        this.productos = this.productos.filter(p => p.id !== productoId);
        this.renderRemisionesTable();
        this.calcularRemisionesTotales();
    }

    calcularRemisionesImporte(producto) {
        return producto.cantidad * producto.precio * (1 - producto.descuento / 100);
    }

    calcularRemisionesTotales() {
        // Detectar tipo de operación (contado, crédito o anticipo)
        const tipoPago = document.getElementById('vn-rem-toggle-pago')?.getAttribute('data-state') || 'contado';
        const flete = parseFloat(document.getElementById('vn-rem-flete-val')?.value) || 0;
        let subtotal1 = 0, descuento = 0, subtotal2 = 0, iva = 0, total = 0;

        if (tipoPago === 'anticipo') {
            // Modo ANTICIPO
            const montoAnticipo = parseFloat(document.getElementById('vn-rem-monto-anticipo')?.value) || 0;

            subtotal1 = montoAnticipo;
            descuento = 0;
            subtotal2 = montoAnticipo;
            iva = montoAnticipo * 0.16;
            total = subtotal2 + iva;

        } else {
            // Modo NORMAL (contado o crédito)
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
        this.updateRemisionesDisplay(this.dom.importeDisplay, total, '$');

        this.updateRemisionesHiddenField('vn-rem-total-subtotal1', subtotal1);
        this.updateRemisionesHiddenField('vn-rem-total-descuento', descuento);
        this.updateRemisionesHiddenField('vn-rem-total-flete', flete);
        this.updateRemisionesHiddenField('vn-rem-total-subtotal2', subtotal2);
        this.updateRemisionesHiddenField('vn-rem-iva', iva);
        this.updateRemisionesHiddenField('vn-rem-importe', total);

        window.CreditoVentas?.evaluar('vn-rem');
    }

    updateRemisionesHiddenField(id, value) {
        const field = document.getElementById(id);
        if (field) {
            field.value = value.toFixed(2);
        }
    }

    updateRemisionesDisplay(element, value, prefix = '$') {
        if (element) {
            element.textContent = `${prefix}${value.toFixed(2)}`;
        }
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
// 4. GESTOR DE FORMULARIO
// ============================================
class RemisionesFormManager {
    constructor(productManager) {
        this.productManager = productManager;
        this.form = document.getElementById('vn-rem-formCotizacion');
        this.btnSubmit = document.querySelector('.vn-rem-btn-submit');
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
        const toggle = document.getElementById('vn-rem-toggle-pago');
        const tipoPago = toggle?.getAttribute('data-state') || 'contado';

        const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
        if (token) formData.append('__RequestVerificationToken', token);

        // Campos generales
        const camposGenerales = [
            'vn-rem-tipo-docto-mov',
            'vn-rem-folio', 'vn-rem-documentid', 'vn-rem-cliente',
            'vn-rem-rfc', 'vn-rem-vendedor', 'vn-rem-moneda',
            'vn-rem-paridad', 'vn-rem-concepto',
            'vn-rem-metodo-pago', 'vn-rem-forma-pago',
            'vn-rem-uso-cfdi', 'vn-rem-comentarios'
        ];

        camposGenerales.forEach(id => {
            const el = document.getElementById(id);
            if (el) formData.append(id.replace('vn-rem-', ''), el.value || '');
        });
        formData.set('sucursal', suc.getValue());
        formData.set('almacen', alm.getValue());
        // Campos condicionales
        if (tipoPago === 'credito') {
            formData.append('limiteCredito', document.getElementById('vn-rem-limite-credito')?.value || '');
            formData.append('plazo', document.getElementById('vn-rem-plazo')?.value || '');
            formData.append('fechaPago', document.getElementById('vn-rem-fecha-pago')?.value || '');
        }

        if (tipoPago === 'anticipo') {
            formData.append('fechaAnticipo', document.getElementById('vn-rem-fecha-anticipo')?.value || '');
        }
        formData.append('ordenCompra', document.getElementById('vn-rem-orden-compra-val')?.value || '');

        // Verificación por escáner: el backend la revalida cuando el setting
        // `escaneo_remision_obligatorio` está activo (VNRemision.Guardar).
        formData.append('escaneoJSON',
            JSON.stringify(window.EscaneoRemision?.datos('vn-rem') || []));

        // Productos
        const productos = this.productManager.getRemisionesProductosData();
        if (tipoPago !== 'anticipo') {
            formData.append('productosJSON', JSON.stringify(productos));
        }

        // Tokens de autorización: el servidor los necesita para saber si este usuario
        // puede vender por debajo del piso de la regla.
        formData.append('descuentoToken', vnRemAuth.descuentoToken || '');
        formData.append('precioToken', vnRemAuth.precioToken || '');

        // Totales
        const totales = this.productManager.totales;
        formData.append('subtotal1', totales.subtotal1.toFixed(2));
        formData.append('descuento', totales.descuento.toFixed(2));
        formData.append('flete', totales.flete.toFixed(2));
        formData.append('subtotal2', totales.subtotal2.toFixed(2));
        formData.append('iva', totales.iva.toFixed(2));
        formData.append('total', totales.total.toFixed(2));

        formData.append('tipo', tipoPago);
        // Identifica la autorización de gerencia cuando el documento aún no existe.
        formData.append('creditoToken', window.CreditoVentas?.token('vn-rem') || '');

        return formData;
    }

    validarRemisionesFormulario() {
        const errores = [];

        const togglePago = document.getElementById('vn-rem-toggle-pago');
        const tipoPago = togglePago ? togglePago.getAttribute('data-state') : 'contado';

        // Cliente
        const cliente = document.getElementById('vn-rem-cliente')?.value;
        if (!cliente || cliente.trim() === '') {
            errores.push('Debe seleccionar un cliente');
        }

        // Productos
        const productos = this.productManager.getRemisionesProductosData();
        if (tipoPago !== 'anticipo' && productos.length === 0) {
            errores.push('Debe agregar al menos un producto');
        }

        // Vendedor
        const vendedor = document.getElementById('vn-rem-vendedor')?.value;
        if (!vendedor || vendedor === 'Seleccionar vendedor') {
            errores.push('Debe seleccionar un vendedor');
        }

        // RFC
        const rfcInput = document.getElementById('vn-rem-rfc');
        const rfc = rfcInput ? rfcInput.value.trim() : '';
        if (!rfc) {
            errores.push('Debe ingresar el RFC');
        }

        // Moneda
        const monedaSelect = tomManager.getInstance('vn-rem-moneda');
        const monedaValue = monedaSelect?.getValue();
        if (!monedaValue) {
            errores.push('Debe seleccionar una Moneda');
        }

        // Uso CFDI
        const usoCfdiSelect = tomManager.getInstance('vn-rem-uso-cfdi');
        const usoCfdiValue = usoCfdiSelect?.getValue();
        if (!usoCfdiValue) {
            errores.push('Debe seleccionar un Uso CFDI');
        }

        // Forma de pago
        const formaPagoSelect = tomManager.getInstance('vn-rem-forma-pago');
        const formaPagoValue = formaPagoSelect?.getValue();
        if (!formaPagoValue) {
            errores.push('Debe seleccionar una Forma de Pago');
        }

        // Fecha de pago
        const fechaPagoInput = document.getElementById('vn-rem-fecha-pago');
        const fechaPago = fechaPagoInput ? fechaPagoInput.value.trim() : '';

        if (tipoPago === 'credito' && !fechaPago) {
            errores.push('Debe ingresar la Fecha de Pago (solo para crédito)');
        }

        // Crédito: solo agrega errores cuando la venta es a crédito y excede el límite.
        errores.push(...(window.CreditoVentas?.validarAntesDeGuardar('vn-rem') || []));

        return errores;
    }

    obtenerRemisionesConfiguracionPorTipo(tipoMovimiento) {
        const configuraciones = {
            'alta': {
                endpoint: '/VNRemision/Guardar',
                metodo: 'POST',
                mensajeExito: 'Remisión creada exitosamente',
                textoCarga: 'Guardando...',
                validarProductos: true
            },
            'modificacion': {
                endpoint: '/VNRemision/Modificar',
                metodo: 'PUT',
                mensajeExito: 'Remisión modificada exitosamente',
                textoCarga: 'Actualizando...',
                validarProductos: true
            },
            'consulta': {
                endpoint: '/VNRemision/Consultar',
                metodo: 'GET',
                mensajeExito: 'Consulta realizada exitosamente',
                textoCarga: 'Consultando...',
                validarProductos: false
            }
        };

        return configuraciones[tipoMovimiento] || configuraciones['alta'];
    }

    async enviarRemisionesCotizacion() {
        const tipoMovimiento = document.getElementById('vn-rem-tipo-docto-mov')?.value || 'alta';
        const config = this.obtenerRemisionesConfiguracionPorTipo(tipoMovimiento);

        // 🔹 1. Validar formulario antes de continuar
        const errores = this.validarRemisionesFormulario();
        if (errores.length > 0) {
            toastMixin?.fire({ icon: 'error', title: errores.join(', ') });
            return;
        }

        // 🔹 1.b Verificación de mercancía por escáner. Solo detiene el guardado cuando
        //        el setting `escaneo_remision_obligatorio` está activo: en ese caso abre
        //        el modal y no continúa hasta que todas las partidas estén escaneadas.
        const verificado = await (window.EscaneoRemision?.asegurar('vn-rem') ?? true);
        if (!verificado) {
            toastMixin?.fire({
                icon: 'warning',
                title: 'La remisión no se generó: falta verificar la mercancía con el escáner'
            });
            return;
        }

        // 🔹 2. Obtener centros de costos (DESHABILITADO TEMPORALMENTE)
        // const centrosResponse = await GetData({ path: '/DatosGenerales/DatosCentroCostos' });

        // if (!centrosResponse.success) {
        //     await Swal.fire({
        //         icon: 'error',
        //         title: centrosResponse.message || 'Error al cargar centros de costos'
        //     });
        //     return;
        // }

        // const centros = centrosResponse.data || centrosResponse.result || [];
        // if (centros.length === 0) {
        //     await Swal.fire({
        //         icon: 'warning',
        //         title: 'No hay centros de costos disponibles'
        //     });
        //     return;
        // }

        // 🔹 3. Convertir array de objetos a formato que SweetAlert entiende (DESHABILITADO TEMPORALMENTE)
        // const inputOptions = centros.reduce((acc, c) => {
        //     acc[c.areaid] = c.nombre;
        //     return acc;
        // }, {});

        // 🔹 4. Mostrar el select de centros (DESHABILITADO TEMPORALMENTE)
        // const { value: centroSeleccionado } = await Swal.fire({
        //     title: 'Selecciona el Centro de Costos',
        //     input: 'select',
        //     inputOptions,
        //     inputPlaceholder: 'Selecciona un centro de costos',
        //     showCancelButton: true,
        //     confirmButtonText: 'Continuar',
        //     cancelButtonText: 'Cancelar',
        //     inputValidator: (value) => {
        //         if (!value) return 'Por favor selecciona un centro de costos';
        //     }
        // });

        // if (!centroSeleccionado) return; // usuario canceló o no seleccionó nada

        // 🔹 5. Bloquear botón
        const originalText = this.btnSubmit.innerHTML;
        this.btnSubmit.disabled = true;
        this.btnSubmit.innerHTML = `<i class="fas fa-spinner fa-spin"></i> ${config.textoCarga}`;

        try {
            // 🔹 6. Crear FormData (CentroCostosId deshabilitado temporalmente)
            const formData = this.recopilarRemisionesDatosFormulario();
            // formData.append('CentroCostosId', centroSeleccionado);

            const fetchConfig = {
                method: config.metodo.toUpperCase(),
                body: formData
            };

            // 🔹 7. Enviar primer fetch
            const response = await fetch(config.endpoint, fetchConfig);
            const result = await response.json();

            if (result.success) {
                console.log("✅ Remisión guardada correctamente:", result);

                const encId = result.enc_id || result.IdEncabezado || null;

                if (window.Swal) {
                    await Swal.fire({
                        icon: 'success',
                        title: `${result.message || ''} Folio: ${result.folio_generado || ''}`.trim() || config.mensajeExito,
                        confirmButtonText: 'OK',
                        allowOutsideClick: false,
                        allowEscapeKey: false
                    });
                }

                // 🔄 AQUÍ recargas la página
                window.location.reload();

                if (encId) {
                    console.log('📄 ID de encabezado generado:', encId);
                }
            }
            else {
                toastMixin?.fire({
                    icon: 'error',
                    title: result.message || 'Ocurrió un error en el servidor al guardar la remisión'
                });
            }

        } catch (error) {
            console.error('❌ Error enviando remisión:', error);
            toastMixin?.fire({
                icon: 'error',
                title: error.message || 'Ocurrió un error al enviar la remisión'
            });
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

        document.getElementById('vn-rem-cliente').value = '';
        document.getElementById('vn-rem-rfc').value = '';
        document.getElementById('vn-rem-info-proveedor').value = '';
    }

    verRemisionesDatosFormulario() {
        const formData = this.recopilarRemisionesDatosFormulario();
        const obj = {};
        for (let [key, value] of formData.entries()) {
            obj[key] = value;
        }
        console.log('Datos de remisión a enviar:', obj);
        return obj;
    }
}

// ============================================
// 5. APLICACIÓN PRINCIPAL
// ============================================
// Tokens de autorización del usuario. Nacionales no tiene candado por contraseña sobre
// los campos: con esto, quien tenga permiso directo puede bajar del piso y el resto no.
const vnRemAuth = { precioToken: null, descuentoToken: null };

const RemisionesApp = {
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
    clienteCveResuelta: '',

    /**
     * Devuelve el id del cliente del documento, resolviéndolo desde la clave si hace falta.
     * En una remisión el cliente casi siempre llega al cargar el pedido de origen.
     */
    async asegurarClienteId() {
        const cve = document.getElementById('vn-rem-cliente')?.value || '';

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

    initRemisiones() {
        this.form = document.getElementById('vn-rem-formCotizacion');
        this.btnSubmit = document.querySelector('.vn-rem-btn-submit');

        ReglasPrecio.cargarPermisos().then(t => Object.assign(vnRemAuth, t));

        // Crédito: la remisión ya entrega mercancía a crédito, así que bloquea.
        window.CreditoVentas?.registrar({
            prefix: 'vn-rem',
            modo: 'bloquear',
            documento: 'remision',
            claseBanner: 'vn-credit-banner',
            endpointSolicitud: '/VNPedido/EnviarSolicitudGerente',
            getTipoPago: () =>
                document.getElementById('vn-rem-toggle-pago')?.getAttribute('data-state') || 'contado',
            totalId: 'vn-rem-importe',
            submitSelector: '.vn-rem-btn-submit',
            clienteInputId: 'vn-rem-cliente',
            folioId: 'vn-rem-folio',
            documentIdId: 'vn-rem-documentid',
            getProductos: () => RemisionesApp.productManagerInstance?.getRemisionesProductosData() || [],
            getTotales: () => RemisionesApp.productManagerInstance?.totales || {}
        });

        // Verificación de mercancía por escáner antes de aceptar la remisión.
        // Obligatoria u opcional según el setting `escaneo_remision_obligatorio`,
        // que el modal recibe renderizado en data-obligatorio.
        window.EscaneoRemision?.registrar({
            prefix: 'vn-rem',
            btnAbrirId: 'vn-rem-esc-abrir',
            getProductos: () => RemisionesApp.productManagerInstance?.getRemisionesProductosData() || []
        });

        // Inicializar gestor de clientes
        this.clienteManager = new RemisionesDataManager({
            endpoint: '/DatosGenerales/BuscarC',
            detailEndpoint: '/DatosGenerales/BuscarCliente',
            modalId: 'vn-rem-modalBuscarCliente',
            inputId: 'vn-rem-inputBuscarCliente',
            resultsId: 'vn-rem-listaResultadosClientes',
            pageSizeId: 'vn-rem-pageSizeClientes',
            btnClearId: 'vn-rem-btnLimpiarClientes',
            spinnerId: 'vn-rem-spinnerClientes',
            paginationId: 'vn-rem-paginationClientes',
            recordsFromId: 'vn-rem-recordsFromClientes',
            recordsToId: 'vn-rem-recordsToClientes',
            totalRecordsId: 'vn-rem-totalRecordsClientes',
            shouldCloseOnSelect: true
        });

        this.clienteManager.onRemisionesSelect = (cliente) => {
            document.getElementById('vn-rem-cliente').value = cliente.id || '';
            document.getElementById('vn-rem-rfc').value = cliente.rfc || '';
            document.getElementById('vn-rem-info-proveedor').value =
                [cliente.dir, cliente.col, cliente.pob, cliente.cp]
                    .filter(x => x)
                    .join(',\n');

            const vendedorInstance = tomManager.getInstance('vn-rem-vendedor');
            if (vendedorInstance && cliente.cve_vdr) {
                vendedorInstance.setValue(cliente.cve_vdr, true);
            }
            const fpagoInstance = tomManager.getInstance('vn-rem-forma-pago');
            if (fpagoInstance && cliente.forma_pago) {
                fpagoInstance.setValue(cliente.forma_pago, true);
            }
            const cfdiInstance = tomManager.getInstance('vn-rem-uso-cfdi');
            if (cfdiInstance && cliente.uso_sugerido) {
                cfdiInstance.setValue(cliente.uso_sugerido, true);
            }
            window.CreditoVentas?.setCliente('vn-rem', cliente);

            // El cliente define la lista de precios y las reglas por cliente: se manda
            // explícito al buscar productos y se recalcula lo ya capturado.
            RemisionesApp.clienteIdActual = parseInt(cliente.id_cliente, 10) || 0;
            RemisionesApp.clienteCveResuelta = cliente.id || '';
            RemisionesApp.recotizarPartidas(cliente.descripcion);
        };

        // Inicializar gestor de productos
        this.productoManager = new RemisionesDataManager({
            endpoint: '/DatosGenerales/BuscarP',
            detailEndpoint: '/DatosGenerales/BuscarProducto',
            modalId: 'vn-rem-modalBuscarProducto',
            inputId: 'vn-rem-inputBuscarProducto',
            resultsId: 'vn-rem-listaResultadosProductos',
            pageSizeId: 'vn-rem-pageSizeProductos',
            btnClearId: 'vn-rem-btnLimpiarProductos',
            spinnerId: 'vn-rem-spinnerProductos',
            paginationId: 'vn-rem-paginationProductos',
            recordsFromId: 'vn-rem-recordsFromProductos',
            recordsToId: 'vn-rem-recordsToProductos',
            totalRecordsId: 'vn-rem-totalRecordsProductos',
            shouldCloseOnSelect: false
        });

        // Instancia de ProductManager
        this.productManagerInstance = new RemisionesProductManager();

        this.productoManager.getExtraParams = async () => ({ clienteId: await this.asegurarClienteId() });
        this.productoManager.onRemisionesSelect = (producto) => {
            this.productManagerInstance.agregarRemisionesProducto(producto);
        };

        // Inicializar gestor de documentos.
        // Ahora lista PEDIDOS LISTOS (todos los surtidos completos) y al seleccionarlos
        // consolida los hijos normal/tubo/modula en una sola remisión.
        this.documentoManager = new RemisionesDocumentoManager({
            endpoint: '/VNRemision/BuscarPedidosListos',
            detailEndpoint: '/VNRemision/ObtenerPedidoParaRemision',
            modalId: 'vn-rem-modalBuscarDocumentos',
            inputId: 'vn-rem-inputBuscarDocumento',
            resultsId: 'vn-rem-listaResultadosDocumentos',
            pageSizeId: 'vn-rem-pageSizeDocumentos',
            btnClearId: 'vn-rem-btnLimpiarDocumentos',
            spinnerId: 'vn-rem-spinnerDocumentos',
            paginationId: 'vn-rem-paginationDocumentos',
            recordsFromId: 'vn-rem-recordsFromDocumentos',
            recordsToId: 'vn-rem-recordsToDocumentos',
            totalRecordsId: 'vn-rem-totalRecordsDocumentos',
            shouldCloseOnSelect: true,
            filtros: {}
        });

        // Configurar filtros de documentos
        const filtroEstado = document.getElementById('vn-rem-filtroEstado');
        const filtroFecha = document.getElementById('vn-rem-filtroFecha');

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
                // El endpoint devuelve un objeto con error:true cuando el pedido aún tiene
                // surtidos pendientes: no se debe cargar, solo avisar.
                if (documento && documento.error) {
                    if (window.Swal) {
                        Swal.fire({
                            icon: 'warning',
                            title: 'Pedido no disponible',
                            text: documento.message || 'El pedido aún no está listo para remisión.'
                        });
                    } else if (window.toastMixin) {
                        toastMixin.fire({ icon: 'warning', title: documento.message || 'Pedido no disponible' });
                    }
                    return;
                }

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

        // Inicializar gestor de formulario
        this.formManager = new RemisionesFormManager(this.productManagerInstance);

        // Observar cambios en tipo de movimiento
        const tipoMovSelect = document.getElementById('vn-rem-tipo-docto-mov');
        tipoMovSelect?.addEventListener('change', (e) => {
            this.manejarRemisionesCambioTipoMovimiento(e.target.value);
        });

        this.manejarRemisionesCambioTipoMovimiento(tipoMovSelect?.value || 'alta');

        console.log('Sistema de remisiones inicializado correctamente');

        window.verRemisionesDatosFormulario = () => this.formManager.verRemisionesDatosFormulario();

        //if (typeof rol !== undefined && rol !== 'Gerente') {
        //    this.deshabilitarRemisionesFormulario()
        //} else {
        //    this.habilitarRemisionesFormulario()
        //}
    },

    async cargarDocumentoCompleto(documento) {
        // Las partidas cambian por completo: lo verificado con el escáner ya no aplica.
        window.EscaneoRemision?.limpiar('vn-rem');

        // 1. Información general
        document.getElementById('vn-rem-sucursal').value = documento.sucursal || '';
        document.getElementById('vn-rem-almacen').value = documento.almacen || '';
        document.getElementById('vn-rem-documentid').value = documento.id_encabezado || '';

        // 2. Información del cliente
        document.getElementById('vn-rem-cliente').value = documento.cli_prov || '';
        document.getElementById('vn-rem-rfc').value = documento.rfc || '';
        document.getElementById('vn-rem-info-proveedor').value = documento.info_cli || '';
        document.getElementById('vn-rem-paridad').value = documento.par || '';

        // 3. TomSelect para vendedor
        const vendedorInstance = tomManager.getInstance('vn-rem-vendedor');
        if (vendedorInstance && documento.vdr_cpr) {
            vendedorInstance.setValue(documento.vdr_cpr, true);
        }

        // 4. TomSelect para moneda
        const monedaInstance = tomManager.getInstance('vn-rem-moneda');
        if (monedaInstance && documento.ccy) {
            monedaInstance.setValue(documento.ccy, true);
        }

        // 5. TomSelect para uso CFDI
        const cfdiInstance = tomManager.getInstance('vn-rem-uso-cfdi');
        if (cfdiInstance && documento.usoCfdi) {
            cfdiInstance.setValue(documento.usoCfdi, true);
        }

        // 6. TomSelect para forma de pago
        const formaPagoInstance = tomManager.getInstance('vn-rem-forma-pago');
        if (formaPagoInstance && documento.formaPago) {
            formaPagoInstance.setValue(documento.formaPago, true);
        }

        // TomSelect para Forma de Pago
        const formaFP = tomManager.getInstance('vn-rem-forma-pago');
        if (formaFP && documento.f_pago) {
            formaFP.setValue(documento.f_pago, true);
        }

        // TomSelect para CFDI
        const formaCDFI = tomManager.getInstance('vn-rem-uso-cfdi');
        if (formaCDFI && documento.cfdi) {
            formaCDFI.setValue(documento.cfdi, true);
        }

        // TomSelect para Sucursal
        const formaSucursal = tomManager.getInstance('vn-rem-sucursal');
        if (formaSucursal && documento.suc) {
            formaSucursal.setValue(documento.suc, false);
        }

        // TomSelect para Almacen
        const formaAlmacen = tomManager.getInstance('vn-rem-almacen');
        if (formaAlmacen && documento.alm) {
            formaAlmacen.setValue(documento.alm, true);
        }


        // 6.1 Sincronizar el toggle contado/crédito con lo que trae el documento.
        // El método de pago manda (PPD = crédito); si el documento viene sin mdp se usa
        // tipo_proceso, donde se guarda "…_credito" / "…_contado".
        const mdpDoc = (documento.mdp || '').toString().trim().toUpperCase();
        const procesoDoc = (documento.tipo_proceso || '').toString().toLowerCase();
        if (mdpDoc || procesoDoc) {
            const tipoPagoDoc = mdpDoc
                ? (mdpDoc === 'PPD' ? 'credito' : 'contado')
                : (procesoDoc.includes('credito') ? 'credito' : 'contado');
            setTipoPagoRemision(tipoPagoDoc);
        }

        // 7. Condiciones de pago
        // Mandan las condiciones capturadas en el pedido (pl_dias / fecha_pago); el plazo del
        // catálogo del cliente (pl_crd) es solo el respaldo para documentos viejos que se
        // guardaron sin ellas. Antes se usaba pl_crd siempre y la fecha nunca se cargaba,
        // así que al guardar la remisión reclamaba "Debe ingresar la Fecha de Pago".
        document.getElementById('vn-rem-limite-credito').value = documento.lim_crd || '';
        document.getElementById('vn-rem-plazo').value =
            parseInt(documento.pl_dias) > 0 ? documento.pl_dias : (documento.pl_crd || '');
        document.getElementById('vn-rem-metodo-pago').value =
            mdpDoc || (documento.mdp || '');
        document.getElementById('vn-rem-fecha-pago').value =
            documento.fecha_pago || documento.fechaPago || '';
        document.getElementById('vn-rem-concepto').value = documento.coment1 || '';
        document.getElementById('vn-rem-comentarios').value = documento.coment_aut || '';

        // 8. Cargar productos
        if (documento.productos && Array.isArray(documento.productos)) {
            this.productManagerInstance.productos = [];

            documento.productos.forEach(prod => {
                const productoData = {
                    id: `prod_${Date.now()}_${Math.random()}`,
                    productoId: prod.producto_id || prod.id,
                    descripcion: prod.descripcion,
                    existencia: prod.existencia,
                    cantidad: parseFloat(prod.cantidad) || 0,
                    precio: parseFloat(prod.precio) || 0,
                    // Referencia del precio pactado en el documento de origen.
                    precioOriginal: parseFloat(prod.precio) || 0,
                    descuento: parseFloat(prod.descuento) || 0,
                    unidad: prod.unidad || prod.udm || 'PZA',
                    existenciaGeneral: prod.existenciaGeneral || 0,
                    existenciaModular: prod.existenciaModular || 0,
                    comentario: prod.comentario || ''
                };
                this.productManagerInstance.productos.push(productoData);
            });

            this.productManagerInstance.renderRemisionesTable();

            // El documento trae el precio pactado pero no los límites de la regla.
            RemisionesApp.asegurarClienteId()
                .then(clienteId => ReglasPrecio.hidratarLimites(
                    this.productManagerInstance.productos,
                    clienteId,
                    '/DatosGenerales/BuscarProducto'))
                .then(n => { if (n > 0) this.productManagerInstance.renderRemisionesTable(); });
        }

        // 9. Cargar flete y recalcular totales
        const fleteInput = document.getElementById('vn-rem-flete-val');
        if (fleteInput && documento.flete) {
            const chkFlete = document.getElementById("vn-rem-flete");

            chkFlete.checked = true;
            chkFlete.dispatchEvent(new Event("change"));

            fleteInput.value = documento.flete;
        }

        const ordenCompraInput = document.getElementById('vn-rem-orden-compra-val');
        if (ordenCompraInput && documento.ordencompra) {
            const chkOrdenCompra = document.getElementById("vn-rem-orden-compra-check");
            chkOrdenCompra.checked = true;
            chkOrdenCompra.dispatchEvent(new Event("change"));
            ordenCompraInput.value = documento.ordencompra;
        }

        this.productManagerInstance.calcularRemisionesTotales();

        // 10. Cambiar tipo de movimiento a modificación
        const tipoMovSelect = document.getElementById('vn-rem-tipo-docto-mov');
        if (tipoMovSelect && documento.folio) {
            tipoMovSelect.value = 'modificacion';
            this.manejarRemisionesCambioTipoMovimiento('modificacion');
        }

        // Crédito del cliente que viene con el documento (solo se usa si es a crédito).
        if (documento.estatus_credito) {
            window.CreditoVentas?.setCliente('vn-rem', {
                id: documento.cli_prov,
                lim_crd: documento.lim_crd || 0,
                credito_usado: documento.credito_usado || 0,
                credito_disponible: documento.credito_disponible || 0,
                estatus_cliente: documento.estatus_cliente
            });
        }

        // Si el pedido de origen ya fue autorizado por gerencia, no se vuelve a bloquear.
        window.CreditoVentas?.refrescarAutorizacion('vn-rem');
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

        const formElements = remisionContenedor.querySelectorAll('input, select, textarea, button');
        formElements.forEach(element => {
            if (element.id !== 'vn-rem-tipo-docto-mov') {
                element.disabled = true;
                element.classList.add('vn-rem-disabled-field');
            }
        });

        const btnBuscarCliente = remisionContenedor.querySelector('[data-bs-target="#vn-rem-modalBuscarCliente"]');
        const btnBuscarProducto = remisionContenedor.querySelector('[data-bs-target="#vn-rem-modalBuscarProducto"]');
        if (btnBuscarCliente) btnBuscarCliente.disabled = true;
        if (btnBuscarProducto) btnBuscarProducto.disabled = true;

        const botonesEliminar = remisionContenedor.querySelectorAll('.vn-rem-btn-eliminar');
        botonesEliminar.forEach(btn => btn.disabled = true);

        if (window.tomManager) {
            ['vn-rem-vendedor', 'vn-rem-moneda', 'vn-rem-uso-cfdi', 'vn-rem-forma-pago'].forEach(id => {
                const instance = window.tomManager.instances?.get(id);
                if (instance) instance.disable();
            });
        }

        const btn = remisionContenedor.querySelector('.vn-rem-btn-submit');
        if (btn) {
            btn.disabled = false;
        }
        const btnDocConsulta = remisionContenedor.querySelector('.vn-rem-floating-btn');
        if (btnDocConsulta) {
            btnDocConsulta.disabled = false;
        }
        const btnPreview = remisionContenedor.querySelector('.vn-rem-ticket-btn');
        if (btnPreview) {
            btnPreview.disabled = false;
        }
    },

    habilitarRemisionesFormulario() {
        const remisionContenedor = document.getElementById('remision-contenedor');
        if (!remisionContenedor) return;

        const formElements = remisionContenedor.querySelectorAll('input, select, textarea, button');
        formElements.forEach(element => {
            element.disabled = false;
            element.classList.remove('vn-rem-disabled-field');
        });

        const btnBuscarCliente = remisionContenedor.querySelector('[data-bs-target="#vn-rem-modalBuscarCliente"]');
        const btnBuscarProducto = remisionContenedor.querySelector('[data-bs-target="#vn-rem-modalBuscarProducto"]');
        if (btnBuscarCliente) btnBuscarCliente.disabled = false;
        if (btnBuscarProducto) btnBuscarProducto.disabled = false;

        if (window.tomManager) {
            ['vn-rem-vendedor', 'vn-rem-moneda', 'vn-rem-uso-cfdi', 'vn-rem-forma-pago'].forEach(id => {
                const instance = window.tomManager.instances?.get(id);
                if (instance) instance.enable();
            });
        }

        const tipoMovimiento = document.getElementById('vn-rem-tipo-docto-mov')?.value;
        const btn = remisionContenedor.querySelector('.vn-rem-btn-submit');
        if (btn) {
            if (tipoMovimiento === 'modificacion') {
                btn.innerHTML = '<i class="fas fa-save"></i> Actualizar Remisión';
            } else {
                btn.innerHTML = '<i class="fas fa-save"></i> Guardar Remisión';
            }
        }
    }

};

// ============================================
// 6. FUNCIONES DE AUTENTICACIÓN
// ============================================
async function handleRemisionesDescuentoAuth(e) {
    if (!e.target.classList.contains('vn-rem-descuento')) return;
    await pedirRemisionesAutenticacion(e.target);
}

async function handleRemisionesDescuentoF8(e) {
    if (e.key !== 'F8') return;

    const input = document.activeElement;
    if (!input.classList.contains('vn-rem-descuento')) return;

    e.preventDefault();
    await pedirRemisionesAutenticacion(input);
}

async function pedirRemisionesAutenticacion(input) {
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
        RemisionesApp.initRemisiones();
        document.addEventListener('dblclick', handleRemisionesDescuentoAuth);
        document.addEventListener('keydown', handleRemisionesDescuentoF8);
        const fleteInput = document.getElementById('vn-rem-flete-val');
        if (!fleteInput) return;

        fleteInput.addEventListener('input', function () {
            //this.calcularTotales();
        });

        fleteInput.addEventListener('change', function () {
            //this.calcularTotales();
        });

    });
} else {
    RemisionesApp.initRemisiones();
    document.addEventListener('dblclick', handleRemisionesDescuentoAuth);
    document.addEventListener('keydown', handleRemisionesDescuentoF8);
}
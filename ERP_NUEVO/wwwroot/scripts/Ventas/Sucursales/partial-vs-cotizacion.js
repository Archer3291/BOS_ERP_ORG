let tomManager;
tomManager = new TomSelectManager();

/**
 * SISTEMA DE COTIZACIÓN OPTIMIZADO
 * JavaScript Vanilla con patrones modernos
 */

// ============================================
// 0. ESTADO GLOBAL DE AUTENTICACIÓN
// ============================================
const authState = {
    descuentoToken: null,
    precioToken: null
};

// ============================================
// 1. CLASE BASE PARA GESTIÓN DE DATOS (DRY)
// ============================================
class DataManager {
    constructor(config) {
        this.endpoint = config.endpoint;
        this.detailEndpoint = config.detailEndpoint;
        this.modalId = config.modalId;
        this.cache = new Map();
        this.cacheTimeout = 5 * 60 * 1000;
        this.abortController = null;
        this.shouldCloseOnSelect = config.shouldCloseOnSelect !== false;

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
        this.init();
    }

    init() {
        this.dom.modal?.addEventListener('show.bs.modal', () => this.onModalShow());
        this.dom.input?.addEventListener('input', (e) => this.handleSearch(e.target.value));
        this.dom.pageSize?.addEventListener('change', (e) => this.handlePageSizeChange(e.target.value));
        this.dom.btnClear?.addEventListener('click', () => this.handleClear());

        this.dom.results?.addEventListener('click', (e) => {
            const btn = e.target.closest('[data-select-id]');
            if (btn) this.handleSelect(btn.dataset.selectId);
        });

        this.dom.pagination?.addEventListener('click', (e) => {
            e.preventDefault();
            const btn = e.target.closest('[data-page]');
            if (btn && !btn.closest('.disabled')) {
                this.state.page = parseInt(btn.dataset.page);
                this.fetchData();
            }
        });
    }

    onModalShow() { this.handleClear(); this.fetchData(); }

    handleSearch(value) {
        clearTimeout(this.typingTimer);
        this.typingTimer = setTimeout(() => {
            this.state.lastSearch = value;
            this.state.page = 1;
            this.fetchData();
        }, 300);
    }

    handlePageSizeChange(value) {
        this.state.pageSize = parseInt(value);
        this.state.page = 1;
        this.fetchData();
    }

    handleClear() {
        this.dom.input.value = '';
        this.state.lastSearch = '';
        this.state.page = 1;
        this.fetchData();
    }

    getCachedData(key) {
        const cached = this.cache.get(key);
        if (cached && Date.now() - cached.timestamp < this.cacheTimeout) return cached.data;
        return null;
    }

    setCachedData(key, data) { this.cache.set(key, { data, timestamp: Date.now() }); }
    clearCache() { this.cache.clear(); }

    async fetchData() {
        if (this.state.loading) this.abortController?.abort();

        const cacheKey = `${this.state.lastSearch}-${this.state.page}-${this.state.pageSize}`;
        const cached = this.getCachedData(cacheKey);
        if (cached) { this.renderResults(cached); return; }

        this.state.loading = true;
        this.showSpinner(true);
        this.abortController = new AbortController();

        try {
            const url = `${this.endpoint}?nombre=${encodeURIComponent(this.state.lastSearch)}&page=${this.state.page}&pageSize=${this.state.pageSize}`;
            const response = await fetch(url, { signal: this.abortController.signal });
            if (!response.ok) throw new Error('Error en la respuesta');

            const data = await response.json();
            this.state.totalRecords = data.total || 0;
            const results = { items: data.items || data, total: this.state.totalRecords };
            this.setCachedData(cacheKey, results);
            this.renderResults(results);
        } catch (error) {
            if (error.name !== 'AbortError') { console.error('Error fetching data:', error); this.renderError(); }
        } finally {
            this.state.loading = false;
            this.showSpinner(false);
        }
    }

    showSpinner(show) { this.dom.spinner?.classList.toggle('d-none', !show); }

    renderResults(data) {
        const items = data.items || [];
        if (items.length === 0) {
            this.dom.results.innerHTML = `
                <li class="list-group-item text-center text-muted py-5">
                    <i class="fas fa-search fa-3x mb-3 opacity-25"></i>
                    <p>No se encontraron resultados</p>
                </li>`;
            this.updateCounters();
            return;
        }

        const fragment = document.createDocumentFragment();
        items.forEach(item => {
            const li = document.createElement('li');
            li.className = 'list-group-item d-flex justify-content-between align-items-center hover-shadow';
            li.innerHTML = `
                <div>
                    <h6 class="mb-1 fw-semibold text-primary">${this.escapeHtml(item.descripcion)}</h6>
                    <small class="text-muted">ID: ${this.escapeHtml(item.id)}</small>
                </div>
                <button type="button" class="btn btn-sm btn-primary" data-select-id="${this.escapeHtml(item.id)}">
                    <i class="fas fa-check me-1"></i>Seleccionar
                </button>`;
            fragment.appendChild(li);
        });

        this.dom.results.innerHTML = '';
        this.dom.results.appendChild(fragment);
        this.updateCounters();
        this.renderPagination();
    }

    renderError() {
        this.dom.results.innerHTML = `
            <li class="list-group-item text-center text-danger py-5">
                <i class="fas fa-exclamation-triangle fa-3x mb-3"></i>
                <p>Error al cargar los datos</p>
            </li>`;
    }

    updateCounters() {
        const from = (this.state.page - 1) * this.state.pageSize + 1;
        const to = Math.min(this.state.page * this.state.pageSize, this.state.totalRecords);
        if (this.dom.recordsFrom) this.dom.recordsFrom.textContent = from;
        if (this.dom.recordsTo) this.dom.recordsTo.textContent = to;
        if (this.dom.totalRecords) this.dom.totalRecords.textContent = this.state.totalRecords;
    }

    renderPagination() {
        const totalPages = Math.ceil(this.state.totalRecords / this.state.pageSize);
        if (totalPages <= 1) { this.dom.pagination.innerHTML = ''; return; }

        const maxPages = 5;
        let startPage = Math.max(this.state.page - Math.floor(maxPages / 2), 1);
        let endPage = Math.min(startPage + maxPages - 1, totalPages);
        startPage = Math.max(endPage - maxPages + 1, 1);

        const fragment = document.createDocumentFragment();
        fragment.appendChild(this.createPageButton('Anterior', this.state.page - 1, this.state.page === 1));
        for (let i = startPage; i <= endPage; i++) fragment.appendChild(this.createPageButton(i, i, false, i === this.state.page));
        fragment.appendChild(this.createPageButton('Siguiente', this.state.page + 1, this.state.page === totalPages));

        this.dom.pagination.innerHTML = '';
        this.dom.pagination.appendChild(fragment);
    }

    createPageButton(text, page, disabled = false, active = false) {
        const li = document.createElement('li');
        li.className = `page-item${disabled ? ' disabled' : ''}${active ? ' active' : ''}`;
        li.innerHTML = `<button type="button" class="page-link" data-page="${page}">${text}</button>`;
        return li;
    }

    async handleSelect(id) {
        try {
            const response = await fetch(await this.buildDetailUrl(id));
            if (!response.ok) throw new Error('Error al obtener detalles');
            const data = await response.json();
            if (data && data[0]) {
                this.onSelect(data[0]);
                if (this.shouldCloseOnSelect) bootstrap.Modal.getInstance(this.dom.modal)?.hide();
            }
        } catch (error) {
            console.error('Error selecting item:', error);
            alert('Error al seleccionar el elemento');
        }
    }

    // El detalle del producto depende del cliente (lista de precios y reglas), así que
    // quien use el manager puede añadir parámetros con getExtraParams().
    async buildDetailUrl(id) {
        let url = `${this.detailEndpoint}?id=${encodeURIComponent(id)}`;
        const extra = typeof this.getExtraParams === 'function' ? await this.getExtraParams() : null;

        Object.entries(extra || {}).forEach(([clave, valor]) => {
            if (valor !== null && valor !== undefined && valor !== '')
                url += `&${encodeURIComponent(clave)}=${encodeURIComponent(valor)}`;
        });

        return url;
    }

    onSelect(item) { console.log('Item selected:', item); }

    escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }
}

// ============================================
// 2. GESTOR DE PRODUCTOS — reemplaza completo
// ============================================
class ProductManager {
    constructor() {
        this.productos = [];
        this.totales = { subtotal1: 0, descuento: 0, flete: 0, subtotal2: 0, iva: 0, total: 0 };

        this.dom = {
            table: document.getElementById('vs-cot-productosTable'),
            subtotal1Display: document.getElementById('vs-cot-total-subtotal1-display'),
            descuentoDisplay: document.getElementById('vs-cot-total-descuento-display'),
            fleteDisplay: document.getElementById('vs-cot-total-flete-display'),
            subtotal2Display: document.getElementById('vs-cot-total-subtotal2-display'),
            ivaDisplay: document.getElementById('vs-cot-iva-display'),
            importeDisplay: document.getElementById('vs-cot-importe-display'),
            fleteInput: document.getElementById('vs-cot-flete-val')
        };

        this.init();
    }

    init() {
        this.dom.table?.addEventListener('input', (e) => {
            const input = e.target;
            if (input.classList.contains('vs-cot-cantidad') ||
                input.classList.contains('vs-cot-comentario')) {
                const productoId = input.closest('tr')?.dataset.productoId;
                if (productoId) this.actualizarFila(productoId);
            }
        });

        this.dom.table?.addEventListener('blur', (e) => {
            const input = e.target;
            if (input.classList.contains('vs-cot-precio') ||
                input.classList.contains('vs-cot-descuento')) {
                const productoId = input.closest('tr')?.dataset.productoId;
                if (productoId) this.actualizarFila(productoId);
            }
        }, true);

        this.dom.table?.addEventListener('click', (e) => {
            const btnEliminar = e.target.closest('.vs-cot-btn-eliminar');
            if (btnEliminar) {
                const productoId = btnEliminar.closest('tr')?.dataset.productoId;
                if (productoId) this.eliminarProducto(productoId);
                return;
            }
            // Existencias por almacén: propio de sucursales, donde el vendedor decide
            // si vende con lo que hay o pide un traspaso.
            const btnAlmacen = e.target.closest('.vs-cot-btn-almacenes');
            if (btnAlmacen) {
                const { id, productoId, descripcion } = btnAlmacen.dataset;
                this.verExistencias(id, productoId, descripcion);
                return;
            }
            const overlay = e.target.closest('.vs-cot-field-overlay');
            if (overlay) {
                pedirAutenticacionGlobal(overlay.dataset.tipo);
                return;
            }
        });

        if (this.dom.fleteInput) {
            this.dom.fleteInput.addEventListener('input', () => this.calcularTotales());
            this.dom.fleteInput.addEventListener('change', () => this.calcularTotales());
        }
    }

    agregarProducto(producto) {
        // precio, precioOriginal, precioMinimo, descuento, descuentoMaximo,
        // aplicarAutomatico y tipoRegla salen del módulo compartido, que es donde vive
        // el contrato de obtener_precio_final para los cuatro documentos.
        const productoData = Object.assign({
            id: `prod_${Date.now()}`,
            productoId: producto.id,
            descripcion: producto.descripcion,
            cantidad: 1,
            unidad: producto.udm || 'PZA',
            comentario: '',
            // La existencia local es lo que decide si se puede vender ya o hay que pedir
            // traspaso: es la columna que distingue a la cotización de sucursal.
            existencia: producto.existencia ?? 0,
            existenciaGeneral: producto.existenciaGeneral || 0,
            existenciaModular: producto.existenciaModular || 0
        }, ReglasPrecio.camposDeRegla(producto));

        this.productos.push(productoData);
        this.renderTable();
        this.calcularTotales();

        toastMixin.fire({
            icon: 'success',
            title: `Producto agregado: ${this.escapeHtml(producto.descripcion)}`
        });
    }

    renderTable() {
        const tbody = this.dom.table;
        if (!tbody) return;
        tbody.innerHTML = '';

        if (this.productos.length === 0) {
            tbody.innerHTML = `
<tr class="vs-cot-empty-state">
    <td colspan="11" class="text-center py-5">
        <i class="fas fa-box-open fa-3x mb-3 opacity-25"></i>
        <div>No hay productos agregados</div>
        <small class="text-muted">Haz clic en "Agregar" para comenzar</small>
    </td>
</tr>`;
            return;
        }

        const fragment = document.createDocumentFragment();
        const descDesbloqueado = !!authState.descuentoToken;
        const precioDesbloqueado = !!authState.precioToken;

        this.productos.forEach((p, index) => {
            const importe = this.calcularImporte(p);
            const existencia = p.existencia ?? 0;

            // Rojo sin stock, ámbar si no alcanza para la cantidad pedida, verde si sí.
            const badgeClass = existencia <= 0
                ? 'bg-danger'
                : existencia < p.cantidad
                    ? 'bg-warning text-dark'
                    : 'bg-success';

            const tr = document.createElement('tr');
            tr.dataset.productoId = p.id;
            tr.dataset.productoKey = p.productoId;

            // ══════════════════════════════════════════════════════════════
            // CELDA PRECIO
            // SIEMPRE protegida con contraseña — sin excepción.
            // AUTO o MANUAL no cambia esto, solo cambia el valor mostrado.
            // ══════════════════════════════════════════════════════════════
            const precioCell = document.createElement('td');
            precioCell.style.position = 'relative';

            const precioInput = document.createElement('input');
            precioInput.type = 'number';
            precioInput.className = 'vs-cot-product-input vs-cot-precio';
            precioInput.value = p.precio.toFixed(2);  // siempre precio base puro
            precioInput.min = p.precioMinimo.toFixed(2);
            precioInput.step = '0.01';
            precioInput.dataset.precioOriginal = p.precioOriginal.toFixed(2);
            precioInput.dataset.precioMinimo = p.precioMinimo.toFixed(2);
            precioInput.dataset.esAuto = p.aplicarAutomatico ? '1' : '0';

            if (!precioDesbloqueado) {
                // Bloqueado: overlay que pide contraseña al hacer clic
                precioInput.disabled = true;
                const overlayPrecio = document.createElement('div');
                overlayPrecio.className = 'vs-cot-field-overlay';
                overlayPrecio.dataset.tipo = 'precio';
                overlayPrecio.title = 'Haz clic para desbloquear precios';
                overlayPrecio.style.cssText =
                    'position:absolute;top:0;left:0;width:100%;height:100%;cursor:pointer;z-index:10;';
                precioCell.appendChild(precioInput);
                precioCell.appendChild(overlayPrecio);
            } else {
                // Desbloqueado con token: editable
                precioInput.disabled = false;
                precioCell.appendChild(precioInput);
            }

            // ══════════════════════════════════════════════════════════════
            // CELDA DESCUENTO
            // SIEMPRE protegida con contraseña — sin excepción.
            // AUTO  → valor pre-llenado con descuento_sugerido (ej: 30)
            // MANUAL → valor en 0
            // El max attribute limita el tope en ambos casos.
            // ══════════════════════════════════════════════════════════════
            const descCell = document.createElement('td');
            descCell.style.position = 'relative';

            const descInput = document.createElement('input');
            descInput.type = 'number';
            descInput.className = 'vs-cot-product-input vs-cot-descuento';
            descInput.value = p.descuento;  // 30 si AUTO, 0 si MANUAL
            descInput.min = '0';
            descInput.max = p.descuentoMaximo.toFixed(2);
            descInput.step = '0.01';
            descInput.dataset.descMaximo = p.descuentoMaximo.toFixed(2);
            descInput.dataset.esAuto = p.aplicarAutomatico ? '1' : '0';

            if (!descDesbloqueado) {
                // Bloqueado: overlay que pide contraseña al hacer clic
                descInput.disabled = true;
                const overlayDesc = document.createElement('div');
                overlayDesc.className = 'vs-cot-field-overlay';
                overlayDesc.dataset.tipo = 'descuento';
                overlayDesc.title = 'Haz clic para desbloquear descuentos';
                overlayDesc.style.cssText =
                    'position:absolute;top:0;left:0;width:100%;height:100%;cursor:pointer;z-index:10;';
                descCell.appendChild(descInput);
                descCell.appendChild(overlayDesc);
            } else {
                // Desbloqueado con token: editable hasta el tope máximo
                descInput.disabled = false;
                descCell.appendChild(descInput);
            }

            // ── Resto de celdas ───────────────────────────────────────────
            tr.innerHTML = `
        <td class="text-center fw-bold">${index + 1}</td>
        <td>${this.escapeHtml(p.productoId)}</td>
        <td style="max-width:200px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;"
            title="${this.escapeHtml(p.descripcion)}">${this.escapeHtml(p.descripcion)}</td>
        <td class="text-center">
            <span class="badge ${badgeClass} fs-6 px-2 py-1" id="vs-cot-exist-${p.id}">
                ${existencia}
            </span>
        </td>
        <td>
            <div class="input-group input-group-sm" style="min-width:110px;">
                <input type="number"
                       class="vs-cot-product-input vs-cot-cantidad form-control form-control-sm"
                       value="${p.cantidad}" min="0" step="0.01">
                <button type="button"
                        class="btn btn-outline-info btn-sm vs-cot-btn-almacenes"
                        title="Ver existencias por almacén"
                        data-id="${this.escapeHtml(p.id)}"
                        data-producto-id="${this.escapeHtml(p.productoId)}"
                        data-descripcion="${this.escapeHtml(p.descripcion)}">
                    <i class="fas fa-warehouse"></i>
                </button>
            </div>
        </td>
        <td>${this.escapeHtml(p.unidad)}</td>`;

            tr.appendChild(precioCell);
            tr.appendChild(descCell);

            const resto = document.createElement('template');
            resto.innerHTML = `
        <td class="vs-cot-importe fw-bold">${importe.toFixed(2)}</td>
        <td>
            <input type="text" class="vs-cot-product-input vs-cot-comentario"
                   value="${this.escapeHtml(p.comentario)}" placeholder="Comentario...">
        </td>
        <td>
            <button type="button" class="vs-cot-btn-delete vs-cot-btn-eliminar">
                <i class="fas fa-trash"></i>
            </button>
        </td>`;
            tr.append(...resto.content.childNodes);
            fragment.appendChild(tr);
        });

        tbody.appendChild(fragment);
    }

    actualizarFila(productoId) {
        const producto = this.productos.find(p => p.id === productoId);
        if (!producto) return;

        const tr = this.dom.table.querySelector(`tr[data-producto-id="${productoId}"]`);
        if (!tr) return;

        producto.cantidad = parseFloat(tr.querySelector('.vs-cot-cantidad').value) || 0;
        producto.comentario = tr.querySelector('.vs-cot-comentario').value || '';

        // ── Precio y descuento contra la regla vigente ────────────────────
        // Las comprobaciones viven en el módulo compartido para que los cuatro
        // documentos apliquen exactamente el mismo criterio.
        const precioInput = tr.querySelector('.vs-cot-precio');
        if (precioInput && !precioInput.disabled) {
            const res = ReglasPrecio.validarPrecio(
                producto,
                parseFloat(precioInput.value) || 0,
                !!authState.precioToken);

            producto.precio = res.valor;
            if (res.aviso) precioInput.value = res.valor.toFixed(2);
            ReglasPrecio.avisar(res.aviso);
        }

        const descInput = tr.querySelector('.vs-cot-descuento');
        if (descInput && !descInput.disabled) {
            const res = ReglasPrecio.validarDescuento(
                producto,
                parseFloat(descInput.value) || 0,
                !!authState.descuentoToken);

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

        tr.querySelector('.vs-cot-importe').textContent = this.calcularImporte(producto).toFixed(2);
        this.actualizarBadgeExistencia(productoId);
        this.calcularTotales();
    }

    eliminarProducto(productoId) {
        this.productos = this.productos.filter(p => p.id !== productoId);
        this.renderTable();
        this.calcularTotales();
    }

    // Vuelve a consultar cada partida con la lista de precios del cliente actual.
    // El precio se resuelve al agregar el producto y no se recalcula solo, así que sin
    // esto cambiar de cliente deja la cotización con los precios de la lista anterior.
    async recotizarPorCliente(clienteId, nombreCliente) {
        const actualizadas = await ReglasPrecio.recotizar(
            this.productos, clienteId, '/DatosGenerales/BuscarProductoCotizacion');

        if (actualizadas === 0) return;

        this.renderTable();
        this.calcularTotales();
        ReglasPrecio.avisar(
            `Precios recalculados con la lista de ${nombreCliente || 'el cliente'} `
            + `(${actualizadas} partida${actualizadas === 1 ? '' : 's'})`,
            'info');
    }

    desbloquearCampos(tipo) {
        const selector = tipo === 'descuento' ? '.vs-cot-descuento' : '.vs-cot-precio';
        this.dom.table?.querySelectorAll(selector).forEach(input => {
            input.removeAttribute('disabled');
            input.parentElement?.querySelector('.vs-cot-field-overlay')?.remove();
        });
    }

    calcularImporte(producto) {
        // Descuento se aplica sobre el importe bruto, no sobre el precio unitario
        const bruto = producto.cantidad * producto.precio;
        return bruto * (1 - producto.descuento / 100);
    }

    calcularTotales() {
        const totales = this.productos.reduce((acc, p) => {
            const bruto = p.cantidad * p.precio;
            acc.subtotal1 += bruto;
            acc.totalDescuento += bruto * (p.descuento / 100);
            return acc;
        }, { subtotal1: 0, totalDescuento: 0 });

        const flete = parseFloat(document.getElementById('vs-cot-flete-val').value) || 0;
        const subtotal2 = totales.subtotal1 - totales.totalDescuento + flete;
        const iva = subtotal2 * 0.16;
        const total = subtotal2 + iva;

        this.totales = { subtotal1: totales.subtotal1, descuento: totales.totalDescuento, flete, subtotal2, iva, total };

        this.updateDisplay(this.dom.subtotal1Display, totales.subtotal1);
        this.updateDisplay(this.dom.descuentoDisplay, totales.totalDescuento, '-$');
        this.updateDisplay(this.dom.fleteDisplay, flete);
        this.updateDisplay(this.dom.subtotal2Display, subtotal2);
        this.updateDisplay(this.dom.ivaDisplay, iva);
        this.updateDisplay(this.dom.importeDisplay, total, '$');

        this.updateHiddenField('vs-cot-total-subtotal1', totales.subtotal1);
        this.updateHiddenField('vs-cot-total-descuento', totales.totalDescuento);
        this.updateHiddenField('vs-cot-total-flete', flete);
        this.updateHiddenField('vs-cot-total-subtotal2', subtotal2);
        this.updateHiddenField('vs-cot-iva', iva);
        this.updateHiddenField('vs-cot-importe', total);

        window.CreditoVentas?.evaluar('vs-cot');
    }

    updateHiddenField(id, value) {
        const field = document.getElementById(id);
        if (field) field.value = value.toFixed(2);
    }

    updateDisplay(element, value, prefix = '$') {
        if (element) element.textContent = `${prefix}${value.toFixed(2)}`;
    }

    escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    getProductosData() {
        return this.productos.map(p => ({
            ...p,
            importe: this.calcularImporte(p)
        }));
    }

    async verExistencias(id, productoId, descripcion) {
        const prod = this.productos.find(p => p.id === id);
        if (!prod) return;

        const modalEl = document.getElementById('vs-cot-modalExistencias');
        const spinner = document.getElementById('vs-cot-existencias-spinner');
        const content = document.getElementById('vs-cot-existencias-content');
        const infoEl = document.getElementById('vs-cot-existencias-producto-info');

        infoEl.innerHTML = `
        <div class="alert alert-info mb-0 py-2">
            <i class="fas fa-box me-2"></i>
            <strong>${this.escapeHtml(descripcion)}</strong>
            <span class="text-muted ms-2">(${this.escapeHtml(productoId)})</span>
        </div>`;
        content.innerHTML = '';
        spinner.classList.remove('d-none');

        // ✅ Corrección: usar getInstance o crear nueva instancia
        let modal = bootstrap.Modal.getInstance(modalEl);
        if (!modal) {
            modal = new bootstrap.Modal(modalEl);
        }
        modal.show();

        try {
            // Necesitamos el id_catproductos numérico.
            // BuscarProductoCotizacion devuelve el objeto completo;
            // reutilizamos lo que ya tenemos en prod o hacemos fetch.
            const response = await fetch(
                `/DatosGenerales/BuscarExistenciasPorAlmacen?productoId=${encodeURIComponent(prod.productoId)}`
            );
            const result = await response.json();

            if (!result.success) throw new Error(result.message);

            const rows = result.data;

            if (!rows || rows.length === 0) {
                content.innerHTML = `
                <div class="alert alert-warning">
                    <i class="fas fa-exclamation-triangle me-2"></i>
                    Sin existencias en almacenes de tipo Stock.
                </div>`;
            } else {
                const totalGeneral = rows.reduce((s, r) => s + parseFloat(r.existencia), 0);

                content.innerHTML = `
                <table class="table table-sm table-hover table-bordered mb-0">
                    <thead class="table-dark">
                        <tr>
                            <th><i class="fas fa-warehouse me-1"></i>Almacén</th>
                            <th class="text-end">Existencia</th>
                        </tr>
                    </thead>
                    <tbody>
                        ${rows.map(r => `
                            <tr>
                                <td>${this.escapeHtml(r.almacen)}</td>
                                <td class="text-end fw-bold
                                    ${parseFloat(r.existencia) <= 0
                        ? 'text-danger'
                        : 'text-success'}">
                                    ${parseFloat(r.existencia).toLocaleString('es-MX')}
                                </td>
                            </tr>`).join('')}
                    </tbody>
                    <tfoot class="table-secondary">
                        <tr>
                            <td class="fw-bold">Total general</td>
                            <td class="text-end fw-bold">
                                ${totalGeneral.toLocaleString('es-MX')}
                            </td>
                        </tr>
                    </tfoot>
                </table>`;
            }
        } catch (err) {
            content.innerHTML = `
            <div class="alert alert-danger">
                <i class="fas fa-times-circle me-2"></i>
                Error al consultar: ${this.escapeHtml(err.message)}
            </div>`;
        } finally {
            spinner.classList.add('d-none');
        }
    }

    // Actualiza el badge de existencia en la fila cuando cambia la cantidad
    actualizarBadgeExistencia(productoId) {
        const prod = this.productos.find(p => p.id === productoId);
        const badge = document.getElementById(`vs-cot-exist-${productoId}`);
        if (!prod || !badge) return;

        const existencia = prod.existencia ?? 0;
        badge.textContent = existencia;
        badge.className = 'badge fs-6 px-2 py-1 ' + (
            existencia <= 0 ? 'bg-danger'
                : existencia < prod.cantidad ? 'bg-warning text-dark'
                    : 'bg-success'
        );
    }
}
// ============================================
// 2b. AUTENTICACIÓN GLOBAL POR TIPO DE CAMPO
// ============================================
async function pedirAutenticacionGlobal(tipo) {
    if (tipo === 'descuento' && authState.descuentoToken) return;
    if (tipo === 'precio' && authState.precioToken) return;

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
            if (tipo === 'descuento') authState.descuentoToken = result.token;
            else authState.precioToken = result.token;

            App.productManagerInstance?.desbloquearCampos(tipo);

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
// 3. GESTOR DE FORMULARIO
// ============================================
class FormManager {
    constructor(productManager) {
        this.productManager = productManager;
        this.form = document.getElementById('vs-cot-formCotizacion');
        this.btnSubmit = document.querySelector('.vs-cot-btn-submit');
        this.init();
    }

    init() {
        this.btnSubmit?.addEventListener('click', (e) => {
            e.preventDefault();
            this.enviarCotizacion();
        }, { once: false });
    }

    recopilarDatosFormulario() {
        const formData = new FormData();

        const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
        if (token) formData.append('__RequestVerificationToken', token);

        formData.append('sucursal', document.getElementById('vs-cot-sucursal')?.value || '');
        formData.append('almacen', document.getElementById('vs-cot-almacen')?.value || '');
        formData.append('tipoMovimiento', document.getElementById('vs-cot-tipo-docto-mov')?.value || '');
        formData.append('folio', document.getElementById('vs-cot-folio')?.value || '');

        formData.append('cliente', document.getElementById('vs-cot-cliente')?.value || '');
        formData.append('rfc', document.getElementById('vs-cot-rfc')?.value || '');
        formData.append('vendedor', document.getElementById('vs-cot-vendedor')?.value || '');
        formData.append('contacto', document.getElementById('vs-cot-contacto')?.value || '');
        formData.append('moneda', document.getElementById('vs-cot-moneda')?.value || '');
        formData.append('paridad', document.getElementById('vs-cot-paridad')?.value || '');
        formData.append('concepto', document.getElementById('vs-cot-concepto')?.value || '');
        formData.append('incoterm', document.getElementById('vs-cot-incoterm')?.value || '');
        formData.append('ordenCompra', document.getElementById('vs-cot-orden-compra-val')?.value || '');

        formData.append('limiteCredito', document.getElementById('vs-cot-limite-credito')?.value || '');
        formData.append('plazo', document.getElementById('vs-cot-plazo')?.value || '');
        formData.append('fechaPago', document.getElementById('vs-cot-fecha-pago')?.value || '');
        formData.append('formaPago', document.getElementById('vs-cot-forma-pago')?.value || '');
        formData.append('usoCFDI', document.getElementById('vs-cot-uso-cfdi')?.value || '');
        formData.append('comentarios', document.getElementById('vs-cot-comentarios')?.value || '');

        formData.append('tipoPago', document.getElementById('vs-cot-tipo-pago')?.value || 'contado');

        // ★ Los productos vienen del array JS validado, no del DOM directamente
        const productos = this.productManager.getProductosData();
        formData.append('productosJSON', JSON.stringify(productos));

        const totales = this.productManager.totales;
        formData.append('subtotal1', totales.subtotal1.toFixed(2));
        formData.append('descuento', totales.descuento.toFixed(2));
        formData.append('flete', totales.flete.toFixed(2));
        formData.append('subtotal2', totales.subtotal2.toFixed(2));
        formData.append('iva', totales.iva.toFixed(2));
        formData.append('total', totales.total.toFixed(2));

        formData.append('descuentoToken', authState.descuentoToken || '');
        formData.append('precioToken', authState.precioToken || '');

        return formData;
    }

    // Dentro de FormManager — reemplaza solo el método validarFormulario
    validarFormulario() {
        const errores = [];
        const productos = this.productManager.getProductosData();

        const cliente = document.getElementById('vs-cot-cliente')?.value;
        if (!cliente?.trim()) errores.push('Debe seleccionar un cliente');

        if (productos.length === 0) errores.push('Debe agregar al menos un producto');

        const vendedor = document.getElementById('vs-cot-vendedor')?.value;
        if (!vendedor || vendedor === 'Seleccionar vendedor')
            errores.push('Debe seleccionar un vendedor');

        //// Descuento > 0 requiere token, sin excepción
        //if (!authState.descuentoToken) {
        //    const conDescuento = productos.some(p => p.descuento > 0);
        //    if (conDescuento) errores.push('Hay productos con descuento. Se requiere autorización de gerente.');
        //}

        //// Precio modificado respecto al original requiere token
        //if (!authState.precioToken) {
        //    const conPrecioModificado = productos.some(p =>
        //        Math.abs(p.precio - p.precioOriginal) > 0.001
        //    );
        //    if (conPrecioModificado) errores.push('Hay productos con precio modificado. Se requiere autorización.');
        //}

        return errores;
    }

    obtenerConfiguracionPorTipo(tipoMovimiento) {
        const configuraciones = {
            'alta': {
                endpoint: '/VSCotizacion/Guardar',
                metodo: 'POST',
                mensajeExito: 'Cotización creada exitosamente',
                textoCarga: 'Guardando...',
                validarProductos: true
            },
            'modificacion': {
                endpoint: '/VSCotizacion/Modificar',
                metodo: 'PUT',
                mensajeExito: 'Cotización modificada exitosamente',
                textoCarga: 'Actualizando...',
                validarProductos: true
            },
            'consulta': {
                endpoint: '/VSCotizacion/Consultar',
                metodo: 'GET',
                mensajeExito: 'Consulta realizada exitosamente',
                textoCarga: 'Consultando...',
                validarProductos: false
            }
        };
        return configuraciones[tipoMovimiento] || configuraciones['alta'];
    }

    // Guardar en sucursal no es solo un POST: primero se comprueba que el stock local
    // alcance y, si no, se ofrece generar la solicitud de traspaso; al final se ofrece el
    // ticket para la impresora del mostrador.

    async enviarCotizacion() {
        const tipoMovimiento = document.getElementById('vs-cot-tipo-docto-mov')?.value || 'alta';
        const config = this.obtenerConfiguracionPorTipo(tipoMovimiento);

        const errores = this.validarFormulario(config.validarProductos);
        if (errores.length > 0) {
            toastMixin.fire({ icon: 'error', title: errores.join(', ') });
            return;
        }

        const originalText = this.btnSubmit.innerHTML;
        this.btnSubmit.disabled = true;
        this.btnSubmit.innerHTML = `<i class="fas fa-spinner fa-spin"></i> Verificando existencias...`;

        try {
            // ── PASO 1: Verificar existencias locales ANTES de guardar ─────────
            const productos = this.productManager.getProductosData();
            const formDataVerif = new FormData();
            const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
            if (token) formDataVerif.append('__RequestVerificationToken', token);
            formDataVerif.append('productosJSON', JSON.stringify(productos));

            const verificacion = await fetch('/VSCotizacion/VerificarExistencias', {
                method: 'POST',
                body: formDataVerif
            });
            const verifResult = await verificacion.json();

            if (!verifResult.success) throw new Error(verifResult.message);

            // ── PASO 2: Si hay faltantes, preguntar al usuario ─────────────────
            if (verifResult.hayFaltantes) {
                const debeCrearTraspaso = await this._mostrarDialogoFaltantes(verifResult.faltantes);

                if (debeCrearTraspaso === null) {
                    // Canceló todo
                    return;
                }

                // ── PASO 3: Guardar la cotización normalmente ──────────────────
                this.btnSubmit.innerHTML = `<i class="fas fa-spinner fa-spin"></i> Guardando...`;
                const folioGenerado = await this._guardarCotizacion(config);
                if (!folioGenerado) return;

                // ── PASO 4: Si quiere traspaso, generarlo ──────────────────────
                if (debeCrearTraspaso) {
                    this.btnSubmit.innerHTML = `<i class="fas fa-spinner fa-spin"></i> Creando traspaso...`;
                    await this._crearTraspaso(verifResult.faltantes, folioGenerado);
                }

                // Mostrar ticket y recargar
                await this._mostrarTicketYRecargar(folioGenerado);

            } else {
                // Sin faltantes — flujo normal
                this.btnSubmit.innerHTML = `<i class="fas fa-spinner fa-spin"></i> Guardando...`;
                const folioGenerado = await this._guardarCotizacion(config);
                if (!folioGenerado) return;
                await this._mostrarTicketYRecargar(folioGenerado);
            }

        } catch (error) {
            console.error('Error enviando cotización:', error);
            toastMixin.fire({ icon: 'error', title: error.message || 'Ocurrió un error' });
        } finally {
            this.btnSubmit.disabled = false;
            this.btnSubmit.innerHTML = originalText;
        }
    }

    // ── Helpers privados ──────────────────────────────────────────────────────────

    async _mostrarDialogoFaltantes(faltantes) {
        const filas = faltantes.map(f => `
        <tr>
            <td style="padding:6px 10px;text-align:left;">${f.descripcion}</td>
            <td style="padding:6px 10px;text-align:center;">${f.cantidadSolicitada}</td>
            <td style="padding:6px 10px;text-align:center;color:#dc2626;">${f.existenciaLocal}</td>
            <td style="padding:6px 10px;text-align:center;font-weight:bold;color:#b91c1c;">${f.faltante}</td>
        </tr>`).join('');

        const { value } = await Swal.fire({
            title: '⚠️ Stock insuficiente en tu sucursal',
            html: `
            <p style="color:#64748b;margin-bottom:12px;">
                Los siguientes productos no tienen suficiente existencia local.
                ¿Deseas generar una <strong>solicitud de traspaso</strong> automática
                y notificar al encargado de almacén?
            </p>
            <div style="overflow-x:auto;max-height:220px;border-radius:8px;border:1px solid #e2e8f0;">
                <table style="width:100%;border-collapse:collapse;font-size:13px;">
                    <thead style="background:#1e3a5f;color:white;position:sticky;top:0;">
                        <tr>
                            <th style="padding:8px 10px;text-align:left;">Producto</th>
                            <th style="padding:8px 10px;">Solicitado</th>
                            <th style="padding:8px 10px;">Existencia</th>
                            <th style="padding:8px 10px;">Faltante</th>
                        </tr>
                    </thead>
                    <tbody>${filas}</tbody>
                </table>
            </div>`,
            icon: 'warning',
            showDenyButton: true,
            showCancelButton: true,
            confirmButtonText: '<i class="fas fa-exchange-alt me-1"></i> Sí, crear traspaso',
            denyButtonText: '<i class="fas fa-save me-1"></i> Solo guardar',
            cancelButtonText: 'Cancelar',
            confirmButtonColor: '#1e3a5f',
            denyButtonColor: '#6c757d',
            width: '600px'
        });

        if (value === true) return true;   // crear traspaso
        if (value === false) return false;  // solo guardar
        return null;                        // canceló
    }

    async _guardarCotizacion(config) {
        const formData = this.recopilarDatosFormulario();

        const response = await fetch(config.endpoint, {
            method: config.metodo.toUpperCase(),
            body: formData
        });
        const result = await response.json();

        if (!result.success) {
            toastMixin.fire({ icon: 'error', title: result.message || 'Error al guardar' });
            return null;
        }

        return result; // objeto completo con folio_generado
    }

    async _crearTraspaso(faltantes, resultGuardado) {
        try {
            const formData = new FormData();
            const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
            if (token) formData.append('__RequestVerificationToken', token);
            formData.append('productosJSON', JSON.stringify(faltantes));
            formData.append('folioReferencia', resultGuardado.folio_generado || '');

            const response = await fetch('/VSCotizacion/CrearSolicitudTraspaso', {
                method: 'POST',
                body: formData
            });
            const result = await response.json();

            if (result.success) {
                toastMixin.fire({
                    icon: 'success',
                    title: `Traspaso ${result.folioTraspaso} generado. Se notificó al encargado.`
                });
            } else {
                toastMixin.fire({
                    icon: 'warning',
                    title: `Cotización guardada, pero hubo un error al crear el traspaso: ${result.message}`
                });
            }
        } catch (err) {
            console.error('Error creando traspaso:', err);
            toastMixin.fire({
                icon: 'warning',
                title: 'Cotización guardada, pero no se pudo crear el traspaso.'
            });
        }
    }

    async _mostrarTicketYRecargar(result) {
        const swalResult = await Swal.fire({
            icon: 'success',
            title: '¡Cotización guardada!',
            html: `<p>${result.message || ''}</p><p><strong>Folio: ${result.folio_generado || ''}</strong></p>`,
            showCancelButton: true,
            confirmButtonText: '<i class="fas fa-receipt me-1"></i> Ver Ticket',
            cancelButtonText: 'Cerrar',
            confirmButtonColor: '#0d6efd',
            cancelButtonColor: '#6c757d'
        });

        if (swalResult.isConfirmed) {
            const productos = this.productManager.getProductosData();
            const totales = this.productManager.totales;
            await App.ticketManager.generarTicket(result, productos, totales);
        } else {
            location.reload();
        }
    }

    limpiarFormulario() {
        this.form?.reset();
        this.productManager.productos = [];
        this.productManager.renderTable();
        this.productManager.calcularTotales();
        document.getElementById('vs-cot-cliente').value = '';
        document.getElementById('vs-cot-rfc').value = '';
        document.getElementById('vs-cot-info-proveedor').value = '';
    }

    verDatosFormulario() {
        const formData = this.recopilarDatosFormulario();
        const obj = {};
        for (let [key, value] of formData.entries()) obj[key] = value;
        console.log('Datos a enviar:', obj);
        return obj;
    }
}

// ============================================
// GESTOR DE TICKET CON IMPRESIÓN BIXOLON
// Optimizado para 32 caracteres (ancho real)
// ============================================
class TicketManager {
    constructor() {
        this.modalHtml = this.createModalStructure();
        this.injectModalToDOM();
        this.dom = {
            modal: document.getElementById('vs-cot-modalTicket'),
            ticketContent: document.getElementById('vs-cot-ticketContent'),
            qrContainer: document.getElementById('vs-cot-qrContainer'),
            btnDescargar: document.getElementById('vs-cot-btnDescargarTicket')
        };
        this.printerUrl = 'http://localhost:5050/print/';
        this.init();
    }

    createModalStructure() {
        return `
            <div class="modal fade" id="vs-cot-modalTicket" tabindex="-1" aria-labelledby="modalTicketLabel" aria-hidden="true">
                <div class="modal-dialog modal-dialog-centered">
                    <div class="modal-content">
                        <div class="modal-header bg-primary text-white">
                            <h5 class="modal-title" id="modalTicketLabel">
                                <i class="fas fa-receipt me-2"></i>Ticket de Cotización
                            </h5>
                            <button type="button" class="btn-close btn-close-white" data-bs-dismiss="modal" aria-label="Close"></button>
                        </div>
                        <div class="modal-body p-0">
                            <div id="vs-cot-ticketContent" class="ticket-wrapper"></div>
                        </div>
                        <div class="modal-footer">
                            <button type="button" class="btn btn-secondary" data-bs-dismiss="modal">
                                <i class="fas fa-times me-1"></i>Cerrar
                            </button>
                            <button type="button" id="vs-cot-btnDescargarTicket" class="btn btn-primary">
                                <i class="fas fa-download me-1"></i>Descargar PDF
                            </button>
                        </div>
                    </div>
                </div>
            </div>

            <style>
                .ticket-wrapper {
                    background: white;
                    padding: 30px;
                    max-width: 400px;
                    margin: 0 auto;
                    font-family: 'Courier New', monospace;
                }
                .ticket-header {
                    text-align: center;
                    border-bottom: 2px dashed #333;
                    padding-bottom: 15px;
                    margin-bottom: 20px;
                }
                .ticket-header h3 {
                    margin: 0;
                    font-size: 20px;
                    font-weight: bold;
                    text-transform: uppercase;
                }
                .ticket-header p {
                    margin: 5px 0;
                    font-size: 12px;
                    color: #666;
                }
                .ticket-section {
                    margin-bottom: 15px;
                    padding-bottom: 10px;
                    border-bottom: 1px dashed #ccc;
                }
                .ticket-section:last-child {
                    border-bottom: 2px dashed #333;
                }
                .ticket-row {
                    display: flex;
                    justify-content: space-between;
                    margin: 5px 0;
                    font-size: 13px;
                }
                .ticket-row.bold {
                    font-weight: bold;
                }
                .ticket-row label {
                    font-weight: bold;
                    color: #333;
                }
                .ticket-productos {
                    margin: 15px 0;
                }
                .ticket-producto-item {
                    margin: 10px 0;
                    padding: 8px 0;
                    border-bottom: 1px dotted #ddd;
                    font-size: 12px;
                }
                .ticket-producto-nombre {
                    font-weight: bold;
                    margin-bottom: 3px;
                    word-wrap: break-word;
                }
                .ticket-producto-detalles {
                    display: flex;
                    justify-content: space-between;
                    color: #666;
                    font-size: 11px;
                }
                .ticket-totales {
                    margin-top: 15px;
                    padding-top: 10px;
                }
                .ticket-totales .ticket-row {
                    font-size: 14px;
                }
                .ticket-total-final {
                    font-size: 18px !important;
                    font-weight: bold !important;
                    margin-top: 10px;
                    padding-top: 10px;
                    border-top: 2px solid #333;
                }
                .ticket-qr {
                    text-align: center;
                    margin: 20px 0;
                    padding: 15px 0;
                }
                .ticket-qr canvas,
                .ticket-qr img {
                    max-width: 150px;
                    height: auto;
                    margin: 0 auto;
                }
                .ticket-footer {
                    text-align: center;
                    margin-top: 20px;
                    padding-top: 15px;
                    border-top: 2px dashed #333;
                    font-size: 11px;
                    color: #666;
                }
                .ticket-footer p {
                    margin: 5px 0;
                }
                @media print {
                    .modal-header,
                    .modal-footer {
                        display: none !important;
                    }
                    .ticket-wrapper {
                        padding: 10px;
                    }
                }
            </style>
        `;
    }

    injectModalToDOM() {
        const tempDiv = document.createElement('div');
        tempDiv.innerHTML = this.modalHtml;
        document.body.appendChild(tempDiv.firstElementChild);
        const style = tempDiv.querySelector('style');
        if (style) document.head.appendChild(style);
    }

    init() {
        this.dom.btnDescargar?.addEventListener('click', () => this.descargarTicket());
    }

    //async imprimirTicket() {
    //    try {
    //        const btnImprimir = this.dom.btnImprimir;
    //        const textoOriginal = btnImprimir.innerHTML;
    //        btnImprimir.disabled = true;
    //        btnImprimir.innerHTML = '<i class="fas fa-spinner fa-spin me-1"></i>Imprimiendo...';

    //        const comandosESCPOS = this.generarComandosESCPOS();

    //        const response = await fetch(this.printerUrl, {
    //            method: 'POST',
    //            headers: {
    //                'Content-Type': 'text/plain; charset=utf-8'
    //            },
    //            body: comandosESCPOS
    //        });

    //        if (!response.ok) {
    //            throw new Error(`Error en la impresora: ${response.status}`);
    //        }

    //        toastMixin.fire({
    //            icon: 'success',
    //            title: 'Ticket enviado a la impresora'
    //        });

    //        btnImprimir.innerHTML = textoOriginal;
    //        btnImprimir.disabled = false;

    //    } catch (error) {
    //        console.error('Error imprimiendo ticket:', error);

    //        let mensaje = 'Error al imprimir el ticket';
    //        if (error.message.includes('Failed to fetch')) {
    //            mensaje = 'No se puede conectar con la impresora. Verifica que el servicio esté corriendo en localhost:5050';
    //        }

    //        toastMixin.fire({
    //            icon: 'error',
    //            title: mensaje
    //        });

    //        this.dom.btnImprimir.disabled = false;
    //        this.dom.btnImprimir.innerHTML = '<i class="fas fa-print me-1"></i>Imprimir Ticket';
    //    }
    //}

    //generarComandosESCPOS() {
    //    const ESC = '\x1B';
    //    const GS = '\x1D';
    //    const NL = '\n';

    //    const INIT = ESC + '@';
    //    const LEFT = ESC + 'a' + '0';
    //    const CENTER = ESC + 'a' + '1';

    //    const BOLD_ON = ESC + 'E' + '1';
    //    const BOLD_OFF = ESC + 'E' + '0';

    //    const SIZE_NORMAL = GS + '!' + '\x00';
    //    const SIZE_TALL = GS + '!' + '\x01';

    //    const CHARSET = ESC + 't' + '\x00';
    //    const LINE_SPACING = ESC + '3' + '\x08';

    //    const WIDTH = 42;
    //    const LINE = '-'.repeat(WIDTH);

    //    const folio = this.obtenerFolio();
    //    const fecha = new Date().toLocaleString('es-MX', {
    //        day: '2-digit',
    //        month: '2-digit',
    //        year: 'numeric',
    //        hour: '2-digit',
    //        minute: '2-digit'
    //    });

    //    const cliente = document.getElementById('vs-cot-cliente')?.value || 'PUBLICO EN GENERAL';

    //    const vendedorSelect = tomManager.getInstance('vs-cot-vendedor');
    //    const vValue = vendedorSelect?.getValue();
    //    const vendedor = vValue ? vendedorSelect.options[vValue]?.nombre : 'N/A';

    //    const sucursalText = document.getElementById('vs-cot-sucursal')?.value || 'MATRIZ';

    //    const productos = this.obtenerProductosDelTicket();
    //    const totales = this.obtenerTotalesDelTicket();

    //    let t = '';

    //    // ================= INIT =================
    //    t += INIT;
    //    t += CHARSET;
    //    t += LINE_SPACING;

    //    // ================= HEADER =================
    //    t += CENTER + BOLD_ON + SIZE_TALL;
    //    t += 'COTIZACION' + NL;

    //    t += SIZE_NORMAL + BOLD_OFF;

    //    t += LEFT;
    //    t += `FOLIO : ${folio}` + NL;
    //    t += `FECHA : ${fecha}` + NL;
    //    t += LINE + NL;

    //    // ================= DATOS CLIENTE =================
    //    t += BOLD_ON;
    //    t += 'DATOS DEL CLIENTE' + NL;
    //    t += BOLD_OFF;

    //    t += 'CLIENTE : ' + this.truncarTexto(cliente, WIDTH - 11) + NL;
    //    t += 'VENDEDOR: ' + this.truncarTexto(vendedor, WIDTH - 11) + NL;
    //    t += 'SUCURSAL: ' + this.truncarTexto(sucursalText, WIDTH - 11) + NL;
    //    t += LINE + NL;

    //    // ================= PRODUCTOS =================
    //    productos.forEach((p, i) => {
    //        t += `${i + 1}. ${this.truncarTexto(p.descripcion, WIDTH - 4)}` + NL;

    //        const qty = `${p.cantidad}${p.unidad}`;
    //        const precio = `x$${p.precio.toFixed(2)}`;
    //        const left = `${qty} ${precio}`;
    //        const right = `$${p.importe.toFixed(2)}`;

    //        t += this.formatearLineaTotales(left, right, WIDTH) + NL;
    //    });

    //    t += LINE + NL;

    //    // ================= TOTALES =================
    //    t += this.formatearLineaTotales('SUBTOTAL', `$${totales.subtotal1.toFixed(2)}`, WIDTH) + NL;

    //    if (totales.descuento > 0) {
    //        t += this.formatearLineaTotales('DESCUENTO', `-$${totales.descuento.toFixed(2)}`, WIDTH) + NL;
    //    }

    //    if (totales.flete > 0) {
    //        t += this.formatearLineaTotales('FLETE', `$${totales.flete.toFixed(2)}`, WIDTH) + NL;
    //    }

    //    t += this.formatearLineaTotales('IVA 16%', `$${totales.iva.toFixed(2)}`, WIDTH) + NL;
    //    t += LINE + NL;

    //    // ================= TOTAL =================
    //    t += BOLD_ON + SIZE_TALL;
    //    t += this.formatearLineaTotales('TOTAL', `$${totales.total.toFixed(2)}`, WIDTH) + NL;
    //    t += SIZE_NORMAL + BOLD_OFF;
    //    t += LINE + NL;

    //    // ================= QR =================
    //    t += NL + CENTER;

    //    const qrData = `${folio}`;
    //    const qrLen = qrData.length + 3;
    //    const pL = String.fromCharCode(qrLen % 256);
    //    const pH = String.fromCharCode(Math.floor(qrLen / 256));

    //    t += GS + '(k' + '\x04\x00' + '1A' + '2\x00';
    //    t += GS + '(k' + '\x03\x00' + '1C' + '\x06';
    //    t += GS + '(k' + '\x03\x00' + '1E' + '1';
    //    t += GS + '(k' + pL + pH + '1P0' + qrData;
    //    t += GS + '(k' + '\x03\x00' + '1Q' + '0' + NL;

    //    // ================= FOOTER =================
    //    t += BOLD_ON;
    //    t += 'GRACIAS POR SU PREFERENCIA' + NL;
    //    t += BOLD_OFF;

    //    // ================= CORTE =================
    //    t += ESC + 'd' + '\x03';
    //    t += ESC + 'd' + '\x03';
    //    t += '\n\n\n';
    //    t += '\n\n\n';
    //    t += GS + 'V' + '\x01';

    //    return t;
    //}




    // Helper mejorado para totales
    //formatearLineaTotales(etiqueta, valor, ancho = 32) {
    //    const espacios = ancho - etiqueta.length - valor.length;
    //    if (espacios < 1) {
    //        return etiqueta + '\n' + ' '.repeat(Math.max(0, ancho - valor.length)) + valor;
    //    }
    //    return etiqueta + ' '.repeat(espacios) + valor;
    //}

    //formatearLineaTotales(etiqueta, valor) {
    //    const ancho = 32;
    //    const espacios = ancho - etiqueta.length - valor.length;
    //    if (espacios < 1) {
    //        return etiqueta + '\n' + ' '.repeat(Math.max(0, ancho - valor.length)) + valor;
    //    }
    //    return etiqueta + ' '.repeat(espacios) + valor;
    //}

    //truncarTexto(texto, maxLength) {
    //    if (texto.length <= maxLength) return texto;
    //    return texto.substring(0, maxLength - 3) + '...';
    //}

    obtenerFolio() {
        const folioElement = document.querySelector('#vs-cot-ticketContent .ticket-header p');
        if (folioElement) {
            return folioElement.textContent.replace('Folio: ', '').trim();
        }
        return 'SIN-FOLIO';
    }

    async generarTicket(datosRespuesta, productosData, totales) {
        const folio = datosRespuesta.folio_generado || 'SIN-FOLIO';
        const fecha = new Date().toLocaleString('es-MX', {
            year: 'numeric',
            month: '2-digit',
            day: '2-digit',
            hour: '2-digit',
            minute: '2-digit'
        });

        const cliente = document.getElementById('vs-cot-cliente')?.value || 'N/A';
        const vendedorSelect = tomManager.getInstance('vs-cot-vendedor');
        const value = vendedorSelect?.getValue();
        const vendedor = value ? vendedorSelect.options[value]?.nombre || 'N/A' : 'N/A';

        const ticketHTML = `
            <div class="ticket-header"  style="color: #000000;">
                <h3>COTIZACIÓN</h3>
                <p>Folio: ${this.escapeHtml(folio)}</p>
                <p>${fecha}</p>
            </div>

            <div class="ticket-section">
                <div class="ticket-row">
                    <label>Cliente:</label>
                    <span style="color: #000000;">${this.escapeHtml(cliente)}</span>
                </div>
                <div class="ticket-row">
                    <label>Vendedor:</label>
                    <span style="color: #000000;">${this.escapeHtml(vendedor)}</span>
                </div>
                <div class="ticket-row">
                    <label>Sucursal:</label>
                    <span style="color: #000000;">${this.escapeHtml(sucursalText)}</span>
                </div>
            </div>

            <div class="ticket-section"  style="color: #000000;">
                <h4 style="margin: 10px 0; font-size: 14px; text-align: center,  color: #000000;">PRODUCTOS</h4>
                <div class="ticket-productos"  style="color: #000000;">
                    ${this.generarListaProductos(productosData)}
                </div>
            </div>

            <div class="ticket-section ticket-totales">
                <div class="ticket-row">
                    <label>Subtotal:</label>
                    <span style="color: #000000;">$${totales.subtotal1.toFixed(2)}</span>
                </div>
                ${totales.descuento > 0 ? `
                <div class="ticket-row">
                    <label>Descuento:</label>
                    <span style="color: #000000;">-$${totales.descuento.toFixed(2)}</span>
                </div>` : ''}
                ${totales.flete > 0 ? `
                <div class="ticket-row">
                    <label>Flete:</label>
                    <span style="color: #000000;">$${totales.flete.toFixed(2)}</span>
                </div>` : ''}
                <div class="ticket-row">
                    <label>IVA (16%):</label>
                    <span style="color: #000000;">$${totales.iva.toFixed(2)}</span>
                </div>
                <div class="ticket-row ticket-total-final">
                    <label>TOTAL:</label>
                    <span style="color: #000000;">$${totales.total.toFixed(2)}</span>
                </div>
            </div>

            <div class="ticket-qr" id="vs-cot-qrContainer">
            </div>

            <div class="ticket-footer">
                <p><strong>¡Gracias por su preferencia!</strong></p>
                <p>Este documento es solo una cotización</p>
                <p>No tiene validez fiscal</p>
            </div>
        `;

        this.dom.ticketContent.innerHTML = ticketHTML;
        await this.generarQR(folio);

        const modal = new bootstrap.Modal(this.dom.modal);

        // Escuchar el cierre del modal
        this.dom.modal.addEventListener('hidden.bs.modal', () => {
            location.reload();
        }, { once: true }); // `once: true` para que solo se ejecute una vez

        modal.show();
    }

    generarListaProductos(productos) {
        if (!productos || productos.length === 0) {
            return '<p style="text-align: center; color: #999;">Sin productos</p>';
        }

        return productos.map(p => `
            <div class="ticket-producto-item">
                <div class="ticket-producto-nombre">
                    ${this.escapeHtml(p.descripcion)}
                </div>
                <div class="ticket-producto-detalles">
                    <span style="color: #000000;">${p.cantidad} ${p.unidad || 'PZA'} x $${p.precio.toFixed(2)}</span>
                    <span class="bold" style="color: #000000;">$${p.importe.toFixed(2)}</span>
                </div>
            </div>
        `).join('');
    }

    async generarQR(folio) {
        try {
            const qrContainer = document.getElementById('vs-cot-qrContainer');
            qrContainer.innerHTML = '<p style="font-size: 12px; margin-top: 5px;">Folio: ' + this.escapeHtml(folio) + '</p>';

            const qrDiv = document.createElement('div');
            qrDiv.id = 'qrcode-temp';
            qrContainer.prepend(qrDiv);

            if (typeof QRCode === 'undefined') {
                await this.loadQRCodeLibrary();
            }

            new QRCode(qrDiv, {
                text: folio,
                width: 150,
                height: 150,
                colorDark: "#000000",
                colorLight: "#ffffff",
                correctLevel: QRCode.CorrectLevel.H
            });

        } catch (error) {
            console.error('Error generando QR:', error);
            const qrContainer = document.getElementById('vs-cot-qrContainer');
            qrContainer.innerHTML = `
                <div style="border: 2px solid #ccc; padding: 10px; text-align: center;">
                    <p style="font-weight: bold; margin: 5px 0;">${this.escapeHtml(folio)}</p>
                    <p style="font-size: 11px; color: #666;">Código QR no disponible</p>
                </div>
            `;
        }
    }

    async loadQRCodeLibrary() {
        return new Promise((resolve, reject) => {
            const script = document.createElement('script');
            script.src = 'https://cdnjs.cloudflare.com/ajax/libs/qrcodejs/1.0.0/qrcode.min.js';
            script.onload = resolve;
            script.onerror = reject;
            document.head.appendChild(script);
        });
    }

    async descargarTicket() {
        try {
            if (typeof jspdf === 'undefined') {
                await this.loadJsPDF();
            }
            const PAPER_WIDTH = 72;
            const { jsPDF } = window.jspdf;
            const doc = new jsPDF({
                orientation: 'portrait',
                unit: 'mm',
                format: [PAPER_WIDTH, 297]
            });

            const pageWidth = PAPER_WIDTH;
            const margin = 4;
            const contentWidth = pageWidth - (margin * 2);
            let yPosition = margin;

            // Colores modernos con negro más intenso
            const primaryColor = [0, 0, 0];
            const accentColor = [0, 0, 0];
            const darkGray = [0, 0, 0];
            const lightGray = [25, 25, 25];

            const addCenteredText = (text, fontSize, color = null, isBold = false) => {
                doc.setFontSize(fontSize);
                doc.setFont('helvetica', isBold ? 'bold' : 'normal');
                if (color) doc.setTextColor(...color);
                else doc.setTextColor(0, 0, 0);

                const textWidth = doc.getTextWidth(text);
                const x = (pageWidth - textWidth) / 2;
                doc.text(text, x, yPosition);
                yPosition += fontSize * 0.45;
            };

            const addModernLine = (thickness = 0.3, color = lightGray) => {
                doc.setDrawColor(...color);
                doc.setLineWidth(thickness);
                doc.line(margin, yPosition, pageWidth - margin, yPosition);
                yPosition += 3;
            };

            const addTwoColumnText = (label, value, fontSize = 9, isBold = false) => {
                doc.setFontSize(fontSize);
                doc.setTextColor(0, 0, 0);
                doc.setFont('helvetica', 'normal');
                doc.text(label, margin, yPosition);

                doc.setFont('helvetica', isBold ? 'bold' : 'normal');
                doc.setTextColor(0, 0, 0);
                const valueText = String(value);
                const valueWidth = doc.getTextWidth(valueText);
                doc.text(valueText, pageWidth - margin - valueWidth, yPosition);
                yPosition += 4.5;
            };

            const addHighlightBox = (text, value) => {
                const boxHeight = 8;
                doc.setFillColor(240, 240, 240);
                doc.roundedRect(margin, yPosition - 1, contentWidth, boxHeight, 1, 1, 'F');

                doc.setFontSize(10);
                doc.setFont('helvetica', 'bold');
                doc.setTextColor(0, 0, 0);
                doc.text(text, margin + 2, yPosition + 4);

                const valueWidth = doc.getTextWidth(value);
                doc.setTextColor(0, 0, 0);
                doc.text(value, pageWidth - margin - valueWidth - 2, yPosition + 4);
                yPosition += boxHeight + 2;
            };

            // Función para truncar texto con límite de caracteres
            const truncarTexto = (texto, maxCaracteres = 40) => {
                if (texto.length <= maxCaracteres) return texto;
                return texto.substring(0, maxCaracteres - 3) + '...';
            };

            // Logo y Folio en la misma línea
            const folio = this.obtenerFolio();
            const logoHeight = 8;
            const logoWidthDef = 20;
            const folioStartX = margin + logoWidthDef + 3;

            try {
                const logoPath = window.location.origin + `/Content/img/${empresa}/logo.png`;
                doc.addImage(logoPath, 'PNG', margin, yPosition, logoWidthDef, logoHeight);
                doc.setFontSize(9);
                doc.setFont('helvetica', 'bold');
                doc.setTextColor(...primaryColor);
                doc.text(folio, folioStartX, yPosition + 5);
                yPosition += logoHeight + 2;
            } catch (error) {
                console.warn('No se pudo cargar el logo:', error);
                addCenteredText(folio, 11, primaryColor, true);
                yPosition += 1;
            }

            // Fecha
            const fecha = new Date().toLocaleString('es-MX', {
                year: 'numeric',
                month: '2-digit',
                day: '2-digit',
                hour: '2-digit',
                minute: '2-digit'
            });

            doc.setFontSize(7.5);
            doc.setFont('helvetica', 'normal');
            doc.setTextColor(...lightGray);
            doc.text(fecha, folioStartX, yPosition);
            yPosition += 2;
            addModernLine(0.2);

            // Información del cliente
            const cliente = document.getElementById('vs-cot-cliente')?.value || 'N/A';
            const vendedorSelect = tomManager.getInstance('vs-cot-vendedor');
            const value = vendedorSelect?.getValue();
            const vendedor = value ? vendedorSelect.options[value]?.nombre || 'N/A' : 'N/A';

            doc.setFontSize(8);
            doc.setFont('helvetica', 'bold');
            doc.setTextColor(...accentColor);
            doc.text('INFORMACIÓN', margin, yPosition);
            yPosition += 4;

            addTwoColumnText('Cliente:', cliente, 8.5);
            addTwoColumnText('Vendedor:', vendedor, 8.5);
            addTwoColumnText('Sucursal:', sucursalText, 8.5);
            addModernLine(0.2);

            // Header de productos
            doc.setFontSize(8);
            doc.setFont('helvetica', 'bold');
            doc.setTextColor(...accentColor);
            doc.text('PRODUCTOS', margin, yPosition);
            yPosition += 5;

            const productos = this.obtenerProductosDelTicket();

            doc.setFontSize(8);
            productos.forEach((p, index) => {
                // Nombre del producto TRUNCADO a 40 caracteres
                doc.setFont('helvetica', 'bold');
                doc.setTextColor(...darkGray);
                const nombreTruncado = truncarTexto(p.descripcion, 30);
                const nombreLines = doc.splitTextToSize(nombreTruncado, contentWidth);
                nombreLines.forEach((line, lineIndex) => {
                    doc.text(line, margin, yPosition);
                    yPosition += lineIndex === nombreLines.length - 1 ? 2.5 : 2.5;
                });

                // Detalles y precio en la misma línea
                doc.setFont('helvetica', 'normal');
                doc.setTextColor(...lightGray);
                doc.setFontSize(7);
                const detalles = `${p.cantidad} ${p.unidad} × ${Currency.format(p.precio)}`;
                doc.text(detalles, margin, yPosition);

                doc.setFont('helvetica', 'bold');
                doc.setTextColor(0, 0, 0);
                doc.setFontSize(8);
                const importe = `${Currency.format(p.importe)}`;
                const importeWidth = doc.getTextWidth(importe);
                doc.text(importe, pageWidth - margin - importeWidth, yPosition);
                yPosition += 3;
            });

            yPosition += 2;
            addModernLine(0.3, darkGray);

            // Totales
            const totales = this.obtenerTotalesDelTicket();

            doc.setFontSize(8);
            addTwoColumnText('Subtotal:', `${Currency.format(totales.subtotal1)}`, 8.5);

            if (totales.descuento > 0) {
                doc.setTextColor(0, 0, 0);
                addTwoColumnText('Descuento:', `-${Currency.format(totales.descuento)}`, 8.5);
            }

            if (totales.flete > 0) {
                addTwoColumnText('Flete:', `${Currency.format(totales.flete)}`, 8.5);
            }

            addTwoColumnText('IVA (16%):', `${Currency.format(totales.iva)}`, 8.5);

            yPosition += 2;
            addModernLine(0.3, darkGray);

            // Total destacado
            addHighlightBox('TOTAL:', `${Currency.format(totales.total)}`);
            addModernLine(0.2);

            // QR Code
            yPosition += 2;
            const qrImage = await this.obtenerQRComoImagen();
            if (qrImage) {
                const qrSize = 25;
                const qrX = (pageWidth - qrSize) / 2;
                doc.setFillColor(255, 255, 255);
                doc.setDrawColor(...lightGray);
                doc.setLineWidth(0.3);
                const padding = 2;
                doc.roundedRect(qrX - padding, yPosition - padding, qrSize + (padding * 2), qrSize + (padding * 2), 1, 1, 'FD');
                doc.addImage(qrImage, 'PNG', qrX, yPosition, qrSize, qrSize);
                yPosition += qrSize + 4;
            }

            // Footer
            addModernLine(0.2);
            yPosition += 2;
            addCenteredText('¡Gracias por su preferencia!', 9, primaryColor, true);
            yPosition += 1;
            addCenteredText('Este documento es solo una cotización', 7, lightGray);
            addCenteredText('No tiene validez fiscal', 7, lightGray);

            const nombreArchivo = `cotizacion-${folio.replace(/[^a-zA-Z0-9]/g, '-')}.pdf`;
            const navegador = detectarNavegador();
            doc.autoPrint();
            const blob = doc.output('bloburl');

            if (['edge', 'brave', 'opera'].includes(navegador)) {
                window.open(blob, '_blank');
            } else {
                const iframe = document.createElement('iframe');
                iframe.style.display = 'none';
                iframe.src = blob;
                document.body.appendChild(iframe);
            }

            toastMixin.fire({
                icon: 'success',
                title: 'Ticket PDF generado exitosamente'
            });

        } catch (error) {
            console.error('Error generando ticket PDF:', error);
            toastMixin.fire({
                icon: 'error',
                title: 'Error al generar el ticket: ' + error.message
            });
        }
    }




    async loadJsPDF() {
        return new Promise((resolve, reject) => {
            const script = document.createElement('script');
            script.src = 'https://cdnjs.cloudflare.com/ajax/libs/jspdf/2.5.1/jspdf.umd.min.js';
            script.onload = resolve;
            script.onerror = reject;
            document.head.appendChild(script);
        });
    }

    obtenerProductosDelTicket() {
        const productosElements = document.querySelectorAll('.ticket-producto-item');
        const productos = [];

        productosElements.forEach(item => {
            const nombre = item.querySelector('.ticket-producto-nombre')?.textContent.trim() || '';
            const detalles = item.querySelector('.ticket-producto-detalles')?.textContent.trim() || '';

            const match = detalles.match(/([\d.]+)\s+(\w+)\s+x\s+\$([\d.]+)\s+\$([\d.]+)/);

            if (match) {
                productos.push({
                    descripcion: nombre,
                    cantidad: parseFloat(match[1]),
                    unidad: match[2],
                    precio: parseFloat(match[3]),
                    importe: parseFloat(match[4])
                });
            }
        });

        return productos;
    }

    obtenerTotalesDelTicket() {
        const extractValue = (label) => {
            const rows = document.querySelectorAll('.ticket-totales .ticket-row');
            for (let row of rows) {
                const labelText = row.querySelector('label')?.textContent.trim();
                if (labelText && labelText.includes(label)) {
                    const valueText = row.querySelector('span')?.textContent.trim() || '0';
                    return parseFloat(valueText.replace(/[$,\s]/g, ''));
                }
            }
            return 0;
        };

        return {
            subtotal1: extractValue('Subtotal'),
            descuento: extractValue('Descuento'),
            flete: extractValue('Flete'),
            iva: extractValue('IVA'),
            total: extractValue('TOTAL')
        };
    }

    async obtenerQRComoImagen() {
        try {
            const qrCanvas = document.querySelector('#vs-cot-qrContainer canvas');
            if (qrCanvas) {
                return qrCanvas.toDataURL('image/png');
            }

            const qrImg = document.querySelector('#vs-cot-qrContainer img');
            if (qrImg) {
                return qrImg.src;
            }

            return null;
        } catch (error) {
            console.warn('No se pudo obtener el código QR:', error);
            return null;
        }
    }

    escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }
}

function detectarNavegador() {

    // 🔹 Navegadores modernos (Chromium)
    if (navigator.userAgentData?.brands) {
        const brands = navigator.userAgentData.brands.map(b => b.brand.toLowerCase());

        if (brands.includes('google chrome')) return 'chrome';
        if (brands.includes('microsoft edge')) return 'edge';
        if (brands.includes('brave')) return 'brave';
        if (brands.includes('opera')) return 'opera';
        if (brands.includes('firefox')) return 'firefox';

        return 'chromium';
    }

    // 🔹 Fallback clásico
    const ua = navigator.userAgent.toLowerCase();

    if (ua.includes('edg/')) return 'edge';
    if (ua.includes('opr/') || ua.includes('opera')) return 'opera';
    if (ua.includes('brave')) return 'brave';
    if (ua.includes('chrome')) return 'chrome';
    if (ua.includes('firefox')) return 'firefox';
    if (ua.includes('safari')) return 'safari';

    return 'otro';
}

// ============================================
// 4. INICIALIZACIÓN
// ============================================
const App = {
    clienteManager: null,
    productoManager: null,
    productManagerInstance: null,
    formManager: null,
    ticketManager: null,
    form: null,
    btnSubmit: null,
    // id_cliente del documento. Se manda explícito en cada consulta de precio en vez de
    // depender de Session["idCliente"], que es global al usuario y se pisa entre pestañas.
    clienteIdActual: 0,
    clienteCveResuelta: '',

    /**
     * Devuelve el id del cliente del documento, resolviéndolo desde la clave si hace
     * falta (aquí normalmente ya lo dejó puesto el modal, pero así el criterio es el
     * mismo en los cuatro documentos).
     */
    async asegurarClienteId() {
        const cve = document.getElementById('vs-cot-cliente')?.value || '';

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

    async init() {
        this.form = document.getElementById('form-2');
        this.btnSubmit = document.querySelector('.vs-cot-btn-submit');

        // Crédito: en la cotización solo se informa — todavía no se compromete crédito.
        window.CreditoVentas?.registrar({
            prefix: 'vs-cot',
            modo: 'advertir',
            documento: 'cotizacion',
            // La solicitud va al controlador de sucursal: el de Industriales, al
            // aprobar, reconstruye un pedido VIPED desde la cotizacion de origen.
            endpointSolicitud: '/VSPedido/EnviarSolicitudGerente',
            getTipoPago: () => document.getElementById('vs-cot-tipo-pago')?.value || 'contado',
            totalId: 'vs-cot-importe',
            submitSelector: '.vs-cot-btn-submit',
            clienteInputId: 'vs-cot-cliente',
            folioId: 'vs-cot-folio'
        });

        this.clienteManager = new DataManager({
            endpoint: '/DatosGenerales/BuscarC',
            detailEndpoint: '/DatosGenerales/BuscarCliente',
            modalId: 'vs-cot-modalBuscarCliente',
            inputId: 'vs-cot-inputBuscarCliente',
            resultsId: 'vs-cot-listaResultadosClientes',
            pageSizeId: 'vs-cot-pageSizeClientes',
            btnClearId: 'vs-cot-btnLimpiarClientes',
            spinnerId: 'vs-cot-spinnerClientes',
            paginationId: 'vs-cot-paginationClientes',
            recordsFromId: 'vs-cot-recordsFromClientes',
            recordsToId: 'vs-cot-recordsToClientes',
            totalRecordsId: 'vs-cot-totalRecordsClientes',
            shouldCloseOnSelect: true
        });

        this.clienteManager.onSelect = (cliente) => {
            document.getElementById('vs-cot-cliente').value = cliente.id || '';
            document.getElementById('vs-cot-rfc').value = cliente.rfc || '';
            document.getElementById('vs-cot-info-proveedor').value =
                [cliente.dir, cliente.col, cliente.pob, cliente.cp].filter(x => x).join(',\n');
            document.getElementById('vs-cot-lista-precios').value = cliente.cod_ant || 'Sin asignar';
            asignarVendedorSucursal('vs-cot-vendedor', cliente);
            window.CreditoVentas?.setCliente('vs-cot', cliente);

            // El cliente define la lista de precios y las reglas por cliente: se manda
            // explícito al buscar productos y se recalcula lo ya capturado.
            this.clienteIdActual = parseInt(cliente.id_cliente, 10) || 0;
            this.clienteCveResuelta = cliente.id || '';
            this.productManagerInstance?.recotizarPorCliente(
                this.clienteIdActual, cliente.descripcion);
        };

        this.productoManager = new DataManager({
            endpoint: '/DatosGenerales/BuscarPCotizacion',
            detailEndpoint: '/DatosGenerales/BuscarProductoCotizacion',
            modalId: 'vs-cot-modalBuscarProducto',
            inputId: 'vs-cot-inputBuscarProducto',
            resultsId: 'vs-cot-listaResultadosProductos',
            pageSizeId: 'vs-cot-pageSizeProductos',
            btnClearId: 'vs-cot-btnLimpiarProductos',
            spinnerId: 'vs-cot-spinnerProductos',
            paginationId: 'vs-cot-paginationProductos',
            recordsFromId: 'vs-cot-recordsFromProductos',
            recordsToId: 'vs-cot-recordsToProductos',
            totalRecordsId: 'vs-cot-totalRecordsProductos',
            shouldCloseOnSelect: false
        });
        await this.cargarPermisosUsuario();
        // ProductManager y FormManager UNA sola vez
        this.productManagerInstance = new ProductManager();
        this.productoManager.getExtraParams = async () => ({ clienteId: await this.asegurarClienteId() });
        this.productoManager.onSelect = (producto) => {
            this.productManagerInstance.agregarProducto(producto);
        };
        this.formManager = new FormManager(this.productManagerInstance);
        this.ticketManager = new TicketManager();

        // Tipo de movimiento
        const tipoMovSelect = document.getElementById('vs-cot-tipo-docto-mov');
        tipoMovSelect?.addEventListener('change', (e) =>
            this.manejarCambioTipoMovimiento(e.target.value)
        );
        this.manejarCambioTipoMovimiento(tipoMovSelect?.value || 'alta');

        console.log('Sistema de cotización inicializado correctamente');
        window.verDatosFormulario = () => this.formManager.verDatosFormulario();
    },

    async cargarPermisosUsuario() {
        try {
            const response = await fetch('/DatosGenerales/ObtenerPermisosUsuario');
            if (!response.ok) return;
            const permisos = await response.json();

            if (permisos.tienePrecio && permisos.tokenPrecio) {
                authState.precioToken = permisos.tokenPrecio;
            }
            if (permisos.tieneDescuento && permisos.tokenDescuento) {
                authState.descuentoToken = permisos.tokenDescuento;
            }
        } catch (err) {
            console.warn('No se pudieron verificar permisos automáticos:', err);
        }
    },

    manejarCambioTipoMovimiento(tipo) {
        if (tipo === 'consulta') this.deshabilitarFormulario();
        else this.habilitarFormulario();
    },

    deshabilitarFormulario() {
        const form = this.form || this.formManager?.form || document.getElementById('vs-cot-formCotizacion');
        if (!form) return;
        form.querySelectorAll('input, select, textarea, button').forEach(el => {
            if (el.id !== 'vs-cot-tipo-docto-mov') {
                el.disabled = true;
                el.classList.add('vs-cot-disabled-field');
            }
        });
        document.querySelector('[data-bs-target="#vs-cot-modalBuscarCliente"]')?.setAttribute('disabled', true);
        document.querySelector('[data-bs-target="#vs-cot-modalBuscarProducto"]')?.setAttribute('disabled', true);
        document.querySelectorAll('.vs-cot-btn-eliminar').forEach(btn => btn.disabled = true);
        if (window.tomManager) {
            ['vs-cot-vendedor', 'vs-cot-moneda', 'vs-cot-uso-cfdi', 'vs-cot-forma-pago']
                .forEach(id => tomManager.instances.get(id)?.disable());
        }
        const btn = this.btnSubmit || document.querySelector('.vs-cot-btn-submit');
        if (btn) btn.innerHTML = '<i class="fas fa-search"></i> Consultar';
    },

    habilitarFormulario() {
        const form = this.form || this.formManager?.form || document.getElementById('vs-cot-formCotizacion');
        if (!form) return;
        form.querySelectorAll('input, select, textarea, button').forEach(el => {
            el.disabled = false;
            el.classList.remove('vs-cot-disabled-field');
        });
        document.querySelector('[data-bs-target="#vs-cot-modalBuscarCliente"]')?.removeAttribute('disabled');
        document.querySelector('[data-bs-target="#vs-cot-modalBuscarProducto"]')?.removeAttribute('disabled');
        if (window.tomManager) {
            ['vs-cot-vendedor', 'vs-cot-moneda', 'vs-cot-uso-cfdi', 'vs-cot-forma-pago']
                .forEach(id => tomManager.instances.get(id)?.enable());
        }
        const tipoMovimiento = document.getElementById('vs-cot-tipo-docto-mov')?.value;
        const btn = this.btnSubmit || document.querySelector('.vs-cot-btn-submit');
        if (btn) {
            btn.innerHTML = tipoMovimiento === 'modificacion'
                ? '<i class="fas fa-save"></i> Actualizar Cotización'
                : '<i class="fas fa-save"></i> Guardar Cotización';
        }
        // Re-aplicar bloqueo de descuento/precio si los tokens no existen
        this.productManagerInstance?.renderTable();
    },
};

// ============================================
// 5. FUNCIONES AUXILIARES
// ============================================
// El banner de crédito vive en Scripts/Ventas/credito-ventas.js (CreditoVentas),
// compartido por cotización, pedido, remisión y factura.

// ============================================
// 5b. VENDEDOR POR DEFECTO EN MOSTRADOR
// ============================================
/**
 * El cliente genérico de mostrador no tiene vendedor asignado en el catálogo, y en una
 * sucursal la mayoría de las ventas son suyas: sin esto el vendedor quedaba vacío y la
 * validación del formulario frenaba cada venta de piso. Se le asigna el 999 (genérico)
 * con el nombre del usuario que está capturando.
 */
function asignarVendedorSucursal(selectId, cliente) {
    const inst = tomManager.getInstance(selectId);
    if (!inst) return;

    const esMostrador = (cliente?.descripcion || '').toLowerCase().includes('mostrador');

    if (esMostrador) {
        const VENDEDOR_MOSTRADOR = '999';
        if (!Object.keys(inst.options).includes(VENDEDOR_MOSTRADOR)) {
            inst.addOption({ id: VENDEDOR_MOSTRADOR, nombre: vendedor });
            inst.refreshOptions(true);
        }
        inst.setValue(VENDEDOR_MOSTRADOR, true);
        return;
    }

    if (cliente?.cve_vdr) inst.setValue(cliente.cve_vdr, true);
    else inst.clear();
}

// ============================================
// 6. TOM SELECT
// ============================================
GetData({ path: '/DatosGenerales/DatosSelect' }).then((_res) => {
    const { vendedores, monedas, usocfdi: cfdi, formaspago: fpago, incoterms, tasas, sucursales, almacenes } = _res;

    const mkOpt = (prefix, data, escape) =>
        `<div class="${prefix}-custom-option" style="color:var(--text-dark);">${escape(data.nombre)} (ID: ${escape(data.id)})</div>`;
    const mkItem = (prefix, data, escape) =>
        `<div class="${prefix}-custom-item" style="color:var(--text-dark);">${escape(data.nombre)}</div>`;

    const tasaForMoneda = (value, targetId) => {
        const v = value === 'PESOS' ? 'peso' : value === 'EURO' ? 'euro' : value === 'DLLS' ? 'dolar' : null;
        const tasaData = tasas?.[0];
        document.getElementById(targetId).value = (tasaData && v && tasaData[v] !== undefined) ? tasaData[v] : '';
    };

    const filtrarAlmacenes = (sucursalId, almacenKey) => {
        const inst = tomManager.getInstance(almacenKey);
        if (!inst) return;
        const filtrados = almacenes.filter(a => a.sucursal_id == sucursalId);
        inst.clear(); inst.clearOptions(); inst.addOptions(filtrados); inst.refreshOptions(false);
    };

    const create = (id, opts) => tomManager.create(id, {
        selector: `#${id}`, valueField: 'id', displayField: 'nombre',
        searchField: ['id', 'nombre'], maxItems: 1, create: false, ...opts
    });

    // ── Cotización ──
    create('vs-cot-vendedor', { options: vendedores, placeholder: 'Seleccione un vendedor...', dropdownParent: 'body', render: { option: (d, e) => mkOpt('vs-cot', d, e), item: (d, e) => mkItem('vs-cot', d, e) }, onChange: v => console.log('Vendedor:', v) });
    create('vs-cot-moneda', { options: monedas, placeholder: 'Seleccione una moneda...', dropdownParent: 'body', render: { option: (d, e) => mkOpt('vs-cot', d, e), item: (d, e) => mkItem('vs-cot', d, e) }, onChange: v => tasaForMoneda(v, 'vs-cot-paridad') });
    create('vs-cot-uso-cfdi', { options: cfdi, placeholder: 'Seleccione el cfdi...', render: { option: (d, e) => mkOpt('vs-cot', d, e), item: (d, e) => mkItem('vs-cot', d, e) }, onChange: v => console.log('CFDI:', v) });
    create('vs-cot-forma-pago', { options: fpago, placeholder: 'Seleccione la forma de pago...', render: { option: (d, e) => mkOpt('vs-cot', d, e), item: (d, e) => mkItem('vs-cot', d, e) }, onChange: v => console.log('FPago:', v) });

    // ── Pedido ──
    create('vs-ped-vendedor', { options: vendedores, placeholder: 'Seleccione un vendedor...', render: { option: (d, e) => mkOpt('vs-ped', d, e), item: (d, e) => mkItem('vs-ped', d, e) }, onChange: v => console.log('Vendedor:', v) });
    create('vs-ped-moneda', { options: monedas, placeholder: 'Seleccione una moneda...', render: { option: (d, e) => mkOpt('vs-ped', d, e), item: (d, e) => mkItem('vs-ped', d, e) }, onChange: v => tasaForMoneda(v, 'vs-ped-paridad') });
    create('vs-ped-uso-cfdi', { options: cfdi, placeholder: 'Seleccione el cfdi...', render: { option: (d, e) => mkOpt('vs-ped', d, e), item: (d, e) => mkItem('vs-ped', d, e) }, onChange: v => console.log('CFDI:', v) });
    create('vs-ped-forma-pago', { options: fpago, placeholder: 'Seleccione la forma de pago...', render: { option: (d, e) => mkOpt('vs-ped', d, e), item: (d, e) => mkItem('vs-ped', d, e) }, onChange: v => console.log('FPago:', v) });
    create('vs-ped-incoterm', { options: incoterms, placeholder: 'Seleccione el incoterm...', render: { option: (d, e) => mkOpt('vs-ped', d, e), item: (d, e) => mkItem('vs-ped', d, e) }, onChange: v => console.log('Incoterm:', v) });
    create('vs-ped-sucursal', { options: sucursales, placeholder: 'Seleccione la sucursal...', render: { option: (d, e) => mkOpt('vs-ped', d, e), item: (d, e) => mkItem('vs-ped', d, e) }, onChange: id => filtrarAlmacenes(id, 'vs-ped-almacen') });
    create('vs-ped-almacen', { options: [], placeholder: 'Seleccione el almacén...', render: { option: (d, e) => mkOpt('vs-ped', d, e), item: (d, e) => mkItem('vs-ped', d, e) }, onChange: v => console.log('Almacén:', v) });

    // ── Remisión ──
    create('vs-rem-vendedor', { options: vendedores, placeholder: 'Seleccione un vendedor...', render: { option: (d, e) => mkOpt('vs-rem', d, e), item: (d, e) => mkItem('vs-rem', d, e) }, onChange: v => console.log('Vendedor:', v) });
    create('vs-rem-moneda', { options: monedas, placeholder: 'Seleccione una moneda...', render: { option: (d, e) => mkOpt('vs-rem', d, e), item: (d, e) => mkItem('vs-rem', d, e) }, onChange: v => tasaForMoneda(v, 'vs-rem-paridad') });
    create('vs-rem-uso-cfdi', { options: cfdi, placeholder: 'Seleccione el cfdi...', render: { option: (d, e) => mkOpt('vs-rem', d, e), item: (d, e) => mkItem('vs-rem', d, e) }, onChange: v => console.log('CFDI:', v) });
    create('vs-rem-forma-pago', { options: fpago, placeholder: 'Seleccione la forma de pago...', render: { option: (d, e) => mkOpt('vs-rem', d, e), item: (d, e) => mkItem('vs-rem', d, e) }, onChange: v => console.log('FPago:', v) });
    create('vs-rem-incoterm', { options: incoterms, placeholder: 'Seleccione el incoterm...', render: { option: (d, e) => mkOpt('vs-rem', d, e), item: (d, e) => mkItem('vs-rem', d, e) }, onChange: v => console.log('Incoterm:', v) });
    create('vs-rem-sucursal', { options: sucursales, placeholder: 'Seleccione la sucursal...', render: { option: (d, e) => mkOpt('vs-rem', d, e), item: (d, e) => mkItem('vs-rem', d, e) }, onChange: id => filtrarAlmacenes(id, 'vs-rem-almacen') });
    create('vs-rem-almacen', { options: [], placeholder: 'Seleccione el almacén...', render: { option: (d, e) => mkOpt('vs-rem', d, e), item: (d, e) => mkItem('vs-rem', d, e) }, onChange: v => console.log('Almacén:', v) });

    // ── Factura ──
    create('vs-fac-vendedor', { options: vendedores, placeholder: 'Seleccione un vendedor...', render: { option: (d, e) => mkOpt('vs-fac', d, e), item: (d, e) => mkItem('vs-fac', d, e) }, onChange: v => console.log('Vendedor:', v) });
    create('vs-fac-moneda', { options: monedas, placeholder: 'Seleccione una moneda...', render: { option: (d, e) => mkOpt('vs-fac', d, e), item: (d, e) => mkItem('vs-fac', d, e) }, onChange: v => tasaForMoneda(v, 'vs-fac-paridad') });
    create('vs-fac-uso-cfdi', { options: cfdi, placeholder: 'Seleccione el cfdi...', render: { option: (d, e) => mkOpt('vs-fac', d, e), item: (d, e) => mkItem('vs-fac', d, e) }, onChange: v => console.log('CFDI:', v) });
    create('vs-fac-forma-pago', { options: fpago, placeholder: 'Seleccione la forma de pago...', render: { option: (d, e) => mkOpt('vs-fac', d, e), item: (d, e) => mkItem('vs-fac', d, e) }, onChange: v => console.log('FPago:', v) });
    create('vs-fac-incoterm', { options: incoterms, placeholder: 'Seleccione el incoterm...', render: { option: (d, e) => mkOpt('vs-fac', d, e), item: (d, e) => mkItem('vs-fac', d, e) }, onChange: v => console.log('Incoterm:', v) });
    create('vs-fac-sucursal', { options: sucursales, placeholder: 'Seleccione la sucursal...', render: { option: (d, e) => mkOpt('vs-fac', d, e), item: (d, e) => mkItem('vs-fac', d, e) }, onChange: id => filtrarAlmacenes(id, 'vs-fac-almacen') });
    create('vs-fac-almacen', { options: [], placeholder: 'Seleccione el almacén...', render: { option: (d, e) => mkOpt('vs-fac', d, e), item: (d, e) => mkItem('vs-fac', d, e) }, onChange: v => console.log('Almacén:', v) });

    // ── Factura Anticipo ──
    create('vs-fac-ant-vendedor', { options: vendedores, placeholder: 'Seleccione un vendedor...', render: { option: (d, e) => mkOpt('vs-fac', d, e), item: (d, e) => mkItem('vs-fac', d, e) }, onChange: v => console.log('Vendedor:', v) });
    create('vs-fac-ant-moneda', { options: monedas, placeholder: 'Seleccione una moneda...', render: { option: (d, e) => mkOpt('vs-fac', d, e), item: (d, e) => mkItem('vs-fac', d, e) }, onChange: v => tasaForMoneda(v, 'vs-fac-paridad') });
    create('vs-fac-ant-uso-cfdi', { options: cfdi, placeholder: 'Seleccione el cfdi...', render: { option: (d, e) => mkOpt('vs-fac', d, e), item: (d, e) => mkItem('vs-fac', d, e) }, onChange: v => console.log('CFDI:', v) });
    create('vs-fac-ant-forma-pago', { options: fpago, placeholder: 'Seleccione la forma de pago...', render: { option: (d, e) => mkOpt('vs-fac', d, e), item: (d, e) => mkItem('vs-fac', d, e) }, onChange: v => console.log('FPago:', v) });
    create('vs-fac-ant-incoterm', { options: incoterms, placeholder: 'Seleccione el incoterm...', render: { option: (d, e) => mkOpt('vs-fac', d, e), item: (d, e) => mkItem('vs-fac', d, e) }, onChange: v => console.log('Incoterm:', v) });
    create('vs-fac-ant-sucursal', { options: sucursales, placeholder: 'Seleccione la sucursal...', render: { option: (d, e) => mkOpt('vs-fac', d, e), item: (d, e) => mkItem('vs-fac', d, e) }, onChange: id => filtrarAlmacenes(id, 'vs-fac-almacen') });
    create('vs-fac-ant-almacen', { options: [], placeholder: 'Seleccione el almacén...', render: { option: (d, e) => mkOpt('vs-fac', d, e), item: (d, e) => mkItem('vs-fac', d, e) }, onChange: v => console.log('Almacén:', v) });

    // Valores por defecto
    tomManager.getInstance('vs-cot-moneda').setValue('PESOS', false);
    tomManager.getInstance('vs-fac-ant-moneda').setValue('PESOS', false);
    tomManager.getInstance('vs-ped-sucursal').setValue(sucursalUsuario, false);
    tomManager.getInstance('vs-ped-almacen').setValue('1', false);
    tomManager.getInstance('vs-rem-sucursal').setValue(sucursalUsuario, false);
    tomManager.getInstance('vs-rem-almacen').setValue('1', false);
    tomManager.getInstance('vs-fac-sucursal').setValue(sucursalUsuario, false);
    tomManager.getInstance('vs-fac-almacen').setValue('1', false);
});

// ============================================
// 7. ARRANQUE
// ============================================
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => App.init());
} else {
    App.init();
}
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
            table: document.getElementById('vi-cot-productosTable'),
            subtotal1Display: document.getElementById('vi-cot-total-subtotal1-display'),
            descuentoDisplay: document.getElementById('vi-cot-total-descuento-display'),
            fleteDisplay: document.getElementById('vi-cot-total-flete-display'),
            subtotal2Display: document.getElementById('vi-cot-total-subtotal2-display'),
            ivaDisplay: document.getElementById('vi-cot-iva-display'),
            importeDisplay: document.getElementById('vi-cot-importe-display'),
            fleteInput: document.getElementById('vi-cot-flete-val')
        };

        this.init();
    }

    init() {
        this.dom.table?.addEventListener('input', (e) => {
            const input = e.target;
            if (input.classList.contains('vi-cot-cantidad') ||
                input.classList.contains('vi-cot-comentario')) {
                const productoId = input.closest('tr')?.dataset.productoId;
                if (productoId) this.actualizarFila(productoId);
            }
        });

        this.dom.table?.addEventListener('blur', (e) => {
            const input = e.target;
            if (input.classList.contains('vi-cot-precio') ||
                input.classList.contains('vi-cot-descuento')) {
                const productoId = input.closest('tr')?.dataset.productoId;
                if (productoId) this.actualizarFila(productoId);
            }
        }, true);

        this.dom.table?.addEventListener('click', (e) => {
            const btnEliminar = e.target.closest('.vi-cot-btn-eliminar');
            if (btnEliminar) {
                const productoId = btnEliminar.closest('tr')?.dataset.productoId;
                if (productoId) this.eliminarProducto(productoId);
                return;
            }
            const overlay = e.target.closest('.vi-cot-field-overlay');
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
            comentario: ''
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
<tr class="vi-cot-empty-state">
    <td colspan="10" class="text-center py-5">
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
            const tr = document.createElement('tr');
            tr.dataset.productoId = p.id;

            // ══════════════════════════════════════════════════════════════
            // CELDA PRECIO
            // SIEMPRE protegida con contraseña — sin excepción.
            // AUTO o MANUAL no cambia esto, solo cambia el valor mostrado.
            // ══════════════════════════════════════════════════════════════
            const precioCell = document.createElement('td');
            precioCell.style.position = 'relative';

            const precioInput = document.createElement('input');
            precioInput.type = 'number';
            precioInput.className = 'vi-cot-product-input vi-cot-precio';
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
                overlayPrecio.className = 'vi-cot-field-overlay';
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
            descInput.className = 'vi-cot-product-input vi-cot-descuento';
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
                overlayDesc.className = 'vi-cot-field-overlay';
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
        <td>
            <input type="number" class="vi-cot-product-input vi-cot-cantidad"
                   value="${p.cantidad}" min="0" step="0.01">
        </td>
        <td>${this.escapeHtml(p.unidad)}</td>`;

            tr.appendChild(precioCell);
            tr.appendChild(descCell);

            const resto = document.createElement('template');
            resto.innerHTML = `
        <td class="vi-cot-importe fw-bold">${importe.toFixed(2)}</td>
        <td>
            <input type="text" class="vi-cot-product-input vi-cot-comentario"
                   value="${this.escapeHtml(p.comentario)}" placeholder="Comentario...">
        </td>
        <td>
            <button type="button" class="vi-cot-btn-delete vi-cot-btn-eliminar">
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

        producto.cantidad = parseFloat(tr.querySelector('.vi-cot-cantidad').value) || 0;
        producto.comentario = tr.querySelector('.vi-cot-comentario').value || '';

        // ── Precio y descuento contra la regla vigente ────────────────────
        // Las comprobaciones viven en el módulo compartido para que los cuatro
        // documentos apliquen exactamente el mismo criterio.
        const precioInput = tr.querySelector('.vi-cot-precio');
        if (precioInput && !precioInput.disabled) {
            const res = ReglasPrecio.validarPrecio(
                producto,
                parseFloat(precioInput.value) || 0,
                !!authState.precioToken);

            producto.precio = res.valor;
            if (res.aviso) precioInput.value = res.valor.toFixed(2);
            ReglasPrecio.avisar(res.aviso);
        }

        const descInput = tr.querySelector('.vi-cot-descuento');
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

        tr.querySelector('.vi-cot-importe').textContent = this.calcularImporte(producto).toFixed(2);
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
        const selector = tipo === 'descuento' ? '.vi-cot-descuento' : '.vi-cot-precio';
        this.dom.table?.querySelectorAll(selector).forEach(input => {
            input.removeAttribute('disabled');
            input.parentElement?.querySelector('.vi-cot-field-overlay')?.remove();
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

        const flete = parseFloat(document.getElementById('vi-cot-flete-val').value) || 0;
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

        this.updateHiddenField('vi-cot-total-subtotal1', totales.subtotal1);
        this.updateHiddenField('vi-cot-total-descuento', totales.totalDescuento);
        this.updateHiddenField('vi-cot-total-flete', flete);
        this.updateHiddenField('vi-cot-total-subtotal2', subtotal2);
        this.updateHiddenField('vi-cot-iva', iva);
        this.updateHiddenField('vi-cot-importe', total);

        window.CreditoVentas?.evaluar('vi-cot');
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
        this.form = document.getElementById('vi-cot-formCotizacion');
        this.btnSubmit = document.querySelector('.vi-cot-btn-submit');
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

        formData.append('sucursal', document.getElementById('vi-cot-sucursal')?.value || '');
        formData.append('almacen', document.getElementById('vi-cot-almacen')?.value || '');
        formData.append('tipoMovimiento', document.getElementById('vi-cot-tipo-docto-mov')?.value || '');
        formData.append('folio', document.getElementById('vi-cot-folio')?.value || '');

        formData.append('cliente', document.getElementById('vi-cot-cliente')?.value || '');
        formData.append('rfc', document.getElementById('vi-cot-rfc')?.value || '');
        formData.append('vendedor', document.getElementById('vi-cot-vendedor')?.value || '');
        formData.append('contacto', document.getElementById('vi-cot-contacto')?.value || '');
        formData.append('moneda', document.getElementById('vi-cot-moneda')?.value || '');
        formData.append('paridad', document.getElementById('vi-cot-paridad')?.value || '');
        formData.append('concepto', document.getElementById('vi-cot-concepto')?.value || '');
        formData.append('incoterm', document.getElementById('vi-cot-incoterm')?.value || '');
        formData.append('ordenCompra', document.getElementById('vi-cot-orden-compra-val')?.value || '');

        formData.append('limiteCredito', document.getElementById('vi-cot-limite-credito')?.value || '');
        formData.append('plazo', document.getElementById('vi-cot-plazo')?.value || '');
        formData.append('fechaPago', document.getElementById('vi-cot-fecha-pago')?.value || '');
        formData.append('formaPago', document.getElementById('vi-cot-forma-pago')?.value || '');
        formData.append('usoCFDI', document.getElementById('vi-cot-uso-cfdi')?.value || '');
        formData.append('comentarios', document.getElementById('vi-cot-comentarios')?.value || '');

        formData.append('tipoPago', document.getElementById('vi-cot-tipo-pago')?.value || 'contado');

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

        const cliente = document.getElementById('vi-cot-cliente')?.value;
        if (!cliente?.trim()) errores.push('Debe seleccionar un cliente');

        if (productos.length === 0) errores.push('Debe agregar al menos un producto');

        const vendedor = document.getElementById('vi-cot-vendedor')?.value;
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
                endpoint: '/VICotizacion/Guardar',
                metodo: 'POST',
                mensajeExito: 'Cotización creada exitosamente',
                textoCarga: 'Guardando...',
                validarProductos: true
            },
            'modificacion': {
                endpoint: '/VNCotizacion/Modificar',
                metodo: 'PUT',
                mensajeExito: 'Cotización modificada exitosamente',
                textoCarga: 'Actualizando...',
                validarProductos: true
            },
            'consulta': {
                endpoint: '/VNCotizacion/Consultar',
                metodo: 'GET',
                mensajeExito: 'Consulta realizada exitosamente',
                textoCarga: 'Consultando...',
                validarProductos: false
            }
        };
        return configuraciones[tipoMovimiento] || configuraciones['alta'];
    }

    async enviarCotizacion() {
        const tipoMovimiento = document.getElementById('vi-cot-tipo-docto-mov')?.value || 'alta';
        const config = this.obtenerConfiguracionPorTipo(tipoMovimiento);

        const errores = this.validarFormulario();
        if (errores.length > 0) {
            toastMixin.fire({ icon: 'error', title: errores.join(' | ') });
            return;
        }

        const originalText = this.btnSubmit.innerHTML;
        this.btnSubmit.disabled = true;
        this.btnSubmit.innerHTML = `<i class="fas fa-spinner fa-spin"></i> ${config.textoCarga}`;

        try {
            const formData = this.recopilarDatosFormulario();
            const response = await fetch(config.endpoint, {
                method: config.metodo.toUpperCase(),
                body: formData
            });
            const result = await response.json();

            if (result.success) {
                await Swal.fire({
                    icon: 'success',
                    title: `${result.message || ''} Folio: ${result.folio_generado || ''}`.trim() || config.mensajeExito,
                    confirmButtonText: 'Aceptar'
                });
                window.location.reload();
            } else {
                toastMixin.fire({ icon: 'error', title: result.message || 'Ocurrió un error en el servidor' });
            }
        } catch (error) {
            console.error('Error enviando cotización:', error);
            toastMixin.fire({ icon: 'error', title: error.message || 'Ocurrió un error al enviar la cotización' });
        } finally {
            this.btnSubmit.disabled = false;
            this.btnSubmit.innerHTML = originalText;
        }
    }

    limpiarFormulario() {
        this.form?.reset();
        this.productManager.productos = [];
        this.productManager.renderTable();
        this.productManager.calcularTotales();
        document.getElementById('vi-cot-cliente').value = '';
        document.getElementById('vi-cot-rfc').value = '';
        document.getElementById('vi-cot-info-proveedor').value = '';
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
// 4. INICIALIZACIÓN
// ============================================
const App = {
    clienteManager: null,
    productoManager: null,
    productManagerInstance: null,
    formManager: null,
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
        const cve = document.getElementById('vi-cot-cliente')?.value || '';

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
        this.btnSubmit = document.querySelector('.vi-cot-btn-submit');

        // Crédito: en la cotización solo se informa — todavía no se compromete crédito.
        window.CreditoVentas?.registrar({
            prefix: 'vi-cot',
            modo: 'advertir',
            documento: 'cotizacion',
            getTipoPago: () => document.getElementById('vi-cot-tipo-pago')?.value || 'contado',
            totalId: 'vi-cot-importe',
            submitSelector: '.vi-cot-btn-submit',
            clienteInputId: 'vi-cot-cliente',
            folioId: 'vi-cot-folio'
        });

        this.clienteManager = new DataManager({
            endpoint: '/DatosGenerales/BuscarC',
            detailEndpoint: '/DatosGenerales/BuscarCliente',
            modalId: 'vi-cot-modalBuscarCliente',
            inputId: 'vi-cot-inputBuscarCliente',
            resultsId: 'vi-cot-listaResultadosClientes',
            pageSizeId: 'vi-cot-pageSizeClientes',
            btnClearId: 'vi-cot-btnLimpiarClientes',
            spinnerId: 'vi-cot-spinnerClientes',
            paginationId: 'vi-cot-paginationClientes',
            recordsFromId: 'vi-cot-recordsFromClientes',
            recordsToId: 'vi-cot-recordsToClientes',
            totalRecordsId: 'vi-cot-totalRecordsClientes',
            shouldCloseOnSelect: true
        });

        this.clienteManager.onSelect = (cliente) => {
            document.getElementById('vi-cot-cliente').value = cliente.id || '';
            document.getElementById('vi-cot-rfc').value = cliente.rfc || '';
            document.getElementById('vi-cot-info-proveedor').value =
                [cliente.dir, cliente.col, cliente.pob, cliente.cp].filter(x => x).join(',\n');
            document.getElementById('vi-cot-lista-precios').value = cliente.cod_ant || 'Sin asignar';
            const vendedorInstance = tomManager.getInstance('vi-cot-vendedor');
            if (vendedorInstance && cliente.cve_vdr) vendedorInstance.setValue(cliente.cve_vdr, true);
            window.CreditoVentas?.setCliente('vi-cot', cliente);

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
            modalId: 'vi-cot-modalBuscarProducto',
            inputId: 'vi-cot-inputBuscarProducto',
            resultsId: 'vi-cot-listaResultadosProductos',
            pageSizeId: 'vi-cot-pageSizeProductos',
            btnClearId: 'vi-cot-btnLimpiarProductos',
            spinnerId: 'vi-cot-spinnerProductos',
            paginationId: 'vi-cot-paginationProductos',
            recordsFromId: 'vi-cot-recordsFromProductos',
            recordsToId: 'vi-cot-recordsToProductos',
            totalRecordsId: 'vi-cot-totalRecordsProductos',
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

        // Tipo de movimiento
        const tipoMovSelect = document.getElementById('vi-cot-tipo-docto-mov');
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
        const form = this.form || this.formManager?.form || document.getElementById('vi-cot-formCotizacion');
        if (!form) return;
        form.querySelectorAll('input, select, textarea, button').forEach(el => {
            if (el.id !== 'vi-cot-tipo-docto-mov') {
                el.disabled = true;
                el.classList.add('vi-cot-disabled-field');
            }
        });
        document.querySelector('[data-bs-target="#vi-cot-modalBuscarCliente"]')?.setAttribute('disabled', true);
        document.querySelector('[data-bs-target="#vi-cot-modalBuscarProducto"]')?.setAttribute('disabled', true);
        document.querySelectorAll('.vi-cot-btn-eliminar').forEach(btn => btn.disabled = true);
        if (window.tomManager) {
            ['vi-cot-vendedor', 'vi-cot-moneda', 'vi-cot-uso-cfdi', 'vi-cot-forma-pago']
                .forEach(id => tomManager.instances.get(id)?.disable());
        }
        const btn = this.btnSubmit || document.querySelector('.vi-cot-btn-submit');
        if (btn) btn.innerHTML = '<i class="fas fa-search"></i> Consultar';
    },

    habilitarFormulario() {
        const form = this.form || this.formManager?.form || document.getElementById('vi-cot-formCotizacion');
        if (!form) return;
        form.querySelectorAll('input, select, textarea, button').forEach(el => {
            el.disabled = false;
            el.classList.remove('vi-cot-disabled-field');
        });
        document.querySelector('[data-bs-target="#vi-cot-modalBuscarCliente"]')?.removeAttribute('disabled');
        document.querySelector('[data-bs-target="#vi-cot-modalBuscarProducto"]')?.removeAttribute('disabled');
        if (window.tomManager) {
            ['vi-cot-vendedor', 'vi-cot-moneda', 'vi-cot-uso-cfdi', 'vi-cot-forma-pago']
                .forEach(id => tomManager.instances.get(id)?.enable());
        }
        const tipoMovimiento = document.getElementById('vi-cot-tipo-docto-mov')?.value;
        const btn = this.btnSubmit || document.querySelector('.vi-cot-btn-submit');
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
    create('vi-cot-vendedor', { options: vendedores, placeholder: 'Seleccione un vendedor...', dropdownParent: 'body', render: { option: (d, e) => mkOpt('vi-cot', d, e), item: (d, e) => mkItem('vi-cot', d, e) }, onChange: v => console.log('Vendedor:', v) });
    create('vi-cot-moneda', { options: monedas, placeholder: 'Seleccione una moneda...', dropdownParent: 'body', render: { option: (d, e) => mkOpt('vi-cot', d, e), item: (d, e) => mkItem('vi-cot', d, e) }, onChange: v => tasaForMoneda(v, 'vi-cot-paridad') });
    create('vi-cot-uso-cfdi', { options: cfdi, placeholder: 'Seleccione el cfdi...', render: { option: (d, e) => mkOpt('vi-cot', d, e), item: (d, e) => mkItem('vi-cot', d, e) }, onChange: v => console.log('CFDI:', v) });
    create('vi-cot-forma-pago', { options: fpago, placeholder: 'Seleccione la forma de pago...', render: { option: (d, e) => mkOpt('vi-cot', d, e), item: (d, e) => mkItem('vi-cot', d, e) }, onChange: v => console.log('FPago:', v) });

    // ── Pedido ──
    create('vi-ped-vendedor', { options: vendedores, placeholder: 'Seleccione un vendedor...', render: { option: (d, e) => mkOpt('vi-ped', d, e), item: (d, e) => mkItem('vi-ped', d, e) }, onChange: v => console.log('Vendedor:', v) });
    create('vi-ped-moneda', { options: monedas, placeholder: 'Seleccione una moneda...', render: { option: (d, e) => mkOpt('vi-ped', d, e), item: (d, e) => mkItem('vi-ped', d, e) }, onChange: v => tasaForMoneda(v, 'vi-ped-paridad') });
    create('vi-ped-uso-cfdi', { options: cfdi, placeholder: 'Seleccione el cfdi...', render: { option: (d, e) => mkOpt('vi-ped', d, e), item: (d, e) => mkItem('vi-ped', d, e) }, onChange: v => console.log('CFDI:', v) });
    create('vi-ped-forma-pago', { options: fpago, placeholder: 'Seleccione la forma de pago...', render: { option: (d, e) => mkOpt('vi-ped', d, e), item: (d, e) => mkItem('vi-ped', d, e) }, onChange: v => console.log('FPago:', v) });
    create('vi-ped-incoterm', { options: incoterms, placeholder: 'Seleccione el incoterm...', render: { option: (d, e) => mkOpt('vi-ped', d, e), item: (d, e) => mkItem('vi-ped', d, e) }, onChange: v => console.log('Incoterm:', v) });
    create('vi-ped-sucursal', { options: sucursales, placeholder: 'Seleccione la sucursal...', render: { option: (d, e) => mkOpt('vi-ped', d, e), item: (d, e) => mkItem('vi-ped', d, e) }, onChange: id => filtrarAlmacenes(id, 'vi-ped-almacen') });
    create('vi-ped-almacen', { options: [], placeholder: 'Seleccione el almacén...', render: { option: (d, e) => mkOpt('vi-ped', d, e), item: (d, e) => mkItem('vi-ped', d, e) }, onChange: v => console.log('Almacén:', v) });

    // ── Remisión ──
    create('vi-rem-vendedor', { options: vendedores, placeholder: 'Seleccione un vendedor...', render: { option: (d, e) => mkOpt('vi-rem', d, e), item: (d, e) => mkItem('vi-rem', d, e) }, onChange: v => console.log('Vendedor:', v) });
    create('vi-rem-moneda', { options: monedas, placeholder: 'Seleccione una moneda...', render: { option: (d, e) => mkOpt('vi-rem', d, e), item: (d, e) => mkItem('vi-rem', d, e) }, onChange: v => tasaForMoneda(v, 'vi-rem-paridad') });
    create('vi-rem-uso-cfdi', { options: cfdi, placeholder: 'Seleccione el cfdi...', render: { option: (d, e) => mkOpt('vi-rem', d, e), item: (d, e) => mkItem('vi-rem', d, e) }, onChange: v => console.log('CFDI:', v) });
    create('vi-rem-forma-pago', { options: fpago, placeholder: 'Seleccione la forma de pago...', render: { option: (d, e) => mkOpt('vi-rem', d, e), item: (d, e) => mkItem('vi-rem', d, e) }, onChange: v => console.log('FPago:', v) });
    create('vi-rem-incoterm', { options: incoterms, placeholder: 'Seleccione el incoterm...', render: { option: (d, e) => mkOpt('vi-rem', d, e), item: (d, e) => mkItem('vi-rem', d, e) }, onChange: v => console.log('Incoterm:', v) });
    create('vi-rem-sucursal', { options: sucursales, placeholder: 'Seleccione la sucursal...', render: { option: (d, e) => mkOpt('vi-rem', d, e), item: (d, e) => mkItem('vi-rem', d, e) }, onChange: id => filtrarAlmacenes(id, 'vi-rem-almacen') });
    create('vi-rem-almacen', { options: [], placeholder: 'Seleccione el almacén...', render: { option: (d, e) => mkOpt('vi-rem', d, e), item: (d, e) => mkItem('vi-rem', d, e) }, onChange: v => console.log('Almacén:', v) });

    // ── Factura ──
    create('vi-fac-vendedor', { options: vendedores, placeholder: 'Seleccione un vendedor...', render: { option: (d, e) => mkOpt('vi-fac', d, e), item: (d, e) => mkItem('vi-fac', d, e) }, onChange: v => console.log('Vendedor:', v) });
    create('vi-fac-moneda', { options: monedas, placeholder: 'Seleccione una moneda...', render: { option: (d, e) => mkOpt('vi-fac', d, e), item: (d, e) => mkItem('vi-fac', d, e) }, onChange: v => tasaForMoneda(v, 'vi-fac-paridad') });
    create('vi-fac-uso-cfdi', { options: cfdi, placeholder: 'Seleccione el cfdi...', render: { option: (d, e) => mkOpt('vi-fac', d, e), item: (d, e) => mkItem('vi-fac', d, e) }, onChange: v => console.log('CFDI:', v) });
    create('vi-fac-forma-pago', { options: fpago, placeholder: 'Seleccione la forma de pago...', render: { option: (d, e) => mkOpt('vi-fac', d, e), item: (d, e) => mkItem('vi-fac', d, e) }, onChange: v => console.log('FPago:', v) });
    create('vi-fac-incoterm', { options: incoterms, placeholder: 'Seleccione el incoterm...', render: { option: (d, e) => mkOpt('vi-fac', d, e), item: (d, e) => mkItem('vi-fac', d, e) }, onChange: v => console.log('Incoterm:', v) });
    create('vi-fac-sucursal', { options: sucursales, placeholder: 'Seleccione la sucursal...', render: { option: (d, e) => mkOpt('vi-fac', d, e), item: (d, e) => mkItem('vi-fac', d, e) }, onChange: id => filtrarAlmacenes(id, 'vi-fac-almacen') });
    create('vi-fac-almacen', { options: [], placeholder: 'Seleccione el almacén...', render: { option: (d, e) => mkOpt('vi-fac', d, e), item: (d, e) => mkItem('vi-fac', d, e) }, onChange: v => console.log('Almacén:', v) });

    // ── Factura Anticipo ──
    create('vi-fac-ant-vendedor', { options: vendedores, placeholder: 'Seleccione un vendedor...', render: { option: (d, e) => mkOpt('vi-fac', d, e), item: (d, e) => mkItem('vi-fac', d, e) }, onChange: v => console.log('Vendedor:', v) });
    create('vi-fac-ant-moneda', { options: monedas, placeholder: 'Seleccione una moneda...', render: { option: (d, e) => mkOpt('vi-fac', d, e), item: (d, e) => mkItem('vi-fac', d, e) }, onChange: v => tasaForMoneda(v, 'vi-fac-paridad') });
    create('vi-fac-ant-uso-cfdi', { options: cfdi, placeholder: 'Seleccione el cfdi...', render: { option: (d, e) => mkOpt('vi-fac', d, e), item: (d, e) => mkItem('vi-fac', d, e) }, onChange: v => console.log('CFDI:', v) });
    create('vi-fac-ant-forma-pago', { options: fpago, placeholder: 'Seleccione la forma de pago...', render: { option: (d, e) => mkOpt('vi-fac', d, e), item: (d, e) => mkItem('vi-fac', d, e) }, onChange: v => console.log('FPago:', v) });
    create('vi-fac-ant-incoterm', { options: incoterms, placeholder: 'Seleccione el incoterm...', render: { option: (d, e) => mkOpt('vi-fac', d, e), item: (d, e) => mkItem('vi-fac', d, e) }, onChange: v => console.log('Incoterm:', v) });
    create('vi-fac-ant-sucursal', { options: sucursales, placeholder: 'Seleccione la sucursal...', render: { option: (d, e) => mkOpt('vi-fac', d, e), item: (d, e) => mkItem('vi-fac', d, e) }, onChange: id => filtrarAlmacenes(id, 'vi-fac-almacen') });
    create('vi-fac-ant-almacen', { options: [], placeholder: 'Seleccione el almacén...', render: { option: (d, e) => mkOpt('vi-fac', d, e), item: (d, e) => mkItem('vi-fac', d, e) }, onChange: v => console.log('Almacén:', v) });

    // Valores por defecto
    tomManager.getInstance('vi-cot-moneda').setValue('PESOS', false);
    tomManager.getInstance('vi-fac-ant-moneda').setValue('PESOS', false);
    tomManager.getInstance('vi-ped-sucursal').setValue(sucursalUsuario, false);
    tomManager.getInstance('vi-ped-almacen').setValue('1', false);
    tomManager.getInstance('vi-rem-sucursal').setValue(sucursalUsuario, false);
    tomManager.getInstance('vi-rem-almacen').setValue('1', false);
    tomManager.getInstance('vi-fac-sucursal').setValue(sucursalUsuario, false);
    tomManager.getInstance('vi-fac-almacen').setValue('1', false);
});

// ============================================
// 7. ARRANQUE
// ============================================
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => App.init());
} else {
    App.init();
}
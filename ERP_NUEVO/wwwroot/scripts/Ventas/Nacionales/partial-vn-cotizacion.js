let tomManager;
tomManager = new TomSelectManager();

/**
 * SISTEMA DE COTIZACIÓN OPTIMIZADO
 * JavaScript Vanilla con patrones modernos
 */

// ============================================
// 0. UTILIDADES - TOAST NOTIFICATION
// ============================================

// ============================================
// 1. CLASE BASE PARA GESTIÓN DE DATOS (DRY)
// ============================================
class DataManager {
    constructor(config) {
        this.endpoint = config.endpoint;
        this.detailEndpoint = config.detailEndpoint;
        this.modalId = config.modalId;
        this.cache = new Map();
        this.cacheTimeout = 5 * 60 * 1000; // 5 minutos
        this.abortController = null;
        this.shouldCloseOnSelect = config.shouldCloseOnSelect !== false; // Por defecto true

        // Referencias DOM (caché)
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
        // Event listeners con delegación
        this.dom.modal?.addEventListener('show.bs.modal', () => this.onModalShow());
        this.dom.input?.addEventListener('input', (e) => this.handleSearch(e.target.value));
        this.dom.pageSize?.addEventListener('change', (e) => this.handlePageSizeChange(e.target.value));
        this.dom.btnClear?.addEventListener('click', () => this.handleClear());

        // Delegación de eventos para resultados (mejor performance)
        this.dom.results?.addEventListener('click', (e) => {
            const btn = e.target.closest('[data-select-id]');
            if (btn) {
                const id = btn.dataset.selectId;
                this.handleSelect(id);
            }
        });

        // Delegación para paginación
        this.dom.pagination?.addEventListener('click', (e) => {
            e.preventDefault();
            const btn = e.target.closest('[data-page]');
            if (btn && !btn.closest('.disabled')) {
                this.state.page = parseInt(btn.dataset.page);
                this.fetchData();
            }
        });
    }

    onModalShow() {
        this.handleClear();
        this.fetchData();
    }

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

    // Caché con timestamp
    getCachedData(key) {
        const cached = this.cache.get(key);
        if (cached && Date.now() - cached.timestamp < this.cacheTimeout) {
            return cached.data;
        }
        return null;
    }

    setCachedData(key, data) {
        this.cache.set(key, { data, timestamp: Date.now() });
    }

    clearCache() {
        this.cache.clear();
    }

    async fetchData() {
        if (this.state.loading) {
            this.abortController?.abort();
        }

        const cacheKey = `${this.state.lastSearch}-${this.state.page}-${this.state.pageSize}`;
        const cached = this.getCachedData(cacheKey);

        if (cached) {
            this.renderResults(cached);
            return;
        }

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
            if (error.name !== 'AbortError') {
                console.error('Error fetching data:', error);
                this.renderError();
            }
        } finally {
            this.state.loading = false;
            this.showSpinner(false);
        }
    }

    showSpinner(show) {
        this.dom.spinner?.classList.toggle('d-none', !show);
    }

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

        // Usar DocumentFragment para mejor performance
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
        if (totalPages <= 1) {
            this.dom.pagination.innerHTML = '';
            return;
        }

        const maxPages = 5;
        let startPage = Math.max(this.state.page - Math.floor(maxPages / 2), 1);
        let endPage = Math.min(startPage + maxPages - 1, totalPages);
        startPage = Math.max(endPage - maxPages + 1, 1);

        const fragment = document.createDocumentFragment();

        // Botón Anterior
        const prevLi = this.createPageButton('Anterior', this.state.page - 1, this.state.page === 1);
        fragment.appendChild(prevLi);

        // Páginas numeradas
        for (let i = startPage; i <= endPage; i++) {
            const li = this.createPageButton(i, i, false, i === this.state.page);
            fragment.appendChild(li);
        }

        // Botón Siguiente
        const nextLi = this.createPageButton('Siguiente', this.state.page + 1, this.state.page === totalPages);
        fragment.appendChild(nextLi);

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
                this.onSelect(data[0]);
                // No cerrar el modal para productos (permite agregar múltiples)
                if (this.shouldCloseOnSelect) {
                    bootstrap.Modal.getInstance(this.dom.modal)?.hide();
                }
            }
        } catch (error) {
            console.error('Error selecting item:', error);
            alert('Error al seleccionar el elemento');
        }
    }

    // Método a sobreescribir en instancias
    onSelect(item) {
        console.log('Item selected:', item);
    }

    // Utilidad para escapar HTML (prevenir XSS)
    escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }
}

// ============================================
// 2. GESTOR DE PRODUCTOS
// ============================================
class ProductManager {
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
            table: document.getElementById('vn-cot-productosTable'),
            subtotal1Display: document.getElementById('vn-cot-total-subtotal1-display'),
            descuentoDisplay: document.getElementById('vn-cot-total-descuento-display'),
            fleteDisplay: document.getElementById('vn-cot-total-flete-display'),
            subtotal2Display: document.getElementById('vn-cot-total-subtotal2-display'),
            ivaDisplay: document.getElementById('vn-cot-iva-display'),
            importeDisplay: document.getElementById('vn-cot-importe-display'),
            fleteInput: document.getElementById('vn-cot-flete-val')
        };

        this.init();
    }

    init() {
        // Delegación de eventos en la tabla
        this.dom.table?.addEventListener('input', (e) => {
            const input = e.target;
            if (input.classList.contains('vn-cot-cantidad') ||
                input.classList.contains('vn-cot-precio') ||
                input.classList.contains('vn-cot-descuento') ||
                input.classList.contains('vn-cot-comentario')) {
                const tr = input.closest('tr');
                const productoId = tr?.dataset.productoId;
                if (productoId) this.actualizarFila(productoId);
            }
        });

        this.dom.table?.addEventListener('click', (e) => {
            const btn = e.target.closest('.vn-cot-btn-eliminar');
            if (btn) {
                const tr = btn.closest('tr');
                const productoId = tr?.dataset.productoId;
                if (productoId) this.eliminarProducto(productoId);
            }
        });

        if (this.dom.fleteInput) {
            this.dom.fleteInput.addEventListener('input', () => {
                this.calcularTotales();
            });

            this.dom.fleteInput.addEventListener('change', () => {
                this.calcularTotales();
            });
        }
    }

    agregarProducto(producto) {
        // Siempre agregar como nuevo producto (sin verificar si existe)
        const productoData = Object.assign({
            id: `prod_${Date.now()}`,
            productoId: producto.id,
            descripcion: producto.descripcion,
            cantidad: 1,
            unidad: producto.udm || 'PZA',
            existenciaGeneral: producto.existenciaGeneral || 0,
            existenciaModular: producto.existenciaModular || 0,
            comentario: ''
        },
        // precio, precioMinimo, descuento sugerido y tope salen de la regla vigente
        // (Scripts/Ventas/reglas-precio-ventas.js), igual que en ventas industriales.
        ReglasPrecio.camposDeRegla(producto));

        this.productos.push(productoData);
        this.renderTable();
        this.calcularTotales();

        // Mostrar notificación de producto agregado
        toastMixin.fire({
            icon: 'success',
            title: `Producto agregado: ${this.escapeHtml(producto.descripcion)}`
        });
    }

    renderTable() {
        const tbody = this.dom.table;
        const thead = this.dom.thead;
        if (!tbody) return;

        // Limpiar mensaje vacío si existe
        const emptyRow = tbody.querySelector('.vn-cot-empty-state');
        if (emptyRow) emptyRow.remove();

        // Limpiar tabla
        tbody.innerHTML = '';

        // Opcional: ocultar columna de descuento en <thead>
        if (thead) {
            const descuentoTh = thead.querySelector('.vn-cot-th-descuento');
            if (descuentoTh) descuentoTh.style.display = (rol === 'Gerente') ? '' : 'none';
        }

        if (this.productos.length === 0) {
            tbody.innerHTML = `
<tr class="vn-cot-empty-state">
    <td colspan="12" class="text-center py-5"> <!-- Cambiado de 11 a 12 -->
        <i class="fas fa-box-open fa-3x mb-3 opacity-25"></i>
        <div>No hay productos agregados</div>
        <small class="text-muted">Haz clic en "Agregar" para comenzar</small>
    </td>
</tr>`;
            return;
        }

        const fragment = document.createDocumentFragment();

        this.productos.forEach((p, index) => { // ← Agregamos el index aquí
            const importe = this.calcularImporte(p);
            const tr = document.createElement('tr');
            tr.dataset.productoId = p.id;

            // Solo incluir la celda de descuento si el rol es Gerente
            // max sale del tope de la regla: en PRECIO_FIJO es 0 y el campo queda fijo.
            const descuentoCell = (rol === 'Gerente')
                ? `<td>
     <input type="number" class="vn-cot-product-input vn-cot-descuento" value="${p.descuento}" min="0" max="${(p.descuentoMaximo != null ? p.descuentoMaximo : 100).toFixed(2)}" step="0.01" readonly>
   </td>`
                : '';

            tr.innerHTML = `
        <td class="text-center fw-bold">${index + 1}</td> <!-- Nueva celda con índice -->
        <td>${this.escapeHtml(p.productoId)}</td>
        <td style="max-width: 200px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis;">
            ${this.escapeHtml(p.descripcion)}
        </td>
        <td>
            <input type="number" class="vn-cot-product-input vn-cot-cantidad" value="${p.cantidad}" min="0" step="0.01">
        </td>
        <td>${this.escapeHtml(p.unidad)}</td>
        <td>
            <input type="number" class="vn-cot-product-input vn-cot-precio" value="${p.precio.toFixed(2)}" min="${(p.precioMinimo || 0).toFixed(2)}" step="0.01">
        </td>
        ${descuentoCell}
        <td class="vn-cot-importe fw-bold">${importe.toFixed(2)}</td>                 
        <td>
        <input type="text" class="vn-cot-product-input vn-cot-comentario" value="${this.escapeHtml(p.comentario)}" placeholder="Comentario...">
        </td>
        <td>
            <button type="button" class="vn-cot-btn-delete vn-cot-btn-eliminar">
                <i class="fas fa-trash"></i>
            </button>
        </td>`;

            fragment.appendChild(tr);
        });

        tbody.appendChild(fragment);
    }

    actualizarFila(productoId) {
        const producto = this.productos.find(p => p.id === productoId);
        if (!producto) return;

        const tr = this.dom.table.querySelector(`tr[data-producto-id="${productoId}"]`);
        if (!tr) return;

        producto.cantidad = parseFloat(tr.querySelector('.vn-cot-cantidad').value) || 0;
        producto.comentario = tr.querySelector('.vn-cot-comentario').value || '';

        // ── Precio y descuento contra la regla vigente ────────────────────────
        // El input de precio es libre en nacionales, así que aquí es donde se respeta
        // el piso de la regla. El servidor revalida lo mismo al guardar.
        const precioInput = tr.querySelector('.vn-cot-precio');
        if (precioInput) {
            const res = ReglasPrecio.validarPrecio(
                producto,
                parseFloat(precioInput.value) || 0,
                !!vnCotAuth.precioToken);

            producto.precio = res.valor;
            if (res.aviso) precioInput.value = res.valor.toFixed(2);
            ReglasPrecio.avisar(res.aviso);
        }

        const descuentoInput = tr.querySelector('.vn-cot-descuento');
        if (descuentoInput) {
            const res = ReglasPrecio.validarDescuento(
                producto,
                parseFloat(descuentoInput.value) || 0,
                !!vnCotAuth.descuentoToken);

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

        const importe = this.calcularImporte(producto);
        tr.querySelector('.vn-cot-importe').textContent = importe.toFixed(2);

        this.calcularTotales();
    }

    eliminarProducto(productoId) {
        this.productos = this.productos.filter(p => p.id !== productoId);
        this.renderTable();
        this.calcularTotales();
    }

    calcularImporte(producto) {
        return producto.cantidad * producto.precio * (1 - producto.descuento / 100);
    }

    calcularTotales() {
        let subtotal1 = 0;
        const totales = this.productos.reduce((acc, p) => {
            const importeSinDesc = p.cantidad * p.precio;
            const descuentoMonto = importeSinDesc * (p.descuento / 100);

            acc.subtotal1 += importeSinDesc;
            acc.totalDescuento += descuentoMonto;

            return acc;
        }, { subtotal1: 0, totalDescuento: 0 });

        const flete = parseFloat(document.getElementById('vn-cot-flete-val').value) || 0;
        const subtotal2 = totales.subtotal1 - totales.totalDescuento + flete;
        const iva = subtotal2 * 0.16;
        const total = subtotal2 + iva;

        // Guardar totales en una propiedad para acceso externo
        this.totales = {
            subtotal1: totales.subtotal1,
            descuento: totales.totalDescuento,
            flete: flete,
            subtotal2: subtotal2,
            iva: iva,
            total: total
        };

        // Actualizar displays con formato
        this.updateDisplay(this.dom.subtotal1Display, totales.subtotal1);
        this.updateDisplay(this.dom.descuentoDisplay, totales.totalDescuento, '-$');
        this.updateDisplay(this.dom.fleteDisplay, flete);
        this.updateDisplay(this.dom.subtotal2Display, subtotal2);
        this.updateDisplay(this.dom.ivaDisplay, iva);
        this.updateDisplay(this.dom.importeDisplay, total, '$');

        // Actualizar campos hidden si existen (para mantener compatibilidad)
        this.updateHiddenField('vn-cot-total-subtotal1', totales.subtotal1);
        this.updateHiddenField('vn-cot-total-descuento', totales.totalDescuento);
        this.updateHiddenField('vn-cot-total-flete', flete);
        this.updateHiddenField('vn-cot-total-subtotal2', subtotal2);
        this.updateHiddenField('vn-cot-iva', iva);
        this.updateHiddenField('vn-cot-importe', total);

        window.CreditoVentas?.evaluar('vn-cot');
    }

    updateHiddenField(id, value) {
        const field = document.getElementById(id);
        if (field) {
            field.value = value.toFixed(2);
        }
    }

    updateDisplay(element, value, prefix = '$') {
        if (element) {
            element.textContent = `${prefix}${value.toFixed(2)}`;
        }
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
// 3. GESTOR DE FORMULARIO
// ============================================
class FormManager {
    constructor(productManager) {
        this.productManager = productManager;
        this.form = document.getElementById('vn-cot-formCotizacion');
        this.btnSubmit = document.querySelector('.vn-cot-btn-submit');
        this.init();
    }

    init() {
        this.btnSubmit?.addEventListener('click', (e) => {
            e.preventDefault();
            this.enviarCotizacion();
        }, { once: false });
    }

    recopilarDatosFormulario() {
        // Recopilar todos los datos del formulario
        const formData = new FormData();

        const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
        if (token) {
            formData.append('__RequestVerificationToken', token);
        }

        // Información General
        formData.append('sucursal', document.getElementById('vn-cot-sucursal')?.value || '');
        formData.append('almacen', document.getElementById('vn-cot-almacen')?.value || '');
        formData.append('tipoMovimiento', document.getElementById('vn-cot-tipo-docto-mov')?.value || '');
        formData.append('folio', document.getElementById('vn-cot-folio')?.value || '');

        // Información del Cliente
        formData.append('cliente', document.getElementById('vn-cot-cliente')?.value || '');
        formData.append('rfc', document.getElementById('vn-cot-rfc')?.value || '');
        formData.append('vendedor', document.getElementById('vn-cot-vendedor')?.value || '');
        formData.append('contacto', document.getElementById('vn-cot-contacto')?.value || '');
        formData.append('moneda', document.getElementById('vn-cot-moneda')?.value || '');
        formData.append('paridad', document.getElementById('vn-cot-paridad')?.value || '');
        formData.append('concepto', document.getElementById('vn-cot-concepto')?.value || '');
        formData.append('incoterm', document.getElementById('vn-cot-incoterm')?.value || '');

        formData.append('ordenCompra', document.getElementById('vn-cot-orden-compra-val')?.value || '');

        // Condiciones de Pago
        formData.append('limiteCredito', document.getElementById('vn-cot-limite-credito')?.value || '');
        formData.append('plazo', document.getElementById('vn-cot-plazo')?.value || '');
        formData.append('fechaPago', document.getElementById('vn-cot-fecha-pago')?.value || '');
        formData.append('formaPago', document.getElementById('vn-cot-forma-pago')?.value || '');
        formData.append('usoCFDI', document.getElementById('vn-cot-uso-cfdi')?.value || '');
        formData.append('comentarios', document.getElementById('vn-cot-comentarios')?.value || '');

        // Contado o crédito: es lo que después preselecciona el toggle del pedido.
        formData.append('tipoPago', document.getElementById('vn-cot-tipo-pago')?.value || 'contado');

        // Productos - Enviar como JSON string o como campos individuales
        const productos = this.productManager.getProductosData();
        formData.append('productosJSON', JSON.stringify(productos));

        // Tokens de autorización: el servidor los necesita para saber si este usuario
        // puede cotizar por debajo del piso de la regla.
        formData.append('descuentoToken', vnCotAuth.descuentoToken || '');
        formData.append('precioToken', vnCotAuth.precioToken || '');

        // Totales - Obtener directamente del ProductManager
        const totales = this.productManager.totales;
        formData.append('subtotal1', totales.subtotal1.toFixed(2));
        formData.append('descuento', totales.descuento.toFixed(2));
        formData.append('flete', totales.flete.toFixed(2));
        formData.append('subtotal2', totales.subtotal2.toFixed(2));
        formData.append('iva', totales.iva.toFixed(2));
        formData.append('total', totales.total.toFixed(2));

        return formData;
    }

    validarFormulario() {
        const errores = [];

        // Validar cliente
        const cliente = document.getElementById('vn-cot-cliente')?.value;
        if (!cliente || cliente.trim() === '') {
            errores.push('Debe seleccionar un cliente');
        }

        // Validar productos
        const productos = this.productManager.getProductosData();
        if (productos.length === 0) {
            errores.push('Debe agregar al menos un producto');
        }

        // Validar vendedor
        const vendedor = document.getElementById('vn-cot-vendedor')?.value;
        if (!vendedor || vendedor === 'Seleccionar vendedor') {
            errores.push('Debe seleccionar un vendedor');
        }

        return errores;
    }

    obtenerConfiguracionPorTipo(tipoMovimiento) {
        const configuraciones = {
            'alta': {
                endpoint: '/VNCotizacion/Guardar',
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
        const tipoMovimiento = document.getElementById('vn-cot-tipo-docto-mov')?.value || 'alta';
        const config = this.obtenerConfiguracionPorTipo(tipoMovimiento);

        // Validar formulario (solo si no es consulta)
        const errores = this.validarFormulario(config.validarProductos);
        if (errores.length > 0) {
            toastMixin.fire({ icon: 'error', title: errores.join(', ') });
            return;
        }

        // Bloquear botón
        const originalText = this.btnSubmit.innerHTML;
        this.btnSubmit.disabled = true;
        this.btnSubmit.innerHTML = `<i class="fas fa-spinner fa-spin"></i> ${config.textoCarga}`;

        try {
            const formData = this.recopilarDatosFormulario();
            const fetchConfig = {
                method: config.metodo.toUpperCase(),
                body: formData
            };

            const response = await fetch(config.endpoint, fetchConfig);
            const result = await response.json();

            // Revisar el success del servidor
            if (result.success) {
                await Swal.fire({
                    icon: 'success',
                    title: `${result.message || ''} Folio: ${result.folio_generado || ''}`.trim() || config.mensajeExit,
                    confirmButtonText: 'Aceptar'
                });

                // 🔄 Recargar después de cerrar el Swal
                window.location.reload();
            }
            else {
                toastMixin.fire({
                    icon: 'error',
                    title: result.message || 'Ocurrió un error en el servidor'
                });
            }

        } catch (error) {
            console.error('Error enviando cotización:', error);
            toastMixin.fire({
                icon: 'error',
                title: error.message || 'Ocurrió un error al enviar la cotización'
            });
        } finally {
            this.btnSubmit.disabled = false;
            this.btnSubmit.innerHTML = originalText;
        }
    }

    manejarRespuesta(resultado, mensajeExito, tipoMovimiento) {
        if (resultado.success || resultado.exito) {
            toastMixin.fire({
                icon: 'success',
                title: resultado.mensaje || mensajeExito
            });

            // Acciones específicas según el tipo
            switch (tipoMovimiento) {
                case 'alta':
                    // Limpiar formulario después de crear
                    // this.limpiarFormulario();
                    break;
                case 'modificacion':
                    // Tal vez actualizar algunos campos
                    break;
                case 'consulta':
                    // Mostrar datos consultados
                    console.log('Datos consultados:', resultado);
                    break;
            }
        } else {
            throw new Error(resultado.mensaje || 'Error al procesar la solicitud');
        }
    }

    limpiarFormulario() {
        // Limpiar todos los campos
        this.form?.reset();
        this.productManager.productos = [];
        this.productManager.renderTable();
        this.productManager.calcularTotales();

        document.getElementById('vn-cot-cliente').value = '';
        document.getElementById('vn-cot-rfc').value = '';
        document.getElementById('vn-cot-info-proveedor').value = '';

    }

    // Método para debug - ver qué se enviará
    verDatosFormulario() {
        const formData = this.recopilarDatosFormulario();
        const obj = {};
        for (let [key, value] of formData.entries()) {
            obj[key] = value;
        }
        console.log('Datos a enviar:', obj);
        return obj;
    }
}

// ============================================
// 4. INICIALIZACIÓN
// ============================================
// Tokens de autorización del usuario. Nacionales no tiene el candado por contraseña que
// sí tienen las cotizaciones industriales: el precio siempre fue editable. Con esto, quien
// tenga permiso directo puede bajar del piso de la regla y el resto no.
const vnCotAuth = { precioToken: null, descuentoToken: null };

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
     * Devuelve el id del cliente del documento, resolviéndolo desde la clave si hace falta.
     */
    async asegurarClienteId() {
        const cve = document.getElementById('vn-cot-cliente')?.value || '';

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
     * Sin esto, cambiar de cliente dejaba el documento con los precios del anterior.
     */
    async recotizarPartidas(nombreCliente) {
        const pm = this.productManagerInstance;
        if (!pm || !pm.productos || pm.productos.length === 0) return;

        const actualizadas = await ReglasPrecio.recotizar(
            pm.productos, await this.asegurarClienteId(),
            '/DatosGenerales/BuscarProductoCotizacion');

        if (actualizadas === 0) return;

        pm.renderTable();
        pm.calcularTotales();
        ReglasPrecio.avisar(
            `Precios recalculados con la lista de ${nombreCliente || 'el cliente'} `
            + `(${actualizadas} partida${actualizadas === 1 ? '' : 's'})`,
            'info');
    },

    init() {
        ReglasPrecio.cargarPermisos().then(t => Object.assign(vnCotAuth, t));

        // Cachear referencia al formulario y al botón SUBMIT lo antes posible
        this.form = document.getElementById('form-2');
        this.btnSubmit = document.querySelector('.vn-cot-btn-submit');

        // Crédito: en la cotización solo se informa — todavía no se compromete crédito.
        window.CreditoVentas?.registrar({
            prefix: 'vn-cot',
            modo: 'advertir',
            documento: 'cotizacion',
            claseBanner: 'vn-credit-banner',
            endpointSolicitud: '/VNPedido/EnviarSolicitudGerente',
            getTipoPago: () => document.getElementById('vn-cot-tipo-pago')?.value || 'contado',
            totalId: 'vn-cot-importe',
            submitSelector: '.vn-cot-btn-submit',
            clienteInputId: 'vn-cot-cliente',
            folioId: 'vn-cot-folio'
        });

        // Inicializar gestor de clientes
        this.clienteManager = new DataManager({
            endpoint: '/DatosGenerales/BuscarC',
            detailEndpoint: '/DatosGenerales/BuscarCliente',
            modalId: 'vn-cot-modalBuscarCliente',
            inputId: 'vn-cot-inputBuscarCliente',
            resultsId: 'vn-cot-listaResultadosClientes',
            pageSizeId: 'vn-cot-pageSizeClientes',
            btnClearId: 'vn-cot-btnLimpiarClientes',
            spinnerId: 'vn-cot-spinnerClientes',
            paginationId: 'vn-cot-paginationClientes',
            recordsFromId: 'vn-cot-recordsFromClientes',
            recordsToId: 'vn-cot-recordsToClientes',
            totalRecordsId: 'vn-cot-totalRecordsClientes',
            shouldCloseOnSelect: true
        });

        this.clienteManager.onSelect = (cliente) => {
            document.getElementById('vn-cot-cliente').value = cliente.id || '';
            document.getElementById('vn-cot-rfc').value = cliente.rfc || '';
            document.getElementById('vn-cot-info-proveedor').value =
                [cliente.dir, cliente.col, cliente.pob, cliente.cp]
                    .filter(x => x)
                    .join(',\n');

            const vendedorInstance = tomManager.getInstance('vn-cot-vendedor');
            if (vendedorInstance && cliente.cve_vdr) {
                vendedorInstance.setValue(cliente.cve_vdr, true);
            }
            window.CreditoVentas?.setCliente('vn-cot', cliente);

            // El cliente define la lista de precios y las reglas por cliente: se manda
            // explícito al buscar productos y se recalcula lo ya capturado.
            this.clienteIdActual = parseInt(cliente.id_cliente, 10) || 0;
            this.clienteCveResuelta = cliente.id || '';
            this.recotizarPartidas(cliente.descripcion);
        };

        // Inicializar gestor de productos (DataManager)
        this.productoManager = new DataManager({
            endpoint: '/DatosGenerales/BuscarPCotizacion',
            detailEndpoint: '/DatosGenerales/BuscarProductoCotizacion',
            modalId: 'vn-cot-modalBuscarProducto',
            inputId: 'vn-cot-inputBuscarProducto',
            resultsId: 'vn-cot-listaResultadosProductos',
            pageSizeId: 'vn-cot-pageSizeProductos',
            btnClearId: 'vn-cot-btnLimpiarProductos',
            spinnerId: 'vn-cot-spinnerProductos',
            paginationId: 'vn-cot-paginationProductos',
            recordsFromId: 'vn-cot-recordsFromProductos',
            recordsToId: 'vn-cot-recordsToProductos',
            totalRecordsId: 'vn-cot-totalRecordsProductos',
            shouldCloseOnSelect: false
        });

        // Instancia de ProductManager (tabla)
        this.productManagerInstance = new ProductManager();

        this.productoManager.getExtraParams = async () => ({ clienteId: await this.asegurarClienteId() });
        this.productoManager.onSelect = (producto) => {
            this.productManagerInstance.agregarProducto(producto);
        };

        // Inicializar gestor de formulario (ahora que productManager existe)
        this.formManager = new FormManager(this.productManagerInstance);

        // Conectar botón submit al método del FormManager (si existe)
        if (this.btnSubmit) {
            this._handleClick = (e) => {
                e.preventDefault();
                //this.enviarCotizacion();
            };
            this.btnSubmit?.addEventListener('click', this._handleClick);
        }

        // Observar cambios en el tipo de movimiento (ya tenemos form disponible)
        const tipoMovSelect = document.getElementById('vn-cot-tipo-docto-mov');
        tipoMovSelect?.addEventListener('change', (e) => {
            this.manejarCambioTipoMovimiento(e.target.value);
        });

        // Verificar estado inicial — ahora con form inicializado
        this.manejarCambioTipoMovimiento(tipoMovSelect?.value || 'alta');

        console.log('Sistema de cotización inicializado correctamente');

        // Exponer método para debug (opcional)
        window.verDatosFormulario = () => this.formManager.verDatosFormulario();
    },

    manejarCambioTipoMovimiento(tipoMovimiento) {
        if (tipoMovimiento === 'consulta') {
            this.deshabilitarFormulario();
        } else {
            this.habilitarFormulario();
        }
    },

    deshabilitarFormulario() {
        // fallback seguro al form real
        const form = this.form || this.formManager?.form || document.getElementById('vn-cot-formCotizacion');
        if (!form) return; // si no existe, salir sin crash

        const formElements = form.querySelectorAll('input, select, textarea, button');
        formElements.forEach(element => {
            if (element.id !== 'vn-cot-tipo-docto-mov') {
                element.disabled = true;
                element.classList.add('vn-cot-disabled-field');
            }
        });

        const btnBuscarCliente = document.querySelector('[data-bs-target="#vn-cot-modalBuscarCliente"]');
        const btnBuscarProducto = document.querySelector('[data-bs-target="#vn-cot-modalBuscarProducto"]');
        if (btnBuscarCliente) btnBuscarCliente.disabled = true;
        if (btnBuscarProducto) btnBuscarProducto.disabled = true;

        const botonesEliminar = document.querySelectorAll('.vn-cot-btn-eliminar');
        botonesEliminar.forEach(btn => btn.disabled = true);

        if (window.tomManager) {
            ['vn-cot-vendedor', 'vn-cot-moneda', 'vn-cot-uso-cfdi', 'vn-cot-forma-pago'].forEach(id => {
                const instance = window.tomManager.instances.get(id);
                if (instance) instance.disable();
            });
        }

        const btn = this.btnSubmit || this.formManager?.btnSubmit || document.querySelector('.vn-cot-btn-submit');
        if (btn) {
            btn.innerHTML = '<i class="fas fa-search"></i> Consultar';
        }
    },

    habilitarFormulario() {
        const form = this.form || this.formManager?.form || document.getElementById('vn-cot-formCotizacion');
        if (!form) return;

        const formElements = form.querySelectorAll('input, select, textarea, button');
        formElements.forEach(element => {
            element.disabled = false;
            element.classList.remove('vn-cot-disabled-field');
        });

        const btnBuscarCliente = document.querySelector('[data-bs-target="#vn-cot-modalBuscarCliente"]');
        const btnBuscarProducto = document.querySelector('[data-bs-target="#vn-cot-modalBuscarProducto"]');
        if (btnBuscarCliente) btnBuscarCliente.disabled = false;
        if (btnBuscarProducto) btnBuscarProducto.disabled = false;

        if (window.tomManager) {
            ['vn-cot-vendedor', 'vn-cot-moneda', 'vn-cot-uso-cfdi', 'vn-cot-forma-pago'].forEach(id => {
                const instance = window.tomManager.instances.get(id);
                if (instance) instance.enable();
            });
        }

        const tipoMovimiento = document.getElementById('vn-cot-tipo-docto-mov')?.value;
        const btn = this.btnSubmit || this.formManager?.btnSubmit || document.querySelector('.vn-cot-btn-submit');
        if (btn) {
            if (tipoMovimiento === 'modificacion') {
                btn.innerHTML = '<i class="fas fa-save"></i> Actualizar Cotización';
            } else {
                btn.innerHTML = '<i class="fas fa-save"></i> Guardar Cotización';
            }
        }
    },
};

async function handleDescuentoAuth(e) {
    if (!e.target.classList.contains('vn-cot-descuento')) return;
    await pedirAutenticacion(e.target);
}

async function handleDescuentoF8(e) {
    if (e.key !== 'F8') return;

    // Solo si el foco está en un input de descuento
    const input = document.activeElement;
    if (!input.classList.contains('vn-cot-descuento')) return;

    e.preventDefault();
    await pedirAutenticacion(input);
}

async function pedirAutenticacion(input) {
    // Si ya está editable, no hacemos nada
    if (!input.hasAttribute('readonly')) return;

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

    if (!formValues) return; // canceló

    try {
        // Crear FormData
        const formData = new FormData();
        formData.append('Username', formValues.username);
        formData.append('Password', formValues.password);

        const response = await fetch('/DatosGenerales/ValidarDescuento', {
            method: 'POST',
            body: formData
        });

        const result = await response.json();

        if (result.success) {
            toastMixin.fire({
                icon: 'success',
                title: 'Autenticación exitosa. Ahora puede aplicar el descuento.'
            });
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

GetData({
    path: '/DatosGenerales/DatosSelect',
}).then((_res) => {
    const vendedores = _res.vendedores;
    const monedas = _res.monedas;
    const cfdi = _res.usocfdi;
    const fpago = _res.formaspago;
    const incoterms = _res.incoterms;
    const tasas = _res.tasas;
    const sucursales = _res.sucursales;
    const almacenes = _res.almacenes;

    // TomSelect para vendedores
    tomManager.create('vn-cot-vendedor', {
        selector: '#vn-cot-vendedor',
        options: vendedores,
        valueField: 'id',
        displayField: 'nombre',
        searchField: ['id', 'nombre'],
        placeholder: 'Seleccione un vendedor...',
        maxItems: 1,
        create: false,
        render: {
            option: (data, escape) =>
                `<div class="vn-cot-custom-option"; style="color: var(--text-dark);">${escape(data.nombre)} (ID: ${escape(data.id)})</div>`,
            item: (data, escape) =>
                `<div class="vn-cot-custom-item"; style="color: var(--text-dark);">${escape(data.nombre)}</div>`
        },
        onChange: (value) => {
            // Aquí puedes guardar el valor seleccionado en tu estado si es necesario
            console.log("Vendedor seleccionado:", value);
        }
    });
    // TomSelect para monedas
    tomManager.create('vn-cot-moneda', {
        selector: '#vn-cot-moneda',
        options: monedas,
        valueField: 'id',
        displayField: 'nombre',
        searchField: ['id', 'nombre'],
        placeholder: 'Seleccione una moneda...',
        maxItems: 1,
        create: false,
        render: {
            option: (data, escape) =>
                `<div class="vn-cot-custom-option"; style="color: var(--text-dark);">${escape(data.nombre)} (ID: ${escape(data.id)})</div>`,
            item: (data, escape) =>
                `<div class="vn-cot-custom-item"; style="color: var(--text-dark);">${escape(data.nombre)}</div>`
        },
        onChange: (value) => {
            console.log("Moneda seleccionada:", value);

            let v;
            if (value === "PESOS") {
                v = "peso";
            } else if (value === "EURO") {
                v = "euro";
            } else if (value === "DLLS") {
                v = "dolar";
            }

            // Aseguramos que exista tasas[0]
            const tasaData = tasas?.[0];
            if (!tasaData || !v) {
                document.getElementById('vn-cot-paridad').value = '';
                return;
            }

            // Obtener la tasa correspondiente
            const tasaSeleccionada = tasaData[v];

            // Actualizar el input si existe la tasa
            if (tasaSeleccionada !== undefined) {
                document.getElementById('vn-cot-paridad').value = tasaSeleccionada;
            } else {
                document.getElementById('vn-cot-paridad').value = '';
            }
        }
    });
    // TomSelect para cfdi
    tomManager.create('vn-cot-uso-cfdi', {
        selector: '#vn-cot-uso-cfdi',
        options: cfdi,
        valueField: 'id',
        displayField: 'nombre',
        searchField: ['id', 'nombre'],
        placeholder: 'Seleccione el cfdi...',
        maxItems: 1,
        create: false,
        render: {
            option: (data, escape) =>
                `<div class="vn-cot-custom-option"; style="color: var(--text-dark);">${escape(data.nombre)} (ID: ${escape(data.id)})</div>`,
            item: (data, escape) =>
                `<div class="vn-cot-custom-item"; style="color: var(--text-dark);">${escape(data.nombre)}</div>`
        },
        onChange: (value) => {
            console.log("CFDI seleccionado:", value);
        }
    });
    // TomSelect para formas de pago
    tomManager.create('vn-cot-forma-pago', {
        selector: '#vn-cot-forma-pago',
        options: fpago,
        valueField: 'id',
        displayField: 'nombre',
        searchField: ['id', 'nombre'],
        placeholder: 'Seleccione la forma de pago...',
        maxItems: 1,
        create: false,
        render: {
            option: (data, escape) =>
                `<div class="vn-cot-custom-option"; style="color: var(--text-dark);">${escape(data.nombre)} (ID: ${escape(data.id)})</div>`,
            item: (data, escape) =>
                `<div class="vn-cot-custom-item"; style="color: var(--text-dark);">${escape(data.nombre)}</div>`
        },
        onChange: (value) => {
            console.log("Forma de pago seleccionada:", value);
        }
    });
    tomManager.create('vn-ped-vendedor', {
        selector: '#vn-ped-vendedor',
        options: vendedores,
        valueField: 'id',
        displayField: 'nombre',
        searchField: ['id', 'nombre'],
        placeholder: 'Seleccione un vendedor...',
        maxItems: 1,
        create: false,
        render: {
            option: (data, escape) =>
                `<div class="vn-ped-custom-option" style="color: var(--text-dark);">${escape(data.nombre)} (ID: ${escape(data.id)})</div>`,
            item: (data, escape) =>
                `<div class="vn-ped-custom-item" style="color: var(--text-dark);">${escape(data.nombre)}</div>`
        },
        onChange: (value) => {
            console.log("Vendedor seleccionado:", value);
        }
    });
    tomManager.create('vn-ped-moneda', {
        selector: '#vn-ped-moneda',
        options: monedas,
        valueField: 'id',
        displayField: 'nombre',
        searchField: ['id', 'nombre'],
        placeholder: 'Seleccione una moneda...',
        maxItems: 1,
        create: false,
        render: {
            option: (data, escape) =>
                `<div class="vn-ped-custom-option" style="color: var(--text-dark);">${escape(data.nombre)} (ID: ${escape(data.id)})</div>`,
            item: (data, escape) =>
                `<div class="vn-ped-custom-item" style="color: var(--text-dark);">${escape(data.nombre)}</div>`
        },
        onChange: (value) => {
            console.log("Moneda seleccionada:", value);

            let v;
            if (value === "PESOS") {
                v = "peso";
            } else if (value === "EURO") {
                v = "euro";
            } else if (value === "DLLS") {
                v = "dolar";
            }

            // Aseguramos que exista tasas[0]
            const tasaData = tasas?.[0];
            if (!tasaData || !v) {
                document.getElementById('vn-ped-paridad').value = '';
                return;
            }

            // Obtener la tasa correspondiente
            const tasaSeleccionada = tasaData[v];

            // Actualizar el input si existe la tasa
            if (tasaSeleccionada !== undefined) {
                document.getElementById('vn-ped-paridad').value = tasaSeleccionada;
            } else {
                document.getElementById('vn-ped-paridad').value = '';
            }
        }
    });
    tomManager.create('vn-ped-uso-cfdi', {
        selector: '#vn-ped-uso-cfdi',
        options: cfdi,
        valueField: 'id',
        displayField: 'nombre',
        searchField: ['id', 'nombre'],
        placeholder: 'Seleccione el cfdi...',
        maxItems: 1,
        create: false,
        render: {
            option: (data, escape) =>
                `<div class="vn-ped-custom-option" style="color: var(--text-dark);">${escape(data.nombre)} (ID: ${escape(data.id)})</div>`,
            item: (data, escape) =>
                `<div class="vn-ped-custom-item" style="color: var(--text-dark);">${escape(data.nombre)}</div>`
        },
        onChange: (value) => {
            console.log("CFDI seleccionado:", value);
        }
    });
    tomManager.create('vn-ped-forma-pago', {
        selector: '#vn-ped-forma-pago',
        options: fpago,
        valueField: 'id',
        displayField: 'nombre',
        searchField: ['id', 'nombre'],
        placeholder: 'Seleccione la forma de pago...',
        maxItems: 1,
        create: false,
        render: {
            option: (data, escape) =>
                `<div class="vn-ped-custom-option" style="color: var(--text-dark);">${escape(data.nombre)} (ID: ${escape(data.id)})</div>`,
            item: (data, escape) =>
                `<div class="vn-ped-custom-item" style="color: var(--text-dark);">${escape(data.nombre)}</div>`
        },
        onChange: (value) => {
            console.log("Forma de pago seleccionada:", value);
        }
    });
    tomManager.create('vn-ped-incoterm', {
        selector: '#vn-ped-incoterm',
        options: incoterms,
        valueField: 'id',
        displayField: 'nombre',
        searchField: ['id', 'nombre'],
        placeholder: 'Seleccione el incoterm...',
        maxItems: 1,
        create: false,
        render: {
            option: (data, escape) =>
                `<div class="vn-ped-custom-option" style="color: var(--text-dark);">${escape(data.nombre)} (ID: ${escape(data.id)})</div>`,
            item: (data, escape) =>
                `<div class="vn-ped-custom-item" style="color: var(--text-dark);">${escape(data.nombre)}</div>`
        },
        onChange: (value) => {
            console.log("Forma de pago seleccionada:", value);
        }
    });

    // Crear TomSelect de sucursales
    const sucursalSelect = tomManager.create('vn-ped-sucursal', {
        selector: '#vn-ped-sucursal',
        options: sucursales,
        valueField: 'id',
        displayField: 'nombre',
        searchField: ['id', 'nombre'],
        placeholder: 'Seleccione la sucursal...',
        maxItems: 1,
        create: false,
        render: {
            option: (data, escape) =>
                `<div class="vn-ped-custom-option" style="color: var(--text-dark);">
                ${escape(data.nombre)} (ID: ${escape(data.id)})
            </div>`,
            item: (data, escape) =>
                `<div class="vn-ped-custom-item" style="color: var(--text-dark);">
                ${escape(data.nombre)}
            </div>`
        },
        onChange: (sucursalId) => {
            console.log("Sucursal seleccionada:", sucursalId);

            // Filtrar almacenes de esa sucursal
            const almacenesFiltrados = almacenes.filter(a => a.sucursal_id == sucursalId);

            // Obtener la instancia del TomSelect de almacén
            const almacenInstance = tomManager.getInstance('vn-ped-almacen');
            if (!almacenInstance) return;

            // Limpiar y recargar opciones
            almacenInstance.clear();         // Quita el valor seleccionado
            almacenInstance.clearOptions();  // Limpia las opciones anteriores
            almacenInstance.addOptions(almacenesFiltrados); // Agrega solo los almacenes de esa sucursal
            almacenInstance.refreshOptions(false); // Refresca el desplegable

            console.log("Almacenes disponibles:", almacenesFiltrados);
        }
    });
    // Crear TomSelect de almacenes
    tomManager.create('vn-ped-almacen', {
        selector: '#vn-ped-almacen',
        options: [], // inicialmente vacío, se llenará según la sucursal
        valueField: 'id',
        displayField: 'nombre',
        searchField: ['id', 'nombre'],
        placeholder: 'Seleccione el almacén...',
        maxItems: 1,
        create: false,
        render: {
            option: (data, escape) =>
                `<div class="vn-ped-custom-option" style="color: var(--text-dark);">
                ${escape(data.nombre)} (ID: ${escape(data.id)})
            </div>`,
            item: (data, escape) =>
                `<div class="vn-ped-custom-item" style="color: var(--text-dark);">
                ${escape(data.nombre)}
            </div>`
        },
        onChange: (value) => {
            console.log("Almacén seleccionado:", value);
        }
    });

    //Tom para remisines
    tomManager.create('vn-rem-vendedor', {
        selector: '#vn-rem-vendedor',
        options: vendedores,
        valueField: 'id',
        displayField: 'nombre',
        searchField: ['id', 'nombre'],
        placeholder: 'Seleccione un vendedor...',
        maxItems: 1,
        create: false,
        render: {
            option: (data, escape) =>
                `<div class="vn-rem-custom-option" style="color: var(--text-dark);">${escape(data.nombre)} (ID: ${escape(data.id)})</div>`,
            item: (data, escape) =>
                `<div class="vn-rem-custom-item" style="color: var(--text-dark);">${escape(data.nombre)}</div>`
        },
        onChange: (value) => {
            console.log("Vendedor seleccionado:", value);
        }
    });
    tomManager.create('vn-rem-moneda', {
        selector: '#vn-rem-moneda',
        options: monedas,
        valueField: 'id',
        displayField: 'nombre',
        searchField: ['id', 'nombre'],
        placeholder: 'Seleccione una moneda...',
        maxItems: 1,
        create: false,
        render: {
            option: (data, escape) =>
                `<div class="vn-rem-custom-option" style="color: var(--text-dark);">${escape(data.nombre)} (ID: ${escape(data.id)})</div>`,
            item: (data, escape) =>
                `<div class="vn-rem-custom-item" style="color: var(--text-dark);">${escape(data.nombre)}</div>`
        },
        onChange: (value) => {
            console.log("Moneda seleccionada:", value);

            let v;
            if (value === "PESOS") {
                v = "peso";
            } else if (value === "EURO") {
                v = "euro";
            } else if (value === "DLLS") {
                v = "dolar";
            }

            // Aseguramos que exista tasas[0]
            const tasaData = tasas?.[0];
            if (!tasaData || !v) {
                document.getElementById('vn-rem-paridad').value = '';
                return;
            }

            // Obtener la tasa correspondiente
            const tasaSeleccionada = tasaData[v];

            // Actualizar el input si existe la tasa
            if (tasaSeleccionada !== undefined) {
                document.getElementById('vn-rem-paridad').value = tasaSeleccionada;
            } else {
                document.getElementById('vn-rem-paridad').value = '';
            }
        }
    });
    tomManager.create('vn-rem-uso-cfdi', {
        selector: '#vn-rem-uso-cfdi',
        options: cfdi,
        valueField: 'id',
        displayField: 'nombre',
        searchField: ['id', 'nombre'],
        placeholder: 'Seleccione el cfdi...',
        maxItems: 1,
        create: false,
        render: {
            option: (data, escape) =>
                `<div class="vn-rem-custom-option" style="color: var(--text-dark);">${escape(data.nombre)} (ID: ${escape(data.id)})</div>`,
            item: (data, escape) =>
                `<div class="vn-rem-custom-item" style="color: var(--text-dark);">${escape(data.nombre)}</div>`
        },
        onChange: (value) => {
            console.log("CFDI seleccionado:", value);
        }
    });
    tomManager.create('vn-rem-forma-pago', {
        selector: '#vn-rem-forma-pago',
        options: fpago,
        valueField: 'id',
        displayField: 'nombre',
        searchField: ['id', 'nombre'],
        placeholder: 'Seleccione la forma de pago...',
        maxItems: 1,
        create: false,
        render: {
            option: (data, escape) =>
                `<div class="vn-rem-custom-option" style="color: var(--text-dark);">${escape(data.nombre)} (ID: ${escape(data.id)})</div>`,
            item: (data, escape) =>
                `<div class="vn-rem-custom-item" style="color: var(--text-dark);">${escape(data.nombre)}</div>`
        },
        onChange: (value) => {
            console.log("Forma de pago seleccionada:", value);
        }
    });
    tomManager.create('vn-rem-incoterm', {
        selector: '#vn-rem-incoterm',
        options: incoterms,
        valueField: 'id',
        displayField: 'nombre',
        searchField: ['id', 'nombre'],
        placeholder: 'Seleccione el incoterm...',
        maxItems: 1,
        create: false,
        render: {
            option: (data, escape) =>
                `<div class="vn-rem-custom-option" style="color: var(--text-dark);">${escape(data.nombre)} (ID: ${escape(data.id)})</div>`,
            item: (data, escape) =>
                `<div class="vn-rem-custom-item" style="color: var(--text-dark);">${escape(data.nombre)}</div>`
        },
        onChange: (value) => {
            console.log("Forma de pago seleccionada:", value);
        }
    });

    // Crear TomSelect de sucursales
    const sucursalSelect2 = tomManager.create('vn-rem-sucursal', {
        selector: '#vn-rem-sucursal',
        options: sucursales,
        valueField: 'id',
        displayField: 'nombre',
        searchField: ['id', 'nombre'],
        placeholder: 'Seleccione la sucursal...',
        maxItems: 1,
        create: false,
        render: {
            option: (data, escape) =>
                `<div class="vn-rem-custom-option" style="color: var(--text-dark);">
            ${escape(data.nombre)} (ID: ${escape(data.id)})
        </div>`,
            item: (data, escape) =>
                `<div class="vn-rem-custom-item" style="color: var(--text-dark);">
            ${escape(data.nombre)}
        </div>`
        },
        onChange: (sucursalId) => {
            console.log("Sucursal seleccionada:", sucursalId);

            // Filtrar almacenes de esa sucursal
            const almacenesFiltrados = almacenes.filter(a => a.sucursal_id == sucursalId);

            // Obtener la instancia del TomSelect de almacén
            const almacenInstance = tomManager.getInstance('vn-rem-almacen');
            if (!almacenInstance) return;

            // Limpiar y recargar opciones
            almacenInstance.clear();         // Quita el valor seleccionado
            almacenInstance.clearOptions();  // Limpia las opciones anteriores
            almacenInstance.addOptions(almacenesFiltrados); // Agrega solo los almacenes de esa sucursal
            almacenInstance.refreshOptions(false); // Refresca el desplegable

            console.log("Almacenes disponibles:", almacenesFiltrados);
        }
    });
    // Crear TomSelect de almacenes
    tomManager.create('vn-rem-almacen', {
        selector: '#vn-rem-almacen',
        options: [], // inicialmente vacío, se llenará según la sucursal
        valueField: 'id',
        displayField: 'nombre',
        searchField: ['id', 'nombre'],
        placeholder: 'Seleccione el almacén...',
        maxItems: 1,
        create: false,
        render: {
            option: (data, escape) =>
                `<div class="vn-rem-custom-option" style="color: var(--text-dark);">
            ${escape(data.nombre)} (ID: ${escape(data.id)})
        </div>`,
            item: (data, escape) =>
                `<div class="vn-rem-custom-item" style="color: var(--text-dark);">
            ${escape(data.nombre)}
        </div>`
        },
        onChange: (value) => {
            console.log("Almacén seleccionado:", value);
        }
    });

    //Tom para facturas
    tomManager.create('vn-fac-vendedor', {
        selector: '#vn-fac-vendedor',
        options: vendedores,
        valueField: 'id',
        displayField: 'nombre',
        searchField: ['id', 'nombre'],
        placeholder: 'Seleccione un vendedor...',
        maxItems: 1,
        create: false,
        render: {
            option: (data, escape) =>
                `<div class="vn-fac-custom-option" style="color: var(--text-dark);">${escape(data.nombre)} (ID: ${escape(data.id)})</div>`,
            item: (data, escape) =>
                `<div class="vn-fac-custom-item" style="color: var(--text-dark);">${escape(data.nombre)}</div>`
        },
        onChange: (value) => {
            console.log("Vendedor seleccionado:", value);
        }
    });
    tomManager.create('vn-fac-moneda', {
        selector: '#vn-fac-moneda',
        options: monedas,
        valueField: 'id',
        displayField: 'nombre',
        searchField: ['id', 'nombre'],
        placeholder: 'Seleccione una moneda...',
        maxItems: 1,
        create: false,
        render: {
            option: (data, escape) =>
                `<div class="vn-fac-custom-option" style="color: var(--text-dark);">${escape(data.nombre)} (ID: ${escape(data.id)})</div>`,
            item: (data, escape) =>
                `<div class="vn-fac-custom-item" style="color: var(--text-dark);">${escape(data.nombre)}</div>`
        },
        onChange: (value) => {
            console.log("Moneda seleccionada:", value);

            let v;
            if (value === "PESOS") {
                v = "peso";
            } else if (value === "EURO") {
                v = "euro";
            } else if (value === "DLLS") {
                v = "dolar";
            }

            // Aseguramos que exista tasas[0]
            const tasaData = tasas?.[0];
            if (!tasaData || !v) {
                document.getElementById('vn-fac-paridad').value = '';
                return;
            }

            // Obtener la tasa correspondiente
            const tasaSeleccionada = tasaData[v];

            // Actualizar el input si existe la tasa
            if (tasaSeleccionada !== undefined) {
                document.getElementById('vn-fac-paridad').value = tasaSeleccionada;
            } else {
                document.getElementById('vn-fac-paridad').value = '';
            }
        }
    });
    tomManager.create('vn-fac-uso-cfdi', {
        selector: '#vn-fac-uso-cfdi',
        options: cfdi,
        valueField: 'id',
        displayField: 'nombre',
        searchField: ['id', 'nombre'],
        placeholder: 'Seleccione el cfdi...',
        maxItems: 1,
        create: false,
        render: {
            option: (data, escape) =>
                `<div class="vn-fac-custom-option" style="color: var(--text-dark);">${escape(data.nombre)} (ID: ${escape(data.id)})</div>`,
            item: (data, escape) =>
                `<div class="vn-fac-custom-item" style="color: var(--text-dark);">${escape(data.nombre)}</div>`
        },
        onChange: (value) => {
            console.log("CFDI seleccionado:", value);
        }
    });
    tomManager.create('vn-fac-forma-pago', {
        selector: '#vn-fac-forma-pago',
        options: fpago,
        valueField: 'id',
        displayField: 'nombre',
        searchField: ['id', 'nombre'],
        placeholder: 'Seleccione la forma de pago...',
        maxItems: 1,
        create: false,
        render: {
            option: (data, escape) =>
                `<div class="vn-fac-custom-option" style="color: var(--text-dark);">${escape(data.nombre)} (ID: ${escape(data.id)})</div>`,
            item: (data, escape) =>
                `<div class="vn-fac-custom-item" style="color: var(--text-dark);">${escape(data.nombre)}</div>`
        },
        onChange: (value) => {
            console.log("Forma de pago seleccionada:", value);
        }
    });
    tomManager.create('vn-fac-incoterm', {
        selector: '#vn-fac-incoterm',
        options: incoterms,
        valueField: 'id',
        displayField: 'nombre',
        searchField: ['id', 'nombre'],
        placeholder: 'Seleccione el incoterm...',
        maxItems: 1,
        create: false,
        render: {
            option: (data, escape) =>
                `<div class="vn-fac-custom-option" style="color: var(--text-dark);">${escape(data.nombre)} (ID: ${escape(data.id)})</div>`,
            item: (data, escape) =>
                `<div class="vn-fac-custom-item" style="color: var(--text-dark);">${escape(data.nombre)}</div>`
        },
        onChange: (value) => {
            console.log("Forma de pago seleccionada:", value);
        }
    });

    // Crear TomSelect de sucursales
    const sucursalSelect3 = tomManager.create('vn-fac-sucursal', {
        selector: '#vn-fac-sucursal',
        options: sucursales,
        valueField: 'id',
        displayField: 'nombre',
        searchField: ['id', 'nombre'],
        placeholder: 'Seleccione la sucursal...',
        maxItems: 1,
        create: false,
        render: {
            option: (data, escape) =>
                `<div class="vn-fac-custom-option" style="color: var(--text-dark);">
        ${escape(data.nombre)} (ID: ${escape(data.id)})
    </div>`,
            item: (data, escape) =>
                `<div class="vn-fac-custom-item" style="color: var(--text-dark);">
        ${escape(data.nombre)}
    </div>`
        },
        onChange: (sucursalId) => {
            console.log("Sucursal seleccionada:", sucursalId);

            // Filtrar almacenes de esa sucursal
            const almacenesFiltrados = almacenes.filter(a => a.sucursal_id == sucursalId);

            // Obtener la instancia del TomSelect de almacén
            const almacenInstance = tomManager.getInstance('vn-fac-almacen');
            if (!almacenInstance) return;

            // Limpiar y recargar opciones
            almacenInstance.clear();         // Quita el valor seleccionado
            almacenInstance.clearOptions();  // Limpia las opciones anteriores
            almacenInstance.addOptions(almacenesFiltrados); // Agrega solo los almacenes de esa sucursal
            almacenInstance.refreshOptions(false); // Refresca el desplegable

            console.log("Almacenes disponibles:", almacenesFiltrados);
        }
    });
    // Crear TomSelect de almacenes
    tomManager.create('vn-fac-almacen', {
        selector: '#vn-fac-almacen',
        options: [], // inicialmente vacío, se llenará según la sucursal
        valueField: 'id',
        displayField: 'nombre',
        searchField: ['id', 'nombre'],
        placeholder: 'Seleccione el almacén...',
        maxItems: 1,
        create: false,
        render: {
            option: (data, escape) =>
                `<div class="vn-fac-custom-option" style="color: var(--text-dark);">
        ${escape(data.nombre)} (ID: ${escape(data.id)})
    </div>`,
            item: (data, escape) =>
                `<div class="vn-fac-custom-item" style="color: var(--text-dark);">
        ${escape(data.nombre)}
    </div>`
        },
        onChange: (value) => {
            console.log("Almacén seleccionado:", value);
        }
    });

    //
    mon = tomManager.getInstance('vn-cot-moneda');
    mon.setValue("PESOS", false);

    //Pedido
    suc = tomManager.getInstance('vn-ped-sucursal');
    suc.setValue(sucursalUsuario, false);
    alm = tomManager.getInstance('vn-ped-almacen');
    alm.setValue("1", false);
    //remision
    suc = tomManager.getInstance('vn-rem-sucursal');
    suc.setValue(sucursalUsuario, false);
    alm = tomManager.getInstance('vn-rem-almacen');
    alm.setValue("1", false);
    //factura
    suc = tomManager.getInstance('vn-fac-sucursal');
    suc.setValue(sucursalUsuario, false);
    alm = tomManager.getInstance('vn-fac-almacen');
    alm.setValue("1", false);
});

// Inicializar cuando el DOM esté listo
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => App.init());
    document.addEventListener('dblclick', handleDescuentoAuth);
    document.addEventListener('keydown', handleDescuentoF8);

} else {
    App.init();
}
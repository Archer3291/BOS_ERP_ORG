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
            const response = await fetch(`${this.detailEndpoint}?id=${encodeURIComponent(id)}`);
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
        this.monedaActual = 'PESOS';
        this.paridadActual = 1;
        this.totales = { subtotal1: 0, descuento: 0, flete: 0, subtotal2: 0, total: 0 };

        this.dom = {
            table: document.getElementById('vin-cot-productosTable'),
            subtotal1Display: document.getElementById('vin-cot-total-subtotal1-display'),
            descuentoDisplay: document.getElementById('vin-cot-total-descuento-display'),
            fleteDisplay: document.getElementById('vin-cot-total-flete-display'),
            subtotal2Display: document.getElementById('vin-cot-total-subtotal2-display'),
            importeDisplay: document.getElementById('vin-cot-importe-display'),
            fleteInput: document.getElementById('vin-cot-flete-val')
        };

        this.init();
    }

    init() {
        this.dom.table?.addEventListener('input', (e) => {
            const input = e.target;
            if (input.classList.contains('vin-cot-cantidad') ||
                input.classList.contains('vin-cot-precio') ||
                input.classList.contains('vin-cot-descuento') ||
                input.classList.contains('vin-cot-comentario')) {
                const productoId = input.closest('tr')?.dataset.productoId;
                if (productoId) this.actualizarFila(productoId);
            }
        });

        this.dom.table?.addEventListener('click', (e) => {
            const btnEliminar = e.target.closest('.vin-cot-btn-eliminar');
            if (btnEliminar) {
                const productoId = btnEliminar.closest('tr')?.dataset.productoId;
                if (productoId) this.eliminarProducto(productoId);
                return;
            }
            const overlay = e.target.closest('.vin-cot-field-overlay');
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

    convertirPrecio(precioMXN) {
        if (!precioMXN || this.paridadActual <= 0) return precioMXN;
        return precioMXN / this.paridadActual;
    }

    agregarProducto(producto) {
        const esAuto = producto.aplicar_automatico === true ||
            producto.aplicar_automatico === 'true' ||
            producto.aplicar_automatico === 1;
        const tipoRegla = producto.tipo_regla || 'BASE';

        // ── PRECIO UNITARIO ───────────────────────────────────────────────
        // precio_minimo = precio base puro del catálogo (antes de cualquier descuento)
        // precio_final  = precio_base * (1 - desc/100) — NO lo usamos para el input
        //
        // Regla: el input precio SIEMPRE muestra el precio base puro (1453.63)
        // sin importar si es AUTO o MANUAL.
        // precio_minimo viene como v_precio_base desde la función SQL en todos los casos.
                const precioBase = parseFloat(producto.precio_minimo || producto.precio || 0); // ← esto ya está, sin cambios

        // ── DESCUENTO INICIAL ─────────────────────────────────────────────
        // AUTO  → descuento_sugerido (ej: 30) — se pre-llena en el input
        // MANUAL → 0 siempre — el usuario lo ingresa manualmente
        const descuentoInicial = esAuto
            ? parseFloat(producto.descuento_sugerido || 0)
            : 0;

        const descuentoMaximo = parseFloat(producto.descuento_maximo || 100);

        const productoData = {
            id: `prod_${Date.now()}`,
            productoId: producto.id,
            descripcion: producto.descripcion,
            cantidad: 1,
            precio: precioBase,
            precioOriginal: precioBase,
            precioMinimo: precioBase,
            descuento: descuentoInicial,
            descuentoMaximo: descuentoMaximo,
            aplicarAutomatico: esAuto,
            tipoRegla: tipoRegla,
            unidad: producto.udm || 'PZA',
            comentario: '',
            stockSRS: parseFloat(producto.stock_srs || 0),
            stockDetalle: producto.stock_detalle || [] 
        };

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
<tr class="vin-cot-empty-state">
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
                    // Precio MOSTRADO en la moneda actual (no cambia el almacenado)
        const precioMostrado = this.convertirPrecio(p.precio);
        const precioMinimoMostrado = this.convertirPrecio(p.precioMinimo);
        const precioOriginalMostrado = this.convertirPrecio(p.precioOriginal);


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
            precioInput.className = 'vin-cot-product-input vin-cot-precio';
            precioInput.value = precioMostrado.toFixed(2);
            precioInput.min = precioMinimoMostrado.toFixed(2);
            precioInput.step = '0.01';
            precioInput.dataset.precioOriginal = precioOriginalMostrado.toFixed(2);
            precioInput.dataset.precioMinimo = precioMinimoMostrado.toFixed(2);
            precioInput.dataset.esAuto = p.aplicarAutomatico ? '1' : '0';

            if (!precioDesbloqueado) {
                // Bloqueado: overlay que pide contraseña al hacer clic
                precioInput.disabled = true;
                const overlayPrecio = document.createElement('div');
                overlayPrecio.className = 'vin-cot-field-overlay';
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
            descInput.className = 'vin-cot-product-input vin-cot-descuento';
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
                overlayDesc.className = 'vin-cot-field-overlay';
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
        title="${this.escapeHtml(p.descripcion)}">${this.escapeHtml(p.descripcion)}</td>`;

            // ── Celda stock — Badges con dot de intensidad ─────────────────
            // ── Celda stock — B1: acento lateral + chip de cantidad ────────
            const stockCell = document.createElement('td');
            stockCell.style.cssText = 'padding:6px 8px;vertical-align:middle;';

            if (!p.stockDetalle || p.stockDetalle.length === 0) {
                stockCell.innerHTML = `
        <div style="display:flex;align-items:center;justify-content:center;height:100%;">
            <span style="font-size:11px;color:var(--color-text-tertiary,#aaa);">Sin datos</span>
        </div>`;
            } else {
                const stockValido = p.stockDetalle.filter(d => parseFloat(d.existencia || 0) > 0);

                if (stockValido.length === 0) {
                    stockCell.innerHTML = `
            <div style="display:flex;align-items:center;justify-content:center;height:100%;">
                <span style="font-size:11px;color:var(--color-text-tertiary,#aaa);">Sin stock disponible</span>
            </div>`;
                } else {
                    const max = Math.max(...stockValido.map(d => parseFloat(d.existencia)));

                    const badges = stockValido.map(d => {
                        const existencia = parseFloat(d.existencia);
                        const intensity = existencia / max;
                        const alpha = (0.3 + intensity * 0.7).toFixed(2);

                        return `
<div style="display:flex;align-items:center;gap:8px;
            background:var(--color-background-primary);
            border:0.5px solid var(--color-border-tertiary);
            border-left:3px solid rgba(99,153,34,${alpha});
            border-radius:6px;padding:5px 10px;white-space:nowrap;">
    <span style="font-size:11px;color:var(--color-text-secondary);">
        ${this.escapeHtml(d.sucursal || '')}
    </span>
    <span style="font-size:11px;font-weight:500;color:#27500A;
                 background:#EAF3DE;padding:1px 7px;border-radius:99px;">
        ${existencia.toFixed(2)}
    </span>
</div>`;
                    }).join('');

                    stockCell.innerHTML = `
            <div style="display:flex;flex-wrap:nowrap;gap:12px;align-items:center;">
                ${badges}
            </div>`;
                }
            }
            tr.appendChild(stockCell);

            // ── Resto de celdas ───────────────────────────────────────────
            tr.innerHTML += `
    <td>
        <input type="number" class="vin-cot-product-input vin-cot-cantidad"
               value="${p.cantidad}" min="0" step="0.01">
    </td>
    <td>${this.escapeHtml(p.unidad)}</td>`;

            tr.appendChild(precioCell);
            tr.appendChild(descCell);

            const resto = document.createElement('template');
            resto.innerHTML = `
    <td class="vin-cot-importe fw-bold">${importe.toFixed(2)}</td>
    <td>
        <input type="text" class="vin-cot-product-input vin-cot-comentario"
               value="${this.escapeHtml(p.comentario)}" placeholder="Comentario...">
    </td>
    <td>
        <button type="button" class="vin-cot-btn-delete vin-cot-btn-eliminar">
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

        producto.cantidad = parseFloat(tr.querySelector('.vin-cot-cantidad').value) || 0;
        producto.comentario = tr.querySelector('.vin-cot-comentario').value || '';

        // ── PRECIO ────────────────────────────────────────────────────────
        const precioInput = tr.querySelector('.vin-cot-precio');
        if (precioInput && !precioInput.disabled) {
            const nuevoPrecioDivisa = parseFloat(precioInput.value) || 0;
            // Convertir a MXN para guardar internamente
            const nuevoPrecioMXN = nuevoPrecioDivisa * this.paridadActual;
            const precioOriginalMXN = producto.precioOriginal;
            const precioMinimoMXN = producto.precioMinimo;

            if (!authState.precioToken && Math.abs(nuevoPrecioMXN - precioOriginalMXN) > 0.001) {
                precioInput.value = this.convertirPrecio(precioOriginalMXN).toFixed(2);
                producto.precio = precioOriginalMXN;
                toastMixin.fire({ icon: 'warning', title: 'Se requiere autorización para modificar el precio' });
            } else if (nuevoPrecioMXN < precioMinimoMXN - 0.001) {
                precioInput.value = this.convertirPrecio(precioMinimoMXN).toFixed(2);
                producto.precio = precioMinimoMXN;
                toastMixin.fire({
                    icon: 'warning',
                    title: `Precio mínimo permitido: ${this.convertirPrecio(precioMinimoMXN).toFixed(2)} ${this.monedaActual}`
                });
            } else {
                producto.precio = Math.max(nuevoPrecioMXN, 0);  // guarda en MXN
            }
        }

        // ── DESCUENTO ─────────────────────────────────────────────────────
        const descInput = tr.querySelector('.vin-cot-descuento');
        if (descInput && !descInput.disabled) {
            const nuevoDesc = parseFloat(descInput.value) || 0;
            const descMaximo = parseFloat(descInput.dataset.descMaximo) || 100;

            // Sin token: imposible llegar aquí, pero por seguridad revertir
            if (!authState.descuentoToken && nuevoDesc > 0) {
                descInput.value = producto.descuento.toFixed(2); // revertir al valor previo
                toastMixin.fire({ icon: 'warning', title: 'Se requiere autorización para modificar el descuento' });
                return;
            }

            // Con token: respetar el tope máximo de la regla
            if (nuevoDesc > descMaximo + 0.001) {
                descInput.value = descMaximo.toFixed(2);
                producto.descuento = descMaximo;
                toastMixin.fire({ icon: 'warning', title: `Descuento máximo permitido: ${descMaximo}%` });
            } else {
                producto.descuento = Math.max(nuevoDesc, 0);
            }
        }

        tr.querySelector('.vin-cot-importe').textContent = this.calcularImporte(producto).toFixed(2);
        this.calcularTotales();
    }

    eliminarProducto(productoId) {
        this.productos = this.productos.filter(p => p.id !== productoId);
        this.renderTable();
        this.calcularTotales();
    }

    desbloquearCampos(tipo) {
        const selector = tipo === 'descuento' ? '.vin-cot-descuento' : '.vin-cot-precio';
        this.dom.table?.querySelectorAll(selector).forEach(input => {
            input.removeAttribute('disabled');
            input.parentElement?.querySelector('.vin-cot-field-overlay')?.remove();
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

        const flete = parseFloat(document.getElementById('vin-cot-flete-val').value) || 0;
        const subtotal2 = totales.subtotal1 - totales.totalDescuento + flete;
        const total = subtotal2;

        this.totales = { subtotal1: totales.subtotal1, descuento: totales.totalDescuento, flete, subtotal2, total };

        this.updateDisplay(this.dom.subtotal1Display, totales.subtotal1);
        this.updateDisplay(this.dom.descuentoDisplay, totales.totalDescuento, '-$');
        this.updateDisplay(this.dom.fleteDisplay, flete);
        this.updateDisplay(this.dom.subtotal2Display, subtotal2);
        this.updateDisplay(this.dom.importeDisplay, total, '$');

        this.updateHiddenField('vin-cot-total-subtotal1', totales.subtotal1);
        this.updateHiddenField('vin-cot-total-descuento', totales.totalDescuento);
        this.updateHiddenField('vin-cot-total-flete', flete);
        this.updateHiddenField('vin-cot-total-subtotal2', subtotal2);
        this.updateHiddenField('vin-cot-importe', total);
    }

    updateHiddenField(id, value) {
        const field = document.getElementById(id);
        if (field) field.value = value.toFixed(2);
    }

    updateDisplay(element, value, prefix = '$') {
        const simbolo = this.monedaActual === 'DLLS' ? 'USD '
            : this.monedaActual === 'EURO' ? 'EUR '
                : '$';
        // Convertir totales a la moneda actual para mostrar
        const valorMostrado = (prefix === '-$')
            ? this.convertirPrecio(value)
            : this.convertirPrecio(value);

        const prefixFinal = prefix === '-$' ? `-${simbolo}` : simbolo;
        if (element) element.textContent = `${prefixFinal}${valorMostrado.toFixed(2)}`;
    }

    cambiarMoneda(moneda, paridad) {
        this.monedaActual = moneda;
        this.paridadActual = parseFloat(paridad) || 1;
        this.renderTable();      // re-renderiza con precios convertidos
        this.calcularTotales();  // actualiza los totales mostrados
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
        this.form = document.getElementById('vin-cot-formCotizacion');
        this.btnSubmit = document.querySelector('.vin-cot-btn-submit');
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

        formData.append('sucursal', document.getElementById('vin-cot-sucursal')?.value || '');
        formData.append('almacen', document.getElementById('vin-cot-almacen')?.value || '');
        formData.append('tipoMovimiento', document.getElementById('vin-cot-tipo-docto-mov')?.value || '');
        formData.append('folio', document.getElementById('vin-cot-folio')?.value || '');

        formData.append('cliente', document.getElementById('vin-cot-cliente')?.value || '');
        formData.append('rfc', document.getElementById('vin-cot-rfc')?.value || '');
        formData.append('vendedor', document.getElementById('vin-cot-vendedor')?.value || '');
        formData.append('contacto', document.getElementById('vin-cot-contacto')?.value || '');
        formData.append('moneda', document.getElementById('vin-cot-moneda')?.value || '');
        formData.append('paridad', document.getElementById('vin-cot-paridad')?.value || '');
        formData.append('concepto', document.getElementById('vin-cot-concepto')?.value || '');
        formData.append('incoterm', document.getElementById('vin-cot-incoterm')?.value || '');
        formData.append('ordenCompra', document.getElementById('vin-cot-orden-compra-val')?.value || '');

        formData.append('limiteCredito', document.getElementById('vin-cot-limite-credito')?.value || '');
        formData.append('plazo', document.getElementById('vin-cot-plazo')?.value || '');
        formData.append('fechaPago', document.getElementById('vin-cot-fecha-pago')?.value || '');
        formData.append('formaPago', document.getElementById('vin-cot-forma-pago')?.value || '');
        formData.append('usoCFDI', document.getElementById('vin-cot-uso-cfdi')?.value || '');
        formData.append('comentarios', document.getElementById('vin-cot-comentarios')?.value || '');

        formData.append('tipoPago', document.getElementById('vin-cot-tipo-pago')?.value || 'contado');

        // ★ Los productos vienen del array JS validado, no del DOM directamente
        const productos = this.productManager.getProductosData();

        // ★ Convertir cada producto a la moneda actual antes de enviar
        const paridad = this.productManager.paridadActual || 1;
        const productosConvertidos = productos.map(p => ({
            ...p,
            precio: p.precio / paridad,
            precioOriginal: p.precioOriginal / paridad,
            precioMinimo: p.precioMinimo / paridad,
            importe: p.importe / paridad,
        }));

        formData.append('productosJSON', JSON.stringify(productosConvertidos));

        const totales = this.productManager.totales;
        formData.append('subtotal1', (totales.subtotal1 / paridad).toFixed(2));
        formData.append('descuento', (totales.descuento / paridad).toFixed(2));
        formData.append('flete', (totales.flete / paridad).toFixed(2));
        formData.append('subtotal2', (totales.subtotal2 / paridad).toFixed(2));
        formData.append('total', (totales.total / paridad).toFixed(2));

        formData.append('descuentoToken', authState.descuentoToken || '');
        formData.append('precioToken', authState.precioToken || '');

        return formData;
    }

    // Dentro de FormManager — reemplaza solo el método validarFormulario
    validarFormulario() {
        const errores = [];
        const productos = this.productManager.getProductosData();

        const cliente = document.getElementById('vin-cot-cliente')?.value;
        if (!cliente?.trim()) errores.push('Debe seleccionar un cliente');

        if (productos.length === 0) errores.push('Debe agregar al menos un producto');

        const vendedor = document.getElementById('vin-cot-vendedor')?.value;
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
                endpoint: '/VINCotizacion/Guardar',
                metodo: 'POST',
                mensajeExito: 'Cotización creada exitosamente',
                textoCarga: 'Guardando...',
                validarProductos: true
            },
            'modificacion': {
                endpoint: '/VINCotizacion/Modificar',
                metodo: 'PUT',
                mensajeExito: 'Cotización modificada exitosamente',
                textoCarga: 'Actualizando...',
                validarProductos: true
            },
            'consulta': {
                endpoint: '/VINCotizacion/Consultar',
                metodo: 'GET',
                mensajeExito: 'Consulta realizada exitosamente',
                textoCarga: 'Consultando...',
                validarProductos: false
            }
        };
        return configuraciones[tipoMovimiento] || configuraciones['alta'];
    }

    async enviarCotizacion() {
        const tipoMovimiento = document.getElementById('vin-cot-tipo-docto-mov')?.value || 'alta';
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
                // ── Validar stock del almacén principal (suc=02, alm=10) ──
                const productos = this.productManager.getProductosData();
                const paridad = this.productManager.paridadActual || 1;

                const partidas = [];   // partidas que van a traspaso
                const sinStock = [];   // partidas sin ningún registro en SRS

                for (const p of productos) {
                    const detalle = p.stockDetalle || [];
                    const cantidad = parseFloat(p.cantidad) || 0;

                    const totalSRS = detalle.reduce((s, d) => s + parseFloat(d.existencia || 0), 0);
                    const sinRegistro = detalle.length === 0 || totalSRS === 0;

                    if (sinRegistro) {
                        // Sin stock en ningún almacén SRS → contra-pedido
                        sinStock.push(p.descripcion || p.productoId);
                    } else {
                        // Tiene stock en algún almacén SRS → siempre genera traspaso
                        partidas.push({
                            productoId: p.productoId,
                            descripcion: p.descripcion,
                            unidad: p.unidad,
                            cantidad: cantidad,
                            faltante: cantidad.toFixed(4)
                        });
                    }
                }

                // Notificar productos contra-pedido
                // ── 1. Notificar contra-pedido (siempre que haya, independiente del traspaso) ──
                if (sinStock.length > 0) {
                    await Swal.fire({
                        icon: 'info',
                        title: 'Productos contra pedido',
                        html: `Los siguientes productos no tienen stock en SRS y se atenderán 
                <strong>contra pedido</strong>:<br><br>
                <ul style="text-align:left">
                    ${sinStock.map(n => `<li>${n}</li>`).join('')}
                </ul>`,
                        confirmButtonText: 'Entendido'
                    });
                }

                // ── 2. Solicitud de traspaso (solo si hay faltantes, independiente del contra-pedido) ──
                if (partidas.length > 0) {
                    const fdTraspaso = new FormData();
                    const csrf = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
                    if (csrf) fdTraspaso.append('__RequestVerificationToken', csrf);
                    fdTraspaso.append('folioReferencia', result.folio_generado);
                    fdTraspaso.append('productosJSON', JSON.stringify(partidas));

                    try {
                        const rTraspaso = await fetch('/VINCotizacion/CrearSolicitudTraspaso', {
                            method: 'POST',
                            body: fdTraspaso
                        });
                        const dTraspaso = await rTraspaso.json();

                        if (dTraspaso.success) {
                            await Swal.fire({
                                icon: 'success',
                                title: 'Cotización y Traspaso generados',
                                html: `Cotización: <strong>${result.folio_generado}</strong><br>
                        Traspaso: <strong>${dTraspaso.folioTraspaso}</strong>`,
                                confirmButtonText: 'Aceptar'
                            });
                        } else {
                            toastMixin.fire({
                                icon: 'warning',
                                title: `Traspaso no generado: ${dTraspaso.message}`
                            });
                        }
                    } catch (errT) {
                        console.error('Error creando traspaso:', errT);
                        toastMixin.fire({
                            icon: 'warning',
                            title: 'Cotización guardada, pero no se pudo crear el traspaso.'
                        });
                    }

                } else {
                    // Sin faltantes de traspaso — éxito simple
                    // (puede haber habido contra-pedidos, ya se notificaron arriba)
                    await Swal.fire({
                        icon: 'success',
                        title: `${result.message || ''} Folio: ${result.folio_generado || ''}`.trim(),
                        confirmButtonText: 'Aceptar'
                    });
                }

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
        document.getElementById('vin-cot-cliente').value = '';
        document.getElementById('vin-cot-rfc').value = '';
        document.getElementById('vin-cot-info-proveedor').value = '';
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

    init() {
        this.form = document.getElementById('form-2');
        this.btnSubmit = document.querySelector('.vin-cot-btn-submit');

        this.clienteManager = new DataManager({
            endpoint: '/DatosGenerales/BuscarC',
            detailEndpoint: '/DatosGenerales/BuscarCliente',
            modalId: 'vin-cot-modalBuscarCliente',
            inputId: 'vin-cot-inputBuscarCliente',
            resultsId: 'vin-cot-listaResultadosClientes',
            pageSizeId: 'vin-cot-pageSizeClientes',
            btnClearId: 'vin-cot-btnLimpiarClientes',
            spinnerId: 'vin-cot-spinnerClientes',
            paginationId: 'vin-cot-paginationClientes',
            recordsFromId: 'vin-cot-recordsFromClientes',
            recordsToId: 'vin-cot-recordsToClientes',
            totalRecordsId: 'vin-cot-totalRecordsClientes',
            shouldCloseOnSelect: true
        });

        this.clienteManager.onSelect = (cliente) => {
            document.getElementById('vin-cot-cliente').value = cliente.id || '';
            document.getElementById('vin-cot-rfc').value = cliente.rfc || '';
            document.getElementById('vin-cot-info-proveedor').value =
                [cliente.dir, cliente.col, cliente.pob, cliente.cp].filter(x => x).join(',\n');
            document.getElementById('vin-cot-lista-precios').value = cliente.cod_ant || 'Sin asignar';
            const vendedorInstance = tomManager.getInstance('vin-cot-vendedor');
            if (vendedorInstance && cliente.cve_vdr) vendedorInstance.setValue(cliente.cve_vdr, true);
            mostrarCreditoBanner(cliente);
        };

        this.productoManager = new DataManager({
            endpoint: '/DatosGenerales/BuscarPCotizacion',
            detailEndpoint: '/DatosGenerales/BuscarProductoCotizacion',
            modalId: 'vin-cot-modalBuscarProducto',
            inputId: 'vin-cot-inputBuscarProducto',
            resultsId: 'vin-cot-listaResultadosProductos',
            pageSizeId: 'vin-cot-pageSizeProductos',
            btnClearId: 'vin-cot-btnLimpiarProductos',
            spinnerId: 'vin-cot-spinnerProductos',
            paginationId: 'vin-cot-paginationProductos',
            recordsFromId: 'vin-cot-recordsFromProductos',
            recordsToId: 'vin-cot-recordsToProductos',
            totalRecordsId: 'vin-cot-totalRecordsProductos',
            shouldCloseOnSelect: false
        });

        this.productManagerInstance = new ProductManager();

        this.productoManager.onSelect = (producto) => {
            this.productManagerInstance.agregarProducto(producto);
        };

        this.formManager = new FormManager(this.productManagerInstance);

        const tipoMovSelect = document.getElementById('vin-cot-tipo-docto-mov');
        tipoMovSelect?.addEventListener('change', (e) => this.manejarCambioTipoMovimiento(e.target.value));
        this.manejarCambioTipoMovimiento(tipoMovSelect?.value || 'alta');

        console.log('Sistema de cotización inicializado correctamente');
        window.verDatosFormulario = () => this.formManager.verDatosFormulario();
        window.EnviarFormulario = () => this.formManager.enviarCotizacion(); 
    },

    manejarCambioTipoMovimiento(tipo) {
        if (tipo === 'consulta') this.deshabilitarFormulario();
        else this.habilitarFormulario();
    },

    deshabilitarFormulario() {
        const form = this.form || this.formManager?.form || document.getElementById('vin-cot-formCotizacion');
        if (!form) return;
        form.querySelectorAll('input, select, textarea, button').forEach(el => {
            if (el.id !== 'vin-cot-tipo-docto-mov') {
                el.disabled = true;
                el.classList.add('vin-cot-disabled-field');
            }
        });
        document.querySelector('[data-bs-target="#vin-cot-modalBuscarCliente"]')?.setAttribute('disabled', true);
        document.querySelector('[data-bs-target="#vin-cot-modalBuscarProducto"]')?.setAttribute('disabled', true);
        document.querySelectorAll('.vin-cot-btn-eliminar').forEach(btn => btn.disabled = true);
        if (window.tomManager) {
            ['vin-cot-vendedor', 'vin-cot-moneda', 'vin-cot-uso-cfdi', 'vin-cot-forma-pago']
                .forEach(id => tomManager.instances.get(id)?.disable());
        }
        const btn = this.btnSubmit || document.querySelector('.vin-cot-btn-submit');
        if (btn) btn.innerHTML = '<i class="fas fa-search"></i> Consultar';
    },

    habilitarFormulario() {
        const form = this.form || this.formManager?.form || document.getElementById('vin-cot-formCotizacion');
        if (!form) return;
        form.querySelectorAll('input, select, textarea, button').forEach(el => {
            el.disabled = false;
            el.classList.remove('vin-cot-disabled-field');
        });
        document.querySelector('[data-bs-target="#vin-cot-modalBuscarCliente"]')?.removeAttribute('disabled');
        document.querySelector('[data-bs-target="#vin-cot-modalBuscarProducto"]')?.removeAttribute('disabled');
        if (window.tomManager) {
            ['vin-cot-vendedor', 'vin-cot-moneda', 'vin-cot-uso-cfdi', 'vin-cot-forma-pago']
                .forEach(id => tomManager.instances.get(id)?.enable());
        }
        const tipoMovimiento = document.getElementById('vin-cot-tipo-docto-mov')?.value;
        const btn = this.btnSubmit || document.querySelector('.vin-cot-btn-submit');
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
function mostrarCreditoBanner(cliente) {
    const banner = document.getElementById('vin-cot-credit-banner');
    const icon = document.getElementById('vin-cot-credit-icon');
    const title = document.getElementById('vin-cot-credit-title');
    const sub = document.getElementById('vin-cot-credit-sub');
    const bar = document.getElementById('vin-cot-credit-bar');
    const barWrap = document.getElementById('vin-cot-credit-bar-wrap');
    const pct = document.getElementById('vin-cot-credit-pct');
    if (!banner) return;

    const estatus = cliente.estatus_credito || 'SIN_LIMITE';
    const limite = parseFloat(cliente.lim_crd || 0);
    const usado = parseFloat(cliente.credito_usado || 0);
    const disponible = parseFloat(cliente.credito_disponible || 0);
    const porcentaje = parseFloat(cliente.porcentaje_uso || 0);
    const estatus_cliente = cliente.estatus_cliente;
    const fmt = n => '$' + n.toLocaleString('es-MX', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

    banner.classList.remove('disponible', 'por-vencer', 'excedido', 'sin-limite');
    banner.style.display = 'flex';
    barWrap.style.display = 'block';

    if (estatus === 'SIN_LIMITE') {
        banner.classList.add('sin-limite'); icon.textContent = 'ⓘ';
        title.textContent = 'Sin límite de crédito configurado';
        sub.textContent = 'Este cliente no tiene límite de crédito asignado';
        bar.style.width = '0%'; barWrap.style.display = 'none'; pct.textContent = '';
        toastMixin.fire({ icon: 'info', title: 'Cliente sin límite de crédito configurado' });
    } else if (estatus_cliente === 'suspendido') {
        banner.classList.add('sin-limite'); icon.textContent = 'ⓘ';
        title.textContent = 'Cliente Suspendido';
        sub.textContent = 'Este cliente está suspendido, consulta con tu gerente para realizar el pedido';
        bar.style.width = '0%'; barWrap.style.display = 'none'; pct.textContent = '';
        toastMixin.fire({ icon: 'info', title: 'Cliente suspendido' });
    } else if (estatus === 'EXCEDIDO') {
        banner.classList.add('excedido'); icon.textContent = '✕';
        title.textContent = 'Límite de crédito excedido';
        sub.textContent = `Usado: ${fmt(usado)} de ${fmt(limite)}  |  Excedente: ${fmt(Math.abs(disponible))}`;
        bar.style.width = '100%'; pct.textContent = Math.round(porcentaje) + '%';
        Swal.fire({
            icon: 'warning', title: 'Crédito excedido',
            html: `El cliente <strong>${cliente.descripcion}</strong> ha superado su límite de crédito.<br><br>
                   Usado: <strong>${fmt(usado)}</strong> de <strong>${fmt(limite)}</strong>`,
            confirmButtonText: 'Entendido', confirmButtonColor: '#A32D2D'
        });
    } else if (estatus === 'POR_VENCER') {
        banner.classList.add('por-vencer'); icon.textContent = '⚠';
        title.textContent = 'Crédito próximo al límite';
        sub.textContent = `Usado: ${fmt(usado)} de ${fmt(limite)}  |  Disponible: ${fmt(disponible)}`;
        bar.style.width = Math.min(porcentaje, 100) + '%'; pct.textContent = Math.round(porcentaje) + '%';
        toastMixin.fire({ icon: 'warning', title: `Crédito al ${Math.round(porcentaje)}% — quedan ${fmt(disponible)}` });
    } else {
        banner.classList.add('disponible'); icon.textContent = '✓';
        title.textContent = 'Crédito disponible';
        sub.textContent = `Usado: ${fmt(usado)} de ${fmt(limite)}  |  Disponible: ${fmt(disponible)}`;
        bar.style.width = Math.min(porcentaje, 100) + '%'; pct.textContent = Math.round(porcentaje) + '%';
    }
}

// ============================================
// 6. TOM SELECT
// ============================================
GetData({ path: '/DatosGenerales/DatosSelect' }).then((_res) => {
    const { vendedores, monedas, usocfdi: cfdi, formaspago: fpago, incoterms, tasas, sucursales, almacenes, paises } = _res;
    llenarSelect('vin-fac-receptor-pais', paises, 'id', 'nombre');
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
    create('vin-cot-vendedor', { options: vendedores, placeholder: 'Seleccione un vendedor...', render: { option: (d, e) => mkOpt('vin-cot', d, e), item: (d, e) => mkItem('vin-cot', d, e) }, onChange: v => console.log('Vendedor:', v) });
    create('vin-cot-moneda', {
        options: monedas, placeholder: 'Seleccione una moneda...',
        render: { option: (d, e) => mkOpt('vin-cot', d, e), item: (d, e) => mkItem('vin-cot', d, e) },
        onChange: v => {
            // Paridad fija de 17.20 siempre, sin importar la moneda seleccionada
            document.getElementById('vin-cot-paridad').value = '17.20';
            setTimeout(() => {
                const paridad = 17.20;
                App.productManagerInstance?.cambiarMoneda(v, paridad);
            }, 50);
        }
    });
    create('vin-cot-uso-cfdi', { options: cfdi, placeholder: 'Seleccione el cfdi...', render: { option: (d, e) => mkOpt('vin-cot', d, e), item: (d, e) => mkItem('vin-cot', d, e) }, onChange: v => console.log('CFDI:', v) });
    create('vin-cot-forma-pago', { options: fpago, placeholder: 'Seleccione la forma de pago...', render: { option: (d, e) => mkOpt('vin-cot', d, e), item: (d, e) => mkItem('vin-cot', d, e) }, onChange: v => console.log('FPago:', v) });

    // ── Pedido ──
    create('vin-ped-vendedor', { options: vendedores, placeholder: 'Seleccione un vendedor...', render: { option: (d, e) => mkOpt('vin-ped', d, e), item: (d, e) => mkItem('vin-ped', d, e) }, onChange: v => console.log('Vendedor:', v) });
    create('vin-ped-moneda', { options: monedas, placeholder: 'Seleccione una moneda...', render: { option: (d, e) => mkOpt('vin-ped', d, e), item: (d, e) => mkItem('vin-ped', d, e) }, onChange: v => tasaForMoneda(v, 'vin-ped-paridad') });
    create('vin-ped-uso-cfdi', { options: cfdi, placeholder: 'Seleccione el cfdi...', render: { option: (d, e) => mkOpt('vin-ped', d, e), item: (d, e) => mkItem('vin-ped', d, e) }, onChange: v => console.log('CFDI:', v) });
    create('vin-ped-forma-pago', { options: fpago, placeholder: 'Seleccione la forma de pago...', render: { option: (d, e) => mkOpt('vin-ped', d, e), item: (d, e) => mkItem('vin-ped', d, e) }, onChange: v => console.log('FPago:', v) });
    create('vin-ped-incoterm', { options: incoterms, placeholder: 'Seleccione el incoterm...', render: { option: (d, e) => mkOpt('vin-ped', d, e), item: (d, e) => mkItem('vin-ped', d, e) }, onChange: v => console.log('Incoterm:', v) });
    create('vin-ped-sucursal', { options: sucursales, placeholder: 'Seleccione la sucursal...', render: { option: (d, e) => mkOpt('vin-ped', d, e), item: (d, e) => mkItem('vin-ped', d, e) }, onChange: id => filtrarAlmacenes(id, 'vin-ped-almacen') });
    create('vin-ped-almacen', { options: [], placeholder: 'Seleccione el almacén...', render: { option: (d, e) => mkOpt('vin-ped', d, e), item: (d, e) => mkItem('vin-ped', d, e) }, onChange: v => console.log('Almacén:', v) });

    // ── Remisión ──
    create('vin-rem-vendedor', { options: vendedores, placeholder: 'Seleccione un vendedor...', render: { option: (d, e) => mkOpt('vin-rem', d, e), item: (d, e) => mkItem('vin-rem', d, e) }, onChange: v => console.log('Vendedor:', v) });
    create('vin-rem-moneda', { options: monedas, placeholder: 'Seleccione una moneda...', render: { option: (d, e) => mkOpt('vin-rem', d, e), item: (d, e) => mkItem('vin-rem', d, e) }, onChange: v => tasaForMoneda(v, 'vin-rem-paridad') });
    create('vin-rem-uso-cfdi', { options: cfdi, placeholder: 'Seleccione el cfdi...', render: { option: (d, e) => mkOpt('vin-rem', d, e), item: (d, e) => mkItem('vin-rem', d, e) }, onChange: v => console.log('CFDI:', v) });
    create('vin-rem-forma-pago', { options: fpago, placeholder: 'Seleccione la forma de pago...', render: { option: (d, e) => mkOpt('vin-rem', d, e), item: (d, e) => mkItem('vin-rem', d, e) }, onChange: v => console.log('FPago:', v) });
    create('vin-rem-incoterm', { options: incoterms, placeholder: 'Seleccione el incoterm...', render: { option: (d, e) => mkOpt('vin-rem', d, e), item: (d, e) => mkItem('vin-rem', d, e) }, onChange: v => console.log('Incoterm:', v) });
    create('vin-rem-sucursal', { options: sucursales, placeholder: 'Seleccione la sucursal...', render: { option: (d, e) => mkOpt('vin-rem', d, e), item: (d, e) => mkItem('vin-rem', d, e) }, onChange: id => filtrarAlmacenes(id, 'vin-rem-almacen') });
    create('vin-rem-almacen', { options: [], placeholder: 'Seleccione el almacén...', render: { option: (d, e) => mkOpt('vin-rem', d, e), item: (d, e) => mkItem('vin-rem', d, e) }, onChange: v => console.log('Almacén:', v) });

    // ── Factura ──
    create('vin-fac-vendedor', { options: vendedores, placeholder: 'Seleccione un vendedor...', render: { option: (d, e) => mkOpt('vin-fac', d, e), item: (d, e) => mkItem('vin-fac', d, e) }, onChange: v => console.log('Vendedor:', v) });
    create('vin-fac-moneda', { options: monedas, placeholder: 'Seleccione una moneda...', render: { option: (d, e) => mkOpt('vin-fac', d, e), item: (d, e) => mkItem('vin-fac', d, e) }, onChange: v => tasaForMoneda(v, 'vin-fac-paridad') });
    create('vin-fac-uso-cfdi', { options: cfdi, placeholder: 'Seleccione el cfdi...', render: { option: (d, e) => mkOpt('vin-fac', d, e), item: (d, e) => mkItem('vin-fac', d, e) }, onChange: v => console.log('CFDI:', v) });
    create('vin-fac-forma-pago', { options: fpago, placeholder: 'Seleccione la forma de pago...', render: { option: (d, e) => mkOpt('vin-fac', d, e), item: (d, e) => mkItem('vin-fac', d, e) }, onChange: v => console.log('FPago:', v) });
    create('vin-fac-incoterm', { options: incoterms, placeholder: 'Seleccione el incoterm...', render: { option: (d, e) => mkOpt('vin-fac', d, e), item: (d, e) => mkItem('vin-fac', d, e) }, onChange: v => console.log('Incoterm:', v) });
    create('vin-fac-sucursal', { options: sucursales, placeholder: 'Seleccione la sucursal...', render: { option: (d, e) => mkOpt('vin-fac', d, e), item: (d, e) => mkItem('vin-fac', d, e) }, onChange: id => filtrarAlmacenes(id, 'vin-fac-almacen') });
    create('vin-fac-almacen', { options: [], placeholder: 'Seleccione el almacén...', render: { option: (d, e) => mkOpt('vin-fac', d, e), item: (d, e) => mkItem('vin-fac', d, e) }, onChange: v => console.log('Almacén:', v) });

    // Valores por defecto
    tomManager.getInstance('vin-cot-moneda').setValue('DLLS', false);
    //tomManager.getInstance('vin-ped-sucursal').setValue(sucursalUsuario, false);
    //tomManager.getInstance('vin-ped-almacen').setValue('1', false);
    //tomManager.getInstance('vin-rem-sucursal').setValue(sucursalUsuario, false);
    //tomManager.getInstance('vin-rem-almacen').setValue('1', false);
    //tomManager.getInstance('vin-fac-sucursal').setValue(sucursalUsuario, false);
    //tomManager.getInstance('vin-fac-almacen').setValue('1', false);
});

// ============================================
// 7. ARRANQUE
// ============================================
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => App.init());
} else {
    App.init();
}
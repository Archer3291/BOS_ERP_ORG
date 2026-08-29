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
            console.log("documentos: " + item)
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
// 3. GESTOR DE PRODUCTOS
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
            table: document.getElementById('vn-ped-productosTable'),
            thead: document.querySelector('#vn-ped-productosTable')?.closest('table')?.querySelector('thead'),
            subtotal1Display: document.getElementById('vn-ped-total-subtotal1-display'),
            descuentoDisplay: document.getElementById('vn-ped-total-descuento-display'),
            fleteDisplay: document.getElementById('vn-ped-total-flete-display'),
            subtotal2Display: document.getElementById('vn-ped-total-subtotal2-display'),
            ivaDisplay: document.getElementById('vn-ped-iva-display'),
            importeDisplay: document.getElementById('vn-ped-importe-display')
        };

        this.initPedidosProductManager();
    }

    initPedidosProductManager() {
        this.dom.table?.addEventListener('input', (e) => {
            const input = e.target;
            if (input.classList.contains('vn-ped-cantidad') ||
                input.classList.contains('vn-ped-precio') ||
                input.classList.contains('vn-ped-descuento') ||
                input.classList.contains('vn-ped-comentario')) {
                const tr = input.closest('tr');
                const productoId = tr?.dataset.productoId;
                if (productoId) this.actualizarPedidosFila(productoId);
            }
        });

        this.dom.table?.addEventListener('click', (e) => {
            const btn = e.target.closest('.vn-ped-btn-eliminar');
            if (btn) {
                const tr = btn.closest('tr');
                const productoId = tr?.dataset.productoId;
                if (productoId) this.eliminarPedidosProducto(productoId);
            }
        });

        // Event listener para flete
        document.getElementById('vn-ped-flete-val')?.addEventListener('input', () => {
            this.calcularPedidosTotales();
        });

        // Event listener para anticipo
        document.getElementById('vn-ped-monto-anticipo')?.addEventListener('input', () => {
            this.calcularPedidosTotales();
        });
    }

    agregarPedidosProducto(producto) {
        const productoExistente = this.productos.find(p => p.productoId === producto.id);

        if (productoExistente) {
            productoExistente.cantidad += 1;

            const tr = this.dom.table.querySelector(`tr[data-producto-id="${productoExistente.id}"]`);
            if (tr) {
                const inputCantidad = tr.querySelector('.vn-ped-cantidad');
                inputCantidad.value = productoExistente.cantidad;

                const importe = this.calcularPedidosImporte(productoExistente);
                tr.querySelector('.vn-ped-importe').textContent = importe.toFixed(2);

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
                existencia: parseFloat(producto.existencia) || 0,
                cantidad: 1,
                unidad: producto.udm || 'PZA',
                existenciaStock: parseFloat(producto.existencia_stock) || 0,
                existenciaModula: parseFloat(producto.existencia_modula) || 0,
                esTubo: producto.es_tubo === true,
                prodModula: producto.prod_modula === true,
                comentario: ''
            },
            // precio, precioMinimo, descuento sugerido y tope salen de la regla vigente
            // (Scripts/Ventas/reglas-precio-ventas.js).
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
        const thead = this.dom.thead; // suponiendo que tienes referencia al <thead>
        if (!tbody) return;

        // Limpiar mensaje vacío si existe
        const emptyRow = tbody.querySelector('.vn-ped-empty-state');
        if (emptyRow) emptyRow.remove();

        // Limpiar tabla
        tbody.innerHTML = '';

        // Opcional: ocultar columna de descuento en <thead>
        if (thead) {
            const descuentoTh = thead.querySelector('.vn-ped-th-descuento');
            if (descuentoTh) descuentoTh.style.display = (rol === 'Gerente') ? '' : 'none';
        }

        if (this.productos.length === 0) {
            tbody.innerHTML = `
            <tr class="vn-ped-empty-state">
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
            const importe = this.calcularPedidosImporte(p);
            const tr = document.createElement('tr');
            tr.dataset.productoId = p.id;

            // Solo incluir la celda de descuento si el rol es Gerente
            const descuentoCell = (rol === 'Gerente')
                ? `<td>
         <input type="number" class="vn-ped-product-input vn-ped-descuento" value="${p.descuento}" min="0" max="${(p.descuentoMaximo != null ? p.descuentoMaximo : 100).toFixed(2)}" step="0.01" readonly>
       </td>`
                : '';

            tr.innerHTML = `
            <td class="text-center fw-bold">${index + 1}</td> <!-- Nueva celda con índice -->
            <td>${this.escapePedidosHtml(p.productoId)}</td>
            <td style="max-width: 200px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis;">
                ${this.escapePedidosHtml(p.descripcion)}
                ${this.renderBadgeDestino(p)}
            </td>
            <td class="text-center">
                <span style="
                    display: inline-block;
                    padding: 4px 12px;
                    border-radius: 4px;
                    font-weight: 600;
                    ${this.obtenerEstiloExistencia(p.existenciaStock)}
                ">
                    ${p.existenciaStock || 0}
                </span>
            </td>
            <td class="text-center">
                ${p.prodModula
                    ? `<span style="
                            display: inline-block;
                            padding: 4px 12px;
                            border-radius: 4px;
                            font-weight: 600;
                            ${this.obtenerEstiloExistencia(p.existenciaModula)}
                        ">${p.existenciaModula || 0}</span>`
                    : `<span style="color:var(--vn-gray-400)">—</span>`}
            </td>
            <td>
                <input type="number" class="vn-ped-product-input vn-ped-cantidad" value="${p.cantidad}" min="0" step="0.01">
            </td>
            <td>${this.escapePedidosHtml(p.unidad)}</td>
            <td>
                <input type="number" class="vn-ped-product-input vn-ped-precio" value="${p.precio.toFixed(2)}" min="${(p.precioMinimo || 0).toFixed(2)}" step="0.01">
            </td>
            ${descuentoCell}
            <td class="vn-ped-importe fw-bold">${importe.toFixed(2)}</td>                 
            <td>
                <input type="text" class="vn-ped-product-input vn-ped-comentario" value="${this.escapePedidosHtml(p.comentario)}" placeholder="Comentario...">
            </td>
            <td>
                <button type="button" class="vn-ped-btn-delete vn-ped-btn-eliminar">
                    <i class="fas fa-trash"></i>
                </button>
            </td>`;

            fragment.appendChild(tr);
        });

        tbody.appendChild(fragment);
    }
    // Refleja las mismas reglas de ruteo que VNPedidoController.Guardar. Es sólo
    // informativo: el documento real lo decide el backend contra la existencia viva.
    calcularDestino(p) {
        if (p.esTubo) return { texto: 'Tubos', color: '#fd7e14' };

        if (!p.prodModula) return { texto: 'Stock', color: '#0d6efd' };

        const enModula = parseFloat(p.existenciaModula) || 0;
        const enStock = parseFloat(p.existenciaStock) || 0;
        const cantidad = parseFloat(p.cantidad) || 0;

        if (enModula >= cantidad && enModula > 0) return { texto: 'Modula', color: '#6f42c1' };
        if (enModula > 0) return { texto: 'Modula + Stock', color: '#6f42c1' };
        if (enStock > 0) return { texto: 'Stock', color: '#0d6efd' };

        // Sin existencia en ningún lado: se rutea por la bandera del catálogo.
        return { texto: 'Modula', color: '#6f42c1' };
    }

    renderBadgeDestino(p) {
        const destino = this.calcularDestino(p);
        return `<span class="vn-ped-badge-destino" style="
            display:inline-block;margin-left:6px;padding:1px 7px;
            border-radius:10px;font-size:.68rem;font-weight:700;
            text-transform:uppercase;letter-spacing:.3px;
            background:${destino.color};color:#fff;vertical-align:middle;
        " title="Documento en el que se generará esta partida">${destino.texto}</span>`;
    }

    obtenerEstiloExistencia(existencia) {
        const cantidad = parseFloat(existencia) || 0;

        if (cantidad === 0) {
            // Sin existencia - Rojo
            return 'background-color: #dc3545; color: white;';
        } else if (cantidad > 0 && cantidad <= 5) {
            // Existencia baja - Naranja/Amarillo
            return 'background-color: #ffc107; color: #000;';
        } else if (cantidad > 5 && cantidad <= 20) {
            // Existencia media - Azul claro
            return 'background-color: #0dcaf0; color: #000;';
        } else {
            // Existencia alta - Verde
            return 'background-color: #198754; color: white;';
        }
    }
    actualizarPedidosFila(productoId) {
        const producto = this.productos.find(p => p.id === productoId);
        if (!producto) return;

        const tr = this.dom.table.querySelector(`tr[data-producto-id="${productoId}"]`);
        if (!tr) return;

        producto.cantidad = parseFloat(tr.querySelector('.vn-ped-cantidad').value) || 0;
        producto.comentario = tr.querySelector('.vn-ped-comentario').value || '';

        // ── Precio y descuento contra la regla vigente ────────────────────────
        const precioInput = tr.querySelector('.vn-ped-precio');
        if (precioInput) {
            const res = ReglasPrecio.validarPrecio(
                producto,
                parseFloat(precioInput.value) || 0,
                !!vnPedAuth.precioToken);

            producto.precio = res.valor;
            if (res.aviso) precioInput.value = res.valor.toFixed(2);
            ReglasPrecio.avisar(res.aviso);
        }

        const descuentoInput = tr.querySelector('.vn-ped-descuento');
        if (descuentoInput) {
            const res = ReglasPrecio.validarDescuento(
                producto,
                parseFloat(descuentoInput.value) || 0,
                !!vnPedAuth.descuentoToken);

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

        const importe = this.calcularPedidosImporte(producto);
        tr.querySelector('.vn-ped-importe').textContent = importe.toFixed(2);

        // El documento destino depende de la cantidad frente a lo que hay en Modula,
        // así que el badge se recalcula en cada cambio de cantidad.
        const badge = tr.querySelector('.vn-ped-badge-destino');
        if (badge) badge.outerHTML = this.renderBadgeDestino(producto);

        this.calcularPedidosTotales();
    }

    eliminarPedidosProducto(productoId) {
        this.productos = this.productos.filter(p => p.id !== productoId);
        this.renderPedidosTable();
        this.calcularPedidosTotales();
    }

    calcularPedidosImporte(producto) {
        return producto.cantidad * producto.precio * (1 - producto.descuento / 100);
    }

    calcularPedidosTotales() {
        // Detectar tipo de operación (contado, crédito o anticipo)
        const tipoPago = document.getElementById('vn-ped-toggle-pago')?.getAttribute('data-state') || 'contado';
        const flete = parseFloat(document.getElementById('vn-ped-flete-val')?.value) || 0;
        let subtotal1 = 0, descuento = 0, subtotal2 = 0, iva = 0, total = 0;

        if (tipoPago === 'anticipo') {
            // 🔹 Modo ANTICIPO → usar solo el monto ingresado en el campo
            const montoAnticipo = parseFloat(document.getElementById('vn-ped-monto-anticipo')?.value) || 0;

            subtotal1 = montoAnticipo;
            descuento = 0;
            subtotal2 = montoAnticipo;
            iva = montoAnticipo * 0.16; // si aplica IVA al anticipo
            total = subtotal2 + iva;

        } else {
            // 🔹 Modo NORMAL (contado o crédito) → calcular basado en productos
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

        // 🔹 Guardar resultados en la propiedad interna
        this.totales = { subtotal1, descuento, flete, subtotal2, iva, total };

        // 🔹 Actualizar los displays visuales
        this.updatePedidosDisplay(this.dom.subtotal1Display, subtotal1);
        this.updatePedidosDisplay(this.dom.descuentoDisplay, descuento, '-$');
        this.updatePedidosDisplay(this.dom.fleteDisplay, flete);
        this.updatePedidosDisplay(this.dom.subtotal2Display, subtotal2);
        this.updatePedidosDisplay(this.dom.ivaDisplay, iva);
        this.updatePedidosDisplay(this.dom.importeDisplay, total, '$');

        // 🔹 Actualizar inputs ocultos (para enviar al backend)
        this.updatePedidosHiddenField('vn-ped-total-subtotal1', subtotal1);
        this.updatePedidosHiddenField('vn-ped-total-descuento', descuento);
        this.updatePedidosHiddenField('vn-ped-total-flete', flete);
        this.updatePedidosHiddenField('vn-ped-total-subtotal2', subtotal2);
        this.updatePedidosHiddenField('vn-ped-iva', iva);
        this.updatePedidosHiddenField('vn-ped-importe', total);

        window.CreditoVentas?.evaluar('vn-ped');
    }

    updatePedidosHiddenField(id, value) {
        const field = document.getElementById(id);
        if (field) {
            field.value = value.toFixed(2);
        }
    }

    updatePedidosDisplay(element, value, prefix = '$') {
        if (element) {
            element.textContent = `${prefix}${value.toFixed(2)}`;
        }
    }

    escapePedidosHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    getPedidosProductosData() {
        return this.productos.map(p => ({
            ...p,
            importe: this.calcularPedidosImporte(p)
        }));
    }
}

// ============================================
// 4. GESTOR DE FORMULARIO
// ============================================
class PedidosFormManager {
    constructor(productManager) {
        this.productManager = productManager;
        this.form = document.getElementById('vn-ped-formCotizacion');
        this.btnSubmit = document.querySelector('.vn-ped-btn-submit');
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
        const toggle = document.getElementById('vn-ped-toggle-pago');
        const tipoPago = toggle?.getAttribute('data-state') || 'contado';

        const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
        if (token) formData.append('__RequestVerificationToken', token);

        // --- Campos generales (siempre se envían) ---
        const camposGenerales = [
            'vn-ped-tipo-docto-mov',
            'vn-ped-folio', 'vn-ped-documentid', 'vn-ped-cliente',
            'vn-ped-rfc', 'vn-ped-vendedor', 'vn-ped-moneda',
            'vn-ped-paridad', 'vn-ped-concepto',
            'vn-ped-metodo-pago', 'vn-ped-forma-pago',
            'vn-ped-uso-cfdi', 'vn-ped-comentarios'
        ];

        camposGenerales.forEach(id => {
            const el = document.getElementById(id);
            if (el) formData.append(id.replace('vn-ped-', ''), el.value || '');
        });

        // ⭐ AGREGAR ORDEN DE COMPRA AQUÍ ⭐
        formData.append('ordenCompra', document.getElementById('vn-ped-orden-compra-val')?.value || '');
        formData.set('sucursal', suc.getValue());
        formData.set('almacen', alm.getValue());
        // --- Campos condicionales según tipoPago ---
        if (tipoPago === 'credito') {
            formData.append('limiteCredito', document.getElementById('vn-ped-limite-credito')?.value || '');
            formData.append('plazo', document.getElementById('vn-ped-plazo')?.value || '');
            formData.append('fechaPago', document.getElementById('vn-ped-fecha-pago')?.value || '');
        }

        if (tipoPago === 'anticipo') {
            formData.append('fechaAnticipo', document.getElementById('vn-ped-fecha-anticipo')?.value || '');
        }

        // --- Productos ---
        const productos = this.productManager.getPedidosProductosData();
        if (tipoPago !== 'anticipo') {
            formData.append('productosJSON', JSON.stringify(productos));
        }

        // Tokens de autorización: el servidor los necesita para saber si este usuario
        // puede vender por debajo del piso de la regla.
        formData.append('descuentoToken', vnPedAuth.descuentoToken || '');
        formData.append('precioToken', vnPedAuth.precioToken || '');

        // --- Totales ---
        const totales = this.productManager.totales;
        formData.append('subtotal1', totales.subtotal1.toFixed(2));
        formData.append('descuento', totales.descuento.toFixed(2));
        formData.append('flete', totales.flete.toFixed(2));
        formData.append('subtotal2', totales.subtotal2.toFixed(2));
        formData.append('iva', totales.iva.toFixed(2));
        formData.append('total', totales.total.toFixed(2));

        // --- Tipo de pago final ---
        formData.append('tipo', tipoPago);
        // Identifica la autorización de gerencia cuando el documento aún no existe.
        formData.append('creditoToken', window.CreditoVentas?.token('vn-ped') || '');

        return formData;
    }


    validarPedidosFormulario() {
        const errores = [];

        // Obtener el tipo de pago actual desde el toggle
        const togglePago = document.getElementById('vn-ped-toggle-pago');
        const tipoPago = togglePago ? togglePago.getAttribute('data-state') : 'contado'; // valores: contado, credito, anticipo

        // Cliente
        const cliente = document.getElementById('vn-ped-cliente')?.value;
        if (!cliente || cliente.trim() === '') {
            errores.push('Debe seleccionar un cliente');
        }

        // Productos: solo obligatorios si NO es anticipo
        const productos = this.productManager.getPedidosProductosData();
        if (tipoPago !== 'anticipo' && productos.length === 0) {
            errores.push('Debe agregar al menos un producto');
        }

        // Vendedor
        const vendedor = document.getElementById('vn-ped-vendedor')?.value;
        if (!vendedor || vendedor === 'Seleccionar vendedor') {
            errores.push('Debe seleccionar un vendedor');
        }

        // RFC
        const rfcInput = document.getElementById('vn-ped-rfc');
        const rfc = rfcInput ? rfcInput.value.trim() : '';
        if (!rfc) {
            errores.push('Debe ingresar el RFC');
        }

        // Moneda
        const monedaSelect = tomManager.getInstance('vn-ped-moneda');
        const monedaValue = monedaSelect?.getValue();
        if (!monedaValue) {
            errores.push('Debe seleccionar una Moneda');
        }

        // Uso CFDI
        const usoCfdiSelect = tomManager.getInstance('vn-ped-uso-cfdi');
        const usoCfdiValue = usoCfdiSelect?.getValue();
        if (!usoCfdiValue) {
            errores.push('Debe seleccionar un Uso CFDI');
        }

        // Forma de pago
        const formaPagoSelect = tomManager.getInstance('vn-ped-forma-pago');
        const formaPagoValue = formaPagoSelect?.getValue();
        if (!formaPagoValue) {
            errores.push('Debe seleccionar una Forma de Pago');
        }

        // Fecha de pago: solo requerida si es crédito
        const fechaPagoInput = document.getElementById('vn-ped-fecha-pago');
        const fechaPago = fechaPagoInput ? fechaPagoInput.value.trim() : '';

        if (tipoPago === 'credito' && !fechaPago) {
            errores.push('Debe ingresar la Fecha de Pago (solo para crédito)');
        }

        // Crédito: solo agrega errores cuando la venta es a crédito y excede el límite.
        errores.push(...(window.CreditoVentas?.validarAntesDeGuardar('vn-ped') || []));

        return errores;
    }


    obtenerPedidosConfiguracionPorTipo(tipoMovimiento) {
        const configuraciones = {
            'alta': {
                endpoint: '/VNPedido/Guardar',
                metodo: 'POST',
                mensajeExito: 'Pedido creado exitosamente',
                textoCarga: 'Guardando...',
                validarProductos: true
            },
            'modificacion': {
                endpoint: '/VNPedido/Modificar',
                metodo: 'PUT',
                mensajeExito: 'Pedido modificado exitosamente',
                textoCarga: 'Actualizando...',
                validarProductos: true
            },
            'consulta': {
                endpoint: '/VNPedido/Consultar',
                metodo: 'GET',
                mensajeExito: 'Consulta realizada exitosamente',
                textoCarga: 'Consultando...',
                validarProductos: false
            }
        };

        return configuraciones[tipoMovimiento] || configuraciones['alta'];
    }

    async enviarPedidosCotizacion() {
        const tipoMovimiento = document.getElementById('vn-ped-tipo-docto-mov')?.value || 'alta';
        const config = this.obtenerPedidosConfiguracionPorTipo(tipoMovimiento);

        const errores = this.validarPedidosFormulario();
        if (errores.length > 0) {
            if (window.toastMixin) {
                toastMixin.fire({ icon: 'error', title: errores.join(', ') });
            }
            return;
        }

        const originalText = this.btnSubmit.innerHTML;
        this.btnSubmit.disabled = true;
        this.btnSubmit.innerHTML = `<i class="fas fa-spinner fa-spin"></i> ${config.textoCarga}`;

        try {
            const formData = this.recopilarPedidosDatosFormulario();
            const fetchConfig = {
                method: config.metodo.toUpperCase(),
                body: formData
            };

            const response = await fetch(config.endpoint, fetchConfig);
            const result = await response.json();

            if (result.success) {
                if (window.Swal) {
                    await Swal.fire({
                        icon: 'success',
                        title: '¡Pedido generado exitosamente!',
                        html: this.construirPedidosResumenExito(result),
                        confirmButtonText: 'Aceptar',
                        confirmButtonColor: '#198754'
                    });
                    location.reload();
                }
                console.log('Documentos generados:', result.documentos || result.folio_generado);
            }
            else {
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

        document.getElementById('vn-ped-cliente').value = '';
        document.getElementById('vn-ped-rfc').value = '';
        document.getElementById('vn-ped-info-proveedor').value = '';
    }

    verPedidosDatosFormulario() {
        const formData = this.recopilarPedidosDatosFormulario();
        const obj = {};
        for (let [key, value] of formData.entries()) {
            obj[key] = value;
        }
        console.log('Datos de pedido a enviar:', obj);
        return obj;
    }

    obtenerPedidosEtiquetaTipoDocumento(tipo) {
        const etiquetas = {
            'VNPED': {
                nombre: 'Pedido Nacional',
                icono: 'fa-box',
                color: '#0d6efd'
            },
            'TYBCOT': {
                nombre: 'Tubos y Barras',
                icono: 'fa-ruler-horizontal',
                color: '#fd7e14'
            },
            'MODPED': {
                nombre: 'Pedido Modula',
                icono: 'fa-cubes',
                color: '#6f42c1'
            }
        };
        return etiquetas[tipo] || { nombre: tipo, icono: 'fa-file-invoice', color: '#6c757d' };
    }

    construirPedidosResumenExito(result) {
        const documentos = result.documentos || [];

        if (documentos.length === 0) {
            return `<p style="margin:0;">Folio: <strong>${result.folio_generado || ''}</strong></p>`;
        }

        const tarjetas = documentos.map(doc => {
            const etiqueta = this.obtenerPedidosEtiquetaTipoDocumento(doc.tipo);
            return `
            <div style="
                display:flex;align-items:center;gap:12px;
                border:1px solid #e9ecef;border-radius:10px;
                padding:.75rem 1rem;margin-bottom:.6rem;
                background:#f8f9fa;text-align:left;
            ">
                <div style="
                    width:40px;height:40px;border-radius:8px;
                    background:${etiqueta.color};color:#fff;
                    display:flex;align-items:center;justify-content:center;
                    flex-shrink:0;font-size:1.1rem;
                ">
                    <i class="fas ${etiqueta.icono}"></i>
                </div>
                <div>
                    <div style="font-size:.75rem;font-weight:700;text-transform:uppercase;letter-spacing:.5px;color:${etiqueta.color};">
                        ${etiqueta.nombre}
                    </div>
                    <div style="font-size:.95rem;font-weight:600;color:#212529;font-family:'Courier New',monospace;">
                        ${doc.folio}
                    </div>
                </div>
            </div>`;
        }).join('');

        let encabezado = '';
        if (documentos.length > 1) {
            const nombres = documentos.map(d => this.obtenerPedidosEtiquetaTipoDocumento(d.tipo).nombre);
            encabezado = `<p style="margin:0 0 1rem;color:#6c757d;font-size:.9rem;">
                Se generaron <strong>${documentos.length} documentos</strong> (${nombres.join(', ')}) según el tipo de artículos del pedido.
            </p>`;
        }

        return `<div style="text-align:left;">${encabezado}${tarjetas}</div>`;
    }
}

// ============================================
// 5. APLICACIÓN PRINCIPAL
// ============================================
// Tokens de autorización del usuario. Nacionales no tiene candado por contraseña sobre
// los campos: con esto, quien tenga permiso directo puede bajar del piso y el resto no.
const vnPedAuth = { precioToken: null, descuentoToken: null };

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
    clienteCveResuelta: '',

    /**
     * Devuelve el id del cliente del documento, resolviéndolo desde la clave si hace falta.
     * En un pedido el cliente casi siempre llega al cargar la cotización de origen, y por
     * esa vía solo se conoce la clave.
     */
    async asegurarClienteId() {
        const cve = document.getElementById('vn-ped-cliente')?.value || '';

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

    initPedidos() {
        this.form = document.getElementById('vn-ped-formCotizacion');
        this.btnSubmit = document.querySelector('.vn-ped-btn-submit');

        ReglasPrecio.cargarPermisos().then(t => Object.assign(vnPedAuth, t));

        // Crédito: en el pedido ya se compromete la línea, así que bloquea.
        window.CreditoVentas?.registrar({
            prefix: 'vn-ped',
            modo: 'bloquear',
            documento: 'pedido',
            claseBanner: 'vn-credit-banner',
            endpointSolicitud: '/VNPedido/EnviarSolicitudGerente',
            getTipoPago: () => document.getElementById('vn-ped-tipo-pago')?.value || 'contado',
            totalId: 'vn-ped-importe',
            submitSelector: '.vn-ped-btn-submit',
            clienteInputId: 'vn-ped-cliente',
            folioId: 'vn-ped-folio',
            documentIdId: 'vn-ped-documentid',
            getProductos: () => PedidosApp.productManagerInstance?.getPedidosProductosData() || [],
            getTotales: () => PedidosApp.productManagerInstance?.totales || {},
            // El pedido se genera junto con la solicitud, en estatus pendiente: el reparto
            // Stock/Modula/Tubos depende de las existencias de ahora, así que se calcula una
            // sola vez y con el formulario completo. Aprobar solo activa los documentos.
            getFormData: () => PedidosApp.formManager?.recopilarPedidosDatosFormulario()
        });

        // Inicializar gestor de clientes
        this.clienteManager = new PedidosDataManager({
            endpoint: '/DatosGenerales/BuscarC',
            detailEndpoint: '/DatosGenerales/BuscarCliente',
            modalId: 'vn-ped-modalBuscarCliente',
            inputId: 'vn-ped-inputBuscarCliente',
            resultsId: 'vn-ped-listaResultadosClientes',
            pageSizeId: 'vn-ped-pageSizeClientes',
            btnClearId: 'vn-ped-btnLimpiarClientes',
            spinnerId: 'vn-ped-spinnerClientes',
            paginationId: 'vn-ped-paginationClientes',
            recordsFromId: 'vn-ped-recordsFromClientes',
            recordsToId: 'vn-ped-recordsToClientes',
            totalRecordsId: 'vn-ped-totalRecordsClientes',
            shouldCloseOnSelect: true
        });

        this.clienteManager.onPedidosSelect = (cliente) => {
            document.getElementById('vn-ped-cliente').value = cliente.id || '';
            document.getElementById('vn-ped-rfc').value = cliente.rfc || '';
            document.getElementById('vn-ped-info-proveedor').value =
                [cliente.dir, cliente.col, cliente.pob, cliente.cp]
                    .filter(x => x)
                    .join(',\n');

            const vendedorInstance = tomManager.getInstance('vn-ped-vendedor');
            if (vendedorInstance && cliente.cve_vdr) {
                vendedorInstance.setValue(cliente.cve_vdr, true);
            }
            const fpagoInstance = tomManager.getInstance('vn-ped-forma-pago');
            if (fpagoInstance && cliente.forma_pago) {
                fpagoInstance.setValue(cliente.forma_pago, true);
            }
            const cfdiInstance = tomManager.getInstance('vn-ped-uso-cfdi');
            if (cfdiInstance && cliente.uso_sugerido) {
                cfdiInstance.setValue(cliente.uso_sugerido, true);
            }
            window.CreditoVentas?.setCliente('vn-ped', cliente);

            // El cliente define la lista de precios y las reglas por cliente: se manda
            // explícito al buscar productos y se recalcula lo ya capturado.
            PedidosApp.clienteIdActual = parseInt(cliente.id_cliente, 10) || 0;
            PedidosApp.clienteCveResuelta = cliente.id || '';
            PedidosApp.recotizarPartidas(cliente.descripcion);
        };

        // Inicializar gestor de productos
        this.productoManager = new PedidosDataManager({
            endpoint: '/DatosGenerales/BuscarP',
            detailEndpoint: '/DatosGenerales/BuscarProducto',
            modalId: 'vn-ped-modalBuscarProducto',
            inputId: 'vn-ped-inputBuscarProducto',
            resultsId: 'vn-ped-listaResultadosProductos',
            pageSizeId: 'vn-ped-pageSizeProductos',
            btnClearId: 'vn-ped-btnLimpiarProductos',
            spinnerId: 'vn-ped-spinnerProductos',
            paginationId: 'vn-ped-paginationProductos',
            recordsFromId: 'vn-ped-recordsFromProductos',
            recordsToId: 'vn-ped-recordsToProductos',
            totalRecordsId: 'vn-ped-totalRecordsProductos',
            shouldCloseOnSelect: false
        });

        // Instancia de ProductManager
        this.productManagerInstance = new PedidosProductManager();

        this.productoManager.getExtraParams = async () => ({ clienteId: await this.asegurarClienteId() });
        this.productoManager.onPedidosSelect = (producto) => {
            this.productManagerInstance.agregarPedidosProducto(producto);
        };

        // Inicializar gestor de documentos
        this.documentoManager = new PedidosDocumentoManager({
            endpoint: '/DatosGenerales/BuscarD',
            detailEndpoint: '/DatosGenerales/BuscarDocumento',
            modalId: 'vn-ped-modalBuscarDocumentos',
            inputId: 'vn-ped-inputBuscarDocumento',
            resultsId: 'vn-ped-listaResultadosDocumentos',
            pageSizeId: 'vn-ped-pageSizeDocumentos',
            btnClearId: 'vn-ped-btnLimpiarDocumentos',
            spinnerId: 'vn-ped-spinnerDocumentos',
            paginationId: 'vn-ped-paginationDocumentos',
            recordsFromId: 'vn-ped-recordsFromDocumentos',
            recordsToId: 'vn-ped-recordsToDocumentos',
            totalRecordsId: 'vn-ped-totalRecordsDocumentos',
            shouldCloseOnSelect: true,
            filtros: {}
        });

        // Configurar filtros de documentos
        const filtroEstado = document.getElementById('vn-ped-filtroEstado');
        const filtroFecha = document.getElementById('vn-ped-filtroFecha');

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
        this.formManager = new PedidosFormManager(this.productManagerInstance);

        // Observar cambios en tipo de movimiento
        const tipoMovSelect = document.getElementById('vn-ped-tipo-docto-mov');
        tipoMovSelect?.addEventListener('change', (e) => {
            this.manejarPedidosCambioTipoMovimiento(e.target.value);
        });

        this.manejarPedidosCambioTipoMovimiento(tipoMovSelect?.value || 'alta');

        console.log('Sistema de pedidos inicializado correctamente');

        window.verPedidosDatosFormulario = () => this.formManager.verPedidosDatosFormulario();

        //if (typeof rol !== undefined && rol !== 'Gerente') {
        //    this.deshabilitarPedidosFormulario()
        //} else {
        //    this.habilitarPedidosFormulario()
        //}
    },

    async cargarDocumentoCompleto(documento) {
        // 1. Información general
        //document.getElementById('vn-ped-sucursal').value = documento.sucursal || '';
        document.getElementById('vn-ped-almacen').value = documento.almacen || '';
        document.getElementById('vn-ped-documentid').value = documento.id_encabezado || '';

        // 2. Información del cliente
        document.getElementById('vn-ped-cliente').value = documento.cli_prov || '';
        document.getElementById('vn-ped-rfc').value = documento.rfc || '';
        document.getElementById('vn-ped-info-proveedor').value = documento.info_cli || '';
        document.getElementById('vn-ped-paridad').value = documento.par || '';

        // 3. Vendedor
        const vendedorInstance = tomManager.getInstance('vn-ped-vendedor');
        if (vendedorInstance && (documento.vdr_cpr || documento.cve_vdr)) {
            vendedorInstance.setValue(documento.vdr_cpr || documento.cve_vdr, true);
        }

        // 4. Moneda
        const monedaInstance = tomManager.getInstance('vn-ped-moneda');
        if (monedaInstance && (documento.ccy || documento.moneda)) {
            monedaInstance.setValue(documento.ccy || documento.moneda, true);
        }

        // 5. Uso CFDI
        const cfdiInstance = tomManager.getInstance('vn-ped-uso-cfdi');
        if (cfdiInstance && (documento.usoCfdi || documento.uso_cfdi || documento.uso_sugerido)) {
            cfdiInstance.setValue(documento.usoCfdi || documento.uso_cfdi || documento.uso_sugerido, true);
        }

        // 6. Forma de pago
        const formaPagoInstance = tomManager.getInstance('vn-ped-forma-pago');
        if (formaPagoInstance && (documento.formaPago || documento.forma_pago)) {
            formaPagoInstance.setValue(documento.formaPago || documento.forma_pago, true);
        }

        // ✅ NUEVO: Método de pago
        const metodoPagoInstance = tomManager.getInstance('vn-ped-metodo-pago');
        if (metodoPagoInstance && (documento.metodoPago || documento.metodo_pago)) {
            metodoPagoInstance.setValue(documento.metodoPago || documento.metodo_pago, true);
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

            const metodoPagoEl = document.getElementById('vn-ped-metodo-pago');
            if (metodoPagoEl) {
                metodoPagoEl.value = mdpDoc || (tipoPagoDoc === 'credito' ? 'PPD' : 'PUE');
            }
            selectTipoPagoPed(tipoPagoDoc);
        }

        // 7. Condiciones de pago
        // Una cotización no las trae (se capturan a partir del pedido), pero al recargar un
        // pedido ya guardado sí, y en ese caso mandan las suyas sobre el plazo del catálogo.
        document.getElementById('vn-ped-limite-credito').value = documento.lim_crd || '';
        document.getElementById('vn-ped-plazo').value =
            parseInt(documento.pl_dias) > 0 ? documento.pl_dias : (documento.pl_crd || '');
        document.getElementById('vn-ped-fecha-pago').value =
            documento.fecha_pago || documento.fechaPago || '';
        document.getElementById('vn-ped-concepto').value = documento.coment1 || '';
        document.getElementById('vn-ped-comentarios').value = documento.coment_aut || '';

        // 8. Cargar productos
        if (documento.productos && Array.isArray(documento.productos)) {
            this.productManagerInstance.productos = [];

            documento.productos.forEach(prod => {
                const productoData = {
                    id: `prod_${Date.now()}_${Math.random()}`,
                    productoId: prod.producto_id || prod.id,
                    descripcion: prod.descripcion,
                    existencia: parseFloat(prod.existencia) || 0,
                    cantidad: parseFloat(prod.cantidad) || 0,
                    precio: parseFloat(prod.precio) || 0,
                    // Referencia del precio pactado en el documento de origen: sin ella no
                    // hay contra qué comparar para saber si se modificó a mano.
                    precioOriginal: parseFloat(prod.precio) || 0,
                    descuento: parseFloat(prod.descuento) || 0,
                    unidad: prod.unidad || prod.udm || 'PZA',
                    existenciaStock: parseFloat(prod.existencia_stock) || 0,
                    existenciaModula: parseFloat(prod.existencia_modula) || 0,
                    esTubo: prod.es_tubo === true,
                    prodModula: prod.prod_modula === true,
                    comentario: prod.comentario || ''
                };
                this.productManagerInstance.productos.push(productoData);
            });

            this.productManagerInstance.renderPedidosTable();

            // El documento trae el precio pactado pero no los límites de la regla. Se
            // consultan aparte para que el pedido conozca su piso y su tope sin
            // re-precificar lo que ya se cotizó.
            PedidosApp.asegurarClienteId()
                .then(clienteId => ReglasPrecio.hidratarLimites(
                    this.productManagerInstance.productos,
                    clienteId,
                    '/DatosGenerales/BuscarProducto'))
                .then(n => { if (n > 0) this.productManagerInstance.renderPedidosTable(); });
        }

        // 9. Cargar flete y recalcular totales
        const fleteInput = document.getElementById('vn-ped-flete-val');
        if (fleteInput && documento.flete) {
            const chkFlete = document.getElementById("vn-ped-flete");
            chkFlete.checked = true;
            chkFlete.dispatchEvent(new Event("change"));
            fleteInput.value = documento.flete;
        }

        // ⭐ AGREGAR ORDEN DE COMPRA AQUÍ ⭐
        const ordenCompraInput = document.getElementById('vn-ped-orden-compra-val');
        if (ordenCompraInput && documento.ordencompra) {
            const chkOrdenCompra = document.getElementById("vn-ped-orden-compra-check");
            chkOrdenCompra.checked = true;
            chkOrdenCompra.dispatchEvent(new Event("change"));
            ordenCompraInput.value = documento.ordencompra;
        }

        this.productManagerInstance.calcularPedidosTotales();

        // 10. Cambiar tipo de movimiento a modificación
        const tipoMovSelect = document.getElementById('vn-ped-tipo-docto-mov');
        if (tipoMovSelect && documento.folio) {
            tipoMovSelect.value = 'modificacion';
            this.manejarPedidosCambioTipoMovimiento('modificacion');
        }

        // Crédito del cliente que viene con el documento (solo se usa si es a crédito).
        if (documento.estatus_credito) {
            window.CreditoVentas?.setCliente('vn-ped', {
                id: documento.cli_prov,
                lim_crd: documento.lim_crd || 0,
                credito_usado: documento.credito_usado || 0,
                credito_disponible: documento.credito_disponible || 0,
                estatus_cliente: documento.estatus_cliente
            });
        }

        // Si este documento ya fue autorizado por gerencia, no se vuelve a bloquear.
        window.CreditoVentas?.refrescarAutorizacion('vn-ped');
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

        // Buscar solo dentro del pedido
        const formElements = pedidoContenedor.querySelectorAll('input, select, textarea, button');
        formElements.forEach(element => {
            if (element.id !== 'vn-ped-tipo-docto-mov') {
                element.disabled = true;
                element.classList.add('vn-ped-disabled-field');
            }
        });

        // Botones específicos del pedido
        const btnBuscarCliente = pedidoContenedor.querySelector('[data-bs-target="#vn-ped-modalBuscarCliente"]');
        const btnBuscarProducto = pedidoContenedor.querySelector('[data-bs-target="#vn-ped-modalBuscarProducto"]');
        if (btnBuscarCliente) btnBuscarCliente.disabled = true;
        if (btnBuscarProducto) btnBuscarProducto.disabled = true;

        const botonesEliminar = pedidoContenedor.querySelectorAll('.vn-ped-btn-eliminar');
        botonesEliminar.forEach(btn => btn.disabled = true);

        // Deshabilitar TOM select específicos
        if (window.tomManager) {
            ['vn-ped-vendedor', 'vn-ped-moneda', 'vn-ped-uso-cfdi', 'vn-ped-forma-pago'].forEach(id => {
                const instance = window.tomManager.instances?.get(id);
                if (instance) instance.disable();
            });
        }

        // Botón submit del pedido
        const btn = pedidoContenedor.querySelector('.vn-ped-btn-submit');
        if (btn) {
            btn.disabled = false;
        }
        const btnDocConsulta = pedidoContenedor.querySelector('.vn-ped-floating-btn');
        if (btnDocConsulta) {
            btnDocConsulta.disabled = false;
        }
        const btnPreview = pedidoContenedor.querySelector('.vn-ped-ticket-btn');
        if (btnPreview) {
            btnPreview.disabled = false;
        }
    },

    habilitarPedidosFormulario() {
        const pedidoContenedor = document.getElementById('pedido-contenedor');
        if (!pedidoContenedor) return;

        const formElements = pedidoContenedor.querySelectorAll('input, select, textarea, button');
        formElements.forEach(element => {
            element.disabled = false;
            element.classList.remove('vn-ped-disabled-field');
        });

        const btnBuscarCliente = pedidoContenedor.querySelector('[data-bs-target="#vn-ped-modalBuscarCliente"]');
        const btnBuscarProducto = pedidoContenedor.querySelector('[data-bs-target="#vn-ped-modalBuscarProducto"]');
        if (btnBuscarCliente) btnBuscarCliente.disabled = false;
        if (btnBuscarProducto) btnBuscarProducto.disabled = false;

        if (window.tomManager) {
            ['vn-ped-vendedor', 'vn-ped-moneda', 'vn-ped-uso-cfdi', 'vn-ped-forma-pago'].forEach(id => {
                const instance = window.tomManager.instances?.get(id);
                if (instance) instance.enable();
            });
        }

        const tipoMovimiento = document.getElementById('vn-ped-tipo-docto-mov')?.value;
        const btn = pedidoContenedor.querySelector('.vn-ped-btn-submit');
        if (btn) {
            if (tipoMovimiento === 'modificacion') {
                btn.innerHTML = '<i class="fas fa-save"></i> Actualizar Pedido';
            } else {
                btn.innerHTML = '<i class="fas fa-save"></i> Guardar Pedido';
            }
        }
    }

};

// ============================================
// 6. FUNCIONES DE AUTENTICACIÓN
// ============================================
async function handlePedidosDescuentoAuth(e) {
    if (!e.target.classList.contains('vn-ped-descuento')) return;
    await pedirPedidosAutenticacion(e.target);
}

async function handlePedidosDescuentoF8(e) {
    if (e.key !== 'F8') return;

    const input = document.activeElement;
    if (!input.classList.contains('vn-ped-descuento')) return;

    e.preventDefault();
    await pedirPedidosAutenticacion(input);
}

async function pedirPedidosAutenticacion(input) {
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
        // Buscar el formulario de varias formas
        let form = document.getElementById('form-2')
            || document.querySelector('.form-2')
            || document.querySelector('form[id="form-2"]');

        if (!form) {
            console.error('No se pudo encontrar el formulario');
            return;
        }

        // Deshabilitar todos los campos
        const elementos = form.querySelectorAll('input:not([type="hidden"]), select, textarea');
        elementos.forEach(el => el.disabled = true);

        // Deshabilitar botones
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
        PedidosApp.initPedidos();
        document.addEventListener('dblclick', handlePedidosDescuentoAuth);
        document.addEventListener('keydown', handlePedidosDescuentoF8);
        const fleteInput = document.getElementById('vn-ped-flete-val');
        if (!fleteInput) return;

        // Detecta cambio manual o con teclado
        fleteInput.addEventListener('input', function () {
            //this.calcularTotales();
        });

        // Detecta cuando el usuario sale del campo (por si pega un valor)
        fleteInput.addEventListener('change', function () {
            //this.calcularTotales();
        });

    });
} else {
    PedidosApp.initPedidos();
    document.addEventListener('dblclick', handlePedidosDescuentoAuth);
    document.addEventListener('keydown', handlePedidosDescuentoF8);
}
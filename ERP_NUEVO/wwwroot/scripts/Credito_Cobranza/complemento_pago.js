// ============================================
// 1. CLASE BASE PARA GESTIÓN DE DATOS
// ============================================
class FacturasDataManager {
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
        this.initFacturasDataManager();
    }

    initFacturasDataManager() {
        this.dom.modal?.addEventListener('show.bs.modal', () => this.onFacturasModalShow());
        this.dom.input?.addEventListener('input', (e) => this.handleFacturasSearch(e.target.value));
        this.dom.pageSize?.addEventListener('change', (e) => this.handleFacturasPageSizeChange(e.target.value));
        this.dom.btnClear?.addEventListener('click', () => this.handleFacturasClear());

        this.dom.results?.addEventListener('click', (e) => {
            const btn = e.target.closest('[data-select-id]');
            if (btn) {
                const id = btn.dataset.selectId;
                this.handleFacturasSelect(id);
            }
        });

        this.dom.pagination?.addEventListener('click', (e) => {
            e.preventDefault();
            const btn = e.target.closest('[data-page]');
            if (btn && !btn.closest('.disabled')) {
                this.state.page = parseInt(btn.dataset.page);
                this.fetchFacturasData();
            }
        });
    }

    onFacturasModalShow() {
        this.handleFacturasClear();
        this.fetchFacturasData();
    }

    handleFacturasSearch(value) {
        clearTimeout(this.typingTimer);
        this.typingTimer = setTimeout(() => {
            this.state.lastSearch = value;
            this.state.page = 1;
            this.fetchFacturasData();
        }, 300);
    }

    handleFacturasPageSizeChange(value) {
        this.state.pageSize = parseInt(value);
        this.state.page = 1;
        this.fetchFacturasData();
    }

    handleFacturasClear() {
        this.dom.input.value = '';
        this.state.lastSearch = '';
        this.state.page = 1;
        this.fetchFacturasData();
    }

    getFacturasCachedData(key) {
        const cached = this.cache.get(key);
        if (cached && Date.now() - cached.timestamp < this.cacheTimeout) {
            return cached.data;
        }
        return null;
    }

    setFacturasCachedData(key, data) {
        this.cache.set(key, { data, timestamp: Date.now() });
    }

    clearFacturasCache() {
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

    async fetchFacturasData() {
        if (this.state.loading) {
            this.abortController?.abort();
        }

        const cacheKey = this.buildCacheKey();
        const cached = this.getFacturasCachedData(cacheKey);

        if (cached) {
            this.renderFacturasResults(cached);
            return;
        }

        this.state.loading = true;
        this.showFacturasSpinner(true);
        this.abortController = new AbortController();

        try {
            const url = this.buildUrl();
            const response = await fetch(url, { signal: this.abortController.signal });

            if (!response.ok) throw new Error('Error en la respuesta');

            const data = await response.json();
            this.state.totalRecords = data.total || 0;

            const results = { items: data.items || data, total: this.state.totalRecords };
            this.setFacturasCachedData(cacheKey, results);
            this.renderFacturasResults(results);

        } catch (error) {
            if (error.name !== 'AbortError') {
                console.error('Error fetching facturas data:', error);
                this.renderFacturasError();
            }
        } finally {
            this.state.loading = false;
            this.showFacturasSpinner(false);
        }
    }

    showFacturasSpinner(show) {
        this.dom.spinner?.classList.toggle('d-none', !show);
    }

    renderFacturasResults(data) {
        const items = data.items || [];

        if (items.length === 0) {
            this.dom.results.innerHTML = `
                <li class="list-group-item text-center text-muted py-5">
                    <i class="fas fa-search fa-3x mb-3 opacity-25"></i>
                    <p>No se encontraron resultados</p>
                </li>`;
            this.updateFacturasCounters();
            return;
        }

        const fragment = document.createDocumentFragment();

        items.forEach(item => {
            const li = document.createElement('li');
            li.className = 'list-group-item d-flex justify-content-between align-items-center hover-shadow';
            li.innerHTML = `
                <div>
                    <h6 class="mb-1 fw-semibold text-primary">${this.escapeFacturasHtml(item.descripcion)}</h6>
                    <small class="text-muted">ID: ${this.escapeFacturasHtml(item.id)}</small>
                </div>
                <button type="button" class="btn btn-sm btn-primary" data-select-id="${this.escapeFacturasHtml(item.id)}">
                    <i class="fas fa-check me-1"></i>Seleccionar
                </button>`;
            fragment.appendChild(li);
        });

        this.dom.results.innerHTML = '';
        this.dom.results.appendChild(fragment);

        this.updateFacturasCounters();
        this.renderFacturasPagination();
    }

    renderFacturasError() {
        this.dom.results.innerHTML = `
            <li class="list-group-item text-center text-danger py-5">
                <i class="fas fa-exclamation-triangle fa-3x mb-3"></i>
                <p>Error al cargar los datos</p>
            </li>`;
    }

    updateFacturasCounters() {
        const from = (this.state.page - 1) * this.state.pageSize + 1;
        const to = Math.min(this.state.page * this.state.pageSize, this.state.totalRecords);

        if (this.dom.recordsFrom) this.dom.recordsFrom.textContent = from;
        if (this.dom.recordsTo) this.dom.recordsTo.textContent = to;
        if (this.dom.totalRecords) this.dom.totalRecords.textContent = this.state.totalRecords;
    }

    renderFacturasPagination() {
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

        const prevLi = this.createFacturasPageButton('Anterior', this.state.page - 1, this.state.page === 1);
        fragment.appendChild(prevLi);

        for (let i = startPage; i <= endPage; i++) {
            const li = this.createFacturasPageButton(i, i, false, i === this.state.page);
            fragment.appendChild(li);
        }

        const nextLi = this.createFacturasPageButton('Siguiente', this.state.page + 1, this.state.page === totalPages);
        fragment.appendChild(nextLi);

        this.dom.pagination.innerHTML = '';
        this.dom.pagination.appendChild(fragment);
    }

    createFacturasPageButton(text, page, disabled = false, active = false) {
        const li = document.createElement('li');
        li.className = `page-item${disabled ? ' disabled' : ''}${active ? ' active' : ''}`;
        li.innerHTML = `<button type="button" class="page-link" data-page="${page}">${text}</button>`;
        return li;
    }

    async handleFacturasSelect(id) {
        try {
            const response = await fetch(`${this.detailEndpoint}?id=${encodeURIComponent(id)}`);
            if (!response.ok) throw new Error('Error al obtener detalles');

            const data = await response.json();
            if (data && data[0]) {
                this.onFacturasSelect(data[0]);
                if (this.shouldCloseOnSelect) {
                    bootstrap.Modal.getInstance(this.dom.modal)?.hide();
                }
            }
        } catch (error) {
            console.error('Error selecting facturas item:', error);
            alert('Error al seleccionar el elemento');
        }
    }

    onFacturasSelect(item) {
        console.log('Facturas item selected:', item);
    }

    escapeFacturasHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }
}

// ============================================
// 2. CLASE EXTENDIDA PARA DOCUMENTOS
// ============================================
class FacturasDocumentoManager extends FacturasDataManager {
    renderFacturasResults(data) {
        const items = data.items || [];

        if (items.length === 0) {
            this.dom.results.innerHTML = `
                <li class="list-group-item text-center text-muted py-5">
                    <i class="fas fa-search fa-3x mb-3 opacity-25"></i>
                    <p>No se encontraron documentos</p>
                </li>`;
            this.updateFacturasCounters();
            return;
        }

        const fragment = document.createDocumentFragment();

        items.forEach(item => {
            const li = document.createElement('li');
            li.className = 'list-group-item';
            li.dataset.folio = item.folio;

            li.innerHTML = `
                <div class="d-flex justify-content-between align-items-start">
                    <div class="flex-grow-1">
                        <h6 class="mb-1">
                            <i class="fas fa-file-invoice text-primary me-2"></i>
                            <strong>Folio:</strong> ${this.escapeFacturasHtml(item.folio || '')}
                        </h6>
                        <p class="mb-1 text-muted">
                            <i class="fas fa-user me-2"></i>
                            <strong>Cliente:</strong> ${this.escapeFacturasHtml(item.cli_prov || '')}
                        </p>
                        <div class="d-flex gap-3 small text-muted">
                            <span><i class="fas fa-calendar me-1"></i> ${this.escapeFacturasHtml(item.fecha || '')}</span>
                            <span><i class="fas fa-money-bill-wave me-1"></i> $${parseFloat(item.imp || 0).toFixed(2)}</span>
                            <span><i class="fas fa-user-tie me-1"></i> ${this.escapeFacturasHtml(item.usr0 || '')}</span>
                        </div>
                    </div>
                    <div class="d-flex flex-column gap-2 align-items-end">
                        <button type="button" class="btn btn-sm btn-primary" data-select-id="${this.escapeFacturasHtml(item.id_encabezado)}">
                            <i class="fas fa-check me-1"></i>Cargar
                        </button>
                    </div>
                </div>`;
            fragment.appendChild(li);
        });

        this.dom.results.innerHTML = '';
        this.dom.results.appendChild(fragment);

        this.updateFacturasCounters();
        this.renderFacturasPagination();
    }
}

// ============================================
// 3. GESTOR DE COMPLEMENTOS DE PAGO
// ============================================
class ComplementoPagoManager {
    constructor() {
        this.facturas = [];
        this.totalComplemento = 0;

        this.dom = {
            table: document.getElementById('vi-fac-product-table'),
            tbody: document.getElementById('vi-fac-documentosTable'),

            // Displays del sidebar
            totalFacturasDisplay: document.getElementById('vi-fac-total-facturas-display'),
            saldoPendienteDisplay: document.getElementById('vi-fac-saldo-pendiente-display'),
            montoPagarDisplay: document.getElementById('vi-fac-monto-pagar-display'),
            saldoRestanteDisplay: document.getElementById('vi-fac-saldo-restante-display'),
            totalComplementoDisplay: document.getElementById('vi-fac-total-complemento-display'),

            // Botón auto-llenar y su contenedor
            containerAutoLlenar: document.getElementById('vi-fac-container-autollenar'),
            btnAutoLlenar: document.getElementById('vi-fac-btn-autollenar')
        };

        this.initComplementoPagoManager();
    }

    initComplementoPagoManager() {
        // Evento: cuando el usuario escribe cuánto quiere pagar
        this.dom.tbody?.addEventListener('input', (e) => {
            const input = e.target;
            if (input.classList.contains('cp-importe-pagado')) {
                const tr = input.closest('tr');
                const facturaId = tr?.dataset.facturaId;
                if (facturaId) this.actualizarFactura(facturaId);
            }
        });

        // Evento eliminar factura
        this.dom.tbody?.addEventListener('click', (e) => {
            const btn = e.target.closest('.cp-btn-eliminar');
            if (btn) {
                const tr = btn.closest('tr');
                const facturaId = tr.dataset.facturaId;
                if (facturaId) this.eliminarFactura(facturaId);
            }
        });

        // Evento auto-llenar
        this.dom.btnAutoLlenar?.addEventListener('click', () => {
            this.autoLlenarTodos();
        });
    }

    // ===============================
    // MOSTRAR/OCULTAR BOTÓN AUTO-LLENAR
    // ===============================
    toggleBotonAutoLlenar() {
        if (!this.dom.containerAutoLlenar) return;

        if (this.facturas.length > 0) {
            // Mostrar botón con animación suave
            this.dom.containerAutoLlenar.classList.remove('d-none');
            // Pequeño delay para la animación
            setTimeout(() => {
                this.dom.containerAutoLlenar.style.opacity = '1';
            }, 10);
        } else {
            // Ocultar botón
            this.dom.containerAutoLlenar.style.opacity = '0';
            setTimeout(() => {
                this.dom.containerAutoLlenar.classList.add('d-none');
            }, 300);
        }
    }

    // ===============================
    // AUTO-LLENAR TODOS LOS INPUTS
    // ===============================
    autoLlenarTodos() {
        if (this.facturas.length === 0) {
            toastMixin?.fire({
                icon: 'warning',
                title: 'No hay facturas para auto-llenar'
            });
            return;
        }

        // Auto-llenar cada factura con su saldo anterior
        this.facturas.forEach(factura => {
            factura.importePagado = factura.saldoAnterior;
            factura.saldoInsoluto = 0;
        });

        // Re-renderizar la tabla
        this.renderFacturasTable();

        // Actualizar totales
        this.calcularTotalComplemento();

        toastMixin?.fire({
            icon: 'success',
            title: 'Todas las facturas han sido llenadas al máximo'
        });
    }

    // ===============================
    // AGREGAR FACTURA
    // ===============================
    agregarFactura(factura) {
        const existe = this.facturas.find(f => f.id_encabezado === factura.id_encabezado);
        if (existe) {
            toastMixin?.fire({ icon: "warning", title: "Esta factura ya fue agregada" });
            return;
        }

        const facturaData = {
            id: `fac_${Date.now()}_${Math.random()}`,
            id_encabezado: factura.id_encabezado,
            serie: factura.serie || '',
            folio: factura.folio || '',
            uuid: factura.uuid || '',
            fecha: dateFormatter(factura.fch0) || '',
            total: parseFloat(factura.monto_total) || 0,
            saldoAnterior: parseFloat(factura.saldo_pendiente_real) || 0,
            importePagado: 0,
            saldoInsoluto: parseFloat(factura.saldo_pendiente_real) || 0,
            moneda: factura.moneda || 'MXN',
            metodoPago: factura.metodo_pago || 'PPD'
        };

        this.facturas.push(facturaData);
        this.renderFacturasTable();
        this.calcularTotalComplemento();
        this.toggleBotonAutoLlenar(); // ⭐ MOSTRAR BOTÓN

        toastMixin?.fire({
            icon: "success",
            title: `Factura agregada: ${factura.folio}`
        });
    }

    // ===============================
    // ACTUALIZAR UNA FACTURA
    // ===============================
    actualizarFactura(id) {
        const factura = this.facturas.find(f => f.id === id);
        if (!factura) return;

        const tr = this.dom.tbody.querySelector(`tr[data-factura-id="${id}"]`);
        if (!tr) return;

        const importePagadoInput = tr.querySelector('.cp-importe-pagado');
        let importePagado = parseFloat(importePagadoInput.value) || 0;

        // Validar que no exceda el saldo anterior
        if (importePagado > factura.saldoAnterior) {
            importePagado = factura.saldoAnterior;
            importePagadoInput.value = importePagado.toFixed(2);
            toastMixin?.fire({
                icon: "warning",
                title: "El importe pagado no puede exceder el saldo anterior"
            });
        }

        // Validar que no sea negativo
        if (importePagado < 0) {
            importePagado = 0;
            importePagadoInput.value = 0;
        }

        factura.importePagado = importePagado;
        factura.saldoInsoluto = factura.saldoAnterior - importePagado;

        // Actualizar saldo insoluto en la tabla
        const saldoInsolutoCel = tr.querySelector('.cp-saldo-insoluto');
        if (saldoInsolutoCel) {
            saldoInsolutoCel.textContent = Currency.format(factura.saldoInsoluto);
        }

        this.calcularTotalComplemento();
    }

    // ===============================
    // ELIMINAR FACTURA
    // ===============================
    eliminarFactura(id) {
        this.facturas = this.facturas.filter(f => f.id !== id);
        this.renderFacturasTable();
        this.calcularTotalComplemento();
        this.toggleBotonAutoLlenar(); // ⭐ OCULTAR SI NO HAY FACTURAS

        toastMixin?.fire({
            icon: "info",
            title: 'Factura eliminada'
        });
    }

    // ===============================
    // RENDERIZAR LA TABLA
    // ===============================
    renderFacturasTable() {
        const tbody = this.dom.tbody;
        if (!tbody) return;

        tbody.innerHTML = "";

        if (this.facturas.length === 0) {
            tbody.innerHTML = `
            <tr class="cp-empty-state">
                <td colspan="8" class="text-center py-4">
                    <i class="fas fa-inbox fa-3x text-muted mb-3"></i>
                    <p class="text-muted mb-0">No hay facturas agregadas al complemento de pago</p>
                </td>
            </tr>`;
            return;
        }

        const fragment = document.createDocumentFragment();

        this.facturas.forEach(f => {
            const tr = document.createElement("tr");
            tr.dataset.facturaId = f.id;

            const folioCompleto = f.serie ? `${f.serie}-${f.folio}` : f.folio;

            tr.innerHTML = `
            <td class="text-center">${this.escapeHtml(folioCompleto)}</td>
            <td class="text-truncate" style="max-width: 250px;" title="${this.escapeHtml(f.uuid)}">${this.escapeHtml(f.uuid)}</td>
            <td class="text-center">${this.escapeHtml(f.fecha)}</td>
            <td class="text-end fw-bold">${Currency.format(f.total)}</td>
            <td class="text-end">${Currency.format(f.saldoAnterior)}</td>
            <td>
                <input type="number" 
                       class="cp-importe-pagado form-control form-control-sm text-end"
                       min="0" 
                       max="${f.saldoAnterior}" 
                       step="0.01" 
                       value="${f.importePagado.toFixed(2)}">
            </td>
            <td class="cp-saldo-insoluto text-end fw-bold text-primary">
                ${Currency.format(f.saldoInsoluto)}
            </td>
            <td class="text-center">
                <button type="button" class="btn btn-sm btn-danger cp-btn-eliminar" title="Eliminar factura">
                    <i class="fas fa-trash"></i>
                </button>
            </td>
        `;

            fragment.appendChild(tr);
        });

        tbody.appendChild(fragment);
    }

    // ===============================
    // CALCULAR Y ACTUALIZAR TOTALES
    // ===============================
    calcularTotalComplemento() {
        // Calcular totales
        const totalFacturas = this.facturas.reduce((sum, f) => sum + f.total, 0);
        const saldoPendienteTotal = this.facturas.reduce((sum, f) => sum + f.saldoAnterior, 0);
        const montoPagar = this.facturas.reduce((sum, f) => sum + f.importePagado, 0);
        const saldoRestante = this.facturas.reduce((sum, f) => sum + f.saldoInsoluto, 0);

        this.totalComplemento = montoPagar;

        // Actualizar displays
        if (this.dom.totalFacturasDisplay) {
            this.dom.totalFacturasDisplay.textContent = Currency.format(totalFacturas);
        }

        if (this.dom.saldoPendienteDisplay) {
            this.dom.saldoPendienteDisplay.textContent = Currency.format(saldoPendienteTotal);
        }

        if (this.dom.montoPagarDisplay) {
            this.dom.montoPagarDisplay.textContent = Currency.format(montoPagar);
        }

        if (this.dom.saldoRestanteDisplay) {
            this.dom.saldoRestanteDisplay.textContent = Currency.format(saldoRestante);
        }

        if (this.dom.totalComplementoDisplay) {
            this.dom.totalComplementoDisplay.textContent = Currency.format(this.totalComplemento);
        }

        // Actualizar hidden input
        const hiddenInput = document.getElementById('vi-fac-total-complemento');
        if (hiddenInput) {
            hiddenInput.value = this.totalComplemento.toFixed(2);
        }
    }

    // ===============================
    // OBTENER DATOS PARA ENVIAR
    // ===============================
    getFacturasData() {
        return this.facturas.map(f => ({
            id_encabezado: f.id_encabezado,
            serie: f.serie,
            folio: f.folio,
            uuid: f.uuid,
            fecha: f.fecha,
            total: f.total,
            saldoAnterior: f.saldoAnterior,
            importePagado: f.importePagado,
            saldoInsoluto: f.saldoInsoluto,
            moneda: f.moneda,
            metodoPago: f.metodoPago,
            esManual: f.esManual
        }));
    }

    // ===============================
    // VALIDAR COMPLEMENTO
    // ===============================
    validarComplemento() {
        const errores = [];

        if (this.facturas.length === 0) {
            errores.push('Debe agregar al menos una factura al complemento de pago');
        }

        const facturasSinPago = this.facturas.filter(f => f.importePagado <= 0);
        if (facturasSinPago.length > 0) {
            errores.push('Todas las facturas deben tener un importe pagado mayor a 0');
        }

        if (this.totalComplemento <= 0) {
            errores.push('El total del complemento debe ser mayor a 0');
        }

        return errores;
    }

    // ===============================
    // LIMPIAR COMPLEMENTO
    // ===============================
    limpiar() {
        this.facturas = [];
        this.totalComplemento = 0;
        this.renderFacturasTable();
        this.calcularTotalComplemento();
        this.toggleBotonAutoLlenar(); // ⭐ OCULTAR BOTÓN
    }

    // ===============================
    // ESCAPE HTML
    // ===============================
    escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    // ===============================
    // AGREGAR FACTURA MANUAL
    // ===============================
    agregarFacturaManual(datosManual) {
        const existe = this.facturas.find(f => f.uuid === datosManual.uuid);
        if (existe) {
            toastMixin?.fire({ icon: "warning", title: "Esta factura ya fue agregada" });
            return false;
        }

        const facturaData = {
            id: `fac_manual_${Date.now()}_${Math.random()}`,
            id_encabezado: null,
            serie: datosManual.serie,
            folio: datosManual.folio,
            uuid: datosManual.uuid,
            fecha: datosManual.fecha,
            total: parseFloat(datosManual.total),
            saldoAnterior: parseFloat(datosManual.saldoPendiente),
            importePagado: 0,
            saldoInsoluto: parseFloat(datosManual.saldoPendiente),
            moneda: datosManual.moneda,
            metodoPago: datosManual.metodoPago,
            esManual: true
        };

        this.facturas.push(facturaData);
        this.renderFacturasTable();
        this.calcularTotalComplemento();
        this.toggleBotonAutoLlenar(); // ⭐ MOSTRAR BOTÓN

        toastMixin?.fire({
            icon: "success",
            title: `Factura manual agregada: ${datosManual.serie}-${datosManual.folio}`
        });

        return true;
    }
}

// ============================================
// 4. GESTOR DE BUSCAR FACTURAS PARA CP
// ============================================
class FacturasParaComplementoManager extends FacturasDataManager {
    constructor(config) {
        super(config);
        this.clienteActual = null;
    }

    buildUrl() {
        const params = new URLSearchParams({
            busqueda: this.state.lastSearch || '',
            page: this.state.page,
            pageSize: this.state.pageSize
        });

        if (this.clienteActual) {
            params.append('cliente', this.clienteActual);
        }

        return `${this.endpoint}?${params.toString()}`;
    }

    buildCacheKey() {
        return `${this.state.lastSearch}-${this.state.page}-${this.state.pageSize}-${this.clienteActual || ''}`;
    }

    renderFacturasResults(data) {
        const items = data.items || [];

        if (items.length === 0) {
            this.dom.results.innerHTML = `
                <li class="list-group-item text-center text-muted py-5">
                    <i class="fas fa-file-invoice fa-3x mb-3 opacity-25"></i>
                    <p>No se encontraron facturas pendientes</p>
                </li>`;
            this.updateFacturasCounters();
            return;
        }

        const fragment = document.createDocumentFragment();

        items.forEach(item => {
            const li = document.createElement('li');
            li.className = 'list-group-item hover-shadow';

            li.innerHTML = `
                <div class="d-flex justify-content-between align-items-start">
                    <div class="flex-grow-1">
                        <h6 class="mb-1">
                            <i class="fas fa-file-invoice text-primary me-2"></i>
                            <strong>Folio:</strong> ${this.escapeFacturasHtml(item.folio || '')}
                        </h6>
                        <p class="mb-1">
                            <small class="text-muted">UUID: ${this.escapeFacturasHtml(item.uuid || '')}</small>
                        </p>
                        <div class="d-flex gap-3 small text-muted">
                            <span><i class="fas fa-calendar me-1"></i> ${this.escapeFacturasHtml(dateFormatter(item.fecha) || '')}</span>
                            <span><i class="fas fa-money-bill-wave me-1"></i> Total: ${Currency.format(item.total || 0)}</span>
                        </div>
                    </div>
                    <div class="d-flex flex-column gap-2 align-items-end">
                        <button type="button" class="btn btn-sm btn-primary" data-select-id="${this.escapeFacturasHtml(item.id_encabezado)}">
                            <i class="fas fa-plus me-1"></i> Agregar
                        </button>
                    </div>
                </div>`;
            fragment.appendChild(li);
        });

        this.dom.results.innerHTML = '';
        this.dom.results.appendChild(fragment);

        this.updateFacturasCounters();
        this.renderFacturasPagination();
    }

    setClienteActual(clienteId) {
        this.clienteActual = clienteId;
        this.clearFacturasCache();
        this.state.page = 1;
        this.state.lastSearch = '';
        if (this.dom.input) {
            this.dom.input.value = '';
        }
        this.fetchFacturasData();
    }
}

// ============================================
// 5. GESTOR DE FORMULARIO
// ============================================
class FacturasFormManager {
    constructor(complementoManager) {
        this.complementoManager = complementoManager;
        this.form = document.getElementById('vi-fac-formCotizacion');
        this.btnSubmit = document.querySelector('.vi-fac-btn-submit');
        this.initFacturasFormManager();
    }

    initFacturasFormManager() {
        this.btnSubmit?.addEventListener('click', (e) => {
            e.preventDefault();
            this.enviarComplementoPago();
        });
    }

    recopilarDatosFormulario() {
        const formData = new FormData();

        const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
        if (token) formData.append('__RequestVerificationToken', token);

        const camposGenerales = [
            'vi-fac-sucursal',
            'vi-fac-cliente',
            'vi-fac-rfc',
            'vi-fac-forma-pago',
            'vi-fac-moneda',
            'vi-fac-comentarios',
            'vi-fac-metodo-pago',
            'vi-fac-uso-cfdi',
            'vi-fac-banco',
            'vi-fac-paridad',
            'vi-fac-concepto',
            'vi-fac-fecha-pago-real'
        ];

        camposGenerales.forEach(id => {
            const el = document.getElementById(id);
            if (el) formData.append(id.replace('vi-fac-', ''), el.value || '');
        });

        const facturas = this.complementoManager.getFacturasData();
        formData.append('facturasJSON', JSON.stringify(facturas));
        formData.append('totalComplemento', this.complementoManager.totalComplemento.toFixed(2));

        return formData;
    }

    validarFormulario() {
        const errores = [];

        const cliente = document.getElementById('vi-fac-cliente')?.value;
        if (!cliente || cliente.trim() === '') {
            errores.push('Debe seleccionar un cliente');
        }

        const erroresComplemento = this.complementoManager.validarComplemento();
        errores.push(...erroresComplemento);

        const formaPago = document.getElementById('vi-fac-forma-pago')?.value;
        if (!formaPago || formaPago === 'Seleccionar forma de pago') {
            errores.push('Debe seleccionar una forma de pago');
        }

        const fechaPago = document.getElementById('vi-fac-fecha-pago-real')?.value;
        if (!fechaPago || fechaPago.trim() === '') {
            errores.push('Debe ingresar la fecha de pago');
        }

        return errores;
    }

    async enviarComplementoPago() {

        // Validación inicial
        const errores = this.validarFormulario();
        if (errores.length > 0) {
            mostrarErrorPorPaso({ message: errores.join(', ') });
            return;
        }

        // Mostrar modal de progreso con 1 solo paso
        const progressModal = mostrarModalProgresoMejorado(1);

        const originalText = this.btnSubmit.innerHTML;
        this.btnSubmit.disabled = true;
        this.btnSubmit.innerHTML = `<i class="fas fa-spinner fa-spin"></i> Procesando...`;

        try {
            const formData = this.recopilarDatosFormulario();

            // PASO 1 - enviar datos
            actualizarProgresoDetallado(progressModal, 1, 'Enviando datos del complemento de pago...');

            const response = await fetch('/ComplementoPago/ProcesarDocumentosAsync', {
                method: 'POST',
                body: formData
            });

            // ================================
            // MANEJO DE RESPUESTA HTTP
            // ================================
            if (!response.ok) {
                cerrarModalProgreso(progressModal);
                const errorText = await response.text();
                mostrarErrorServidor(response.status, errorText);
                return;
            }

            let result;
            try {
                result = await response.json();
            } catch (parseError) {
                cerrarModalProgreso(progressModal);
                mostrarErrorFormato();
                return;
            }

            // Simular avance bonito
            if (result.success) {
                await simularProgresoExitoso(progressModal);
            }

            // Cerrar progreso
            cerrarModalProgreso(progressModal);

            // ================================
            // RESPUESTA DEL CONTROLADOR
            // ================================
            if (result.success) {

                // Mostrar mismo tipo de resumen que el flujo grande
                mostrarResumenExitoso(result,1);

                this.limpiarFormulario();

            } else {

                // Error de proceso (controlador)
                mostrarErrorPorPaso(result);

            }

        } catch (error) {

            cerrarModalProgreso(progressModal);
            mostrarErrorConexion(error);

        } finally {

            this.btnSubmit.disabled = false;
            this.btnSubmit.innerHTML = originalText;

        }
    }

    limpiarFormulario() {
        this.form?.reset();
        this.complementoManager.limpiar();

        document.getElementById('vi-fac-cliente').value = '';
        document.getElementById('vi-fac-rfc').value = '';
        document.getElementById('vi-fac-info-proveedor').value = '';
        document.getElementById('vi-fac-fecha-pago-real').value = '';
    }
}
// ============================================
// 6. APLICACIÓN PRINCIPAL
// ============================================
const FacturasApp = {
    clienteManager: null,
    facturasParaCPManager: null,
    complementoManager: null,
    formManager: null,
    initFacturas() {
        // Gestor de búsqueda de clientes
        this.clienteManager = new FacturasDataManager({
            endpoint: '/DatosGenerales/BuscarC',
            detailEndpoint: '/DatosGenerales/BuscarCliente',
            modalId: 'vi-fac-modalBuscarCliente',
            inputId: 'vi-fac-inputBuscarCliente',
            resultsId: 'vi-fac-listaResultadosClientes',
            pageSizeId: 'vi-fac-pageSizeClientes',
            btnClearId: 'vi-fac-btnLimpiarClientes',
            spinnerId: 'vi-fac-spinnerClientes',
            paginationId: 'vi-fac-paginationClientes',
            recordsFromId: 'vi-fac-recordsFromClientes',
            recordsToId: 'vi-fac-recordsToClientes',
            totalRecordsId: 'vi-fac-totalRecordsClientes',
            shouldCloseOnSelect: true
        });

        this.clienteManager.onFacturasSelect = (cliente) => {
            document.getElementById('vi-fac-cliente').value = cliente.id || '';
            document.getElementById('vi-fac-rfc').value = cliente.rfc || '';
            document.getElementById('vi-fac-info-proveedor').value =
                [cliente.dir, cliente.col, cliente.pob, cliente.cp]
                    .filter(x => x)
                    .join(',\n');

            const formaPagoInstance = tomManager.getInstance('vi-fac-forma-pago');
            if (formaPagoInstance && cliente.forma_pago) {
                formaPagoInstance.setValue(cliente.forma_pago, true);
            }
        };

        // Gestor de complementos de pago
        this.complementoManager = new ComplementoPagoManager();

        // Gestor de búsqueda de facturas para complemento
        this.facturasParaCPManager = new FacturasParaComplementoManager({
            endpoint: '/DatosGenerales/BuscarFacturasPendientes',
            detailEndpoint: '/DatosGenerales/BuscarFacturaDetalle',
            modalId: 'vi-fac-modalBuscarFacturasCP',
            inputId: 'vi-fac-inputBuscarFactura',
            resultsId: 'vi-fac-listaResultadosFacturas',
            pageSizeId: 'vi-fac-pageSizeFacturas',
            btnClearId: 'vi-fac-btnLimpiarFacturas',
            spinnerId: 'vi-fac-spinnerFacturas',
            paginationId: 'vi-fac-paginationFacturas',
            recordsFromId: 'vi-fac-recordsFromFacturas',
            recordsToId: 'vi-fac-recordsToFacturas',
            totalRecordsId: 'vi-fac-totalRecordsFacturas',
            shouldCloseOnSelect: false
        });

        // Configurar el modal de búsqueda de facturas
        const modalFacturasCP = document.getElementById('vi-fac-modalBuscarFacturasCP');
        if (modalFacturasCP) {
            modalFacturasCP.addEventListener('show.bs.modal', (e) => {
                const clienteId = document.getElementById('vi-fac-cliente')?.value;
                if (!clienteId || clienteId.trim() === '') {
                    e.preventDefault(); // ⭐ Prevenir que se abra el modal
                    e.stopPropagation();

                    if (window.toastMixin) {
                        toastMixin.fire({
                            icon: 'warning',
                            title: 'Debe seleccionar un cliente primero'
                        });
                    }
                    return false;
                }
                this.facturasParaCPManager.setClienteActual(clienteId);
            });
        }

        this.facturasParaCPManager.onFacturasSelect = async (factura) => {
            try {
                this.complementoManager.agregarFactura(factura);
            } catch (error) {
                console.error('Error agregando factura:', error);
                if (window.Swal) {
                    Swal.fire({
                        icon: 'error',
                        title: 'Error',
                        text: 'No se pudo agregar la factura'
                    });
                }
            }
        };

        // Gestor del formulario
        this.formManager = new FacturasFormManager(this.complementoManager);

        // ===============================
        // MANEJO DE FACTURA MANUAL
        // ===============================
        const formManual = document.getElementById('vi-fac-formFacturaManual');
        const btnLimpiarManual = document.getElementById('vi-fac-btn-limpiar-manual');

        // Validación de UUID en tiempo real
        const uuidInput = document.getElementById('vi-fac-manual-uuid');
        if (uuidInput) {
            uuidInput.addEventListener('input', (e) => {
                const valor = e.target.value.toUpperCase();
                // Formato UUID: 8-4-4-4-12 caracteres
                const regex = /^[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}$/;

                if (valor.length === 36) {
                    if (regex.test(valor)) {
                        e.target.classList.remove('is-invalid');
                        e.target.classList.add('is-valid');
                    } else {
                        e.target.classList.remove('is-valid');
                        e.target.classList.add('is-invalid');
                    }
                } else {
                    e.target.classList.remove('is-valid', 'is-invalid');
                }
            });
        }

        // Validar que saldo no exceda total
        const totalInput = document.getElementById('vi-fac-manual-total');
        const saldoInput = document.getElementById('vi-fac-manual-saldo');

        if (saldoInput && totalInput) {
            saldoInput.addEventListener('input', () => {
                const total = parseFloat(totalInput.value) || 0;
                const saldo = parseFloat(saldoInput.value) || 0;

                if (saldo > total) {
                    saldoInput.value = total;
                    toastMixin?.fire({
                        icon: 'warning',
                        title: 'El saldo no puede ser mayor al total'
                    });
                }
            });
        }

        // Submit del formulario manual
        if (formManual) {
            formManual.addEventListener('submit', (e) => {
                e.preventDefault();

                const datosManual = {
                    serie: document.getElementById('vi-fac-manual-serie').value.trim(), // ⭐ NUEVO CAMPO
                    folio: document.getElementById('vi-fac-manual-folio').value.trim(),
                    uuid: document.getElementById('vi-fac-manual-uuid').value.trim().toUpperCase(),
                    fecha: document.getElementById('vi-fac-manual-fecha').value,
                    total: document.getElementById('vi-fac-manual-total').value,
                    saldoPendiente: document.getElementById('vi-fac-manual-saldo').value,
                    moneda: document.getElementById('vi-fac-manual-moneda').value,
                    metodoPago: document.getElementById('vi-fac-manual-metodo').value
                };

                // Validar UUID formato
                const regexUUID = /^[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}$/;
                if (!regexUUID.test(datosManual.uuid)) {
                    toastMixin?.fire({
                        icon: 'error',
                        title: 'El UUID no tiene un formato válido'
                    });
                    return;
                }

                // Agregar factura
                const agregado = this.complementoManager.agregarFacturaManual(datosManual);

                if (agregado) {
                    // Limpiar formulario
                    formManual.reset();
                    document.getElementById('vi-fac-manual-fecha').valueAsDate = new Date();

                    // Cerrar modal
                    const modal = bootstrap.Modal.getInstance(
                        document.getElementById('vi-fac-modalBuscarFacturasCP')
                    );
                    modal?.hide();
                }
            });
        }

        // Botón limpiar manual
        if (btnLimpiarManual) {
            btnLimpiarManual.addEventListener('click', () => {
                formManual?.reset();
                document.getElementById('vi-fac-manual-fecha').valueAsDate = new Date();
                document.querySelectorAll('.is-valid, .is-invalid').forEach(el => {
                    el.classList.remove('is-valid', 'is-invalid');
                });
            });
        }

        // Establecer fecha actual al abrir el tab manual
        const manualTab = document.getElementById('manual-tab');
        if (manualTab) {
            manualTab.addEventListener('shown.bs.tab', () => {
                const fechaInput = document.getElementById('vi-fac-manual-fecha');
                if (fechaInput && !fechaInput.value) {
                    fechaInput.valueAsDate = new Date();
                }
            });
        }

        console.log('Sistema de complementos de pago con ingreso manual inicializado');
    }
};
// ============================================
// 7. INICIALIZACIÓN GLOBAL
// ============================================
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => {
        FacturasApp.initFacturas();
    });
} else {
    FacturasApp.initFacturas();
}
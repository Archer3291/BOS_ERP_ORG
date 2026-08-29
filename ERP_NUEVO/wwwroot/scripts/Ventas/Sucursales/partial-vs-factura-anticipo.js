// ============================================
// 1. CLASE BASE PARA GESTIÓN DE DATOS
// ============================================
class FacturasAnticipoDataManager {
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
        this.initFacturasAnticipoDataManager();
    }

    initFacturasAnticipoDataManager() {
        this.dom.modal?.addEventListener('show.bs.modal', () => this.onFacturasModalShow());
        this.dom.input?.addEventListener('input', (e) => this.handleFacturasSearch(e.target.value));
        this.dom.pageSize?.addEventListener('change', (e) => this.handleFacturasPageSizeChange(e.target.value));
        this.dom.btnClear?.addEventListener('click', () => this.handleFacturasClear());

        this.dom.results?.addEventListener('click', (e) => {
            const btn = e.target.closest('[data-select-id]');
            if (btn) this.handleFacturasSelect(btn.dataset.selectId);
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
        if (cached && Date.now() - cached.timestamp < this.cacheTimeout) return cached.data;
        return null;
    }

    setFacturasCachedData(key, data) {
        this.cache.set(key, { data, timestamp: Date.now() });
    }

    clearFacturasCache() {
        this.cache.clear();
    }

    buildCacheKey() {
        return `${this.state.lastSearch}-${this.state.page}-${this.state.pageSize}`;
    }

    buildUrl() {
        const params = new URLSearchParams({
            busqueda: this.state.lastSearch || '',
            nombre: this.state.lastSearch || '',
            page: this.state.page,
            pageSize: this.state.pageSize
        });
        return `${this.endpoint}?${params.toString()}`;
    }

    async fetchFacturasData() {
        if (this.state.loading) this.abortController?.abort();

        const cacheKey = this.buildCacheKey();
        const cached = this.getFacturasCachedData(cacheKey);
        if (cached) { this.renderFacturasResults(cached); return; }

        this.state.loading = true;
        this.showFacturasSpinner(true);
        this.abortController = new AbortController();

        try {
            const response = await fetch(this.buildUrl(), { signal: this.abortController.signal });
            if (!response.ok) throw new Error('Error en la respuesta');

            const data = await response.json();
            this.state.totalRecords = data.total || 0;

            const results = { items: data.items || data, total: this.state.totalRecords };
            this.setFacturasCachedData(cacheKey, results);
            this.renderFacturasResults(results);

        } catch (error) {
            if (error.name !== 'AbortError') {
                console.error('Error fetching data:', error);
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
            li.className = 'list-group-item d-flex justify-content-between align-items-center';
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
        if (totalPages <= 1) { this.dom.pagination.innerHTML = ''; return; }

        const maxPages = 5;
        let startPage = Math.max(this.state.page - Math.floor(maxPages / 2), 1);
        let endPage = Math.min(startPage + maxPages - 1, totalPages);
        startPage = Math.max(endPage - maxPages + 1, 1);

        const fragment = document.createDocumentFragment();
        fragment.appendChild(this.createPageButton('Anterior', this.state.page - 1, this.state.page === 1));
        for (let i = startPage; i <= endPage; i++) {
            fragment.appendChild(this.createPageButton(i, i, false, i === this.state.page));
        }
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
            console.error('Error selecting item:', error);
            alert('Error al seleccionar el elemento');
        }
    }

    onFacturasSelect(item) {
        console.log('Item selected:', item);
    }

    escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }
}

// ============================================
// 2. CLASE PARA CLIENTES
// ============================================
class FacturasAnticipoClienteManager extends FacturasAnticipoDataManager {
    // Usa el render base heredado — solo se personaliza onFacturasSelect desde App
}

// ============================================
// 3. CLASE PARA ANTICIPOS
// ============================================
class FacturasAnticipo2Manager extends FacturasAnticipoDataManager {
    constructor(config) {
        super(config);
        this.clienteActual = null;
    }

    buildUrl() {
        const params = new URLSearchParams({
            nombre: this.state.lastSearch || '',
            page: this.state.page,
            pageSize: this.state.pageSize,
            cliente: this.clienteActual || 0
        });
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
                    <i class="fas fa-wallet fa-3x mb-3 opacity-25"></i>
                    <p>No se encontraron anticipos</p>
                </li>`;
            this.updateFacturasCounters();
            return;
        }

        const fragment = document.createDocumentFragment();
        items.forEach(item => {
            const li = document.createElement('li');
            li.className = 'list-group-item hover-shadow';
            li.dataset.folio = item.folio;
            li.innerHTML = `
                <div class="d-flex justify-content-between align-items-start">
                    <div class="flex-grow-1">
                        <h6 class="mb-1">
                            <i class="fas fa-wallet text-success me-2"></i>
                            <strong>Folio:</strong> ${this.escapeHtml(item.folio || '')}
                        </h6>
                        <p class="mb-1 text-muted">
                            <i class="fas fa-user me-2"></i>
                            <strong>Cliente:</strong> ${this.escapeHtml(item.cli_prov || '')}
                        </p>
                        <div class="d-flex gap-3 small text-muted">
                            <span><i class="fas fa-calendar me-1"></i> ${this.escapeHtml(item.fch || '')}</span>
                            <span><i class="fas fa-money-bill-wave me-1"></i> $${parseFloat(item.saldo || 0).toFixed(2)}</span>
                            <span><i class="fas fa-user-tie me-1"></i> ${this.escapeHtml(item.usr0 || '')}</span>
                        </div>
                    </div>
                    <div class="d-flex flex-column gap-2 align-items-end">
                        <button type="button" class="btn btn-sm btn-success" data-select-id="${this.escapeHtml(item.id_encabezado)}">
                            <i class="fas fa-check me-1"></i> Seleccionar
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
        this.dom.input.value = '';
        this.fetchFacturasData();
    }
}

// ============================================
// 4. GESTOR DE ANTICIPOS (tabla + totales)
// ============================================
class FacturasAnticipoProductManager {
    constructor() {
        this.anticipos = [];
        this.totales = {
            subtotal: 0,
            iva: 0,
            total: 0,
            totalAnticipos: 0,
            totalFinal: 0
        };

        this.dom = {
            anticiposTable: document.getElementById('vs-fac-ant-anticiposTable'),
            subtotalDisplay: document.getElementById('vs-fac-ant-total-subtotal1-display'),
            ivaDisplay: document.getElementById('vs-fac-ant-iva-display'),
            importeDisplay: document.getElementById('vs-fac-ant-importe-display'),
            totalAnticiposDisplay: document.getElementById('vs-fac-ant-total-anticipos-display'),
            totalFinalDisplay: document.getElementById('vs-fac-ant-total-final-display')
        };

        this.initEventos();
    }

    initEventos() {
        this.dom.anticiposTable?.addEventListener('click', (e) => {
            const btn = e.target.closest('.vs-fac-ant-btn-eliminar-anticipo');
            if (btn) {
                const tr = btn.closest('tr');
                const anticipoId = tr?.dataset.anticipoId;
                if (anticipoId) this.eliminarAnticipo(anticipoId);
            }
        });

        document.getElementById('vs-fac-ant-monto-anticipo')?.addEventListener('input', () => {
            this.calcularTotales();
        });
    }

    agregarAnticipo(anticipo) {
        const anticipoExistente = this.anticipos.find(a => a.id_encabezado === anticipo.id_encabezado);
        if (anticipoExistente) {
            window.toastMixin?.fire({ icon: 'warning', title: 'Este anticipo ya fue agregado' });
            return;
        }

        const anticipoData = {
            id: `ant_${Date.now()}_${Math.random()}`,
            id_encabezado: anticipo.id_encabezado,
            folio: anticipo.folio,
            cli_prov: anticipo.cli_prov,
            imp: parseFloat(anticipo.saldo || 0),
            fch: anticipo.fch
        };

        this.anticipos.push(anticipoData);
        this.renderAnticiposTable();
        this.calcularTotales();

        window.toastMixin?.fire({ icon: 'success', title: `Anticipo agregado: ${anticipo.folio}` });
    }

    renderAnticiposTable() {
        const tbody = this.dom.anticiposTable;
        if (!tbody) return;

        tbody.innerHTML = '';

        if (this.anticipos.length === 0) {
            tbody.innerHTML = `
                <tr class="vs-fac-ant-empty-state-anticipos">
                    <td colspan="5" class="text-center py-3">
                        <small class="text-muted">No hay anticipos agregados</small>
                    </td>
                </tr>`;
            return;
        }

        const fragment = document.createDocumentFragment();
        this.anticipos.forEach(a => {
            const tr = document.createElement('tr');
            tr.dataset.anticipoId = a.id;
            tr.className = 'table-info';
            tr.innerHTML = `
                <td>${this.escapeHtml(a.folio)}</td>
                <td>${this.escapeHtml(a.cli_prov)}</td>
                <td>${this.escapeHtml(a.fch)}</td>
                <td class="fw-bold text-success">$${a.imp.toFixed(2)}</td>
                <td>
                    <input type="hidden" class="vs-fac-ant-anticipo-id" value="${a.id_encabezado}">
                    <button type="button" class="btn btn-sm btn-danger vs-fac-ant-btn-eliminar-anticipo">
                        <i class="fas fa-trash"></i>
                    </button>
                </td>`;
            fragment.appendChild(tr);
        });

        tbody.appendChild(fragment);
    }

    eliminarAnticipo(anticipoId) {
        this.anticipos = this.anticipos.filter(a => a.id !== anticipoId);
        this.renderAnticiposTable();
        this.calcularTotales();
        window.toastMixin?.fire({ icon: 'info', title: 'Anticipo removido' });
    }

    calcularTotales() {
        const montoAnticipo = parseFloat(document.getElementById('vs-fac-ant-monto-anticipo')?.value) || 0;

        // El monto capturado YA incluye IVA
        const total = montoAnticipo;

        // Desglose proporcional
        const subtotal = Math.round((total / 1.16) * 100) / 100;
        const iva = Math.round((total - subtotal) * 100) / 100;

        const totalAnticiposAplicados = this.anticipos.reduce((sum, a) => sum + a.imp, 0);
        const totalFinal = total - totalAnticiposAplicados;

        this.totales = {
            subtotal,
            iva,
            total,
            totalAnticipos: totalAnticiposAplicados,
            totalFinal
        };

        this.updateDisplay(this.dom.subtotalDisplay, subtotal);
        this.updateDisplay(this.dom.ivaDisplay, iva);
        this.updateDisplay(this.dom.importeDisplay, total);
        this.updateDisplay(this.dom.totalAnticiposDisplay, totalAnticiposAplicados, '-');
        this.updateDisplay(this.dom.totalFinalDisplay, totalFinal);

        this.updateHiddenField('vs-fac-ant-total-subtotal1', subtotal);
        this.updateHiddenField('vs-fac-ant-total-subtotal2', subtotal);
        this.updateHiddenField('vs-fac-ant-iva', iva);
        this.updateHiddenField('vs-fac-ant-importe', total);
        this.updateHiddenField('vs-fac-ant-total-anticipos', totalAnticiposAplicados);
        this.updateHiddenField('vs-fac-ant-total-final', totalFinal);
    }

    updateDisplay(element, value, prefix = '') {
        if (element) element.textContent = `${prefix}$${value.toFixed(2)}`;
    }

    updateHiddenField(id, value) {
        const field = document.getElementById(id);
        if (field) field.value = value.toFixed(2);
    }

    getAnticiposData() {
        return this.anticipos.map(a => ({
            id_encabezado: a.id_encabezado,
            folio: a.folio,
            imp: a.imp
        }));
    }

    escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }
}

// ============================================
// 5. GESTOR DE FORMULARIO
// ============================================
class FacturasAnticipoFormManager {
    constructor(productManager) {
        this.productManager = productManager;
        this.form = document.getElementById('vs-fac-ant-formCotizacion');

        document.querySelector('.vs-fac-ant-btn-submit')?.addEventListener('click', (e) => {
            e.preventDefault();
            enviarFacturasAnticipo();
        });
    }

    recopilarDatos() {
        const formData = new FormData();

        const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
        if (token) formData.append('__RequestVerificationToken', token);

        const campos = [
            'vs-fac-ant-folio', 'vs-fac-ant-documentid', 'vs-fac-ant-cliente',
            'vs-fac-ant-rfc', 'vs-fac-ant-vendedor', 'vs-fac-ant-moneda',
            'vs-fac-ant-paridad', 'vs-fac-ant-concepto',
            'vs-fac-ant-metodo-pago', 'vs-fac-ant-forma-pago',
            'vs-fac-ant-uso-cfdi', 'vs-fac-ant-comentarios',
            'vs-fac-ant-monto-anticipo', 'vs-fac-ant-fecha-anticipo'
        ];

        campos.forEach(id => {
            const el = document.getElementById(id);
            if (el) formData.append(id.replace('vs-fac-ant-', ''), el.value || '');
        });

        formData.append('tipo', 'anticipo');

        const anticipos = this.productManager.getAnticiposData();
        if (anticipos.length > 0) formData.append('anticiposJSON', JSON.stringify(anticipos));

        const totales = this.productManager.totales;
        formData.append('subtotal1', totales.subtotal.toFixed(2));
        formData.append('subtotal2', totales.subtotal.toFixed(2));
        formData.append('iva', totales.iva.toFixed(2));
        formData.append('total', totales.total.toFixed(2));
        formData.append('totalAnticipos', totales.totalAnticipos.toFixed(2));
        formData.append('totalFinal', totales.totalFinal.toFixed(2));

        return formData;
    }

    validar() {
        const errores = [];

        const cliente = document.getElementById('vs-fac-ant-cliente')?.value;
        if (!cliente || cliente.trim() === '') errores.push('Debe seleccionar un cliente');

        const monto = parseFloat(document.getElementById('vs-fac-ant-monto-anticipo')?.value) || 0;
        if (monto <= 0) errores.push('Debe ingresar un monto de anticipo válido');

        const vendedor = document.getElementById('vs-fac-ant-vendedor')?.value;
        if (!vendedor || vendedor === 'Seleccionar vendedor') errores.push('Debe seleccionar un vendedor');

        const rfc = document.getElementById('vs-fac-ant-rfc')?.value?.trim();
        if (!rfc) errores.push('Debe ingresar el RFC');

        const moneda = tomManager?.getInstance('vs-fac-ant-moneda')?.getValue?.() ?? '';
        if (!moneda) errores.push('Debe seleccionar una moneda');

        const usoCfdi = tomManager?.getInstance('vs-fac-ant-uso-cfdi')?.getValue?.() ?? '';
        if (!usoCfdi) errores.push('Debe seleccionar un uso CFDI');

        const formaPago = tomManager?.getInstance('vs-fac-ant-forma-pago')?.getValue?.() ?? '';
        if (!formaPago) errores.push('Debe seleccionar una forma de pago');

        return errores;
    }

    limpiar() {
        this.form?.reset();
        this.productManager.anticipos = [];
        this.productManager.renderAnticiposTable();
        this.productManager.calcularTotales();
        document.getElementById('vs-fac-ant-cliente').value = '';
        document.getElementById('vs-fac-ant-rfc').value = '';
        document.getElementById('vs-fac-ant-info-proveedor').value = '';
    }
}

// ============================================
// 6. FUNCIÓN PRINCIPAL DE ENVÍO
// ============================================
async function enviarFacturasAnticipo() {
    try {
        // 1. Validaciones básicas usando el método existente
        const errores = FacturasAnticipoApp.formManager.validar();
        if (errores && errores.length > 0) {
            window.toastMixin?.fire({ icon: 'error', title: errores.join(', ') });
            return;
        }

        // 2. Obtener y mostrar selección de centro de costos y cuenta bancaria
        const datosSeleccionados = await mostrarSeleccionCentroAnticipo();
        if (!datosSeleccionados) return;

        // 3. Obtener el total de documentos/productos para el modal de progreso
        const productos = FacturasApp.productManagerInstance.getFacturasProductosData();
        const totalDocumentos = productos.length || 1;

        // 5. Mostrar modal de progreso
        const modalProgreso = mostrarModalProgresoMejorado(totalDocumentos);


        const formData = FacturasAnticipoApp.formManager.recopilarDatos();
        formData.append('CentroCostosId', datosSeleccionados.centro);

        const btnSubmit = document.querySelector('.vs-fac-ant-btn-submit');
        const originalText = btnSubmit?.innerHTML || 'Generar Anticipo';
        if (btnSubmit) {
            btnSubmit.disabled = true;
            btnSubmit.innerHTML = `<i class="fas fa-spinner fa-spin"></i> Procesando...`;
        }

        try {
            // 7. LLAMADA ÚNICA AL SERVIDOR (proceso completo)
            actualizarProgresoDetallado(modalProgreso, 1, 'Iniciando proceso de facturación...');

            const response = await fetch('/VSFactura/ProcesarDocumentosAsync', {
                method: 'POST',
                body: formData
            });

            // 8. Validar respuesta HTTP
            if (!response.ok) {
                const errorText = await response.text();
                cerrarModalProgreso(modalProgreso);
                mostrarErrorServidor(response.status, errorText);
                return;
            }

            // 9. Parsear respuesta JSON
            const result = await response.json();

            // 10. Simular progreso visual mientras se procesa
            await simularProgresoExitoso(modalProgreso);

            // 11. Cerrar modal de progreso
            cerrarModalProgreso(modalProgreso);

            // 12. Manejar resultado según éxito o error
            if (result.success) {
                // ✅ ÉXITO - Mostrar resumen completo
                mostrarResumenExitoso(result, totalDocumentos);
            } else {
                // ❌ ERROR - Mostrar detalles del error
                if (result.step) {
                    // Error en un paso específico
                    mostrarErrorPorPaso(result, totalDocumentos);
                } else {
                    // Error general
                    Swal.fire({
                        icon: 'error',
                        title: 'Error en el proceso',
                        text: result.message || 'Ocurrió un error desconocido',
                        confirmButtonColor: '#7c3aed'
                    });
                }
            }

        } catch (error) {
            // Error de conexión o procesamiento
            console.error('❌ Error en enviarFacturasCotizacionSync:', error);
            cerrarModalProgreso(modalProgreso);
            mostrarErrorConexion(error);
        } finally {
            // 13. Restaurar botón
            if (btnSubmit) {
                btnSubmit.disabled = false;
                btnSubmit.innerHTML = originalText;
            }
        }

    } catch (error) {
        console.error('❌ Error general en enviarFacturasCotizacionSync:', error);
        Swal.fire({
            icon: 'error',
            title: 'Error',
            text: error.message || 'Ocurrió un error inesperado',
            confirmButtonColor: '#7c3aed'
        });
    }
}

// ============================================
// 7. MODAL SELECCIÓN DE CENTRO DE COSTOS
// ============================================
async function mostrarSeleccionCentroAnticipo() {
    const CENTRO_DEFAULT = '13';
    try {
        const centrosResponse = await GetData({ path: '/DatosGenerales/DatosCentroCostos' });

        if (!centrosResponse.success) {
            Swal.fire({ icon: 'error', title: centrosResponse.message || 'Error al cargar centros de costos', confirmButtonColor: '#7c3aed' });
            return null;
        }

        const centros = centrosResponse.data || centrosResponse.result || [];
        if (centros.length === 0) {
            Swal.fire({ icon: 'warning', title: 'No hay centros de costos disponibles', confirmButtonColor: '#7c3aed' });
            return null;
        }

        const { value: datosSeleccionados } = await Swal.fire({
            title: '📋 Selecciona Centro de Costos',
            html: `
                <div style="text-align: left; padding: 1rem;">
                    <div style="margin-bottom: 1.5rem;">
                        <label style="display:block;color:#374151;font-weight:600;margin-bottom:0.5rem;font-size:0.95rem;">
                            <i class="fas fa-building"></i> Centro de Costos *
                        </label>
                        <select id="swal-centro-anticipo" style="width:100%;padding:0.75rem;border:2px solid #e5e7eb;border-radius:8px;font-size:1rem;">
                            <option value="">-- Selecciona un centro de costos --</option>
                            ${centros.map(c => `<option value="${c.areaid}">${c.nombre}</option>`).join('')}
                        </select>
                    </div>
                    <div style="margin-top:1.5rem;padding:1rem;background:linear-gradient(135deg,#dbeafe 0%,#bfdbfe 100%);border-radius:8px;border-left:4px solid #3b82f6;">
                        <p style="margin:0;color:#1e40af;font-size:0.85rem;line-height:1.5;">
                            <i class="fas fa-info-circle"></i>
                            <strong>Importante:</strong> Estos datos son necesarios para generar correctamente el anticipo y las pólizas contables asociadas.
                        </p>
                    </div>
                </div>`,
            showCancelButton: true,
            confirmButtonText: '<i class="fas fa-arrow-right"></i> Continuar',
            cancelButtonText: '<i class="fas fa-times"></i> Cancelar',
            confirmButtonColor: '#7c3aed',
            cancelButtonColor: '#6b7280',
            width: '500px',
            focusConfirm: false,
            didOpen: () => {
                document.getElementById('swal-centro-anticipo').value = CENTRO_DEFAULT;
            },
            preConfirm: () => {
                const centro = document.getElementById('swal-centro-anticipo').value;
                if (!centro) {
                    Swal.showValidationMessage('Por favor selecciona un centro de costos');
                    return false;
                }
                return { centro };
            }
        });

        return datosSeleccionados;

    } catch (error) {
        console.error('Error en mostrarSeleccionCentroAnticipo:', error);
        Swal.fire({ icon: 'error', title: 'Error al cargar datos', text: error.message, confirmButtonColor: '#7c3aed' });
        return null;
    }
}

// ============================================
// 8. APLICACIÓN PRINCIPAL
// ============================================
const FacturasAnticipoApp = {
    clienteManager: null,
    anticipoManager: null,
    productManagerInstance: null,
    formManager: null,

    initFacturas() {
        // ── Cliente ────────────────────────────────────────────────
        this.clienteManager = new FacturasAnticipoDataManager({
            endpoint: '/DatosGenerales/BuscarC',
            detailEndpoint: '/DatosGenerales/BuscarCliente',
            modalId: 'vs-fac-ant-modalBuscarCliente',
            inputId: 'vs-fac-ant-inputBuscarCliente',
            resultsId: 'vs-fac-ant-listaResultadosClientes',
            pageSizeId: 'vs-fac-ant-pageSizeClientes',
            btnClearId: 'vs-fac-ant-btnLimpiarClientes',
            spinnerId: 'vs-fac-ant-spinnerClientes',
            paginationId: 'vs-fac-ant-paginationClientes',
            recordsFromId: 'vs-fac-ant-recordsFromClientes',
            recordsToId: 'vs-fac-ant-recordsToClientes',
            totalRecordsId: 'vs-fac-ant-totalRecordsClientes',
            shouldCloseOnSelect: true
        });

        this.clienteManager.onFacturasSelect = (cliente) => {
            document.getElementById('vs-fac-ant-cliente').value = cliente.id || '';
            document.getElementById('vs-fac-ant-rfc').value = cliente.rfc || '';
            document.getElementById('vs-fac-ant-info-proveedor').value =
                [cliente.dir, cliente.col, cliente.pob, cliente.cp].filter(x => x).join(',\n');

            asignarVendedorSucursal('vs-fac-ant-vendedor', cliente);
            tomManager?.getInstance('vs-fac-ant-forma-pago')?.setValue(cliente.forma_pago || '', true);
            tomManager?.getInstance('vs-fac-ant-uso-cfdi')?.setValue(cliente.uso_sugerido || '', true);
        };

        // ── Product / Anticipo Manager ─────────────────────────────
        this.productManagerInstance = new FacturasAnticipoProductManager();

        // ── Anticipo Manager ───────────────────────────────────────
        this.anticipoManager = new FacturasAnticipo2Manager({
            endpoint: '/DatosGenerales/BuscarAnt',
            detailEndpoint: '/DatosGenerales/BuscarAnticipo',
            modalId: 'vs-fac-ant-modalBuscarAnticipos',
            inputId: 'vs-fac-ant-inputBuscarAnticipo',
            resultsId: 'vs-fac-ant-listaResultadosAnticipos',
            pageSizeId: 'vs-fac-ant-pageSizeAnticipos',
            btnClearId: 'vs-fac-ant-btnLimpiarAnticipos',
            spinnerId: 'vs-fac-ant-spinnerAnticipos',
            paginationId: 'vs-fac-ant-paginationAnticipos',
            recordsFromId: 'vs-fac-ant-recordsFromAnticipos',
            recordsToId: 'vs-fac-ant-recordsToAnticipos',
            totalRecordsId: 'vs-fac-ant-totalRecordsAnticipos',
            shouldCloseOnSelect: true
        });

        // Validar cliente antes de abrir modal de anticipos
        const modalAnticipos = document.getElementById('vs-fac-ant-modalBuscarAnticipos');
        if (modalAnticipos) {
            modalAnticipos.addEventListener('show.bs.modal', () => {
                const clienteId = document.getElementById('vs-fac-ant-cliente')?.value;
                if (!clienteId || clienteId.trim() === '') {
                    window.toastMixin?.fire({ icon: 'warning', title: 'Debe seleccionar un cliente primero' });
                    bootstrap.Modal.getInstance(modalAnticipos)?.hide();
                    return;
                }
                this.anticipoManager.setClienteActual(clienteId);
            });
        }

        this.anticipoManager.onFacturasSelect = async (anticipo) => {
            try {
                Swal.fire({ title: 'Agregando anticipo...', html: 'Por favor espere', allowOutsideClick: false, didOpen: () => Swal.showLoading() });
                this.productManagerInstance.agregarAnticipo(anticipo);
                Swal.close();
            } catch (error) {
                console.error('Error agregando anticipo:', error);
                Swal.fire({ icon: 'error', title: 'Error', text: 'No se pudo agregar el anticipo' });
            }
        };

        // ── Form Manager ───────────────────────────────────────────
        this.formManager = new FacturasAnticipoFormManager(this.productManagerInstance);

        // Método de pago fijo PUE para anticipos
        document.getElementById('vs-fac-ant-metodo-pago-hidden').value = 'PUE';

        console.log('Sistema de anticipos inicializado correctamente');
    }
};

// ============================================
// 9. INICIALIZACIÓN
// ============================================
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => FacturasAnticipoApp.initFacturas());
} else {
    FacturasAnticipoApp.initFacturas();
}

window.enviarFacturasAnticipo = enviarFacturasAnticipo;
window.mostrarSeleccionCentroAnticipo = mostrarSeleccionCentroAnticipo;
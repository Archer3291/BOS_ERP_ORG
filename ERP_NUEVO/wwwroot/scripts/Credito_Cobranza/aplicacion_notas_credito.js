// ============================================
// aplicacion_notas_credito.js
// Mismo patrón arquitectónico que complemento_pago.js
// ============================================

// ============================================
// 1. CLASE BASE REUTILIZABLE (misma que CP)
// ============================================
class NCDataManager {
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
        this._initNCDataManager();
    }

    _initNCDataManager() {
        this.dom.modal?.addEventListener('show.bs.modal', () => this._onModalShow());
        this.dom.input?.addEventListener('input', (e) => this._handleSearch(e.target.value));
        this.dom.pageSize?.addEventListener('change', (e) => this._handlePageSizeChange(e.target.value));
        this.dom.btnClear?.addEventListener('click', () => this._handleClear());

        this.dom.results?.addEventListener('click', (e) => {
            const btn = e.target.closest('[data-select-id]');
            if (btn) this._handleSelect(btn.dataset.selectId);
        });

        this.dom.pagination?.addEventListener('click', (e) => {
            e.preventDefault();
            const btn = e.target.closest('[data-page]');
            if (btn && !btn.closest('.disabled')) {
                this.state.page = parseInt(btn.dataset.page);
                this._fetchData();
            }
        });
    }

    _onModalShow() { this._handleClear(); this._fetchData(); }

    _handleSearch(value) {
        clearTimeout(this.typingTimer);
        this.typingTimer = setTimeout(() => {
            this.state.lastSearch = value;
            this.state.page = 1;
            this._fetchData();
        }, 300);
    }

    _handlePageSizeChange(value) {
        this.state.pageSize = parseInt(value);
        this.state.page = 1;
        this._fetchData();
    }

    _handleClear() {
        if (this.dom.input) this.dom.input.value = '';
        this.state.lastSearch = '';
        this.state.page = 1;
        this._fetchData();
    }

    _getCached(key) {
        const c = this.cache.get(key);
        return (c && Date.now() - c.timestamp < this.cacheTimeout) ? c.data : null;
    }

    _setCached(key, data) { this.cache.set(key, { data, timestamp: Date.now() }); }

    clearCache() { this.cache.clear(); }

    _buildCacheKey() {
        return `${this.state.lastSearch}-${this.state.page}-${this.state.pageSize}`;
    }

    _buildUrl() {
        const params = new URLSearchParams({
            busqueda: this.state.lastSearch || '',
            nombre: this.state.lastSearch || '',
            page: this.state.page,
            pageSize: this.state.pageSize
        });
        return `${this.endpoint}?${params.toString()}`;
    }

    async _fetchData() {
        if (this.state.loading) this.abortController?.abort();

        const key = this._buildCacheKey();
        const cached = this._getCached(key);
        if (cached) { this._renderResults(cached); return; }

        this.state.loading = true;
        this._showSpinner(true);
        this.abortController = new AbortController();

        try {
            const res = await fetch(this._buildUrl(), { signal: this.abortController.signal });
            if (!res.ok) throw new Error('Error en la respuesta');

            const data = await res.json();
            this.state.totalRecords = data.total || 0;

            const results = { items: data.items || data, total: this.state.totalRecords };
            this._setCached(key, results);
            this._renderResults(results);
        } catch (err) {
            if (err.name !== 'AbortError') { console.error(err); this._renderError(); }
        } finally {
            this.state.loading = false;
            this._showSpinner(false);
        }
    }

    _showSpinner(show) { this.dom.spinner?.classList.toggle('d-none', !show); }

    _renderResults(data) {
        const items = data.items || [];
        if (items.length === 0) {
            this.dom.results.innerHTML = `
                <tr><td colspan="10" class="text-center py-5 text-muted">
                    <i class="fas fa-search fa-3x mb-3 opacity-25 d-block"></i>
                    No se encontraron resultados
                </td></tr>`;
            this._updateCounters();
            return;
        }
        this._renderItems(items);
        this._updateCounters();
        this._renderPagination();
    }

    _renderItems(items) { /* override en subclases */ }

    _renderError() {
        if (this.dom.results) this.dom.results.innerHTML = `
            <tr><td colspan="10" class="text-center text-danger py-5">
                <i class="fas fa-exclamation-triangle fa-3x mb-3 d-block"></i>
                Error al cargar los datos
            </td></tr>`;
    }

    _updateCounters() {
        const from = (this.state.page - 1) * this.state.pageSize + 1;
        const to = Math.min(this.state.page * this.state.pageSize, this.state.totalRecords);
        if (this.dom.recordsFrom) this.dom.recordsFrom.textContent = from;
        if (this.dom.recordsTo) this.dom.recordsTo.textContent = to;
        if (this.dom.totalRecords) this.dom.totalRecords.textContent = this.state.totalRecords;
    }

    _renderPagination() {
        const totalPages = Math.ceil(this.state.totalRecords / this.state.pageSize);
        if (totalPages <= 1) { this.dom.pagination.innerHTML = ''; return; }

        const maxPages = 5;
        let start = Math.max(this.state.page - Math.floor(maxPages / 2), 1);
        let end = Math.min(start + maxPages - 1, totalPages);
        start = Math.max(end - maxPages + 1, 1);

        const frag = document.createDocumentFragment();
        frag.appendChild(this._pageBtn('Anterior', this.state.page - 1, this.state.page === 1));
        for (let i = start; i <= end; i++) frag.appendChild(this._pageBtn(i, i, false, i === this.state.page));
        frag.appendChild(this._pageBtn('Siguiente', this.state.page + 1, this.state.page === totalPages));

        this.dom.pagination.innerHTML = '';
        this.dom.pagination.appendChild(frag);
    }

    _pageBtn(text, page, disabled = false, active = false) {
        const li = document.createElement('li');
        li.className = `page-item${disabled ? ' disabled' : ''}${active ? ' active' : ''}`;
        li.innerHTML = `<button type="button" class="page-link" data-page="${page}">${text}</button>`;
        return li;
    }

    async _handleSelect(id) {
        try {
            const res = await fetch(`${this.detailEndpoint}?id=${encodeURIComponent(id)}`);
            if (!res.ok) throw new Error('Error al obtener detalles');
            const data = await res.json();
            if (data?.[0]) {
                this.onSelect(data[0]);
                if (this.shouldCloseOnSelect)
                    bootstrap.Modal.getInstance(this.dom.modal)?.hide();
            }
        } catch (err) {
            console.error(err);
            alert('Error al seleccionar el elemento');
        }
    }

    onSelect(item) { console.log('NC item selected:', item); }

    _esc(text) {
        const d = document.createElement('div');
        d.textContent = text;
        return d.innerHTML;
    }

    // Permite al manager actualizar el cliente y refrescar
    setClienteActual(clienteId) {
        this.clienteActual = clienteId;
        this.clearCache();
        this.state.page = 1;
        this.state.lastSearch = '';
        if (this.dom.input) this.dom.input.value = '';
        this._fetchData();
    }

    _buildUrl() {
        const params = new URLSearchParams({
            busqueda: this.state.lastSearch || '',
            page: this.state.page,
            pageSize: this.state.pageSize
        });
        if (this.clienteActual) params.append('cliente', this.clienteActual);
        return `${this.endpoint}?${params.toString()}`;
    }

    _buildCacheKey() {
        return `${this.state.lastSearch}-${this.state.page}-${this.state.pageSize}-${this.clienteActual || ''}`;
    }
}

// ============================================
// 2. MANAGER DE CLIENTES
// ============================================
class NCClienteManager extends NCDataManager {
    _renderItems(items) {
        const frag = document.createDocumentFragment();
        items.forEach(item => {
            const li = document.createElement('li');
            li.className = 'list-group-item d-flex justify-content-between align-items-center';
            li.innerHTML = `
                <div>
                    <h6 class="mb-1 fw-semibold text-primary">${this._esc(item.descripcion || item.nombre || '')}</h6>
                    <small class="text-muted">RFC: ${this._esc(item.rfc || '')} | ID: ${this._esc(item.id || '')}</small>
                </div>
                <button type="button" class="btn btn-sm btn-primary" data-select-id="${this._esc(item.id)}">
                    <i class="fas fa-check me-1"></i>Seleccionar
                </button>`;
            frag.appendChild(li);
        });
        this.dom.results.innerHTML = '';
        this.dom.results.appendChild(frag);
    }
}

// ============================================
// 3. MANAGER DE NOTAS DE CRÉDITO
// ============================================
class NCNotasManager extends NCDataManager {
    _renderItems(items) {
        const frag = document.createDocumentFragment();
        items.forEach(item => {
            const tr = document.createElement('tr');
            tr.innerHTML = `
                <td>
                    <strong>${this._esc(item.folio || '')}</strong>
                    <br><small class="text-muted">${this._esc(item.serie || '')}</small>
                </td>
                <td>
                    <small class="text-truncate d-block" style="max-width:160px" title="${this._esc(item.uuid || '')}">
                        ${this._esc(item.uuid || '')}
                    </small>
                </td>
                <td><span class="badge bg-warning text-dark">${this._esc(item.tipo || 'NC')}</span></td>
                <td>${this._esc(dateFormatter ? dateFormatter(item.fecha) : (item.fecha || ''))}</td>
                <td class="text-end fw-bold">${Currency.format(item.total || 0)}</td>
                <td class="text-end text-success fw-bold">${Currency.format(item.saldo_pendiente || 0)}</td>
                <td>
                    <button type="button" class="btn btn-sm btn-success" data-select-id="${this._esc(item.id_encabezado)}">
                        <i class="fas fa-plus me-1"></i>Agregar
                    </button>
                </td>`;
            frag.appendChild(tr);
        });

        // Encabezado de tabla
        const thead = this.dom.results.closest('table')?.querySelector('thead');
        if (thead && thead.innerHTML === '') {
            thead.innerHTML = `<tr class="table-success">
                <th>Serie/Folio</th><th>UUID</th><th>Tipo</th><th>Fecha</th>
                <th>Total NC</th><th>Saldo Disp.</th><th>Acción</th>
            </tr>`;
        }
        this.dom.results.innerHTML = '';
        this.dom.results.appendChild(frag);
    }
}

// ============================================
// 4. MANAGER DE FACTURAS A AFECTAR
// ============================================
class NCFacturasManager extends NCDataManager {
    _renderItems(items) {
        const frag = document.createDocumentFragment();
        items.forEach(item => {
            const tr = document.createElement('tr');
            tr.innerHTML = `
                <td>
                    <strong>${this._esc(item.folio || '')}</strong>
                    <br><small class="text-muted">${this._esc(item.serie || '')}</small>
                </td>
                <td>
                    <small class="text-truncate d-block" style="max-width:160px" title="${this._esc(item.uuid || '')}">
                        ${this._esc(item.uuid || '')}
                    </small>
                </td>
                <td>${this._esc(dateFormatter ? dateFormatter(item.fecha) : (item.fecha || ''))}</td>
                <td class="text-end fw-bold">${Currency.format(item.total || 0)}</td>
                <td class="text-end text-danger fw-bold">${Currency.format(item.saldo_pendiente || 0)}</td>
                <td>
                    <button type="button" class="btn btn-sm btn-primary" data-select-id="${this._esc(item.id_encabezado)}">
                        <i class="fas fa-plus me-1"></i>Agregar
                    </button>
                </td>`;
            frag.appendChild(tr);
        });

        const thead = this.dom.results.closest('table')?.querySelector('thead');
        if (thead && thead.innerHTML === '') {
            thead.innerHTML = `<tr class="table-primary">
                <th>Serie/Folio</th><th>UUID</th><th>Fecha</th>
                <th>Total</th><th>Saldo Pend.</th><th>Acción</th>
            </tr>`;
        }
        this.dom.results.innerHTML = '';
        this.dom.results.appendChild(frag);
    }
}

// ============================================
// 5. GESTOR DE LA APLICACIÓN DE NOTAS
// ============================================
class AplicacionNotasManager {
    constructor() {
        this.notas = [];   // Notas de crédito a aplicar
        this.facturas = [];   // Facturas a afectar
        this.totalAplicacion = 0;

        this.dom = {
            tbodyNotas: document.getElementById('vi-nc-notasTable'),
            tbodyFacturas: document.getElementById('vi-nc-facturasTable'),

            // Displays resumen
            totalNotasDisplay: document.getElementById('vi-nc-total-notas-display'),
            saldoNCDisplay: document.getElementById('vi-nc-saldo-nc-display'),
            totalFacturasDisplay: document.getElementById('vi-nc-total-facturas-display'),
            saldoFacturasDisplay: document.getElementById('vi-nc-saldo-facturas-display'),
            montoAplicadoDisplay: document.getElementById('vi-nc-monto-aplicado-display'),
            saldoNCRestanteDisplay: document.getElementById('vi-nc-saldo-nc-restante-display'),
            saldoFacRestanteDisplay: document.getElementById('vi-nc-saldo-fac-restante-display'),
            totalAplicacionDisplay: document.getElementById('vi-nc-total-aplicacion-display'),
            alertaDiferencia: document.getElementById('vi-nc-alerta-diferencia'),
            diferenciaDisplay: document.getElementById('vi-nc-diferencia-display'),

            // Botón auto-llenar
            containerAutoLlenar: document.getElementById('vi-nc-container-autollenar'),
            btnAutoLlenar: document.getElementById('vi-nc-btn-autollenar')
        };

        this._init();
    }

    _init() {
        // Input de importe en notas
        this.dom.tbodyNotas?.addEventListener('input', (e) => {
            if (e.target.classList.contains('nc-importe-nota')) {
                const id = e.target.closest('tr')?.dataset.notaId;
                if (id) this._actualizarNota(id);
            }
        });

        // Input de importe en facturas
        this.dom.tbodyFacturas?.addEventListener('input', (e) => {
            if (e.target.classList.contains('nc-importe-factura')) {
                const id = e.target.closest('tr')?.dataset.facturaId;
                if (id) this._actualizarFactura(id);
            }
        });

        // Eliminar nota
        this.dom.tbodyNotas?.addEventListener('click', (e) => {
            const btn = e.target.closest('.nc-btn-eliminar-nota');
            if (btn) this.eliminarNota(btn.closest('tr').dataset.notaId);
        });

        // Eliminar factura
        this.dom.tbodyFacturas?.addEventListener('click', (e) => {
            const btn = e.target.closest('.nc-btn-eliminar-factura');
            if (btn) this.eliminarFactura(btn.closest('tr').dataset.facturaId);
        });

        // Auto-llenar
        this.dom.btnAutoLlenar?.addEventListener('click', () => this.autoLlenarTodos());
    }

    // -----------------------------------------------
    // TOGGLE BOTÓN AUTO-LLENAR
    // -----------------------------------------------
    _toggleAutoLlenar() {
        if (!this.dom.containerAutoLlenar) return;
        if (this.notas.length > 0) {
            this.dom.containerAutoLlenar.classList.remove('d-none');
        } else {
            this.dom.containerAutoLlenar.classList.add('d-none');
        }
    }

    // -----------------------------------------------
    // AUTO-LLENAR TODOS LOS INPUTS DE NOTAS
    // -----------------------------------------------
    autoLlenarTodos() {
        if (this.notas.length === 0) {
            toastMixin?.fire({ icon: 'warning', title: 'No hay notas para auto-llenar' });
            return;
        }
        this.notas.forEach(n => {
            n.importeAplicado = n.saldoAnterior;
            n.saldoRestante = 0;
        });
        this._renderNotasTable();
        this._calcularTotales();
        toastMixin?.fire({ icon: 'success', title: 'Notas llenadas al máximo' });
    }

    // -----------------------------------------------
    // AGREGAR NOTA DE CRÉDITO
    // -----------------------------------------------
    agregarNota(nota) {
        const existe = this.notas.find(n => n.id_encabezado === nota.id_encabezado);
        if (existe) {
            toastMixin?.fire({ icon: 'warning', title: 'Esta nota ya fue agregada' });
            return;
        }

        this.notas.push({
            id: `nc_${Date.now()}_${Math.random()}`,
            id_encabezado: nota.id_encabezado,
            serie: nota.serie || '',
            folio: nota.folio || '',
            uuid: nota.uuid || '',
            tipo: nota.tipo || 'NC',
            fecha: typeof dateFormatter === 'function' ? dateFormatter(nota.fch0) : (nota.fch0 || ''),
            total: parseFloat(nota.monto_total) || 0,
            saldoAnterior: parseFloat(nota.saldo_pendiente_real) || 0,
            importeAplicado: 0,
            saldoRestante: parseFloat(nota.saldo_pendiente_real) || 0,
            moneda: nota.moneda || 'MXN',
            esManual: false
        });

        this._renderNotasTable();
        this._calcularTotales();
        this._toggleAutoLlenar();

        toastMixin?.fire({ icon: 'success', title: `Nota agregada: ${nota.folio}` });
    }

    // -----------------------------------------------
    // AGREGAR NOTA MANUAL
    // -----------------------------------------------
    agregarNotaManual(datos) {
        const existe = this.notas.find(n => n.uuid === datos.uuid);
        if (existe) {
            toastMixin?.fire({ icon: 'warning', title: 'Esta nota ya fue agregada' });
            return false;
        }

        this.notas.push({
            id: `nc_manual_${Date.now()}_${Math.random()}`,
            id_encabezado: null,
            serie: datos.serie,
            folio: datos.folio,
            uuid: datos.uuid,
            tipo: datos.tipo,
            fecha: datos.fecha,
            total: parseFloat(datos.total),
            saldoAnterior: parseFloat(datos.saldoPendiente),
            importeAplicado: 0,
            saldoRestante: parseFloat(datos.saldoPendiente),
            moneda: datos.moneda,
            esManual: true
        });

        this._renderNotasTable();
        this._calcularTotales();
        this._toggleAutoLlenar();

        toastMixin?.fire({ icon: 'success', title: `Nota manual: ${datos.serie}-${datos.folio}` });
        return true;
    }

    // -----------------------------------------------
    // AGREGAR FACTURA
    // -----------------------------------------------
    agregarFactura(factura) {
        const existe = this.facturas.find(f => f.id_encabezado === factura.id_encabezado);
        if (existe) {
            toastMixin?.fire({ icon: 'warning', title: 'Esta factura ya fue agregada' });
            return;
        }

        this.facturas.push({
            id: `fac_${Date.now()}_${Math.random()}`,
            id_encabezado: factura.id_encabezado,
            serie: factura.serie || '',
            folio: factura.folio || '',
            uuid: factura.uuid || '',
            fecha: typeof dateFormatter === 'function' ? dateFormatter(factura.fch0) : (factura.fch0 || ''),
            total: parseFloat(factura.monto_total) || 0,
            saldoAnterior: parseFloat(factura.saldo_pendiente_real) || 0,
            importeAplicado: 0,
            saldoRestante: parseFloat(factura.saldo_pendiente_real) || 0,
            moneda: factura.moneda || 'MXN'
        });

        this._renderFacturasTable();
        this._calcularTotales();

        toastMixin?.fire({ icon: 'success', title: `Factura agregada: ${factura.folio}` });
    }

    // -----------------------------------------------
    // ACTUALIZAR NOTA (importe editado por usuario)
    // -----------------------------------------------
    _actualizarNota(id) {
        const nota = this.notas.find(n => n.id === id);
        if (!nota) return;

        const tr = this.dom.tbodyNotas.querySelector(`tr[data-nota-id="${id}"]`);
        const input = tr?.querySelector('.nc-importe-nota');
        if (!input) return;

        let importe = parseFloat(input.value) || 0;

        if (importe > nota.saldoAnterior) {
            importe = nota.saldoAnterior;
            input.value = importe.toFixed(2);
            toastMixin?.fire({ icon: 'warning', title: 'El importe no puede exceder el saldo anterior' });
        }
        if (importe < 0) { importe = 0; input.value = 0; }

        nota.importeAplicado = importe;
        nota.saldoRestante = nota.saldoAnterior - importe;

        const celSaldo = tr.querySelector('.nc-saldo-restante-nota');
        if (celSaldo) celSaldo.textContent = Currency.format(nota.saldoRestante);

        this._calcularTotales();
    }

    // -----------------------------------------------
    // ACTUALIZAR FACTURA (importe editado por usuario)
    // -----------------------------------------------
    _actualizarFactura(id) {
        const factura = this.facturas.find(f => f.id === id);
        if (!factura) return;

        const tr = this.dom.tbodyFacturas.querySelector(`tr[data-factura-id="${id}"]`);
        const input = tr?.querySelector('.nc-importe-factura');
        if (!input) return;

        let importe = parseFloat(input.value) || 0;

        if (importe > factura.saldoAnterior) {
            importe = factura.saldoAnterior;
            input.value = importe.toFixed(2);
            toastMixin?.fire({ icon: 'warning', title: 'El importe no puede exceder el saldo anterior' });
        }
        if (importe < 0) { importe = 0; input.value = 0; }

        factura.importeAplicado = importe;
        factura.saldoRestante = factura.saldoAnterior - importe;

        const celSaldo = tr.querySelector('.nc-saldo-restante-factura');
        if (celSaldo) celSaldo.textContent = Currency.format(factura.saldoRestante);

        this._calcularTotales();
    }

    // -----------------------------------------------
    // ELIMINAR NOTA
    // -----------------------------------------------
    eliminarNota(id) {
        this.notas = this.notas.filter(n => n.id !== id);
        this._renderNotasTable();
        this._calcularTotales();
        this._toggleAutoLlenar();
        toastMixin?.fire({ icon: 'info', title: 'Nota eliminada' });
    }

    // -----------------------------------------------
    // ELIMINAR FACTURA
    // -----------------------------------------------
    eliminarFactura(id) {
        this.facturas = this.facturas.filter(f => f.id !== id);
        this._renderFacturasTable();
        this._calcularTotales();
        toastMixin?.fire({ icon: 'info', title: 'Factura eliminada' });
    }

    // -----------------------------------------------
    // RENDER TABLA NOTAS
    // -----------------------------------------------
    _renderNotasTable() {
        const tbody = this.dom.tbodyNotas;
        if (!tbody) return;
        tbody.innerHTML = '';

        if (this.notas.length === 0) {
            tbody.innerHTML = `
            <tr class="vi-nc-empty-state">
                <td colspan="9" class="text-center py-4">
                    <i class="fas fa-file-invoice-dollar fa-3x text-muted mb-3 d-block"></i>
                    <p class="text-muted mb-0">No hay notas de crédito agregadas</p>
                </td>
            </tr>`;
            return;
        }

        const frag = document.createDocumentFragment();
        this.notas.forEach(n => {
            const tr = document.createElement('tr');
            tr.dataset.notaId = n.id;
            const folio = n.serie ? `${n.serie}-${n.folio}` : n.folio;
            tr.innerHTML = `
            <td class="text-center">${this._esc(folio)}</td>
            <td class="text-truncate" style="max-width:200px" title="${this._esc(n.uuid)}">${this._esc(n.uuid)}</td>
            <td><span class="badge bg-warning text-dark">${this._esc(n.tipo)}</span></td>
            <td class="text-center">${this._esc(n.fecha)}</td>
            <td class="text-end fw-bold">${Currency.format(n.total)}</td>
            <td class="text-end">${Currency.format(n.saldoAnterior)}</td>
            <td>
                <input type="number"
                       class="nc-importe-nota form-control form-control-sm text-end"
                       min="0" max="${n.saldoAnterior}" step="0.01"
                       value="${n.importeAplicado.toFixed(2)}">
            </td>
            <td class="nc-saldo-restante-nota text-end fw-bold text-success">
                ${Currency.format(n.saldoRestante)}
            </td>
            <td class="text-center">
                <button type="button" class="btn btn-sm btn-danger nc-btn-eliminar-nota">
                    <i class="fas fa-trash"></i>
                </button>
            </td>`;
            frag.appendChild(tr);
        });
        tbody.appendChild(frag);
    }

    // -----------------------------------------------
    // RENDER TABLA FACTURAS
    // -----------------------------------------------
    _renderFacturasTable() {
        const tbody = this.dom.tbodyFacturas;
        if (!tbody) return;
        tbody.innerHTML = '';

        if (this.facturas.length === 0) {
            tbody.innerHTML = `
            <tr class="vi-nc-empty-state">
                <td colspan="8" class="text-center py-4">
                    <i class="fas fa-file-invoice fa-3x text-muted mb-3 d-block"></i>
                    <p class="text-muted mb-0">No hay facturas seleccionadas</p>
                </td>
            </tr>`;
            return;
        }

        const frag = document.createDocumentFragment();
        this.facturas.forEach(f => {
            const tr = document.createElement('tr');
            tr.dataset.facturaId = f.id;
            const folio = f.serie ? `${f.serie}-${f.folio}` : f.folio;
            tr.innerHTML = `
            <td class="text-center">${this._esc(folio)}</td>
            <td class="text-truncate" style="max-width:200px" title="${this._esc(f.uuid)}">${this._esc(f.uuid)}</td>
            <td class="text-center">${this._esc(f.fecha)}</td>
            <td class="text-end fw-bold">${Currency.format(f.total)}</td>
            <td class="text-end">${Currency.format(f.saldoAnterior)}</td>
            <td>
                <input type="number"
                       class="nc-importe-factura form-control form-control-sm text-end"
                       min="0" max="${f.saldoAnterior}" step="0.01"
                       value="${f.importeAplicado.toFixed(2)}">
            </td>
            <td class="nc-saldo-restante-factura text-end fw-bold text-danger">
                ${Currency.format(f.saldoRestante)}
            </td>
            <td class="text-center">
                <button type="button" class="btn btn-sm btn-danger nc-btn-eliminar-factura">
                    <i class="fas fa-trash"></i>
                </button>
            </td>`;
            frag.appendChild(tr);
        });
        tbody.appendChild(frag);
    }

    // -----------------------------------------------
    // CALCULAR Y ACTUALIZAR TOTALES
    // -----------------------------------------------
    _calcularTotales() {
        const totalNotas = this.notas.reduce((s, n) => s + n.total, 0);
        const saldoNCTotal = this.notas.reduce((s, n) => s + n.saldoAnterior, 0);
        const montoAplicadoNC = this.notas.reduce((s, n) => s + n.importeAplicado, 0);
        const saldoNCRestante = this.notas.reduce((s, n) => s + n.saldoRestante, 0);

        const totalFacturas = this.facturas.reduce((s, f) => s + f.total, 0);
        const saldoFacTotal = this.facturas.reduce((s, f) => s + f.saldoAnterior, 0);
        const montoAplicadoFac = this.facturas.reduce((s, f) => s + f.importeAplicado, 0);
        const saldoFacRestante = this.facturas.reduce((s, f) => s + f.saldoRestante, 0);

        // El monto aplicado real es el menor entre lo que dan las notas y lo que reciben las facturas
        const montoAplicado = Math.min(montoAplicadoNC, montoAplicadoFac) || montoAplicadoNC;
        this.totalAplicacion = montoAplicado;

        // Diferencia
        const diferencia = Math.abs(montoAplicadoNC - montoAplicadoFac);

        // Actualizar displays
        const fmt = (el, val) => { if (el) el.textContent = Currency.format(val); };

        fmt(this.dom.totalNotasDisplay, totalNotas);
        fmt(this.dom.saldoNCDisplay, saldoNCTotal);
        fmt(this.dom.totalFacturasDisplay, totalFacturas);
        fmt(this.dom.saldoFacturasDisplay, saldoFacTotal);
        fmt(this.dom.montoAplicadoDisplay, montoAplicado);
        fmt(this.dom.saldoNCRestanteDisplay, saldoNCRestante);
        fmt(this.dom.saldoFacRestanteDisplay, saldoFacRestante);
        fmt(this.dom.totalAplicacionDisplay, this.totalAplicacion);

        // Alerta de diferencia
        if (diferencia > 0.01 && (montoAplicadoNC > 0 || montoAplicadoFac > 0)) {
            if (this.dom.alertaDiferencia) this.dom.alertaDiferencia.classList.remove('d-none');
            fmt(this.dom.diferenciaDisplay, diferencia);
        } else {
            if (this.dom.alertaDiferencia) this.dom.alertaDiferencia.classList.add('d-none');
        }

        // Hidden input
        const hidden = document.getElementById('vi-nc-total-aplicacion');
        if (hidden) hidden.value = this.totalAplicacion.toFixed(2);
    }

    // -----------------------------------------------
    // OBTENER DATOS PARA ENVIAR
    // -----------------------------------------------
    getNotasData() {
        return this.notas.map(n => ({
            id_encabezado: n.id_encabezado,
            serie: n.serie,
            folio: n.folio,
            uuid: n.uuid,
            tipo: n.tipo,
            fecha: n.fecha,
            total: n.total,
            saldoAnterior: n.saldoAnterior,
            importeAplicado: n.importeAplicado,
            saldoRestante: n.saldoRestante,
            moneda: n.moneda,
            esManual: n.esManual
        }));
    }

    getFacturasData() {
        return this.facturas.map(f => ({
            id_encabezado: f.id_encabezado,
            serie: f.serie,
            folio: f.folio,
            uuid: f.uuid,
            fecha: f.fecha,
            total: f.total,
            saldoAnterior: f.saldoAnterior,
            importeAplicado: f.importeAplicado,
            saldoRestante: f.saldoRestante,
            moneda: f.moneda
        }));
    }

    // -----------------------------------------------
    // VALIDAR
    // -----------------------------------------------
    validar() {
        const errores = [];
        if (this.notas.length === 0)
            errores.push('Debe agregar al menos una nota de crédito');

        const notasSinImporte = this.notas.filter(n => n.importeAplicado <= 0);
        if (notasSinImporte.length > 0)
            errores.push('Todas las notas deben tener un importe aplicado mayor a 0');

        if (this.totalAplicacion <= 0)
            errores.push('El total de la aplicación debe ser mayor a 0');

        return errores;
    }

    // -----------------------------------------------
    // LIMPIAR
    // -----------------------------------------------
    limpiar() {
        this.notas = [];
        this.facturas = [];
        this.totalAplicacion = 0;
        this._renderNotasTable();
        this._renderFacturasTable();
        this._calcularTotales();
        this._toggleAutoLlenar();
    }

    _esc(text) {
        const d = document.createElement('div');
        d.textContent = text;
        return d.innerHTML;
    }
}

// ============================================
// 6. GESTOR DEL FORMULARIO
// ============================================
class NCFormManager {
    constructor(aplicacionManager) {
        this.aplicacionManager = aplicacionManager;
        this.form = document.getElementById('vi-nc-formAplicacion');
        this.btnSubmit = document.getElementById('vi-nc-btn-generar');
        this._init();
    }

    _init() {
        this.btnSubmit?.addEventListener('click', (e) => {
            e.preventDefault();
            this._enviar();
        });
    }

    _recopilarDatos() {
        const fd = new FormData();

        const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
        if (token) fd.append('__RequestVerificationToken', token);

        const campos = [
            'vi-nc-sucursal',
            'vi-nc-almacen',
            'vi-nc-cliente',
            'vi-nc-rfc',
            'vi-nc-moneda',
            'vi-nc-paridad',
            'vi-nc-concepto',
            'vi-nc-fecha-aplicacion',
            'vi-nc-uso-cfdi',
            'vi-nc-tipo-aplicacion',
            'vi-nc-banco',
            'vi-nc-comentarios'
        ];

        campos.forEach(id => {
            const el = document.getElementById(id);
            if (el) fd.append(id.replace('vi-nc-', ''), el.value || '');
        });

        fd.append('notasJSON', JSON.stringify(this.aplicacionManager.getNotasData()));
        fd.append('facturasJSON', JSON.stringify(this.aplicacionManager.getFacturasData()));
        fd.append('totalAplicacion', this.aplicacionManager.totalAplicacion.toFixed(2));

        return fd;
    }

    _validar() {
        const errores = [];

        const cliente = document.getElementById('vi-nc-cliente')?.value;
        if (!cliente || !cliente.trim())
            errores.push('Debe seleccionar un cliente');

        const fecha = document.getElementById('vi-nc-fecha-aplicacion')?.value;
        if (!fecha || !fecha.trim())
            errores.push('Debe ingresar la fecha de aplicación');

        const tipo = document.getElementById('vi-nc-tipo-aplicacion')?.value;
        if (!tipo || !tipo.trim())
            errores.push('Debe seleccionar el tipo de aplicación');

        errores.push(...this.aplicacionManager.validar());
        return errores;
    }

    async _enviar() {
        const errores = this._validar();
        if (errores.length > 0) {
            mostrarErrorPorPaso({ message: errores.join(', ') });
            return;
        }

        const progressModal = mostrarModalProgresoMejorado(1);
        const originalText = this.btnSubmit.innerHTML;
        this.btnSubmit.disabled = true;
        this.btnSubmit.innerHTML = `<i class="fas fa-spinner fa-spin"></i> Procesando...`;

        try {
            const fd = this._recopilarDatos();
            actualizarProgresoDetallado(progressModal, 1, 'Enviando datos de la aplicación...');

            const res = await fetch('/AplicacionNotas/ProcesarAplicacionAsync', {
                method: 'POST',
                body: fd
            });

            if (!res.ok) {
                cerrarModalProgreso(progressModal);
                mostrarErrorServidor(res.status, await res.text());
                return;
            }

            let result;
            try { result = await res.json(); }
            catch { cerrarModalProgreso(progressModal); mostrarErrorFormato(); return; }

            if (result.success) await simularProgresoExitoso(progressModal);
            cerrarModalProgreso(progressModal);

            if (result.success) {
                mostrarResumenExitoso(result, 1);
                this._limpiar();
            } else {
                mostrarErrorPorPaso(result);
            }
        } catch (err) {
            cerrarModalProgreso(progressModal);
            mostrarErrorConexion(err);
        } finally {
            this.btnSubmit.disabled = false;
            this.btnSubmit.innerHTML = originalText;
        }
    }

    _limpiar() {
        this.form?.reset();
        this.aplicacionManager.limpiar();
        ['vi-nc-cliente', 'vi-nc-rfc', 'vi-nc-info-cliente', 'vi-nc-fecha-aplicacion'].forEach(id => {
            const el = document.getElementById(id);
            if (el) el.value = '';
        });
    }
}

// ============================================
// 7. APLICACIÓN PRINCIPAL
// ============================================
const AplicacionNotasApp = {
    clienteManager: null,
    notasManager: null,
    facturasManager: null,
    aplicacionManager: null,
    formManager: null,

    init() {
        // ── CLIENTES ──────────────────────────────────
        this.clienteManager = new NCClienteManager({
            endpoint: '/DatosGenerales/BuscarC',
            detailEndpoint: '/DatosGenerales/BuscarCliente',
            modalId: 'vi-nc-modalBuscarCliente',
            inputId: 'vi-nc-inputBuscarCliente',
            resultsId: 'vi-nc-listaResultadosClientes',
            pageSizeId: 'vi-nc-pageSizeClientes',
            btnClearId: 'vi-nc-btnLimpiarClientes',
            spinnerId: 'vi-nc-spinnerClientes',
            paginationId: 'vi-nc-paginationClientes',
            recordsFromId: 'vi-nc-recordsFromClientes',
            recordsToId: 'vi-nc-recordsToClientes',
            totalRecordsId: 'vi-nc-totalRecordsClientes',
            shouldCloseOnSelect: true
        });

        this.clienteManager.onSelect = (cliente) => {
            document.getElementById('vi-nc-cliente').value = cliente.id || '';
            document.getElementById('vi-nc-rfc').value = cliente.rfc || '';
            document.getElementById('vi-nc-info-cliente').value =
                [cliente.dir, cliente.col, cliente.pob, cliente.cp]
                    .filter(x => x).join(',\n');

            // Refrescar ambos modales de búsqueda con el cliente nuevo
            this.notasManager?.setClienteActual(cliente.id);
            this.facturasManager?.setClienteActual(cliente.id);
        };

        // ── GESTOR DE APLICACIÓN ──────────────────────
        this.aplicacionManager = new AplicacionNotasManager();

        // ── NOTAS DE CRÉDITO ──────────────────────────
        this.notasManager = new NCNotasManager({
            endpoint: '/DatosGenerales/BuscarNotasCredito',
            detailEndpoint: '/DatosGenerales/BuscarNotaDetalle',
            modalId: 'vi-nc-modalBuscarNotas',
            inputId: 'vi-nc-inputBuscarNota',
            resultsId: 'vi-nc-listaResultadosNotas',
            pageSizeId: 'vi-nc-pageSizeNotas',
            btnClearId: 'vi-nc-btnLimpiarNotas',
            spinnerId: 'vi-nc-spinnerNotas',
            paginationId: 'vi-nc-paginationNotas',
            recordsFromId: 'vi-nc-recordsFromNotas',
            recordsToId: 'vi-nc-recordsToNotas',
            totalRecordsId: 'vi-nc-totalRecordsNotas',
            shouldCloseOnSelect: false
        });

        // Bloquear modal de notas si no hay cliente
        document.getElementById('vi-nc-modalBuscarNotas')
            ?.addEventListener('show.bs.modal', (e) => {
                const clienteId = document.getElementById('vi-nc-cliente')?.value;
                if (!clienteId?.trim()) {
                    e.preventDefault();
                    toastMixin?.fire({ icon: 'warning', title: 'Debe seleccionar un cliente primero' });
                }
            });

        this.notasManager.onSelect = (nota) => {
            this.aplicacionManager.agregarNota(nota);
        };

        // ── FACTURAS A AFECTAR ────────────────────────
        this.facturasManager = new NCFacturasManager({
            endpoint: '/DatosGenerales/BuscarFacturasPendientes',
            detailEndpoint: '/DatosGenerales/BuscarFacturaDetalle',
            modalId: 'vi-nc-modalBuscarFacturas',
            inputId: 'vi-nc-inputBuscarFactura',
            resultsId: 'vi-nc-listaResultadosFacturas',
            pageSizeId: 'vi-nc-pageSizeFacturas',
            btnClearId: 'vi-nc-btnLimpiarFacturas',
            spinnerId: 'vi-nc-spinnerFacturas',
            paginationId: 'vi-nc-paginationFacturas',
            recordsFromId: 'vi-nc-recordsFromFacturas',
            recordsToId: 'vi-nc-recordsToFacturas',
            totalRecordsId: 'vi-nc-totalRecordsFacturas',
            shouldCloseOnSelect: false
        });

        // Bloquear modal de facturas si no hay cliente
        document.getElementById('vi-nc-modalBuscarFacturas')
            ?.addEventListener('show.bs.modal', (e) => {
                const clienteId = document.getElementById('vi-nc-cliente')?.value;
                if (!clienteId?.trim()) {
                    e.preventDefault();
                    toastMixin?.fire({ icon: 'warning', title: 'Debe seleccionar un cliente primero' });
                }
            });

        this.facturasManager.onSelect = (factura) => {
            this.aplicacionManager.agregarFactura(factura);
        };

        // ── FORMULARIO ────────────────────────────────
        this.formManager = new NCFormManager(this.aplicacionManager);

        // ── FORMULARIO MANUAL DE NOTAS ────────────────
        this._initFormManualNota();

        console.log('Sistema de aplicación de notas de crédito inicializado');
    },

    _initFormManualNota() {
        const form = document.getElementById('vi-nc-formNotaManual');
        const btnLimpiar = document.getElementById('vi-nc-btn-limpiar-manual-nota');
        const uuidInput = document.getElementById('vi-nc-manual-uuid');
        const totalInput = document.getElementById('vi-nc-manual-total');
        const saldoInput = document.getElementById('vi-nc-manual-saldo');
        const regexUUID = /^[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}$/;

        // Validar UUID en tiempo real
        uuidInput?.addEventListener('input', (e) => {
            const val = e.target.value.toUpperCase();
            e.target.value = val;
            if (val.length === 36) {
                e.target.classList.toggle('is-valid', regexUUID.test(val));
                e.target.classList.toggle('is-invalid', !regexUUID.test(val));
            } else {
                e.target.classList.remove('is-valid', 'is-invalid');
            }
        });

        // Saldo no puede superar total
        saldoInput?.addEventListener('input', () => {
            const total = parseFloat(totalInput?.value) || 0;
            const saldo = parseFloat(saldoInput.value) || 0;
            if (saldo > total) {
                saldoInput.value = total;
                toastMixin?.fire({ icon: 'warning', title: 'El saldo no puede ser mayor al total' });
            }
        });

        // Submit manual
        form?.addEventListener('submit', (e) => {
            e.preventDefault();

            const datos = {
                serie: document.getElementById('vi-nc-manual-serie').value.trim(),
                folio: document.getElementById('vi-nc-manual-folio').value.trim(),
                tipo: document.getElementById('vi-nc-manual-tipo').value,
                uuid: document.getElementById('vi-nc-manual-uuid').value.trim().toUpperCase(),
                fecha: document.getElementById('vi-nc-manual-fecha').value,
                total: document.getElementById('vi-nc-manual-total').value,
                saldoPendiente: document.getElementById('vi-nc-manual-saldo').value,
                moneda: document.getElementById('vi-nc-manual-moneda').value
            };

            if (!regexUUID.test(datos.uuid)) {
                toastMixin?.fire({ icon: 'error', title: 'UUID con formato inválido' });
                return;
            }

            const ok = this.aplicacionManager.agregarNotaManual(datos);
            if (ok) {
                form.reset();
                document.getElementById('vi-nc-manual-fecha').valueAsDate = new Date();
                bootstrap.Modal.getInstance(
                    document.getElementById('vi-nc-modalBuscarNotas')
                )?.hide();
            }
        });

        // Limpiar manual
        btnLimpiar?.addEventListener('click', () => {
            form?.reset();
            document.getElementById('vi-nc-manual-fecha').valueAsDate = new Date();
            document.querySelectorAll('.is-valid, .is-invalid')
                .forEach(el => el.classList.remove('is-valid', 'is-invalid'));
        });

        // Fecha por defecto al abrir tab
        document.getElementById('manual-nota-tab')
            ?.addEventListener('shown.bs.tab', () => {
                const fechaInput = document.getElementById('vi-nc-manual-fecha');
                if (fechaInput && !fechaInput.value)
                    fechaInput.valueAsDate = new Date();
            });
    }
};

// ============================================
// 8. INICIALIZACIÓN GLOBAL
// ============================================
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => AplicacionNotasApp.init());
} else {
    AplicacionNotasApp.init();
}

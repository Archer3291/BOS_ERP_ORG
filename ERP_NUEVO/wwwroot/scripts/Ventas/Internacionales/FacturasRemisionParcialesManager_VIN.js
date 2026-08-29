class VINFacturasRemisionParcialesManager {
    constructor() {
        this.remisionesSeleccionadas = new Map();
        this.encabezadosSeleccionados = new Map();
        this.modoActual = 'parciales';

        this.dom = {
            modal: document.getElementById('vin-fac-modalRemisionesParciales'),
            inputBuscar: document.getElementById('vin-fac-inputBuscarRemision'),
            listaRemisiones: document.getElementById('vin-fac-listaRemisiones'),
            tablaPartidas: document.getElementById('vin-fac-tablaPartidas'),
            tbodyPartidas: document.getElementById('vin-fac-tbodyPartidas'),
            btnAgregarSeleccion: document.getElementById('vin-fac-btnAgregarSeleccion'),
            resumenSeleccion: document.getElementById('vin-fac-resumenSeleccion'),
            spinner: document.getElementById('vin-fac-spinnerRemisiones'),
            pagination: document.getElementById('vin-fac-paginationRemisiones'),
            totalRegistros: document.getElementById('vin-fac-totalRemisiones'),
            selectorModo: document.getElementById('vin-fac-selectorModo'),
        };

        this.state = {
            page: 1,
            pageSize: 25,
            busqueda: '',
            remisionActivaId: null,
            loading: false,
            totalRecords: 0,
        };

        this.typingTimer = null;
        this._init();
    }

    _init() {
        this.dom.modal?.addEventListener('show.bs.modal', () => this._onModalShow());

        this.dom.inputBuscar?.addEventListener('input', (e) => {
            clearTimeout(this.typingTimer);
            this.typingTimer = setTimeout(() => {
                this.state.busqueda = e.target.value;
                this.state.page = 1;
                this._cargarListaRemisiones();
            }, 300);
        });

        this.dom.selectorModo?.addEventListener('change', (e) => {
            this.modoActual = e.target.value;
            this.state.page = 1;
            this._mostrarTablaPartidas(false);
            this._cargarListaRemisiones();
        });

        this.dom.listaRemisiones?.addEventListener('click', (e) => {
            const item = e.target.closest('[data-remision-id]');
            if (!item) return;

            const remisionId = parseInt(item.dataset.remisionId);

            if (this.modoActual === 'multiple') {
                this._toggleSeleccionMultiple(remisionId, item);
            } else {
                this._seleccionarRemision(remisionId, item);
            }
        });

        this.dom.tbodyPartidas?.addEventListener('input', (e) => {
            if (e.target.classList.contains('vin-fac-cant-facturar')) {
                this._validarCantidad(e.target);
                this._actualizarImportePartida(e.target.closest('tr'));
            }
        });

        this.dom.tbodyPartidas?.addEventListener('change', (e) => {
            if (e.target.classList.contains('vin-fac-chk-partida')) {
                const tr = e.target.closest('tr');
                const inputCant = tr.querySelector('.vin-fac-cant-facturar');
                if (inputCant) inputCant.disabled = !e.target.checked;
                this._actualizarImportePartida(tr);
            }
        });

        this.dom.btnAgregarSeleccion?.addEventListener('click', () =>
            this._agregarPartidasSeleccionadas());

        this.dom.pagination?.addEventListener('click', (e) => {
            e.preventDefault();
            const btn = e.target.closest('[data-page]');
            if (btn) {
                this.state.page = parseInt(btn.dataset.page);
                this._cargarListaRemisiones();
            }
        });
    }

    _onModalShow() {
        this.dom.inputBuscar.value = '';
        this.state.busqueda = '';
        this.state.page = 1;
        this.state.remisionActivaId = null;
        this._mostrarTablaPartidas(false);
        this._cargarListaRemisiones();
        this._actualizarResumen();
        if (this.dom.selectorModo)
            this.dom.selectorModo.value = this.modoActual;
    }

    async _cargarListaRemisiones() {
        this._setSpinner(true);
        try {
            const clienteId = document.getElementById('vin-fac-cliente')?.value || '';
            const modoServidor = this.modoActual === 'parciales' ? 'parciales' : 'documento';

            const params = new URLSearchParams({
                nombre: this.state.busqueda,
                page: this.state.page,
                pageSize: this.state.pageSize,
                cliente: clienteId,
                modo: modoServidor,
            });

            // ── ENDPOINT INTERNACIONAL ──────────────────────────
            const resp = await fetch(`/DatosGenerales/BuscarDVINrem?${params}`);
            if (!resp.ok) throw new Error(`HTTP ${resp.status}`);

            const data = await resp.json();
            this.state.totalRecords = data.total || 0;
            this._renderListaRemisiones(data.items || []);
            this._renderPaginacion();
            if (this.dom.totalRegistros)
                this.dom.totalRegistros.textContent = this.state.totalRecords;
        } catch (err) {
            console.error('Error cargando remisiones VIN:', err);
            this.dom.listaRemisiones.innerHTML = `
                <li class="list-group-item text-danger text-center py-4">
                    <i class="fas fa-exclamation-triangle me-2"></i>
                    Error al cargar remisiones: ${this._esc(err.message)}
                </li>`;
        } finally {
            this._setSpinner(false);
        }
    }

    _renderListaRemisiones(items) {
        const ul = this.dom.listaRemisiones;
        ul.innerHTML = '';

        if (items.length === 0) {
            ul.innerHTML = `
                <li class="list-group-item text-center text-muted py-5">
                    <i class="fas fa-check-circle fa-3x mb-3 opacity-25 d-block"></i>
                    No hay remisiones internacionales disponibles
                </li>`;
            return;
        }

        items.forEach(r => {
            const yaCargada = this.encabezadosSeleccionados.has(r.id_encabezado);
            const esMultiple = this.modoActual === 'multiple';

            const li = document.createElement('li');
            li.className = `list-group-item list-group-item-action d-flex
                            justify-content-between align-items-start
                            ${yaCargada ? 'list-group-item-success' : ''}`;
            li.dataset.remisionId = r.id_encabezado;
            li.dataset.folio = r.folio;
            li.dataset.importe = r.imp || 0;
            li.dataset.clienteProv = r.cli_prov || '';
            li.style.cursor = 'pointer';

            const badgeSel = yaCargada
                ? '<span class="badge bg-success ms-2"><i class="fas fa-check"></i> Seleccionada</span>'
                : '';

            const importeLabel = esMultiple
                ? `<span class="fw-bold">$${this._fmt(r.imp || 0)}</span>`
                : `<span class="text-success fw-bold">$${this._fmt(r.importe_pendiente)}</span>`;

            const badgesPendientes = !esMultiple ? `
                <span class="badge bg-warning text-dark">
                    <i class="fas fa-clock me-1"></i>${r.partidas_pendientes || 0} pend.
                </span>
                <span class="badge bg-info text-dark">
                    <i class="fas fa-adjust me-1"></i>${r.partidas_parciales || 0} parc.
                </span>` : '';

            li.innerHTML = `
                <div class="flex-grow-1">
                    <div class="d-flex justify-content-between">
                        <h6 class="mb-1 fw-bold">
                            <i class="fas fa-file-alt me-2"></i>${this._esc(r.folio)}
                            ${badgeSel}
                        </h6>
                        <small class="text-muted">${this._esc(dateFormatter(r.fecha) || '')}</small>
                    </div>
                    <p class="mb-1 text-muted small">
                        <i class="fas fa-user me-1"></i>${this._esc(r.cli_prov || '')}
                    </p>
                    <div class="d-flex gap-3 small mt-1">
                        ${badgesPendientes}
                        ${importeLabel}
                    </div>
                </div>
                <i class="fas fa-${esMultiple ? 'check-square' : 'chevron-right'} text-muted mt-1 ms-2"></i>`;
            ul.appendChild(li);
        });
    }

    _toggleSeleccionMultiple(remisionId, liEl) {
        if (this.encabezadosSeleccionados.has(remisionId)) {
            this.encabezadosSeleccionados.delete(remisionId);
            for (const [key] of this.remisionesSeleccionadas) {
                if (key.startsWith(`${remisionId}_`))
                    this.remisionesSeleccionadas.delete(key);
            }
            liEl.classList.remove('list-group-item-success');
            liEl.querySelector('.badge.bg-success')?.remove();
        } else {
            this.encabezadosSeleccionados.set(remisionId, {
                remisionId,
                folio: liEl.dataset.folio,
                importe: parseFloat(liEl.dataset.importe || 0),
                clienteProv: liEl.dataset.clienteProv,
                esCompleta: true,
            });
            const key = `${remisionId}_COMPLETA`;
            this.remisionesSeleccionadas.set(key, {
                remisionId,
                folio: liEl.dataset.folio,
                esCompleta: true,
                cantidadAFacturar: null,
                precio_unitario: 0,
                descuento: 0,
                id_partida_remision: 'ALL',
            });
            liEl.classList.add('list-group-item-success');
            const titulo = liEl.querySelector('h6');
            if (titulo && !titulo.querySelector('.badge.bg-success')) {
                titulo.insertAdjacentHTML('beforeend',
                    ' <span class="badge bg-success ms-2"><i class="fas fa-check"></i> Seleccionada</span>');
            }
        }
        this._actualizarResumen();
    }

    async _seleccionarRemision(remisionId, liEl) {
        this.dom.listaRemisiones
            .querySelectorAll('.list-group-item-action')
            .forEach(el => el.classList.remove('active'));
        liEl.classList.add('active');

        this.state.remisionActivaId = remisionId;
        this._setSpinner(true);
        this._mostrarTablaPartidas(false);

        try {
            // Mismo endpoint de partidas — el servidor ya devuelve
            // la estructura correcta para remisiones internacionales
            const resp = await fetch(
                `/DatosGenerales/BuscarPartidasRemision?id=${remisionId}`
            );
            const data = await resp.json();
            if (!data.success) throw new Error(data.message);

            const doc = data.result[0];

            if (!doc.productos || doc.productos.length === 0) {
                this._mostrarSinPartidas(doc);
                return;
            }

            this._renderTablaPartidas(doc.productos, doc, remisionId);
            this._mostrarTablaPartidas(true);
        } catch (err) {
            console.error('Error cargando partidas VIN:', err);
            Swal.fire({ icon: 'error', title: 'Error', text: err.message });
        } finally {
            this._setSpinner(false);
        }
    }

    _mostrarSinPartidas(doc) {
        this.dom.tbodyPartidas.innerHTML = `
            <tr><td colspan="9" class="text-center text-muted py-4">
                <i class="fas fa-check-circle text-success me-2"></i>
                Todas las partidas de <strong>${this._esc(doc.folio)}</strong>
                ya fueron facturadas completamente
            </td></tr>`;
        this._mostrarTablaPartidas(true);
    }

    _renderTablaPartidas(partidas, encabezado, remisionId) {
        const tbody = this.dom.tbodyPartidas;
        tbody.innerHTML = '';

        partidas.forEach(p => {
            const idPartida = p.id_partida_remision || p.id;
            const key = `${remisionId}_${idPartida}`;
            const yaEnSelec = this.remisionesSeleccionadas.has(key);
            const cantPrevio = yaEnSelec
                ? this.remisionesSeleccionadas.get(key).cantidadAFacturar
                : p.cantidad_pendiente;

            const tr = document.createElement('tr');
            tr.dataset.partidaId = idPartida;
            tr.dataset.remisionId = remisionId;

            tr.innerHTML = `
                <td class="text-center">
                    <input type="checkbox" class="vin-fac-chk-partida form-check-input" checked>
                </td>
                <td class="fw-bold text-primary">${this._esc(p.cve_prod)}</td>
                <td style="max-width:200px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;"
                    title="${this._esc(p.descripcion)}">${this._esc(p.descripcion)}</td>
                <td class="text-center">
                    <span class="badge ${p.estatus === 'parcial' ? 'bg-warning text-dark' : 'bg-success'}">
                        ${this._esc(p.estatus)}
                    </span>
                </td>
                <td class="text-end">${this._fmt(p.cantidad_original)}</td>
                <td class="text-end text-success fw-bold">${this._fmt(p.cantidad_pendiente)}</td>
                <td class="text-center">
                    <input type="number"
                           class="vin-fac-cant-facturar form-control form-control-sm text-end"
                           value="${this._fmt(cantPrevio)}"
                           min="0.0001"
                           max="${p.cantidad_pendiente}"
                           step="0.0001"
                           data-pendiente="${p.cantidad_pendiente}"
                           data-precio="${p.precio_unitario}"
                           data-descuento="${p.descuento}"
                           style="width:100px;">
                </td>
                <td class="text-end fw-bold vin-fac-importe-partida">
                    $${this._fmt(cantPrevio * p.precio_unitario * (1 - p.descuento / 100))}
                </td>
                <td class="text-end text-muted" style="font-size:0.78rem">
                    $${this._fmt(p.importe_pendiente_partida || 0)}
                </td>`;

            tr.dataset.partidaData = JSON.stringify({
                id_partida_remision: idPartida,
                remisionId,
                folio: encabezado?.folio || '',
                cve_prod: p.cve_prod,
                descripcion: p.descripcion,
                unidad: p.unidad,
                precio_unitario: p.precio_unitario,
                descuento: p.descuento,
                cantidad_pendiente: p.cantidad_pendiente,
                pedimentos: p.pedimentos || '[]',
                // Datos específicos de comercio exterior
                fraccion_arancelaria: p.fraccion_arancelaria || '',
                unidad_aduana: p.unidad_aduana || '',
                valor_dolares: p.valor_dolares || 0,
            });

            tbody.appendChild(tr);
        });

        const header = document.getElementById('vin-fac-headerTablaPartidas');
        if (header)
            header.textContent =
                `Partidas de: ${encabezado?.folio || ''} — ${encabezado?.cli_prov || encabezado?.n_cli || ''}`;
    }

    _validarCantidad(input) {
        const max = parseFloat(input.dataset.pendiente) || 0;
        let val = parseFloat(input.value) || 0;
        if (val < 0) val = 0;
        if (val > max) {
            val = max;
            input.value = max;
            input.classList.add('is-invalid');
            setTimeout(() => input.classList.remove('is-invalid'), 1500);
            toastMixin?.fire({ icon: 'warning', title: `Máximo disponible: ${this._fmt(max)}` });
        }
    }

    _actualizarImportePartida(tr) {
        const chk = tr.querySelector('.vin-fac-chk-partida');
        const input = tr.querySelector('.vin-fac-cant-facturar');
        const celdaImporte = tr.querySelector('.vin-fac-importe-partida');
        if (!chk || !input || !celdaImporte) return;

        if (!chk.checked) { celdaImporte.textContent = '$0.00'; return; }

        const cant = parseFloat(input.value) || 0;
        const precio = parseFloat(input.dataset.precio) || 0;
        const dto = parseFloat(input.dataset.descuento) || 0;
        celdaImporte.textContent = `$${this._fmt(cant * precio * (1 - dto / 100))}`;
    }

    _agregarPartidasSeleccionadas() {
        const rows = this.dom.tbodyPartidas.querySelectorAll('tr[data-partida-id]');
        let agregadas = 0;

        rows.forEach(tr => {
            const chk = tr.querySelector('.vin-fac-chk-partida');
            if (!chk?.checked) return;

            const inputCant = tr.querySelector('.vin-fac-cant-facturar');
            const cant = parseFloat(inputCant?.value) || 0;
            if (cant <= 0) return;

            const max = parseFloat(inputCant?.dataset.pendiente) || 0;
            if (cant > max) {
                toastMixin?.fire({ icon: 'error', title: `Cantidad excede el pendiente (${this._fmt(max)})` });
                return;
            }

            const datos = JSON.parse(tr.dataset.partidaData || '{}');
            const key = `${datos.remisionId}_${datos.id_partida_remision}`;

            this.remisionesSeleccionadas.set(key, {
                ...datos,
                cantidadAFacturar: cant,
                esCompleta: false,
            });

            if (!this.encabezadosSeleccionados.has(datos.remisionId)) {
                this.encabezadosSeleccionados.set(datos.remisionId, {
                    remisionId: datos.remisionId,
                    folio: datos.folio,
                });
            }
            agregadas++;
        });

        if (agregadas === 0) {
            toastMixin?.fire({ icon: 'warning', title: 'Selecciona al menos una partida con cantidad > 0' });
            return;
        }

        this._actualizarResumen();
        this._refrescarEstadoListaItem(this.state.remisionActivaId);
        toastMixin?.fire({ icon: 'success', title: `${agregadas} partida(s) agregada(s)` });
    }

    _refrescarEstadoListaItem(remisionId) {
        const li = this.dom.listaRemisiones
            .querySelector(`[data-remision-id="${remisionId}"]`);
        if (!li) return;
        li.classList.add('list-group-item-success');
        const titulo = li.querySelector('h6');
        if (titulo && !titulo.querySelector('.badge.bg-success')) {
            titulo.insertAdjacentHTML('beforeend',
                ' <span class="badge bg-success ms-2"><i class="fas fa-check"></i> En selección</span>');
        }
    }

    _actualizarResumen() {
        const contenedor = this.dom.resumenSeleccion;
        if (!contenedor) return;

        if (this.remisionesSeleccionadas.size === 0) {
            contenedor.innerHTML = `
                <div class="text-center text-muted py-3">
                    <i class="fas fa-info-circle me-2"></i>Sin partidas seleccionadas
                </div>`;
            return;
        }

        const porRemision = new Map();
        this.remisionesSeleccionadas.forEach((datos) => {
            if (!porRemision.has(datos.remisionId))
                porRemision.set(datos.remisionId, {
                    folio: datos.folio,
                    partidas: [],
                    subtotal: 0,
                    esCompleta: datos.esCompleta || false,
                });
            const grupo = porRemision.get(datos.remisionId);

            if (datos.esCompleta) {
                const importe = datos.importe || 0;
                grupo.partidas.push({ ...datos, importe });
                grupo.subtotal += importe;
            } else {
                const importe = datos.cantidadAFacturar
                    * datos.precio_unitario
                    * (1 - datos.descuento / 100);
                grupo.partidas.push({ ...datos, importe });
                grupo.subtotal += importe;
            }
        });

        let totalGlobal = 0;
        let html = '';

        porRemision.forEach((grupo) => {
            totalGlobal += grupo.subtotal;

            const labelPartidas = grupo.esCompleta
                ? '<span class="badge bg-primary me-2">Facturación completa</span>'
                : '';

            html += `
                <div class="mb-3 border rounded p-2" style="font-size:0.85rem;">
                    <div class="d-flex justify-content-between align-items-center mb-2">
                        <strong class="text-primary">
                            <i class="fas fa-file-alt me-1"></i>${this._esc(grupo.folio)}
                            ${labelPartidas}
                        </strong>
                        <span class="fw-bold text-success">$${this._fmt(grupo.subtotal)}</span>
                    </div>
                    ${!grupo.esCompleta ? `
                    <table class="table table-sm mb-0" style="font-size:0.8rem;">
                        <thead><tr>
                            <th>Artículo</th><th>Cant.</th><th>Precio</th>
                            <th>Importe</th><th></th>
                        </tr></thead>
                        <tbody>
                            ${grupo.partidas.map(p => `
                                <tr>
                                    <td>${this._esc(p.cve_prod)}</td>
                                    <td>${this._fmt(p.cantidadAFacturar)}</td>
                                    <td>$${this._fmt(p.precio_unitario)}</td>
                                    <td class="fw-bold">$${this._fmt(p.importe)}</td>
                                    <td>
                                        <button type="button"
                                            class="btn btn-sm btn-outline-danger py-0
                                                   vin-fac-btn-quitar-partida"
                                            data-key="${p.remisionId}_${p.id_partida_remision}">
                                            <i class="fas fa-times"></i>
                                        </button>
                                    </td>
                                </tr>`).join('')}
                        </tbody>
                    </table>` : ''}
                    <button type="button"
                        class="btn btn-sm btn-outline-danger mt-1 vin-fac-btn-quitar-remision"
                        data-remision-id="${grupo.partidas[0]?.remisionId}">
                        <i class="fas fa-trash me-1"></i>Quitar remisión
                    </button>
                </div>`;
        });

        html += `
            <div class="d-flex justify-content-between align-items-center
                        mt-3 pt-2 border-top fw-bold fs-6">
                <span>Total a facturar:</span>
                <span class="text-success">$${this._fmt(totalGlobal)}</span>
            </div>`;

        contenedor.innerHTML = html;

        contenedor.querySelectorAll('.vin-fac-btn-quitar-partida').forEach(btn => {
            btn.addEventListener('click', () => {
                this.remisionesSeleccionadas.delete(btn.dataset.key);
                this._actualizarResumen();
                toastMixin?.fire({ icon: 'info', title: 'Partida removida' });
            });
        });

        contenedor.querySelectorAll('.vin-fac-btn-quitar-remision').forEach(btn => {
            btn.addEventListener('click', () => {
                const remId = parseInt(btn.dataset.remisionId);
                for (const key of [...this.remisionesSeleccionadas.keys()]) {
                    if (key.startsWith(`${remId}_`))
                        this.remisionesSeleccionadas.delete(key);
                }
                this.encabezadosSeleccionados.delete(remId);
                this._actualizarResumen();
                const li = this.dom.listaRemisiones
                    .querySelector(`[data-remision-id="${remId}"]`);
                if (li) {
                    li.classList.remove('list-group-item-success');
                    li.querySelector('.badge.bg-success')?.remove();
                }
                toastMixin?.fire({ icon: 'info', title: 'Remisión removida' });
            });
        });
    }

    async aplicarSeleccionAlFormulario() {
        const pm = FacturasApp?.productManagerInstance;
        if (!pm) return;

        if (this.remisionesSeleccionadas.size === 0) {
            toastMixin?.fire({ icon: 'warning', title: 'No hay partidas seleccionadas' });
            return;
        }

        pm.productos = [];

        try {
            const remisionesCompletas = [...this.encabezadosSeleccionados.values()].filter(r => r.esCompleta);
            const partidasParciales = [...this.remisionesSeleccionadas.values()].filter(d => !d.esCompleta);

            const productosCompletas = await this._expandirRemisiones(remisionesCompletas);
            const productosParciales = partidasParciales.map(d => this._crearProductoParcial(d));

            pm.productos = [...productosCompletas, ...productosParciales];

            pm.renderFacturasTable();
            pm.calcularFacturasTotales();

            this._sincronizarCamposOcultos(pm.productos);
            this._notificarResultado(remisionesCompletas.length, pm.productos.length);
            this._cerrarModal();

        } catch (error) {
            console.error('Error aplicando selección VIN:', error);
            toastMixin?.fire({ icon: 'error', title: 'Error procesando remisiones' });
        }
    }

    async _expandirRemisiones(remisiones) {
        const requests = remisiones.map(async (rem) => {
            try {
                const resp = await fetch(
                    `/DatosGenerales/BuscarPartidasRemision?id=${rem.remisionId}`
                );
                const data = await resp.json();
                if (!data.success) return [];
                const doc = data.result?.[0];
                if (!doc?.productos) return [];
                return doc.productos.map(p => this._crearProductoDesdeRemision(p, rem));
            } catch (err) {
                console.error(`Error en remisión VIN ${rem.remisionId}:`, err);
                return [];
            }
        });
        return (await Promise.all(requests)).flat();
    }

    _crearProductoDesdeRemision(p, rem) {
        return {
            id: crypto.randomUUID(),
            productoId: p.cve_prod,
            descripcion: p.descripcion,
            existencia: 0,
            claveCliente: '',
            pedimento: '',
            cantidad: p.cantidad_pendiente ?? 0,
            precio: p.precio_unitario ?? 0,
            descuento: p.descuento ?? 0,
            unidad: p.unidad || 'PZA',
            existenciaGeneral: 0,
            existenciaModular: 0,
            comentario: `Remisión: ${rem.folio}`,
            pedimentos: p.pedimentos || [],
            fraccion_arancelaria: p.fraccion_arancelaria || '',
            unidad_aduana: p.unidad_aduana || '',
            valor_dolares: p.valor_dolares || 0,
            _remisionId: rem.remisionId,
            _esCompleta: true,
        };
    }

    _crearProductoParcial(datos) {
        return {
            id: crypto.randomUUID(),
            productoId: datos.cve_prod,
            descripcion: datos.descripcion,
            existencia: 0,
            claveCliente: '',
            pedimento: '',
            cantidad: datos.cantidadAFacturar ?? 0,
            precio: datos.precio_unitario ?? 0,
            descuento: datos.descuento ?? 0,
            unidad: datos.unidad || 'PZA',
            existenciaGeneral: 0,
            existenciaModular: 0,
            comentario: `Remisión: ${datos.folio}`,
            pedimentos: datos.pedimentos || [],
            fraccion_arancelaria: datos.fraccion_arancelaria || '',
            unidad_aduana: datos.unidad_aduana || '',
            valor_dolares: datos.valor_dolares || 0,
            _remisionId: datos.remisionId,
            _partidaRemisionId: datos.id_partida_remision,
            _esCompleta: false,
        };
    }

    _sincronizarCamposOcultos(productos) {
        const remisionesIds = [...new Set(productos.map(p => p._remisionId))];

        const idsInput = document.getElementById('vin-fac-remisiones-ids');
        if (idsInput) idsInput.value = remisionesIds.join(',');

        const detalle = productos.map(p => ({
            remisionId: p._remisionId,
            id_partida_remision: p._partidaRemisionId ?? null,
            cve_prod: p.productoId,
            cantidad_a_facturar: p.cantidad,
            precio_unitario: p.precio,
            descuento: p.descuento,
            esCompleta: p._esCompleta,
            fraccion_arancelaria: p.fraccion_arancelaria || '',
            unidad_aduana: p.unidad_aduana || '',
            valor_dolares: p.valor_dolares || 0,
        }));

        const detalleInput = document.getElementById('vin-fac-remisiones-detalle');
        if (detalleInput) detalleInput.value = JSON.stringify(detalle);
    }

    _notificarResultado(totalRem, totalProductos) {
        toastMixin?.fire({
            icon: 'success',
            title: `${totalRem} remisión(es) — ${totalProductos} partida(s) cargadas`
        });
    }

    _cerrarModal() {
        bootstrap.Modal.getInstance(
            document.getElementById('vin-fac-modalRemisionesParciales')
        )?.hide();
    }

    getRemisionesParaEnviar() {
        return [...this.remisionesSeleccionadas.values()].map(d => ({
            remisionId: d.remisionId,
            id_partida_remision: d.id_partida_remision,
            cve_prod: d.cve_prod,
            cantidad_a_facturar: d.cantidadAFacturar ?? null,
            precio_unitario: d.precio_unitario,
            descuento: d.descuento,
            esCompleta: d.esCompleta || false,
        }));
    }

    tieneRemisiones() { return this.remisionesSeleccionadas.size > 0; }
    limpiar() {
        this.remisionesSeleccionadas.clear();
        this.encabezadosSeleccionados.clear();
    }

    _mostrarTablaPartidas(show) {
        if (this.dom.tablaPartidas)
            this.dom.tablaPartidas.style.display = show ? 'block' : 'none';
        const estadoVacio = document.getElementById('vin-fac-estadoVacioPartidas');
        if (estadoVacio) estadoVacio.style.display = show ? 'none' : 'flex';
    }

    _setSpinner(show) { this.dom.spinner?.classList.toggle('d-none', !show); }

    _renderPaginacion() {
        const ul = this.dom.pagination;
        if (!ul) return;
        const totalPages = Math.ceil(this.state.totalRecords / this.state.pageSize);
        if (totalPages <= 1) { ul.innerHTML = ''; return; }
        const pages = [];
        for (let i = Math.max(1, this.state.page - 2);
            i <= Math.min(totalPages, this.state.page + 2); i++) pages.push(i);
        ul.innerHTML = `
            <li class="page-item ${this.state.page === 1 ? 'disabled' : ''}">
                <button class="page-link" data-page="${this.state.page - 1}">‹</button>
            </li>
            ${pages.map(p => `
                <li class="page-item ${p === this.state.page ? 'active' : ''}">
                    <button class="page-link" data-page="${p}">${p}</button>
                </li>`).join('')}
            <li class="page-item ${this.state.page === totalPages ? 'disabled' : ''}">
                <button class="page-link" data-page="${this.state.page + 1}">›</button>
            </li>`;
    }

    _esc(text) {
        const d = document.createElement('div');
        d.textContent = String(text ?? '');
        return d.innerHTML;
    }
    _fmt(val, decimals = 2) { return parseFloat(val || 0).toFixed(decimals); }
}

// ── Inicialización ────────────────────────────────────────────
let vinRemisionParcialesManager = null;

document.addEventListener('DOMContentLoaded', () => {
    vinRemisionParcialesManager = new VINFacturasRemisionParcialesManager();

    document.getElementById('vin-fac-btnAplicarRemisiones')
        ?.addEventListener('click', () =>
            vinRemisionParcialesManager.aplicarSeleccionAlFormulario());

    window.vinRemisionParcialesManager = vinRemisionParcialesManager;

    // ── Parche sobre FacturasFormManager para incluir los campos VIN ──
    if (typeof FacturasFormManager === 'undefined') {
        console.error('FacturasFormManager no encontrado.');
        return;
    }

    const _originalRecopilar =
        FacturasFormManager.prototype.recopilarFacturasDatosFormulario;

    FacturasFormManager.prototype.recopilarFacturasDatosFormulario = function () {
        const formData = _originalRecopilar.call(this);

        const detalleEl = document.getElementById('vin-fac-remisiones-detalle');
        if (detalleEl?.value)
            formData.set('remisionesParcialesJSON', detalleEl.value);

        const idsEl = document.getElementById('vin-fac-remisiones-ids');
        if (idsEl?.value)
            formData.set('remisionesIds', idsEl.value);

        return formData;
    };
});
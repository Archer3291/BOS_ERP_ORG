/* ============================================================================
   Facturación Especial desde Remisiones — compartido por los canales de venta
   ----------------------------------------------------------------------------
   Tres modos, una sola estructura de datos:

     · parcial     → se eligen partidas y cantidades de una remisión
     · multiple    → varias remisiones, cada una completa (todo su pendiente)
     · individual  → una sola remisión completa

   En los tres casos la selección termina siendo una lista de partidas con su
   id_partida_remision REAL. El backend (VentasFacturaBaseController.Guardar_ConRemisiones) usa
   ese id para descontar el saldo en remision_partidas_facturadas, así que
   nunca debe viajar null: ese era el origen del error
   "Error converting value {null} to type 'System.Int32'".
   ========================================================================== */

(function () {
    'use strict';

/*
   Configuración por canal. La declara la vista anfitriona ANTES de cargar este
   script:

       <script>
           window.FacturacionRemisionesConfig = {
               prefix: 'vs-fac',
               endpointRemisiones: '/DatosGenerales/BuscarDVSremFacturables'
           };
       </script>

   · prefix              → prefijo de los ids del modal y del formulario de factura.
   · endpointRemisiones  → búsqueda de remisiones con saldo del canal.
   · incluirPedimentos   → false en Nacional: su factura deserializa productosJSON
                           como Dictionary<string,string> y un arreglo lo rompe.
*/
const CFG = Object.assign({
    prefix: 'vi-fac',
    endpointRemisiones: '/DatosGenerales/BuscarDVIrem',
    incluirPedimentos: true
}, window.FacturacionRemisionesConfig || {});

const P = CFG.prefix;

const VFAC_MODOS = {
    parcial: {
        seleccionDirecta: false,
        multiRemision: true,
        hint: 'Haz clic en una remisión para ver sus partidas, marca las que ' +
              'vas a facturar y ajusta las cantidades.'
    },
    multiple: {
        seleccionDirecta: true,
        multiRemision: true,
        hint: 'Haz clic para agregar o quitar remisiones. Cada una se factura ' +
              'por su saldo pendiente completo.'
    },
    individual: {
        seleccionDirecta: true,
        multiRemision: false,
        hint: 'Se factura una sola remisión completa. Al elegir otra se ' +
              'reemplaza la anterior.'
    }
};

class FacturasRemisionParcialesManager {

    constructor() {
        /** key `${remisionId}::${idPartida}` → partida normalizada */
        this.partidasSeleccionadas = new Map();
        /** remisionId → { folio, cliente } */
        this.remisionesInfo = new Map();

        this.modoActual = 'parcial';
        this.clienteActual = '';

        this.dom = {
            modal: document.getElementById(`${P}-modalRemisionesParciales`),
            btnAbrir: document.getElementById(`${P}-btnEspeciales`),
            inputBuscar: document.getElementById(`${P}-inputBuscarRemision`),
            listaRemisiones: document.getElementById(`${P}-listaRemisiones`),
            tablaPartidas: document.getElementById(`${P}-tablaPartidas`),
            tbodyPartidas: document.getElementById(`${P}-tbodyPartidas`),
            accionesPartidas: document.getElementById(`${P}-accionesPartidas`),
            btnMarcarTodas: document.getElementById(`${P}-btnMarcarTodas`),
            btnAgregarSeleccion: document.getElementById(`${P}-btnAgregarSeleccion`),
            btnAplicar: document.getElementById(`${P}-btnAplicarRemisiones`),
            btnLimpiar: document.getElementById(`${P}-btnLimpiarSeleccion`),
            resumenSeleccion: document.getElementById(`${P}-resumenSeleccion`),
            contadorSeleccion: document.getElementById(`${P}-contadorSeleccion`),
            spinner: document.getElementById(`${P}-spinnerRemisiones`),
            pagination: document.getElementById(`${P}-paginationRemisiones`),
            totalRegistros: document.getElementById(`${P}-totalRemisiones`),
            selectorModo: document.getElementById(`${P}-selectorModo`),
            modoHint: document.getElementById(`${P}-modoHint`),
            headerPartidas: document.getElementById(`${P}-headerTablaPartidas`),
            estadoVacio: document.getElementById(`${P}-estadoVacioPartidas`),
            textoVacio: document.getElementById(`${P}-textoVacioPartidas`),
            clienteBadge: document.getElementById(`${P}-rem-clienteActual`),
            inputCliente: document.getElementById(`${P}-cliente`),
            hiddenIds: document.getElementById(`${P}-remisiones-ids`),
            hiddenDetalle: document.getElementById(`${P}-remisiones-detalle`)
        };

        this.state = {
            page: 1,
            pageSize: 25,
            busqueda: '',
            remisionActivaId: null,
            totalRecords: 0
        };

        this.typingTimer = null;
        this._init();
    }

    get config() {
        return VFAC_MODOS[this.modoActual] || VFAC_MODOS.parcial;
    }

    // ══════════════════════════════════════════════════════════
    // Inicialización
    // ══════════════════════════════════════════════════════════
    _init() {
        this._vigilarCliente();
        this._sincronizarEstadoCliente();
        this._aplicarModoUI();

        this.dom.btnAbrir?.addEventListener('click', () => this.abrir());
        this.dom.modal?.addEventListener('show.bs.modal', () => this._onModalShow());

        this.dom.inputBuscar?.addEventListener('input', (e) => {
            clearTimeout(this.typingTimer);
            const valor = e.target.value;
            this.typingTimer = setTimeout(() => {
                this.state.busqueda = valor;
                this.state.page = 1;
                this._cargarListaRemisiones();
            }, 300);
        });

        this.dom.selectorModo?.addEventListener('change', (e) => {
            this._cambiarModo(e.target.value);
        });

        this.dom.listaRemisiones?.addEventListener('click', (e) => {
            const item = e.target.closest('[data-remision-id]');
            if (!item) return;
            const remisionId = parseInt(item.dataset.remisionId, 10);
            if (!Number.isFinite(remisionId)) return;

            if (this.config.seleccionDirecta) this._seleccionDirecta(remisionId, item);
            else this._abrirPartidas(remisionId, item);
        });

        this.dom.tbodyPartidas?.addEventListener('input', (e) => {
            if (!e.target.classList.contains('vfac-cant-facturar')) return;
            this._validarCantidad(e.target);
            this._actualizarImportePartida(e.target.closest('tr'));
        });

        this.dom.tbodyPartidas?.addEventListener('change', (e) => {
            if (!e.target.classList.contains('vfac-chk-partida')) return;
            const tr = e.target.closest('tr');
            const inputCant = tr.querySelector('.vfac-cant-facturar');
            if (inputCant) inputCant.disabled = !e.target.checked;
            this._actualizarImportePartida(tr);
        });

        this.dom.btnMarcarTodas?.addEventListener('click', () => this._marcarTodas());
        this.dom.btnAgregarSeleccion?.addEventListener('click',
            () => this._agregarPartidasSeleccionadas());
        this.dom.btnAplicar?.addEventListener('click',
            () => this.aplicarSeleccionAlFormulario());
        this.dom.btnLimpiar?.addEventListener('click',
            () => this.limpiarSeleccion({ avisar: true }));

        this.dom.pagination?.addEventListener('click', (e) => {
            e.preventDefault();
            const btn = e.target.closest('[data-page]');
            if (!btn) return;
            const page = parseInt(btn.dataset.page, 10);
            if (!Number.isFinite(page) || page < 1) return;
            this.state.page = page;
            this._cargarListaRemisiones();
        });

        this._actualizarResumen();
    }

    // ══════════════════════════════════════════════════════════
    // Cliente: gate del botón "Especiales"
    // ══════════════════════════════════════════════════════════
    _clienteSeleccionado() {
        return (this.dom.inputCliente?.value || '').trim();
    }

    /**
     * El input de cliente es un input deshabilitado que se llena por código, así
     * que no emite eventos nativos. Interceptamos su setter para publicar uno
     * propio y poder reaccionar (habilitar el botón, invalidar la selección).
     */
    _vigilarCliente() {
        const input = this.dom.inputCliente;
        if (!input) return;

        if (!input.__vfacWatched) {
            const nativo = Object.getOwnPropertyDescriptor(
                HTMLInputElement.prototype, 'value');

            if (nativo?.get && nativo?.set) {
                Object.defineProperty(input, 'value', {
                    configurable: true,
                    enumerable: true,
                    get() { return nativo.get.call(this); },
                    set(v) {
                        const previo = nativo.get.call(this);
                        nativo.set.call(this, v);
                        if (previo !== v) {
                            this.dispatchEvent(new CustomEvent(
                                `${P}:cliente-changed`, { bubbles: true }));
                        }
                    }
                });
                input.__vfacWatched = true;
            }
        }

        [`${P}:cliente-changed`, 'input', 'change'].forEach(ev =>
            input.addEventListener(ev, () => this._sincronizarEstadoCliente()));
    }

    _sincronizarEstadoCliente() {
        const cliente = this._clienteSeleccionado();
        const btn = this.dom.btnAbrir;

        if (btn) {
            btn.disabled = !cliente;
            btn.title = cliente
                ? 'Facturación especial desde remisiones'
                : 'Selecciona primero un cliente';
            btn.style.opacity = cliente ? '' : '0.6';
            btn.style.cursor = cliente ? '' : 'not-allowed';
        }

        // Cambiar de cliente invalida cualquier selección previa: las
        // remisiones pertenecen al cliente anterior.
        if (cliente !== this.clienteActual && this.partidasSeleccionadas.size > 0) {
            this.limpiarSeleccion();
            toastMixin?.fire({
                icon: 'info',
                title: 'Se limpió la selección de remisiones por cambio de cliente'
            });
        }

        this.clienteActual = cliente;
        if (this.dom.clienteBadge)
            this.dom.clienteBadge.textContent = cliente || 'Sin cliente';
    }

    abrir() {
        const cliente = this._clienteSeleccionado();
        if (!cliente) {
            toastMixin?.fire({
                icon: 'warning',
                title: 'Selecciona un cliente antes de abrir Especiales'
            });
            return;
        }
        if (!this.dom.modal) return;
        bootstrap.Modal.getOrCreateInstance(this.dom.modal).show();
    }

    _onModalShow() {
        this._sincronizarEstadoCliente();

        if (this.dom.inputBuscar) this.dom.inputBuscar.value = '';
        this.state.busqueda = '';
        this.state.page = 1;
        this.state.remisionActivaId = null;

        if (this.dom.selectorModo) this.dom.selectorModo.value = this.modoActual;
        this._aplicarModoUI();
        this._mostrarTablaPartidas(false);
        this._cargarListaRemisiones();
        this._actualizarResumen();
    }

    // ══════════════════════════════════════════════════════════
    // Modo
    // ══════════════════════════════════════════════════════════
    _cambiarModo(nuevoModo) {
        if (!VFAC_MODOS[nuevoModo]) nuevoModo = 'parcial';
        if (nuevoModo === this.modoActual) return;

        const cambiar = () => {
            this.modoActual = nuevoModo;
            this.state.page = 1;
            this.state.remisionActivaId = null;
            this._aplicarModoUI();
            this._mostrarTablaPartidas(false);
            this._cargarListaRemisiones();
            this._actualizarResumen();
        };

        // Las selecciones de un modo no son comparables con las de otro
        // (parciales por cantidad vs. remisiones completas): se reinicia.
        if (this.partidasSeleccionadas.size > 0) {
            this._limpiarEstructuras();
            cambiar();
            toastMixin?.fire({
                icon: 'info',
                title: 'Se reinició la selección al cambiar de tipo de facturación'
            });
            return;
        }
        cambiar();
    }

    _aplicarModoUI() {
        const cfg = this.config;

        if (this.dom.modoHint)
            this.dom.modoHint.innerHTML =
                `<i class="fas fa-lightbulb me-1"></i>${this._esc(cfg.hint)}`;

        // Los botones de partidas solo tienen sentido en modo parcial
        if (this.dom.accionesPartidas)
            this.dom.accionesPartidas.style.display =
                cfg.seleccionDirecta ? 'none' : 'flex';

        if (this.dom.textoVacio)
            this.dom.textoVacio.innerHTML = cfg.seleccionDirecta
                ? 'Selecciona las remisiones de la izquierda.<br>' +
                  'Se facturarán completas: aquí verás su detalle.'
                : 'Selecciona una remisión de la izquierda<br>' +
                  'para ver sus partidas pendientes';
    }

    // ══════════════════════════════════════════════════════════
    // Lista de remisiones
    // ══════════════════════════════════════════════════════════
    async _cargarListaRemisiones() {
        const clienteId = this._clienteSeleccionado();

        if (!clienteId) {
            this.dom.listaRemisiones.innerHTML = `
                <li class="list-group-item text-center text-warning py-5">
                    <i class="fas fa-user-slash fa-2x mb-2 d-block opacity-50"></i>
                    Selecciona un cliente para ver sus remisiones
                </li>`;
            this.state.totalRecords = 0;
            this._renderPaginacion();
            if (this.dom.totalRegistros) this.dom.totalRegistros.textContent = '0';
            return;
        }

        this._setSpinner(true);
        try {
            const params = new URLSearchParams({
                nombre: this.state.busqueda,
                page: this.state.page,
                pageSize: this.state.pageSize,
                cliente: clienteId,
                modo: this.modoActual
            });

            const resp = await fetch(`${CFG.endpointRemisiones}?${params}`);
            if (!resp.ok) throw new Error(`HTTP ${resp.status}`);

            const data = await resp.json();
            this.state.totalRecords = data.total || 0;
            this._renderListaRemisiones(data.items || []);
            this._renderPaginacion();
            if (this.dom.totalRegistros)
                this.dom.totalRegistros.textContent = this.state.totalRecords;
        } catch (err) {
            console.error('Error cargando remisiones:', err);
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
                    Este cliente no tiene remisiones con saldo por facturar
                </li>`;
            return;
        }

        items.forEach(r => {
            const remId = Number(r.id_encabezado);
            const seleccionada = this._remisionEstaSeleccionada(remId);
            const sinTracking = Number(r.total_partidas_tracking || 0) === 0;

            const li = document.createElement('li');
            li.className = 'list-group-item list-group-item-action d-flex ' +
                'justify-content-between align-items-start' +
                (seleccionada ? ' list-group-item-success' : '');
            li.dataset.remisionId = remId;
            li.dataset.folio = r.folio || '';
            li.dataset.importe = r.importe_pendiente || 0;
            li.style.cursor = 'pointer';

            // Sin tracking = remisión nueva: todas sus partidas están pendientes.
            const pendientes = sinTracking
                ? Number(r.total_partidas || 0)
                : Number(r.partidas_pendientes || 0);
            const parciales = sinTracking ? 0 : Number(r.partidas_parciales || 0);

            const fecha = (typeof dateFormatter === 'function')
                ? (dateFormatter(r.fecha) || '')
                : (r.fecha || '');

            li.innerHTML = `
                <div class="flex-grow-1">
                    <div class="d-flex justify-content-between">
                        <h6 class="mb-1 fw-bold">
                            <i class="fas fa-file-alt me-2"></i>${this._esc(r.folio)}
                            ${seleccionada ? this._badgeSeleccionada() : ''}
                        </h6>
                        <small class="text-muted">${this._esc(fecha)}</small>
                    </div>
                    <p class="mb-1 text-muted small">
                        <i class="fas fa-user me-1"></i>${this._esc(r.n_cli || r.cli_prov || '')}
                    </p>
                    <div class="d-flex gap-2 small mt-1 flex-wrap align-items-center">
                        <span class="badge bg-warning text-dark">
                            <i class="fas fa-clock me-1"></i>${pendientes} pend.
                        </span>
                        ${parciales > 0 ? `
                        <span class="badge bg-info text-dark">
                            <i class="fas fa-adjust me-1"></i>${parciales} parc.
                        </span>` : ''}
                        <span class="text-success fw-bold ms-auto">
                            $${this._fmt(r.importe_pendiente)}
                        </span>
                    </div>
                </div>
                <i class="fas fa-${this.config.seleccionDirecta ? 'check-square' : 'chevron-right'}
                          text-muted mt-1 ms-2"></i>`;
            ul.appendChild(li);
        });
    }

    _badgeSeleccionada() {
        return '<span class="badge bg-success ms-2">' +
               '<i class="fas fa-check"></i> Seleccionada</span>';
    }

    _remisionEstaSeleccionada(remisionId) {
        for (const p of this.partidasSeleccionadas.values())
            if (p.remisionId === remisionId) return true;
        return false;
    }

    _marcarItemLista(remisionId, seleccionada) {
        const li = this.dom.listaRemisiones
            ?.querySelector(`[data-remision-id="${remisionId}"]`);
        if (!li) return;

        li.classList.toggle('list-group-item-success', seleccionada);
        const titulo = li.querySelector('h6');
        const badge = titulo?.querySelector('.badge.bg-success');

        if (seleccionada && titulo && !badge)
            titulo.insertAdjacentHTML('beforeend', ' ' + this._badgeSeleccionada());
        else if (!seleccionada && badge)
            badge.remove();
    }

    // ══════════════════════════════════════════════════════════
    // Carga de partidas (endpoint único)
    // ══════════════════════════════════════════════════════════
    async _cargarPartidas(remisionId) {
        const resp = await fetch(
            `/DatosGenerales/BuscarPartidasRemision?id=${remisionId}`);
        if (!resp.ok) throw new Error(`HTTP ${resp.status}`);

        const data = await resp.json();
        if (!data.success) throw new Error(data.message || 'Respuesta inválida');

        const doc = data.result?.[0] || {};
        const crudas = Array.isArray(doc.productos) ? doc.productos : [];

        const partidas = [];
        const descartadas = [];

        crudas.forEach(p => {
            const normalizada = this._normalizarPartida(p, remisionId, doc);
            if (normalizada) partidas.push(normalizada);
            else descartadas.push(p?.cve_prod || '?');
        });

        if (descartadas.length > 0) {
            // Sin id_partida_remision el backend no puede descontar el saldo:
            // preferimos avisar antes que generar una factura sin trazabilidad.
            console.warn('Partidas sin id_partida_remision descartadas:', descartadas);
            toastMixin?.fire({
                icon: 'warning',
                title: `${descartadas.length} partida(s) sin identificador fueron omitidas`
            });
        }

        return { doc, partidas };
    }

    /** Devuelve null si la partida no es utilizable (sin id o sin saldo). */
    _normalizarPartida(p, remisionId, doc) {
        const idPartida = Number(p?.id_partida_remision ?? p?.id);
        if (!Number.isFinite(idPartida) || idPartida <= 0) return null;

        const pendiente = Number(p.cantidad_pendiente) || 0;
        if (pendiente <= 0) return null;

        return {
            remisionId,
            folio: doc?.folio || '',
            idPartida,
            cve_prod: p.cve_prod || '',
            descripcion: p.descripcion || '',
            unidad: p.unidad || 'PZA',
            precio: Number(p.precio_unitario) || 0,
            descuento: Number(p.descuento) || 0,
            cantidadOriginal: Number(p.cantidad_original) || 0,
            cantidadPendiente: pendiente,
            cantidadAFacturar: pendiente,
            estatus: p.estatus || 'pendiente',
            pedimentos: this._parsePedimentos(p.pedimentos),
            importePendiente: Number(p.importe_pendiente_partida) || 0
        };
    }

    _parsePedimentos(raw) {
        if (!raw) return [];
        if (Array.isArray(raw)) return raw;
        try {
            const parsed = JSON.parse(raw);
            return Array.isArray(parsed) ? parsed : [];
        } catch { return []; }
    }

    _clave(remisionId, idPartida) { return `${remisionId}::${idPartida}`; }

    // ══════════════════════════════════════════════════════════
    // Modo parcial: abrir partidas para editar cantidades
    // ══════════════════════════════════════════════════════════
    async _abrirPartidas(remisionId, liEl) {
        this.dom.listaRemisiones
            .querySelectorAll('.list-group-item-action')
            .forEach(el => el.classList.remove('active'));
        liEl.classList.add('active');

        this.state.remisionActivaId = remisionId;
        this._setSpinner(true);
        this._mostrarTablaPartidas(false);

        try {
            const { doc, partidas } = await this._cargarPartidas(remisionId);

            if (partidas.length === 0) {
                this._mostrarSinPartidas(doc);
                return;
            }

            this._renderTablaPartidas(partidas, doc);
            this._mostrarTablaPartidas(true);
        } catch (err) {
            console.error('Error cargando partidas:', err);
            Swal?.fire({ icon: 'error', title: 'Error', text: err.message });
        } finally {
            this._setSpinner(false);
        }
    }

    _mostrarSinPartidas(doc) {
        this.dom.tbodyPartidas.innerHTML = `
            <tr><td colspan="9" class="text-center text-muted py-4">
                <i class="fas fa-check-circle text-success me-2"></i>
                Todas las partidas de <strong>${this._esc(doc?.folio || '')}</strong>
                ya fueron facturadas
            </td></tr>`;
        this._mostrarTablaPartidas(true);
    }

    /**
     * @param soloLectura en modos "múltiple"/"individual" la remisión se
     *        factura completa: la tabla es informativa, no editable.
     */
    _renderTablaPartidas(partidas, doc, soloLectura = false) {
        const tbody = this.dom.tbodyPartidas;
        tbody.innerHTML = '';

        partidas.forEach(p => {
            const key = this._clave(p.remisionId, p.idPartida);
            const previa = this.partidasSeleccionadas.get(key);
            const cant = previa ? previa.cantidadAFacturar : p.cantidadPendiente;

            const tr = document.createElement('tr');
            tr.dataset.partidaId = p.idPartida;
            tr.dataset.remisionId = p.remisionId;
            tr.dataset.partidaData = JSON.stringify(p);

            tr.innerHTML = `
                <td class="text-center">
                    <input type="checkbox" class="vfac-chk-partida form-check-input"
                           checked ${soloLectura ? 'disabled' : ''}>
                </td>
                <td class="fw-bold text-primary">${this._esc(p.cve_prod)}</td>
                <td style="max-width:200px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;"
                    title="${this._esc(p.descripcion)}">${this._esc(p.descripcion)}</td>
                <td class="text-center">
                    <span class="badge ${p.estatus === 'parcial' ? 'bg-warning text-dark' : 'bg-success'}">
                        ${this._esc(p.estatus)}
                    </span>
                </td>
                <td class="text-end">${this._fmtCant(p.cantidadOriginal)}</td>
                <td class="text-end text-success fw-bold">${this._fmtCant(p.cantidadPendiente)}</td>
                <td class="text-center">
                    <input type="number"
                           class="vfac-cant-facturar form-control form-control-sm text-end"
                           value="${this._fmtCant(cant)}"
                           min="0"
                           max="${p.cantidadPendiente}"
                           step="0.0001"
                           data-pendiente="${p.cantidadPendiente}"
                           data-precio="${p.precio}"
                           data-descuento="${p.descuento}"
                           ${soloLectura ? 'readonly disabled' : ''}
                           style="width:100px;">
                </td>
                <td class="text-end fw-bold vfac-importe-partida">
                    $${this._fmt(this._importe(cant, p.precio, p.descuento))}
                </td>
                <td class="text-end text-muted" style="font-size:0.78rem">
                    $${this._fmt(p.importePendiente)}
                </td>`;

            tbody.appendChild(tr);
        });

        if (this.dom.headerPartidas) {
            this.dom.headerPartidas.innerHTML =
                `<i class="fas fa-boxes me-1"></i> ` +
                `Partidas de ${this._esc(doc?.folio || '')} — ` +
                `${this._esc(doc?.n_cli || doc?.cli_prov || '')}`;
        }
    }

    _marcarTodas() {
        const checkboxes = this.dom.tbodyPartidas
            .querySelectorAll('.vfac-chk-partida');
        if (checkboxes.length === 0) return;

        const todasMarcadas = Array.from(checkboxes).every(c => c.checked);
        checkboxes.forEach(c => {
            c.checked = !todasMarcadas;
            const tr = c.closest('tr');
            const input = tr.querySelector('.vfac-cant-facturar');
            if (input) input.disabled = todasMarcadas;
            this._actualizarImportePartida(tr);
        });
    }

    _validarCantidad(input) {
        const max = parseFloat(input.dataset.pendiente) || 0;
        let val = parseFloat(input.value);

        if (!Number.isFinite(val) || val < 0) return;   // se valida al agregar

        if (val > max) {
            input.value = this._fmtCant(max);
            input.classList.add('is-invalid');
            setTimeout(() => input.classList.remove('is-invalid'), 1500);
            toastMixin?.fire({
                icon: 'warning',
                title: `Máximo disponible: ${this._fmtCant(max)}`
            });
        }
    }

    _actualizarImportePartida(tr) {
        if (!tr) return;
        const chk = tr.querySelector('.vfac-chk-partida');
        const input = tr.querySelector('.vfac-cant-facturar');
        const celda = tr.querySelector('.vfac-importe-partida');
        if (!chk || !input || !celda) return;

        if (!chk.checked) { celda.textContent = '$0.00'; return; }

        const cant = parseFloat(input.value) || 0;
        const precio = parseFloat(input.dataset.precio) || 0;
        const dto = parseFloat(input.dataset.descuento) || 0;
        celda.textContent = `$${this._fmt(this._importe(cant, precio, dto))}`;
    }

    _agregarPartidasSeleccionadas() {
        const rows = this.dom.tbodyPartidas.querySelectorAll('tr[data-partida-id]');
        let agregadas = 0;
        let rechazadas = 0;

        rows.forEach(tr => {
            const chk = tr.querySelector('.vfac-chk-partida');
            if (!chk?.checked) return;

            const inputCant = tr.querySelector('.vfac-cant-facturar');
            const cant = parseFloat(inputCant?.value);
            const max = parseFloat(inputCant?.dataset.pendiente) || 0;

            if (!Number.isFinite(cant) || cant <= 0) { rechazadas++; return; }
            if (cant > max + 1e-6) { rechazadas++; return; }

            let datos;
            try { datos = JSON.parse(tr.dataset.partidaData); }
            catch { rechazadas++; return; }

            const key = this._clave(datos.remisionId, datos.idPartida);
            this.partidasSeleccionadas.set(key, { ...datos, cantidadAFacturar: cant });
            this.remisionesInfo.set(datos.remisionId, { folio: datos.folio });
            agregadas++;
        });

        if (agregadas === 0) {
            toastMixin?.fire({
                icon: 'warning',
                title: 'Marca al menos una partida con cantidad válida (> 0 y ≤ pendiente)'
            });
            return;
        }

        this._actualizarResumen();
        this._marcarItemLista(this.state.remisionActivaId, true);

        toastMixin?.fire({
            icon: 'success',
            title: rechazadas > 0
                ? `${agregadas} partida(s) agregada(s), ${rechazadas} con cantidad inválida`
                : `${agregadas} partida(s) agregada(s)`
        });
    }

    // ══════════════════════════════════════════════════════════
    // Modos múltiple / individual: remisión completa
    // ══════════════════════════════════════════════════════════
    async _seleccionDirecta(remisionId, liEl) {
        // Toggle: si ya estaba, se quita
        if (this._remisionEstaSeleccionada(remisionId)) {
            this._quitarRemision(remisionId);
            toastMixin?.fire({ icon: 'info', title: 'Remisión removida' });
            return;
        }

        // Modo individual: solo una a la vez
        if (!this.config.multiRemision && this.partidasSeleccionadas.size > 0) {
            [...this.remisionesInfo.keys()].forEach(id => this._quitarRemision(id));
        }

        this._setSpinner(true);
        try {
            const { doc, partidas } = await this._cargarPartidas(remisionId);

            if (partidas.length === 0) {
                toastMixin?.fire({
                    icon: 'warning',
                    title: 'Esta remisión ya no tiene saldo por facturar'
                });
                return;
            }

            partidas.forEach(p => {
                this.partidasSeleccionadas.set(
                    this._clave(p.remisionId, p.idPartida),
                    { ...p, cantidadAFacturar: p.cantidadPendiente });
            });
            this.remisionesInfo.set(remisionId, { folio: doc?.folio || liEl.dataset.folio });
            this.state.remisionActivaId = remisionId;

            this._marcarItemLista(remisionId, true);
            this._actualizarResumen();
            this._renderTablaPartidas(partidas, doc, true);
            this._mostrarTablaPartidas(true);

            toastMixin?.fire({
                icon: 'success',
                title: `${doc?.folio || 'Remisión'}: ${partidas.length} partida(s) agregada(s)`
            });
        } catch (err) {
            console.error('Error agregando remisión completa:', err);
            Swal?.fire({ icon: 'error', title: 'Error', text: err.message });
        } finally {
            this._setSpinner(false);
        }
    }

    _quitarRemision(remisionId) {
        for (const [key, p] of [...this.partidasSeleccionadas]) {
            if (p.remisionId === remisionId) this.partidasSeleccionadas.delete(key);
        }
        this.remisionesInfo.delete(remisionId);
        this._marcarItemLista(remisionId, false);

        if (this.state.remisionActivaId === remisionId) {
            this.state.remisionActivaId = null;
            this._mostrarTablaPartidas(false);
        }
        this._actualizarResumen();
    }

    // ══════════════════════════════════════════════════════════
    // Resumen
    // ══════════════════════════════════════════════════════════
    _actualizarResumen() {
        const contenedor = this.dom.resumenSeleccion;
        const total = this.partidasSeleccionadas.size;

        if (this.dom.contadorSeleccion)
            this.dom.contadorSeleccion.textContent = total;
        if (this.dom.btnAplicar)
            this.dom.btnAplicar.disabled = total === 0;

        if (!contenedor) return;

        if (total === 0) {
            contenedor.innerHTML = `
                <div class="text-center text-muted py-3">
                    <i class="fas fa-info-circle me-2"></i>Sin partidas seleccionadas
                </div>`;
            return;
        }

        const porRemision = new Map();
        this.partidasSeleccionadas.forEach(p => {
            if (!porRemision.has(p.remisionId))
                porRemision.set(p.remisionId, { folio: p.folio, partidas: [], subtotal: 0 });

            const grupo = porRemision.get(p.remisionId);
            const importe = this._importe(p.cantidadAFacturar, p.precio, p.descuento);
            grupo.partidas.push({ ...p, importe });
            grupo.subtotal += importe;
        });

        let totalGlobal = 0;
        let html = '';

        porRemision.forEach((grupo, remisionId) => {
            totalGlobal += grupo.subtotal;

            html += `
                <div class="mb-3 border rounded p-2" style="font-size:0.85rem;">
                    <div class="d-flex justify-content-between align-items-center mb-2">
                        <strong class="text-primary">
                            <i class="fas fa-file-alt me-1"></i>${this._esc(grupo.folio)}
                        </strong>
                        <span class="fw-bold text-success">$${this._fmt(grupo.subtotal)}</span>
                    </div>
                    <table class="table table-sm mb-0" style="font-size:0.78rem;">
                        <thead><tr>
                            <th>Artículo</th><th class="text-end">Cant.</th>
                            <th class="text-end">Importe</th><th></th>
                        </tr></thead>
                        <tbody>
                            ${grupo.partidas.map(p => `
                                <tr>
                                    <td title="${this._esc(p.descripcion)}">${this._esc(p.cve_prod)}</td>
                                    <td class="text-end">${this._fmtCant(p.cantidadAFacturar)}</td>
                                    <td class="text-end fw-bold">$${this._fmt(p.importe)}</td>
                                    <td class="text-end">
                                        <button type="button"
                                            class="btn btn-sm btn-outline-danger py-0 px-1
                                                   vfac-btn-quitar-partida"
                                            data-key="${this._clave(p.remisionId, p.idPartida)}"
                                            title="Quitar partida">
                                            <i class="fas fa-times"></i>
                                        </button>
                                    </td>
                                </tr>`).join('')}
                        </tbody>
                    </table>
                    <button type="button"
                        class="btn btn-sm btn-outline-danger mt-2 w-100 vfac-btn-quitar-remision"
                        data-remision-id="${remisionId}">
                        <i class="fas fa-trash me-1"></i>Quitar remisión
                    </button>
                </div>`;
        });

        html += `
            <div class="d-flex justify-content-between align-items-center
                        mt-3 pt-2 border-top fw-bold fs-6">
                <span>Subtotal a facturar:</span>
                <span class="text-success">$${this._fmt(totalGlobal)}</span>
            </div>`;

        contenedor.innerHTML = html;

        contenedor.querySelectorAll('.vfac-btn-quitar-partida').forEach(btn => {
            btn.addEventListener('click', () => {
                const partida = this.partidasSeleccionadas.get(btn.dataset.key);
                this.partidasSeleccionadas.delete(btn.dataset.key);

                if (partida && !this._remisionEstaSeleccionada(partida.remisionId)) {
                    this.remisionesInfo.delete(partida.remisionId);
                    this._marcarItemLista(partida.remisionId, false);
                }
                this._actualizarResumen();
                toastMixin?.fire({ icon: 'info', title: 'Partida removida' });
            });
        });

        contenedor.querySelectorAll('.vfac-btn-quitar-remision').forEach(btn => {
            btn.addEventListener('click', () => {
                this._quitarRemision(parseInt(btn.dataset.remisionId, 10));
                toastMixin?.fire({ icon: 'info', title: 'Remisión removida' });
            });
        });
    }

    // ══════════════════════════════════════════════════════════
    // Aplicar al formulario
    // ══════════════════════════════════════════════════════════
    async aplicarSeleccionAlFormulario() {
        const pm = FacturasApp?.productManagerInstance;
        if (!pm) {
            toastMixin?.fire({ icon: 'error', title: 'No se encontró el gestor de productos' });
            return;
        }

        if (this.partidasSeleccionadas.size === 0) {
            toastMixin?.fire({ icon: 'warning', title: 'No hay partidas seleccionadas' });
            return;
        }

        // La factura desde remisiones reemplaza el contenido de la tabla:
        // mezclar productos sueltos rompería la trazabilidad de saldos.
        const manuales = (pm.productos || []).filter(p => !p._partidaRemisionId);
        if (manuales.length > 0 && window.Swal) {
            const r = await Swal.fire({
                icon: 'warning',
                title: 'Se reemplazarán los productos actuales',
                html: `La tabla tiene <strong>${manuales.length}</strong> producto(s) ` +
                      `capturado(s) manualmente que se eliminarán.`,
                showCancelButton: true,
                confirmButtonText: 'Continuar',
                cancelButtonText: 'Cancelar'
            });
            if (!r.isConfirmed) return;
        }

        pm.productos = [...this.partidasSeleccionadas.values()]
            .map(p => this._crearProducto(p));

        pm.renderFacturasTable();
        pm.calcularFacturasTotales();

        this._sincronizarCamposOcultos(pm.productos);

        toastMixin?.fire({
            icon: 'success',
            title: `${this.remisionesInfo.size} remisión(es) — ` +
                   `${pm.productos.length} partida(s) cargadas`
        });

        bootstrap.Modal.getInstance(this.dom.modal)?.hide();
    }

    _crearProducto(p) {
        return {
            id: (crypto.randomUUID ? crypto.randomUUID() : `rem_${p.remisionId}_${p.idPartida}`),
            productoId: p.cve_prod,
            descripcion: p.descripcion,
            existencia: 0,
            cantidad: p.cantidadAFacturar,
            precio: p.precio,
            descuento: p.descuento,
            unidad: p.unidad || 'PZA',
            existenciaGeneral: 0,
            existenciaModular: 0,
            comentario: `Remisión: ${p.folio}`,
            ...(CFG.incluirPedimentos ? { pedimentos: p.pedimentos || [] } : {}),
            _remisionId: p.remisionId,
            _partidaRemisionId: p.idPartida,
            _folioRemision: p.folio
        };
    }

    /**
     * Se reconstruye desde pm.productos (no desde el Map) para que, si el
     * usuario borró o editó renglones en la tabla después de aplicar, el
     * detalle enviado al backend refleje exactamente lo que se va a facturar.
     */
    construirDetalleDesdeProductos(productos) {
        return (productos || [])
            .filter(p => p._partidaRemisionId != null && p._remisionId != null)
            .map(p => ({
                remisionId: Number(p._remisionId),
                id_partida_remision: Number(p._partidaRemisionId),
                cve_prod: p.productoId,
                cantidad_a_facturar: Number(p.cantidad) || 0,
                precio_unitario: Number(p.precio) || 0,
                descuento: Number(p.descuento) || 0,
                esCompleta: false,
                descripcion: p.descripcion || '',
                unidad: p.unidad || 'PZA',
                folio_remision: p._folioRemision || ''
            }));
    }

    _sincronizarCamposOcultos(productos) {
        const detalle = this.construirDetalleDesdeProductos(productos);
        const ids = [...new Set(detalle.map(d => d.remisionId))];

        if (this.dom.hiddenIds) this.dom.hiddenIds.value = ids.join(',');
        if (this.dom.hiddenDetalle)
            this.dom.hiddenDetalle.value = detalle.length ? JSON.stringify(detalle) : '';
    }

    limpiarCamposOcultos() {
        if (this.dom.hiddenIds) this.dom.hiddenIds.value = '';
        if (this.dom.hiddenDetalle) this.dom.hiddenDetalle.value = '';
    }

    // ══════════════════════════════════════════════════════════
    // Limpieza
    // ══════════════════════════════════════════════════════════
    _limpiarEstructuras() {
        [...this.remisionesInfo.keys()].forEach(id => this._marcarItemLista(id, false));
        this.partidasSeleccionadas.clear();
        this.remisionesInfo.clear();
        this.limpiarCamposOcultos();
    }

    limpiarSeleccion({ avisar = false } = {}) {
        this._limpiarEstructuras();
        this.state.remisionActivaId = null;
        this._mostrarTablaPartidas(false);
        this._actualizarResumen();
        if (avisar) toastMixin?.fire({ icon: 'info', title: 'Selección limpiada' });
    }

    /** Compatibilidad con el nombre anterior. */
    limpiar() { this._limpiarEstructuras(); this._actualizarResumen(); }

    tieneRemisiones() { return this.partidasSeleccionadas.size > 0; }

    // ══════════════════════════════════════════════════════════
    // Helpers
    // ══════════════════════════════════════════════════════════
    _importe(cantidad, precio, descuento) {
        const c = Number(cantidad) || 0;
        const p = Number(precio) || 0;
        const d = Number(descuento) || 0;
        return c * p * (1 - d / 100);
    }

    _mostrarTablaPartidas(show) {
        if (this.dom.tablaPartidas)
            this.dom.tablaPartidas.style.display = show ? 'block' : 'none';
        if (this.dom.estadoVacio)
            this.dom.estadoVacio.style.display = show ? 'none' : 'flex';
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
                <button type="button" class="page-link" data-page="${this.state.page - 1}">‹</button>
            </li>
            ${pages.map(p => `
                <li class="page-item ${p === this.state.page ? 'active' : ''}">
                    <button type="button" class="page-link" data-page="${p}">${p}</button>
                </li>`).join('')}
            <li class="page-item ${this.state.page === totalPages ? 'disabled' : ''}">
                <button type="button" class="page-link" data-page="${this.state.page + 1}">›</button>
            </li>`;
    }

    _esc(text) {
        const d = document.createElement('div');
        d.textContent = String(text ?? '');
        return d.innerHTML;
    }

    _fmt(val, decimals = 2) {
        return (parseFloat(val) || 0).toFixed(decimals);
    }

    /** Cantidades: hasta 4 decimales, sin ceros de relleno. */
    _fmtCant(val) {
        const n = parseFloat(val) || 0;
        return String(parseFloat(n.toFixed(4)));
    }
}

// ══════════════════════════════════════════════════════════════
// Inicialización
// ══════════════════════════════════════════════════════════════
let remisionParcialesManager = null;

document.addEventListener('DOMContentLoaded', () => {
    remisionParcialesManager = new FacturasRemisionParcialesManager();
    window.remisionParcialesManager = remisionParcialesManager;

    if (typeof FacturasFormManager === 'undefined') {
        console.error('FacturasFormManager no encontrado — revisa el orden de scripts.');
        return;
    }

    // ── Inyectar el detalle de remisiones en el FormData de la factura ──
    const _originalRecopilar =
        FacturasFormManager.prototype.recopilarFacturasDatosFormulario;

    FacturasFormManager.prototype.recopilarFacturasDatosFormulario = function () {
        const formData = _originalRecopilar.call(this);

        // Se recalcula al vuelo: si el usuario borró renglones de la tabla
        // después de aplicar la selección, no debemos descontar ese saldo.
        const productos = this.productManager?.productos || [];
        const detalle = remisionParcialesManager
            ?.construirDetalleDesdeProductos(productos) || [];

        remisionParcialesManager?._sincronizarCamposOcultos(productos);

        if (detalle.length > 0) {
            formData.set('remisionesParcialesJSON', JSON.stringify(detalle));
            formData.set('remisionesIds',
                [...new Set(detalle.map(d => d.remisionId))].join(','));
        } else {
            formData.delete('remisionesParcialesJSON');
            formData.delete('remisionesIds');
        }

        return formData;
    };

    // ── Limpiar la selección cuando se limpia el formulario ──
    const _originalLimpiar = FacturasFormManager.prototype.limpiarFacturasFormulario;

    FacturasFormManager.prototype.limpiarFacturasFormulario = function () {
        _originalLimpiar.call(this);
        remisionParcialesManager?.limpiar();
        remisionParcialesManager?._sincronizarEstadoCliente();
    };
});
})();

// handlers/adenda.js — Corrección de Adenda
// Depende de: utils.js, catalogos.js, state.js

const AdendaHandler = {
    tipoRelacion: '04',

    // Recolecta los cambios para el back (AdendaTypeHandler lee modo, idAddenda, valores).
    collectChanges(cfdi) {
        return {
            modo: AppState.adendaModo || 'solo-corregir',
            idAddenda: AppState.adendaSeleccionada?.id_addenda || 0,
            valores: getAdendaValores(),
        };
    },

    tabs: [
        { id: 'adenda-modo', label: 'Modo', build: _buildTabModo },
        { id: 'adenda-datos', label: 'Datos de la Adenda', build: _buildTabDatos },
    ],

    buildSummary(cfdi) {
        const modo = AppState.adendaModo || 'solo-corregir';
        const adenda = AppState.adendaSeleccionada;
        const valores = getAdendaValores();

        return `
            <div class="row g-3">
                <div class="col-md-6">
                    <div class="form-label-custom">Modo de operación</div>
                    <div style="font-weight:700">
                        ${modo === 'cancelar-reemitir' ? 'Cancelar y reemitir CFDI' : 'Solo corregir Adenda (sin cancelar)'}
                    </div>
                </div>
                <div class="col-md-6">
                    <div class="form-label-custom">Adenda aplicada</div>
                    <div style="font-weight:600">${esc(adenda?.nombre ?? '—')}</div>
                </div>
            </div>
            <div class="section-divider"></div>
            <div class="form-label-custom mb-1">Valores capturados</div>
            <div class="table-wrapper">
                <table class="table-custom">
                    <tbody>
                        ${Object.entries(valores).map(([k, v]) => `
                            <tr><td style="font-weight:600;width:40%">${esc(k)}</td><td>${esc(v)}</td></tr>
                        `).join('') || '<tr><td>Sin datos capturados</td></tr>'}
                    </tbody>
                </table>
            </div>`;
    },

    // ── Dinámicos según AppState.adendaModo ─────────────────────────
    get confirmChecks() {
        const base = [
            { id: 'chkAdenda1', label: 'Verifiqué que la Adenda corregida cumple el formato del cliente' },
        ];
        if (AppState.adendaModo === 'cancelar-reemitir') {
            base.push({ id: 'chkAdenda2', label: 'Confirmo que se requiere un nuevo UUID (no basta con regenerar Adenda)', danger: true });
        }
        return base;
    },

    get execSteps() {
        return AppState.adendaModo === 'cancelar-reemitir'
            ? [
                { text: 'Validar Adenda corregida', sub: 'Contra data_template del cliente' },
                { text: 'Cancelar CFDI original', sub: 'Motivo 01 · Envío al PAC' },
                { text: 'Emitir nuevo CFDI', sub: 'Con Adenda corregida embebida' },
                { text: 'Relacionar con UUID original', sub: 'Tipo relación 04' },
                { text: 'Notificar al receptor', sub: 'XML y PDF por correo' },
                { text: 'Registrar en bitácora', sub: 'Trazabilidad completa' },
            ]
            : [
                { text: 'Validar Adenda corregida', sub: 'Contra data_template del cliente' },
                { text: 'Regenerar XML', sub: 'Mismo UUID · sin timbrado nuevo' },
                { text: 'Regenerar PDF', sub: 'Representación impresa actualizada' },
                { text: 'Notificar al receptor', sub: 'XML y PDF por correo' },
                { text: 'Registrar en bitácora', sub: 'Trazabilidad completa' },
            ];
    },
};

// ── TAB 1: Selección de modo ─────────────────────────────────────────
function _buildTabModo(pane, cfdi) {
    if (!AppState.adendaModo) AppState.adendaModo = 'solo-corregir';

    pane.innerHTML = `
        ${fiscalAlert('info', 'fa-file-code',
        'Corrección de Adenda.',
        'La Adenda no es información validada por el SAT. En la mayoría de los casos basta con ' +
        'regenerar el XML/PDF con la Adenda corregida, sin cancelar el CFDI. Solo cancela y reemite ' +
        'si el cliente exige un nuevo UUID.')}

        ${sectionTitle('¿Qué deseas hacer?')}
        <div id="adendaModoSelector">
            <div class="motivo-card ${AppState.adendaModo === 'solo-corregir' ? 'selected' : ''}"
                 onclick="setAdendaModo(this, 'solo-corregir')">
                <input type="radio" name="adendaModo" value="solo-corregir"
                       ${AppState.adendaModo === 'solo-corregir' ? 'checked' : ''}>
                <div>
                    <div class="motivo-text">Solo corregir la Adenda</div>
                    <div class="motivo-sub">El CFDI original permanece vigente con el mismo UUID. Se regenera el XML/PDF y se reenvía.</div>
                </div>
            </div>
            <div class="motivo-card ${AppState.adendaModo === 'cancelar-reemitir' ? 'selected' : ''}"
                 onclick="setAdendaModo(this, 'cancelar-reemitir')">
                <input type="radio" name="adendaModo" value="cancelar-reemitir"
                       ${AppState.adendaModo === 'cancelar-reemitir' ? 'checked' : ''}>
                <div>
                    <div class="motivo-text">Cancelar y reemitir el CFDI</div>
                    <div class="motivo-sub">Se cancela el CFDI original (motivo 01) y se timbra uno nuevo con relación 04, incluyendo la Adenda corregida.</div>
                </div>
            </div>
        </div>`;

    _updateRelacionCardVisibility();
}

window.setAdendaModo = function (elCard, modo) {
    AppState.adendaModo = modo;
    document.querySelectorAll('#adendaModoSelector .motivo-card').forEach(c => c.classList.remove('selected'));
    elCard.classList.add('selected');
    elCard.querySelector('input[type=radio]').checked = true;
    _updateRelacionCardVisibility();
};

// Oculta "Relación de CFDI" (motivo/UUID) cuando no se va a cancelar
function _updateRelacionCardVisibility() {
    const relCard = document.getElementById('tipoRelacion')?.closest('.card-custom');
    if (!relCard) return;
    const esAdenda = AppState.selectedType?.id === 'adenda';
    const necesitaRelacion = !esAdenda || AppState.adendaModo === 'cancelar-reemitir';
    relCard.style.display = necesitaRelacion ? 'block' : 'none';
}

// ── TAB 2: Datos de la Adenda (plantilla del cliente) ────────────────
function _buildTabDatos(pane, cfdi) {
    pane.innerHTML = `
        ${sectionTitle('Plantilla de Adenda del cliente')}
        <div class="row g-3 mb-3">
            <div class="col-md-6">
                ${formRow('Adenda *', `<select class="form-control-custom" id="adendaSelect" onchange="onAdendaSelected()">
                    <option value="">Cargando...</option>
                </select>`, 'Definidas en cfdi_addenda_def para este cliente.')}
            </div>
            <div class="col-md-6" id="adendaMetaInfo"></div>
        </div>
        <div id="adendaCamposContainer"></div>`;

    cargarAdendasCliente(cfdi.idCliente);
}

function cargarAdendasCliente(idCliente) {
    const sel = document.getElementById('adendaSelect');
    if (!sel) return;
    if (!idCliente) {
        sel.innerHTML = '<option value="">Sin cliente identificado</option>';
        return;
    }
    fetch(`/DatosGeneralesRefacturacion/ObtenerAdendas?idCliente=${idCliente}`)
        .then(r => r.json())
        .then(res => {
            const adendas = res.data ?? [];
            AppState.adendasDisponibles = adendas;
            if (!adendas.length) {
                sel.innerHTML = '<option value="">Este cliente no tiene Adendas configuradas</option>';
                document.getElementById('adendaCamposContainer').innerHTML = fiscalAlert(
                    'warning', 'fa-triangle-exclamation',
                    'Sin plantilla de Adenda.',
                    'No hay ninguna Adenda activa para este cliente.'
                );
                return;
            }
            sel.innerHTML = '<option value="">Selecciona una Adenda...</option>' +
                adendas.map(a => `<option value="${a.id_addenda}">${esc(a.nombre)} (v${esc(a.version ?? '1')})</option>`).join('');
        })
        .catch(() => {
            sel.innerHTML = '<option value="">Error al cargar Adendas</option>';
            showToast('error', 'No se pudieron cargar las Adendas del cliente.');
        });
}

window.onAdendaSelected = function () {
    const id = parseInt(document.getElementById('adendaSelect').value);
    const adenda = (AppState.adendasDisponibles || []).find(a => a.id_addenda === id);
    const meta = document.getElementById('adendaMetaInfo');
    const container = document.getElementById('adendaCamposContainer');

    if (!adenda) { meta.innerHTML = ''; container.innerHTML = ''; return; }

    AppState.adendaSeleccionada = adenda;

    meta.innerHTML = `
        <div class="form-label-custom">Namespace / Prefijo</div>
        <div style="font-family:'JetBrains Mono',monospace;font-size:.78rem">
            ${esc(adenda.xml_prefix ?? 'add')}: ${esc(adenda.xml_namespace)}
        </div>
        <div style="font-size:.75rem;color:var(--text-light);margin-top:.3rem">
            Usa conceptos: ${adenda.usar_conceptos ? 'Sí' : 'No'}
        </div>`;

    const template = typeof adenda.data_template === 'string'
        ? JSON.parse(adenda.data_template)
        : adenda.data_template;

    renderAdendaCampos(container, template);
};
function flattenObject(obj, prefix = '') {
    let result = [];

    Object.entries(obj).forEach(([key, value]) => {
        const fullKey = prefix ? `${prefix}.${key}` : key;

        if (value && typeof value === 'object' && !Array.isArray(value)) {
            result.push(...flattenObject(value, fullKey));
        } else {
            result.push({
                key: fullKey,
                label: fullKey,
                type: 'text',
                required: false,
                default: value
            });
        }
    });

    return result;
}
function renderAdendaCampos(container, template) {
    let campos = [];
    if (template && Array.isArray(template.campos)) {
        campos = template.campos;
    } else if (template && typeof template === 'object') {
        campos = flattenObject(template);
    }

    if (!campos.length) {
        container.innerHTML = fiscalAlert('warning', 'fa-triangle-exclamation',
            'Plantilla vacía.', 'Esta Adenda no define campos capturables.');
        return;
    }

    container.innerHTML = `
        ${sectionTitle('Datos de la Adenda')}
        <div class="row g-3">
            ${campos.map(c => `
                <div class="col-md-4">
                    ${formRow(
        c.label + (c.required ? ' *' : ''),
        c.type === 'select' && Array.isArray(c.options)
            ? `<select class="form-control-custom adenda-campo" data-key="${esc(c.key)}">
                                 ${c.options.map(o => `<option value="${esc(o)}">${esc(o)}</option>`).join('')}
                               </select>`
            : `<input class="form-control-custom adenda-campo" data-key="${esc(c.key)}"
                                      type="${c.type === 'number' ? 'number' : c.type === 'date' ? 'date' : 'text'}"
                                      value="${esc(c.default ?? '')}">`
    )}
                </div>`).join('')}
        </div>`;
}

function getAdendaValores() {
    const valores = {};
    document.querySelectorAll('.adenda-campo').forEach(inp => {
        valores[inp.dataset.key] = inp.value;
    });
    return valores;
}
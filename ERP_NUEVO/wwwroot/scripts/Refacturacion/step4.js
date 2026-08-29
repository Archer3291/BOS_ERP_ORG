// step4.js — Orquestador del paso 4
// Depende de: utils.js, catalogos.js, state.js, y TODOS los handlers

// Registro de handlers — agrega aquí cada nuevo tipo
const STEP4_HANDLERS = {
    'datos-fiscales': DatosFiscalesHandler,
    'conceptos': ConceptosHandler,
    'metodo-pago': MetodoPagoHandler,
    'moneda': MonedaHandler,
    'forma-pago': FormaPagoHandler,
    'parcial': ParcialHandler,
    'devolucion': DevolucionHandler,
    'anticipo': AnticipoHandler,
    'factura-pagada': FacturaPagadaHandler,
    'complemento-pago': ComplementoPagoHandler,
    'impuestos': ImpuestosHandler,
    'adenda': AdendaHandler, 
    //'global': GlobalHandler,
    //'sustitucion-diferida': SustDiferidaHandler,
    //'masiva': MasivaHandler,
};

function buildStep4() {
    const tipo = AppState.selectedType;
    const cfdi = AppState.selectedCFDI;
    const handler = STEP4_HANDLERS[tipo.id];

    // Label del tipo en el header de la card
    const label = document.getElementById('step4TypeLabel');
    label.textContent = tipo.name;
    label.className = `status-badge ${tipo.risk === 'low' ? 'vigente' :
            tipo.risk === 'med' ? 'pendiente' : 'cancelado'
        }`;

    // Limpiar tabs del paso anterior
    const tabsEl = document.getElementById('step4Tabs');
    const content = document.getElementById('step4TabContent');
    tabsEl.innerHTML = '';
    content.innerHTML = '';

    // Sin handler registrado → tab genérico
    if (!handler) {
        content.innerHTML = _buildTabDefault();
        return;
    }

    // Construir tabs del handler
    handler.tabs.forEach((t, i) => {
        const btn = document.createElement('button');
        btn.className = `tab-btn ${i === 0 ? 'active' : ''}`;
        btn.textContent = t.label;
        btn.onclick = () => {
            tabsEl.querySelectorAll('.tab-btn')
                .forEach(b => b.classList.remove('active'));
            btn.classList.add('active');
            content.querySelectorAll('.tab-pane')
                .forEach(p => p.classList.remove('active'));
            document.getElementById('tp-' + t.id).classList.add('active');
        };
        tabsEl.appendChild(btn);

        const pane = document.createElement('div');
        pane.className = `tab-pane ${i === 0 ? 'active' : ''}`;
        pane.id = 'tp-' + t.id;
        content.appendChild(pane);

        t.build(pane, cfdi);
    });

    // Prefills comunes a todos los tipos
    const relInput = document.getElementById('uuidRelacionado');
    if (relInput) relInput.value = cfdi.uuid;

    const tipoRelSelect = document.getElementById('tipoRelacion');
    if (tipoRelSelect && handler.tipoRelacion)
        tipoRelSelect.value = handler.tipoRelacion;
}

function _buildTabDefault() {
    return fiscalAlert('info', 'fa-circle-info',
        'Configura los cambios.',
        'Selecciona el tipo de refacturación en el Paso 1 para ver la configuración.');
}
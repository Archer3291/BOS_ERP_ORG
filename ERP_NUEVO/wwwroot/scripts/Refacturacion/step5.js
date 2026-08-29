// step5.js — Orquestador del paso 5
// Depende de: utils.js, state.js, step4.js (para STEP4_HANDLERS)

function buildStep5() {
    const cfdi = AppState.selectedCFDI;
    const tipo = AppState.selectedType;
    const handler = STEP4_HANDLERS[tipo.id];

    // Resumen: parte común + parte específica del handler
    document.getElementById('step5Summary').innerHTML =
        _buildCommonSummary(cfdi, tipo) +
        (handler?.buildSummary
            ? `<div class="section-divider"></div>${handler.buildSummary(cfdi)}`
            : '');

    // Resumen financiero
    document.getElementById('amountSummary').innerHTML =
        _buildAmountSummary(cfdi);

    // Checks de confirmación
    const extraChecks = handler?.confirmChecks ?? [];
    document.getElementById('confirmChecks').innerHTML =
        _buildCommonChecks(tipo) +
        extraChecks.map(c => _buildCheckItem(c.id, c.label, c.danger)).join('');

    // Pasos de ejecución
    const steps = handler?.execSteps ?? DEFAULT_EXEC_STEPS;
    document.getElementById('execStepsList').innerHTML =
        steps.map((s, i) => _buildExecStep(s, i)).join('');
}

// ── Partes comunes ────────────────────────────────────────────────────────────

function _buildCommonSummary(cfdi, tipo) {
    return `
        <div class="row g-3">
            <div class="col-md-6">
                <div class="form-label-custom">Tipo refacturación</div>
                <div style="font-weight:700">${esc(tipo.name)}</div>
            </div>
            <div class="col-md-6">
                <div class="form-label-custom">CFDI original</div>
                <div style="font-family:'JetBrains Mono',monospace;font-size:.78rem">
                    ${esc(cfdi.uuid)}
                </div>
            </div>
            <div class="col-md-6">
                <div class="form-label-custom">Receptor</div>
                <div style="font-weight:600">${esc(cfdi.receptor)}</div>
            </div>
            <div class="col-md-6">
                <div class="form-label-custom">RFC</div>
                <div style="font-family:'JetBrains Mono',monospace">${esc(cfdi.rfc)}</div>
            </div>
            <div class="col-md-4">
                <div class="form-label-custom">Motivo cancelación</div>
                <div>0${AppState.motivo} · ${AppState.motivo === '01' ? 'Con relación' : 'Sin relación'}</div>
            </div>
            <div class="col-md-4">
                <div class="form-label-custom">Tipo relación</div>
                <div>${document.getElementById('tipoRelacion')?.value ?? '04'} - Sustitución</div>
            </div>
            <div class="col-md-4">
                <div class="form-label-custom">Riesgo</div>
                <span class="type-badge ${tipo.risk}">${tipo.riskLabel}</span>
            </div>
        </div>`;
}

function _buildAmountSummary(cfdi) {
    return `
        <div class="amount-row">
            <span class="label">Subtotal</span>
            <span class="value">$${fmtMoney(cfdi.subtotal)}</span>
        </div>
        <div class="amount-row">
            <span class="label">IVA</span>
            <span class="value">$${fmtMoney(cfdi.iva)}</span>
        </div>
        <div class="amount-row amount-total">
            <span class="label">Total nuevo CFDI</span>
            <span class="value">$${fmtMoney(cfdi.total)}</span>
        </div>
        <div class="amount-row" style="margin-top:.5rem">
            <span class="label" style="font-size:.72rem">Moneda</span>
            <span class="value" style="font-size:.78rem">${esc(cfdi.moneda)}</span>
        </div>`;
}

function _buildCommonChecks(tipo) {
    return [
        { id: 'chk1', label: 'Confirmé que los datos del nuevo CFDI son correctos' },
        { id: 'chk2', label: 'Revisé el impacto contable y de inventario' },
        { id: 'chk3', label: 'Notifiqué al área fiscal' },
    ].map(c => _buildCheckItem(c.id, c.label, false)).join('') +
        (tipo.risk === 'high' || tipo.risk === 'very-high'
            ? _buildCheckItem('chk4', 'Autorización de gerencia obtenida (riesgo alto)', true)
            : '');
}

function _buildCheckItem(id, label, danger) {
    return `
        <div class="checklist-item" style="margin-top:.35rem">
            <input type="checkbox" id="${id}"
                style="accent-color:var(--${danger ? 'danger' : 'accent-blue'}-color)">
            <label for="${id}"
                style="font-size:.85rem;cursor:pointer;margin:0;
                       ${danger ? 'color:var(--danger-color)' : ''}">
                ${label}
            </label>
        </div>`;
}

function _buildExecStep(s, i) {
    return `
        <li class="progress-step-item ${i === 0 ? 'psi-active' : ''}">
            <div class="psi-num">${i + 1}</div>
            <div>
                <div class="psi-text">${s.text}</div>
                <div class="psi-sub">${s.sub}</div>
            </div>
        </li>`;
}

const DEFAULT_EXEC_STEPS = [
    { text: 'Validar CFDI ante SAT', sub: 'Consulta al servicio de validación' },
    { text: 'Solicitar cancelación', sub: 'Envío al PAC certificado' },
    { text: 'Esperar aceptación', sub: 'Hasta 72 hrs hábiles' },
    { text: 'Timbrar nuevo CFDI', sub: 'Generación y sellado digital' },
    { text: 'Actualizar registros internos', sub: 'Contabilidad, inventario, CxC' },
    { text: 'Notificar al receptor', sub: 'XML y PDF por correo' },
    { text: 'Registrar en bitácora', sub: 'Trazabilidad completa' },
];
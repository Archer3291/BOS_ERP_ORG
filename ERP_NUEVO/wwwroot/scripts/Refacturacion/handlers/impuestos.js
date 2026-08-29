// handlers/correccion-impuestos.js
// Base legal: Art. 6, 7, 14 LIVA; Art. 1 LISR; CFF Art. 29-A fracc VII
// Depende de: utils.js, catalogos.js, state.js

var ImpuestosHandler = {

    tipoRelacion: '04',

    tabs: [
        {
            id: 'impuestos',
            label: 'Corrección de impuestos',
            build: function (el, cfdi) { ImpuestosHandler._buildTabImpuestosCorr(el, cfdi); }
        },
        {
            id: 'retenciones',
            label: 'Tabla de retenciones',
            build: function (el, cfdi) { ImpuestosHandler._buildTabRetenciones(el, cfdi); }
        },
    ],

    buildSummary: function (cfdi) {
        const tasa = document.getElementById('corrIVATasa')?.value || '0.16';
        const retISR = document.getElementById('corrRetISR')?.value || '0';
        const retIVA = document.getElementById('corrRetIVA')?.value || '0';
        const motivo = document.getElementById('corrMotivo')?.value || '—';
        const fundo = document.getElementById('corrFundamento')?.value || '—';

        return `
            <div class="row g-3">
                <div class="col-md-4">
                    <div class="form-label-custom">Nueva tasa IVA</div>
                    <div style="font-weight:600">${esc(tasa === 'exento' ? 'Exento' : (parseFloat(tasa) * 100) + '%')}</div>
                </div>
                <div class="col-md-4">
                    <div class="form-label-custom">Retención ISR %</div>
                    <div>${esc(retISR)}%</div>
                </div>
                <div class="col-md-4">
                    <div class="form-label-custom">Retención IVA %</div>
                    <div>${esc(retIVA)}%</div>
                </div>
                <div class="col-md-6">
                    <div class="form-label-custom">Fundamento legal</div>
                    <div style="font-size:.83rem">${esc(fundo)}</div>
                </div>
                <div class="col-md-6">
                    <div class="form-label-custom">Motivo del cambio</div>
                    <div style="font-size:.83rem">${esc(motivo)}</div>
                </div>
            </div>`;
    },

    execSteps: [
        { text: 'Obtener autorización del área fiscal', sub: 'Revisar impacto en declaraciones presentadas' },
        { text: 'Cancelar CFDI original (motivo 01)', sub: 'Envío al PAC certificado' },
        { text: 'Esperar aceptación del receptor', sub: 'Hasta 72 hrs hábiles' },
        { text: 'Emitir nuevo CFDI con impuestos correctos', sub: 'Con la tasa y retenciones validadas' },
        { text: 'Relacionar con UUID original (rel. 04)', sub: 'Nodo CfdiRelacionados' },
        { text: 'Evaluar declaraciones complementarias', sub: 'Si el período ya fue declarado' },
    ],

    confirmChecks: [
        { id: 'chkFiscalAuth', label: 'Autorización del área fiscal obtenida' },
        { id: 'chkDeclaracion', label: 'Impacto en declaraciones del período evaluado' },
    ],

    // ── Tabs internos ─────────────────────────────────────────────────────────

    _buildTabImpuestosCorr: function (el, cfdi) {
        el.innerHTML = `
            ${fiscalAlert('danger', 'fa-radiation',
            'Corrección de impuestos — Riesgo fiscal muy alto.',
            'Cambiar tasas de IVA o retenciones después de emitido el CFDI puede afectar declaraciones ' +
            'del período ya presentadas. Requiere autorización del área fiscal y puede implicar declaraciones complementarias.')}

            ${sectionTitle('Tasa IVA trasladado')}
            <div class="row g-3 mb-3">
                <div class="col-md-4">
                    <label class="form-label-custom">IVA actual (en el CFDI)</label>
                    <div style="background:rgba(220,38,38,0.06);border:1.5px solid rgba(220,38,38,0.25);
                        border-radius:8px;padding:.55rem .9rem;font-weight:700;color:var(--danger-color)">
                        16% · $${fmtMoney(cfdi.iva)}
                    </div>
                </div>
                <div class="col-md-4">
                    ${formRow('Nueva tasa IVA *',
                `<select class="form-control-custom" id="corrIVATasa" onchange="ImpuestosHandler._calcCorrIVA()">
                            <option value="0.16">16% · Tasa general</option>
                            <option value="0.08">8% · Zona fronteriza (Decreto DOF 31/dic/2018)</option>
                            <option value="0">0% · Tasa cero</option>
                            <option value="exento">Exento · Sin traslado</option>
                        </select>`,
                'Verifica el fundamento legal de la tasa antes de cambiar.')}
                </div>
                <div class="col-md-4">
                    ${formRow('Nuevo importe IVA',
                    `<input class="form-control-custom" type="number" id="corrIVAMonto"
                            readonly style="background:var(--bg-gray)" value="${cfdi.iva}">`,
                    'Se calcula automáticamente.')}
                </div>
            </div>

            ${sectionTitle('Retenciones')}
            <div class="row g-3 mb-3">
                <div class="col-md-3">
                    ${formRow('Nueva retención ISR %',
                        `<input class="form-control-custom" type="number" id="corrRetISR"
                            value="0" min="0" max="35" step="0.000001"
                            onchange="ImpuestosHandler._calcCorrIVA()">`,
                        '0%, 10% (honorarios, arrendamiento PF), 25% (extranjeros)')}
                </div>
                <div class="col-md-3">
                    ${formRow('Nueva retención IVA %',
                            `<input class="form-control-custom" type="number" id="corrRetIVA"
                            value="0" min="0" max="16" step="0.000001"
                            onchange="ImpuestosHandler._calcCorrIVA()">`,
                            '0%, 10.666667%, 16%')}
                </div>
                <div class="col-md-3">
                    ${formRow('Importe ret. ISR',
                                `<input class="form-control-custom" type="number" id="corrRetISRMonto" readonly style="background:var(--bg-gray)">`)}
                </div>
                <div class="col-md-3">
                    ${formRow('Importe ret. IVA',
                                    `<input class="form-control-custom" type="number" id="corrRetIVAMonto" readonly style="background:var(--bg-gray)">`)}
                </div>
            </div>

            ${sectionTitle('Justificación fiscal obligatoria')}
            <div class="row g-3">
                <div class="col-md-6">
                    ${formRow('Fundamento legal *',
                                        `<select class="form-control-custom" id="corrFundamento">
                            <option>Art. 6 LIVA — Tasa 0%</option>
                            <option>Art. 9 LIVA — Exentos de IVA</option>
                            <option>Decreto zona fronteriza DOF 31/dic/2018 — 8%</option>
                            <option>Art. 2-A LIVA — Alimentos y medicinas</option>
                            <option>Art. 15 LIVA — Servicios exentos</option>
                            <option>Otro (especificar)</option>
                        </select>`)}
                </div>
                <div class="col-md-6">
                    ${formRow('Descripción del motivo *',
                                            `<textarea class="form-control-custom" id="corrMotivo" rows="2"
                            placeholder="Describe el error y el fundamento para el cambio de tasa..."></textarea>`)}
                </div>
            </div>

            <div id="corrIVAResumen" class="mt-3"></div>`;

        setTimeout(() => ImpuestosHandler._calcCorrIVA(), 80);
    },

    _buildTabRetenciones: function (el, cfdi) {
        el.innerHTML = `
            ${fiscalAlert('info', 'fa-hand-holding',
            'Tabla de retenciones obligatorias SAT.',
            'Las retenciones son obligatorias para ciertos tipos de servicios. ' +
            'El emisor es responsable de retener y enterar los impuestos al SAT.')}

            <div class="table-wrapper mb-3">
                <table class="table-custom">
                    <thead>
                        <tr>
                            <th>Tipo de servicio</th>
                            <th>Ret. ISR %</th>
                            <th>Ret. IVA %</th>
                            <th>Fundamento</th>
                        </tr>
                    </thead>
                    <tbody>
                        ${[
                ['Honorarios (PF)', '10%', '10.666667%', 'Art. 106 LISR / Art. 1-A LIVA'],
                ['Arrendamiento (PF)', '10%', '10.666667%', 'Art. 117 LISR / Art. 1-A LIVA'],
                ['Actividades empresariales (PF)', '1%', '—', 'Art. 106 LISR'],
                ['Servicios de transporte terrestre', '—', '4%', 'Art. 1-A frac. II-b LIVA'],
                ['Comisiones y mediaciones', '—', '—', 'Solo si aplica art. 1-A LIVA'],
                ['Pagos a extranjeros (sin est. perm.)', '25%', '—', 'Art. 158 LISR'],
            ].map(r => `<tr>${r.map(c => `<td style="font-size:.81rem">${c}</td>`).join('')}</tr>`).join('')}
                    </tbody>
                </table>
            </div>`;
    },

    // ── Cálculos internos ─────────────────────────────────────────────────────

    _calcCorrIVA: function () {
        const cfdi = AppState.selectedCFDI;
        if (!cfdi) return;

        const tasa = document.getElementById('corrIVATasa')?.value;
        const retISRPct = parseFloat(document.getElementById('corrRetISR')?.value || 0) / 100;
        const retIVAPct = parseFloat(document.getElementById('corrRetIVA')?.value || 0) / 100;
        const base = cfdi.subtotal;

        const ivaNum = tasa === 'exento' ? 0 : base * parseFloat(tasa || 0);
        const retISR = base * retISRPct;
        const retIVA = base * retIVAPct;
        const newTotal = base + ivaNum - retISR - retIVA;

        const elIVA = document.getElementById('corrIVAMonto');
        if (elIVA) elIVA.value = ivaNum.toFixed(2);
        const r1 = document.getElementById('corrRetISRMonto');
        if (r1) r1.value = retISR.toFixed(2);
        const r2 = document.getElementById('corrRetIVAMonto');
        if (r2) r2.value = retIVA.toFixed(2);

        const res = document.getElementById('corrIVAResumen');
        if (!res) return;
        res.innerHTML = `
            <div class="diff-panel">
                <div class="diff-original">
                    <div class="diff-header"><i class="fas fa-file-xmark me-1"></i>CFDI Original</div>
                    <div class="diff-row"><span class="diff-label">Subtotal</span>
                        <span class="diff-value diff-changed">$${fmtMoney(base)}</span></div>
                    <div class="diff-row"><span class="diff-label">IVA</span>
                        <span class="diff-value diff-changed">$${fmtMoney(cfdi.iva)}</span></div>
                    <div class="diff-row"><span class="diff-label">Total</span>
                        <span class="diff-value diff-changed">$${fmtMoney(cfdi.total)}</span></div>
                </div>
                <div class="diff-new">
                    <div class="diff-header"><i class="fas fa-file-circle-check me-1"></i>Nuevo CFDI</div>
                    <div class="diff-row"><span class="diff-label">Subtotal</span>
                        <span class="diff-value diff-new-val">$${fmtMoney(base)}</span></div>
                    <div class="diff-row">
                        <span class="diff-label">IVA (${tasa === 'exento' ? 'Exento' : (parseFloat(tasa || 0) * 100) + '%'})</span>
                        <span class="diff-value diff-new-val">$${fmtMoney(ivaNum)}</span></div>
                    ${retISR > 0 ? `
                    <div class="diff-row"><span class="diff-label">Ret. ISR</span>
                        <span class="diff-value diff-new-val">-$${fmtMoney(retISR)}</span></div>` : ''}
                    ${retIVA > 0 ? `
                    <div class="diff-row"><span class="diff-label">Ret. IVA</span>
                        <span class="diff-value diff-new-val">-$${fmtMoney(retIVA)}</span></div>` : ''}
                    <div class="diff-row"><span class="diff-label">Total</span>
                        <span class="diff-value diff-new-val">$${fmtMoney(newTotal)}</span></div>
                </div>
            </div>`;
    },
};
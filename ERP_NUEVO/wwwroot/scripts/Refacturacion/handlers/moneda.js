// handlers/moneda.js
// Depende de: utils.js, catalogos.js, state.js
// Base legal: Art. 20 CFF; Regla 2.7.1.26 RMF; DOF tipo de cambio

var MonedaHandler = {

    tipoRelacion: '04',

    // Se guarda el CFDI seleccionado para que los métodos de conversión (que solo reciben
    // el DOM) puedan calcular el factor y las previsualizaciones.
    _cfdi: null,

    // Recolecta los cambios para el back (MonedaTypeHandler lee moneda, tipoCambio, factorConversion).
    collectChanges: function (cfdi) {
        const c = cfdi || MonedaHandler._cfdi || {};
        const newMoneda = document.getElementById('newMoneda')?.value || c.moneda || 'MXN';
        let newTC = parseFloat(document.getElementById('newTC')?.value) || parseFloat(c.tipoCambio) || 1;
        if (newMoneda === 'MXN') newTC = 1;

        const factor = MonedaHandler._calcFactor(c, newMoneda, newTC);

        return {
            moneda: newMoneda,
            tipoCambio: String(newTC),
            factorConversion: String(factor),
        };
    },

    // Factor que reexpresa los importes de cada concepto en la nueva moneda.
    //   misma moneda            → 1 (los importes no cambian; solo se corrige el TC).
    //   modo 'tc'  (default)    → TC_viejo / TC_nuevo (preserva el valor en MXN).
    //   modo 'total'            → total_nuevo / total_actual (repreciar en la nueva moneda).
    _calcFactor: function (cfdi, newMoneda, newTC) {
        const c = cfdi || {};
        const oldMoneda = c.moneda || 'MXN';
        if (newMoneda === oldMoneda) return 1;

        const mode = document.querySelector('input[name="mon-convMode"]:checked')?.value || 'tc';

        if (mode === 'total') {
            const nuevoTotal = parseFloat(document.getElementById('mon-nuevoTotal')?.value);
            const totalActual = parseFloat(c.total) || 0;
            return (nuevoTotal > 0 && totalActual > 0) ? (nuevoTotal / totalActual) : 1;
        }

        // modo 'tc': precio_new = precio_old * (TC_viejo / TC_nuevo) → conserva el valor en pesos.
        const tcOld = parseFloat(c.tipoCambio) || 1;
        return (newTC > 0) ? (tcOld / newTC) : 1;
    },

    tabs: [
        {
            id: 'moneda',
            label: 'Moneda y tipo de cambio',
            build: function (el, cfdi) { MonedaHandler._buildTabMoneda(el, cfdi); }
        },
    ],

    buildSummary: function (cfdi) {
        const newMoneda = document.getElementById('newMoneda')?.value || cfdi.moneda;
        const newTC = document.getElementById('newTC')?.value || cfdi.tipoCambio || '1';
        const cambiaMoneda = newMoneda !== (cfdi.moneda || 'MXN');
        const factor = MonedaHandler._calcFactor(cfdi, newMoneda, parseFloat(newTC) || 1);
        const nuevoTotal = (parseFloat(cfdi.total) || 0) * factor;
        return `
            <div class="row g-3">
                <div class="col-md-4">
                    <div class="form-label-custom">Moneda anterior</div>
                    <div style="font-weight:600;color:var(--danger-color)">${esc(cfdi.moneda)}</div>
                </div>
                <div class="col-md-4">
                    <div class="form-label-custom">Moneda nueva</div>
                    <div style="font-weight:700;color:var(--success-color)">${esc(newMoneda)}</div>
                </div>
                <div class="col-md-4">
                    <div class="form-label-custom">Tipo de cambio</div>
                    <div style="font-weight:600;font-family:'JetBrains Mono',monospace">${esc(newTC)}</div>
                </div>
                <div class="col-md-12">
                    <div class="form-label-custom">Importes</div>
                    <div style="font-size:.85rem">${cambiaMoneda
                        ? `Convertidos (factor ${factor.toFixed(6)}) — Total: <strong>${fmtMoney(nuevoTotal)} ${esc(newMoneda)}</strong>`
                        : `Sin cambio — Total: <strong>${fmtMoney(cfdi.total)} ${esc(cfdi.moneda)}</strong> (solo se corrige el tipo de cambio)`}</div>
                </div>
            </div>`;
    },

    execSteps: [
        { text: 'Verificar TC en DOF para la fecha de emisión', sub: 'Art. 20 CFF — publicado diariamente' },
        { text: 'Cancelar CFDI original (motivo 01)', sub: 'Envío al PAC certificado' },
        { text: 'Esperar aceptación del receptor', sub: 'Hasta 72 hrs hábiles' },
        { text: 'Emitir nuevo CFDI con moneda y TC correctos', sub: 'Generación y sellado digital' },
        { text: 'Relacionar con UUID original (rel. 04)', sub: 'Nodo CfdiRelacionados' },
        { text: 'Notificar al receptor', sub: 'XML y PDF por correo' },
    ],

    confirmChecks: [
        { id: 'chkTCDOF', label: 'Tipo de cambio verificado en DOF para la fecha de emisión del CFDI' },
        { id: 'chkMXNuno', label: 'Si la moneda es MXN el TC es exactamente "1"' },
    ],

    // ── Tab interno ───────────────────────────────────────────────────────────

    _buildTabMoneda: function (el, cfdi) {
        MonedaHandler._cfdi = cfdi; // lo usan _renderConversion/_calcFactor (solo reciben DOM)
        el.innerHTML = `
            ${fiscalAlert('warning', 'fa-coins',
            'Tipo de cambio SAT.',
            'El tipo de cambio debe ser el publicado en el DOF para la fecha de emisión del CFDI (Art. 20 CFF). ' +
            'Si la moneda es MXN, el TC debe ser "1". Para otras divisas, el TC no puede ser "1".')}

            <div class="row g-3">
                <div class="col-md-4">
                    <label class="form-label-custom">Moneda actual</label>
                    <div style="background:rgba(220,38,38,0.06);border:1.5px solid rgba(220,38,38,0.25);
                        border-radius:8px;padding:.55rem .9rem;font-weight:700;color:var(--danger-color)">
                        ${esc(cfdi.moneda)}
                    </div>
                </div>
                <div class="col-md-4">
                    ${formRow('Nueva moneda *',
                fSelect('newMoneda', catOpts(CAT_MONEDA, cfdi.moneda), 'onchange="MonedaHandler._onMonedaChange()"'),
                'Catálogo c_Moneda SAT (ISO 4217)')}
                </div>
                <div class="col-md-4" id="tcRow">
                    ${formRow('Tipo de cambio *',
                    `<input class="form-control-custom" type="number" id="newTC"
                            value="${cfdi.tipoCambio || 1}" step="0.0001" min="0.0001"
                            onblur="MonedaHandler._validateTC()">`,
                    'Art. 20 CFF. Hasta 4 decimales. Publicado en DOF.')}
                </div>
            </div>

            ${sectionTitle('Reglas Art. 20 CFF')}
            <div class="row g-2" id="monedaRules"></div>

            <div id="mon-conversion" class="mt-3"></div>

            <div id="tcDOFNote" style="display:none" class="mt-2">
                ${fiscalAlert('info', 'fa-newspaper',
                        'Consultar DOF.',
                        `Verifica el TC en: <a href="https://www.banxico.org.mx" target="_blank"
                        style="color:var(--accent-blue)">Banco de México</a> o en el DOF del día de emisión.`)}
            </div>`;

        setTimeout(() => MonedaHandler._onMonedaChange(), 80);
    },

    // ── Métodos internos ──────────────────────────────────────────────────────

    _onMonedaChange: function () {
        const moneda = document.getElementById('newMoneda')?.value;
        const tcRow = document.getElementById('tcRow');
        const dofNote = document.getElementById('tcDOFNote');
        const tcInput = document.getElementById('newTC');
        const cat = CAT_MONEDA.find(m => m.clave === moneda);

        if (cat && !cat.tc) {
            if (tcRow) tcRow.style.display = 'none';
            if (tcInput) { tcInput.value = '1'; tcInput.readOnly = true; }
            if (dofNote) dofNote.style.display = 'none';
        } else {
            if (tcRow) tcRow.style.display = '';
            if (tcInput) tcInput.readOnly = false;
            if (dofNote) dofNote.style.display = 'block';
        }

        this._renderMonedaRules(moneda);
        this._renderConversion();
    },

    // ── Conversión de importes (aparece solo cuando la moneda cambia) ──────────
    _renderConversion: function () {
        const host = document.getElementById('mon-conversion');
        if (!host) return;
        const cfdi = MonedaHandler._cfdi || {};
        const oldMoneda = cfdi.moneda || 'MXN';
        const newMoneda = document.getElementById('newMoneda')?.value || oldMoneda;

        if (newMoneda === oldMoneda) {
            host.innerHTML = fiscalAlert('info', 'fa-equals',
                'Los importes no cambian.',
                `Solo se corrige el tipo de cambio. Los importes en ${esc(oldMoneda)} ` +
                `(subtotal, IVA y total) no se modifican; únicamente cambia su equivalencia en pesos.`);
            return;
        }

        host.innerHTML = `
            ${sectionTitle('Conversión de importes a ' + esc(newMoneda))}
            ${fiscalAlert('warning', 'fa-right-left',
                'Los importes deben reexpresarse en la nueva moneda.',
                'No existe una conversión fiscal automática entre monedas. Indica cómo calcular los importes correctos en ' + esc(newMoneda) + '.')}
            <div class="row g-2" style="margin-bottom:.4rem">
                <div class="col-12">
                    <label style="display:flex;gap:.55rem;align-items:flex-start;cursor:pointer;padding:.55rem .8rem;border:1.5px solid rgba(0,0,0,.12);border-radius:8px">
                        <input type="radio" name="mon-convMode" value="tc" checked onchange="MonedaHandler._onConvModeChange()" style="margin-top:.2rem">
                        <span><strong>Convertir por tipo de cambio</strong><br>
                        <span style="font-size:.77rem;color:var(--text-light)">Preserva el valor en pesos (MXN). Úsalo cuando la moneda se capturó mal pero el importe en pesos era el correcto.</span></span>
                    </label>
                </div>
                <div class="col-12">
                    <label style="display:flex;gap:.55rem;align-items:flex-start;cursor:pointer;padding:.55rem .8rem;border:1.5px solid rgba(0,0,0,.12);border-radius:8px">
                        <input type="radio" name="mon-convMode" value="total" onchange="MonedaHandler._onConvModeChange()" style="margin-top:.2rem">
                        <span><strong>Capturar el total corregido</strong><br>
                        <span style="font-size:.77rem;color:var(--text-light)">Escribe el total correcto en ${esc(newMoneda)}; los importes se ajustan proporcionalmente. Úsalo cuando debe repreciarse (p. ej. USD → EUR).</span></span>
                    </label>
                </div>
            </div>
            <div id="mon-convInputs"></div>
            <div id="mon-convPreview" class="mt-2"></div>`;

        MonedaHandler._onConvModeChange();
    },

    _onConvModeChange: function () {
        const inputs = document.getElementById('mon-convInputs');
        if (!inputs) return;
        const cfdi = MonedaHandler._cfdi || {};
        const newMoneda = document.getElementById('newMoneda')?.value || cfdi.moneda;
        const mode = document.querySelector('input[name="mon-convMode"]:checked')?.value || 'tc';

        if (mode === 'total') {
            inputs.innerHTML = `
                <div class="row g-3">
                    <div class="col-md-6">
                        ${formRow('Total corregido en ' + esc(newMoneda) + ' *',
                            `<input class="form-control-custom" type="number" id="mon-nuevoTotal" step="0.01" min="0.01"
                                oninput="MonedaHandler._updateConvPreview()">`,
                            'Importe total que debió facturarse en ' + esc(newMoneda) + '.')}
                    </div>
                </div>`;
        } else {
            inputs.innerHTML = '';
        }
        MonedaHandler._updateConvPreview();
    },

    _updateConvPreview: function () {
        const host = document.getElementById('mon-convPreview');
        if (!host) return;
        const cfdi = MonedaHandler._cfdi || {};
        const newMoneda = document.getElementById('newMoneda')?.value || cfdi.moneda;
        let newTC = parseFloat(document.getElementById('newTC')?.value) || parseFloat(cfdi.tipoCambio) || 1;
        if (newMoneda === 'MXN') newTC = 1;

        const factor = MonedaHandler._calcFactor(cfdi, newMoneda, newTC);
        const nuevoTotal = (parseFloat(cfdi.total) || 0) * factor;
        const valorMXN = nuevoTotal * (newMoneda === 'MXN' ? 1 : newTC);

        host.innerHTML = `
            <div class="alert-custom alert-info-custom">
                <i class="fas fa-calculator"></i>
                <div style="font-size:.82rem">
                    Factor de conversión: <strong>${factor.toFixed(6)}</strong> &nbsp;·&nbsp;
                    Total original: <strong>${fmtMoney(cfdi.total)} ${esc(cfdi.moneda)}</strong> →
                    Total nuevo: <strong>${fmtMoney(nuevoTotal)} ${esc(newMoneda)}</strong>
                    <br><span style="color:var(--text-light)">Equivalente en pesos ≈ $${fmtMoney(valorMXN)} MXN</span>
                </div>
            </div>`;
    },

    _renderMonedaRules: function (moneda) {
        const el = document.getElementById('monedaRules');
        if (!el) return;
        const esMXN = moneda === 'MXN';
        const rules = [
            { ok: true, label: 'Moneda válida en catálogo c_Moneda SAT' },
            {
                ok: true, label: esMXN
                    ? 'TC = 1 (MXN no requiere tipo de cambio)'
                    : 'TC distinto de 1 requerido para moneda extranjera'
            },
            { ok: true, label: 'Los importes en el CFDI se registran en la moneda indicada' },
        ];
        el.innerHTML = rules.map(r => `
            <div class="col-12">
                <div class="checklist-item checked">
                    <div class="check-icon"><i class="fas fa-check"></i></div>
                    <div style="font-size:.83rem;font-weight:500">${r.label}</div>
                </div>
            </div>`).join('');
    },

    _validateTC: function () {
        const el = document.getElementById('newTC');
        if (!el || el.readOnly) return;
        const v = parseFloat(el.value);
        if (!v || v <= 0)
            markError('newTC', 'El tipo de cambio debe ser mayor a 0.');
        else
            clearError('newTC');

        MonedaHandler._updateConvPreview(); // el TC afecta el factor en modo 'tc'
    },
};
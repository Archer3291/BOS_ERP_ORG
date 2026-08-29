// handlers/complemento-pago.js
// Base legal: Regla 2.7.1.14 y 2.7.1.32 RMF; Complemento Pago 2.0; Guía SAT
// Depende de: utils.js, catalogos.js, state.js

var ComplementoPagoHandler = {

    tipoRelacion: '04',

    tabs: [
        {
            id: 'complemento',
            label: 'Corrección de complemento',
            build: function (el, cfdi) { ComplementoPagoHandler._buildTabComplementoPago(el, cfdi); }
        },
        {
            id: 'parcialidades',
            label: 'Historial de parcialidades',
            build: function (el, cfdi) { ComplementoPagoHandler._buildTabParcialidades(el, cfdi); }
        },
    ],

    buildSummary: function (cfdi) {
        const uuid = document.getElementById('compUUID')?.value || '—';
        const motivo = document.getElementById('compMotivo')?.value || '—';
        const monto = document.getElementById('compMonto')?.value || '—';
        const moneda = document.getElementById('compMoneda')?.value || 'MXN';
        const numParc = document.getElementById('compNumParc')?.value || '1';
        const saldoIns = document.getElementById('compSaldoIns')?.value || '—';

        return `
            <div class="row g-3">
                <div class="col-md-6">
                    <div class="form-label-custom">UUID a cancelar</div>
                    <div style="font-family:'JetBrains Mono',monospace;font-size:.82rem">${esc(uuid)}</div>
                </div>
                <div class="col-md-6">
                    <div class="form-label-custom">Motivo de cancelación</div>
                    <div>${esc(motivo)}</div>
                </div>
                <div class="col-md-4">
                    <div class="form-label-custom">Monto correcto</div>
                    <div style="font-weight:600">$${fmtMoney(parseFloat(monto) || 0)} ${esc(moneda)}</div>
                </div>
                <div class="col-md-4">
                    <div class="form-label-custom">Parcialidad No.</div>
                    <div>${esc(numParc)}</div>
                </div>
                <div class="col-md-4">
                    <div class="form-label-custom">Saldo insoluto resultante</div>
                    <div style="font-weight:600">$${fmtMoney(parseFloat(saldoIns) || 0)}</div>
                </div>
            </div>`;
    },

    execSteps: [
        { text: 'Cancelar complemento incorrecto', sub: 'Motivo 01 o 03 según aplique' },
        { text: 'Esperar confirmación del PAC', sub: 'Verificar en portal SAT' },
        { text: 'Emitir nuevo complemento con datos correctos', sub: 'Fecha, monto y forma de pago' },
        { text: 'Verificar cuadratura del REP', sub: 'SaldoInsoluto = SaldoAnterior − ImportePagado' },
        { text: 'Notificar al receptor', sub: 'Enviar XML y PDF del nuevo complemento' },
    ],

    confirmChecks: [
        { id: 'chkUUIDComp', label: 'UUID del complemento a cancelar verificado en el SAT' },
        { id: 'chkCuadratura', label: 'Cuadratura del REP validada (saldo insoluto ≥ 0)' },
    ],

    // ── Tabs internos ─────────────────────────────────────────────────────────

    // Tab 1 — igual al buildTabComplementoPago original:
    // UUID + motivo + datos del nuevo complemento + parcialidades/saldos + alerta cuadratura
    _buildTabComplementoPago: function (el, cfdi) {
        el.innerHTML = `
            ${fiscalAlert('warning', 'fa-file-invoice-dollar',
            'Corrección de complemento de pago.',
            'El Complemento de Pago (REP) es un CFDI independiente. Para corregirlo: ' +
            '(1) Cancelar el complemento incorrecto (motivo 01 o 03). ' +
            '(2) Emitir nuevo complemento con los datos correctos. ' +
            'Los saldos de la factura original se recalculan automáticamente.')}

            <div class="row g-3">
                <div class="col-md-6">
                    ${formRow('UUID del complemento a cancelar *',
                fInput('compUUID', '',
                    'placeholder="UUID del complemento incorrecto" ' +
                    'style="font-family:JetBrains Mono,monospace" ' +
                    'onblur="ComplementoPagoHandler._validateCompUUID()"'),
                'Se valida formato UUID v4.')}
                </div>
                <div class="col-md-6">
                    ${formRow('Motivo de cancelación del complemento',
                    `<select class="form-control-custom" id="compMotivo">
                            <option value="01">01 · Error — se emitirá nuevo complemento correcto</option>
                            <option value="03">03 · Pago no recibido / operación no realizada</option>
                        </select>`)}
                </div>
            </div>

            ${sectionTitle('Datos del nuevo complemento correcto')}
            <div class="row g-3">
                <div class="col-md-3">
                    ${formRow('Fecha del pago *',
                        `<input class="form-control-custom" type="date" id="compFechaPago"
                            value="${new Date().toISOString().split('T')[0]}">`)}
                </div>
                <div class="col-md-3">
                    ${formRow('Monto pagado correcto *',
                            `<input class="form-control-custom" type="number" id="compMonto"
                            step="0.01" min="0.01" placeholder="0.00"
                            onchange="ComplementoPagoHandler._calcSaldoInsoluto()">`,
                            'En la moneda de pago.')}
                </div>
                <div class="col-md-3">
                    ${formRow('Moneda del pago',
                                fSelect('compMoneda', catOpts(CAT_MONEDA, 'MXN'),
                                    'onchange="ComplementoPagoHandler._calcSaldoInsoluto()"'))}
                </div>
                <div class="col-md-3">
                    ${formRow('Forma de pago *',
                                        fSelect('compFormaPago', catOpts(CAT_FORMA_PAGO, '03')))}
                </div>
            </div>

            ${sectionTitle('Parcialidades y saldos')}
            <div class="row g-3">
                <div class="col-md-3">
                    ${formRow('Número de parcialidad *',
                                            `<input class="form-control-custom" type="number" id="compNumParc"
                            value="1" min="1"
                            onchange="ComplementoPagoHandler._calcSaldoInsoluto()">`,
                                            'Número consecutivo del pago.')}
                </div>
                <div class="col-md-3">
                    ${formRow('Importe saldo anterior *',
                                                `<input class="form-control-custom" type="number" id="compSaldoAnt"
                            value="${cfdi.total}" step="0.01"
                            onchange="ComplementoPagoHandler._calcSaldoInsoluto()">`,
                                                'Saldo previo a este pago.')}
                </div>
                <div class="col-md-3">
                    ${formRow('Importe pagado (este complemento) *',
                                                    `<input class="form-control-custom" type="number" id="compImpPag"
                            step="0.01" placeholder="0.00"
                            onchange="ComplementoPagoHandler._calcSaldoInsoluto()">`,
                                                    'Monto de este pago parcial.')}
                </div>
                <div class="col-md-3">
                    ${formRow('Saldo insoluto resultante',
                                                        `<input class="form-control-custom" type="number" id="compSaldoIns"
                            readonly style="background:var(--bg-gray)">`,
                                                        'Se calcula automáticamente.')}
                </div>
            </div>

            ${fiscalAlert('warning', 'fa-triangle-exclamation',
                                                            'Regla de cuadratura del REP.',
                                                            'SaldoInsoluto = SaldoAnterior − ImportePagado. Si el resultado es negativo, el pago supera el saldo.')}

            <div id="compSaldoAlert" class="mt-2"></div>`;

        setTimeout(() => ComplementoPagoHandler._calcSaldoInsoluto(), 80);
    },

    // Tab 2 — igual al buildTabParcialidades original: historial de complementos del CFDI
    _buildTabParcialidades: function (el, cfdi) {
        el.innerHTML = `
            ${fiscalAlert('info', 'fa-list-ol',
            'Historial de parcialidades.',
            'Cada complemento de pago registra una parcialidad. ' +
            'La suma de todos los pagos no puede superar el total del CFDI.')}

            <div class="table-wrapper mb-3">
                <table class="table-custom">
                    <thead>
                        <tr>
                            <th>Parcialidad</th>
                            <th>Fecha pago</th>
                            <th>Monto pagado</th>
                            <th>Saldo anterior</th>
                            <th>Saldo insoluto</th>
                            <th>Estado</th>
                        </tr>
                    </thead>
                    <tbody>
                        ${cfdi.complementos.length > 0
                ? cfdi.complementos.map((c, i) => {
                    const montoPag = parseFloat(c.monto_pagado || c.monto || 0);
                    const saldoAnt = i === 0 ? cfdi.total : 0; // simplificado (igual que el original)
                    const saldoIns = Math.max(0, saldoAnt - montoPag);
                    return `<tr>
                                    <td>${i + 1}</td>
                                    <td>${fmtFecha(c.fecha_pago || c.fecha)}</td>
                                    <td style="font-family:'JetBrains Mono',monospace;font-weight:600">
                                        $${fmtMoney(montoPag)}</td>
                                    <td style="font-family:'JetBrains Mono',monospace">
                                        $${fmtMoney(saldoAnt)}</td>
                                    <td style="font-family:'JetBrains Mono',monospace">
                                        $${fmtMoney(saldoIns)}</td>
                                    <td><span class="status-badge ${statusClass(c.estatus || 'vigente')}">
                                        ${esc(c.estatus || 'Vigente')}</span></td>
                                </tr>`;
                }).join('')
                : `<tr>
                                <td colspan="6" style="text-align:center;color:var(--text-light);padding:1.5rem">
                                    Sin complementos de pago registrados.
                                </td>
                            </tr>`}
                    </tbody>
                </table>
            </div>

            <div class="kpi-card">
                <div class="kpi-label">Total acumulado pagado</div>
                <div class="kpi-value" style="font-size:1.2rem;color:var(--success-color)">
                    $${fmtMoney(cfdi.complementos.reduce((a, c) => a + parseFloat(c.monto_pagado || c.monto || 0), 0))}
                </div>
                <div class="kpi-sub">de $${fmtMoney(cfdi.total)} totales</div>
            </div>`;
    },

    // ── Métodos internos ──────────────────────────────────────────────────────

    _validateCompUUID: function () {
        const el = document.getElementById('compUUID');
        if (!el) return;
        const v = el.value.trim();
        if (v && !validarUUID(v))
            markError('compUUID', 'Formato UUID inválido. Debe ser UUID v4.');
        else
            clearError('compUUID');
    },

    _calcSaldoInsoluto: function () {
        const ant = parseFloat(document.getElementById('compSaldoAnt')?.value || 0) || 0;
        const pag = parseFloat(document.getElementById('compImpPag')?.value || 0) || 0;
        const ins = ant - pag;

        const insEl = document.getElementById('compSaldoIns');
        if (insEl) insEl.value = ins.toFixed(2);

        const alertEl = document.getElementById('compSaldoAlert');
        if (!alertEl) return;

        if (ins < 0) {
            alertEl.innerHTML = fiscalAlert('danger', 'fa-xmark',
                'Saldo negativo.',
                `El pago ($${fmtMoney(pag)}) supera el saldo anterior ($${fmtMoney(ant)}). Verifica los montos.`);
        } else if (ins === 0) {
            alertEl.innerHTML = fiscalAlert('success', 'fa-circle-check',
                'Factura saldada.',
                `El saldo queda en $0.00. La factura quedará completamente pagada.`);
        } else {
            alertEl.innerHTML = '';
        }
    },
};
// handlers/parcialidades.js
// Base legal: Regla 2.7.1.14 RMF; Complemento Pago 2.0; Guía SAT
// Depende de: utils.js, catalogos.js, state.js

var ParcialidadesHandler = {

    tipoRelacion: '04',

    tabs: [
        {
            id: 'parcialidades',
            label: 'Historial de parcialidades',
            build: function (el, cfdi) { ParcialidadesHandler._buildTabParcialidades(el, cfdi); }
        },
    ],

    buildSummary: function (cfdi) {
        const totalPagado = cfdi.complementos.reduce(
            (a, c) => a + parseFloat(c.monto_pagado || c.monto || 0), 0
        );
        const saldoPendiente = cfdi.total - totalPagado;

        return `
            <div class="row g-3">
                <div class="col-md-4">
                    <div class="form-label-custom">Parcialidades registradas</div>
                    <div style="font-weight:600">${cfdi.complementos.length}</div>
                </div>
                <div class="col-md-4">
                    <div class="form-label-custom">Total acumulado pagado</div>
                    <div style="font-weight:700;color:var(--success-color)">$${fmtMoney(totalPagado)}</div>
                </div>
                <div class="col-md-4">
                    <div class="form-label-custom">Saldo pendiente</div>
                    <div style="font-weight:700;color:${saldoPendiente > 0 ? 'var(--warning-color)' : 'var(--success-color)'}">
                        $${fmtMoney(saldoPendiente)}
                    </div>
                </div>
            </div>`;
    },

    execSteps: [
        { text: 'Revisar historial de parcialidades', sub: 'Verificar saldos y fechas de cada pago' },
        { text: 'Identificar complementos con error', sub: 'UUID y parcialidad específica' },
        { text: 'Cancelar complementos incorrectos', sub: 'En orden inverso a su emisión' },
        { text: 'Emitir complementos corregidos', sub: 'Con saldos recalculados' },
        { text: 'Verificar cuadratura final', sub: 'SaldoInsoluto debe coincidir con saldo real' },
    ],

    confirmChecks: [
        { id: 'chkOrdenInverso', label: 'Complementos cancelados en orden inverso (más reciente primero)' },
        { id: 'chkCuadratura', label: 'Saldos verificados: SaldoInsoluto = SaldoAnterior − ImportePagado' },
    ],

    // ── Tab interno ───────────────────────────────────────────────────────────

    _buildTabParcialidades: function (el, cfdi) {
        // Calcular saldos acumulados correctamente
        const rows = cfdi.complementos.map((c, i) => {
            const montoPag = parseFloat(c.monto_pagado || c.monto || 0);
            const saldoAnt = i === 0
                ? cfdi.total
                : cfdi.total - cfdi.complementos
                    .slice(0, i)
                    .reduce((a, x) => a + parseFloat(x.monto_pagado || x.monto || 0), 0);
            const saldoIns = Math.max(0, saldoAnt - montoPag);
            return { c, i, montoPag, saldoAnt, saldoIns };
        });

        const totalPagado = cfdi.complementos.reduce(
            (a, c) => a + parseFloat(c.monto_pagado || c.monto || 0), 0
        );

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
                        ${rows.length > 0 ? rows.map(({ c, i, montoPag, saldoAnt, saldoIns }) => `
                            <tr>
                                <td>${i + 1}</td>
                                <td>${fmtFecha(c.fecha_pago || c.fecha)}</td>
                                <td style="font-family:'JetBrains Mono',monospace;font-weight:600">
                                    $${fmtMoney(montoPag)}</td>
                                <td style="font-family:'JetBrains Mono',monospace">
                                    $${fmtMoney(saldoAnt)}</td>
                                <td style="font-family:'JetBrains Mono',monospace">
                                    $${fmtMoney(saldoIns)}</td>
                                <td>
                                    <span class="status-badge ${statusClass(c.estatus || 'vigente')}">
                                        ${esc(c.estatus || 'Vigente')}
                                    </span>
                                </td>
                            </tr>`).join('') :
                `<tr>
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
                    $${fmtMoney(totalPagado)}
                </div>
                <div class="kpi-sub">de $${fmtMoney(cfdi.total)} totales</div>
            </div>`;
    },
};
// handlers/factura-pagada.js
// Base legal: CFF Art. 29-A; SAT — Cancelación CFDI cobrado; Regla 2.7.1.26
// Depende de: utils.js, catalogos.js, state.js

var FacturaPagadaHandler = {

    tipoRelacion: '01',

    tabs: [
        {
            id: 'pagada',
            label: 'Factura cobrada',
            build: function (el, cfdi) { FacturaPagadaHandler._buildTabPagada(el, cfdi); }
        },
        {
            id: 'rev-contable',
            label: 'Reversión contable',
            build: function (el, cfdi) { FacturaPagadaHandler._buildTabRevContable(el, cfdi); }
        },
    ],

    buildSummary: function (cfdi) {
        const pagFecha = document.getElementById('pagFecha')?.value || '—';
        const pagRef = document.getElementById('pagRef')?.value || '—';
        const pagForma = document.getElementById('pagForma')?.value || '—';
        const revPoliza = document.getElementById('revPoliza')?.value || '—';
        const revFecha = document.getElementById('revFecha')?.value || '—';

        const checks = ['chkConc', 'chkCob', 'chkCont', 'chkGer']
            .map(id => document.getElementById(id)?.checked ? '✔' : '✘');

        return `
            <div class="row g-3">
                <div class="col-md-4">
                    <div class="form-label-custom">Monto cobrado</div>
                    <div style="font-weight:700">$${fmtMoney(cfdi.total)}</div>
                </div>
                <div class="col-md-4">
                    <div class="form-label-custom">Fecha de cobro</div>
                    <div>${esc(pagFecha)}</div>
                </div>
                <div class="col-md-4">
                    <div class="form-label-custom">Referencia bancaria</div>
                    <div style="font-family:'JetBrains Mono',monospace;font-size:.82rem">${esc(pagRef)}</div>
                </div>
                <div class="col-md-4">
                    <div class="form-label-custom">Forma de cobro</div>
                    <div>${esc(pagForma)}</div>
                </div>
                <div class="col-md-4">
                    <div class="form-label-custom">Póliza de reversión</div>
                    <div>${esc(revPoliza)} · ${esc(revFecha)}</div>
                </div>
                <div class="col-md-4">
                    <div class="form-label-custom">Verificaciones previas</div>
                    <div style="font-size:.82rem">
                        Conc. ${checks[0]} · Cobr. ${checks[1]} · Cont. ${checks[2]} · Ger. ${checks[3]}
                    </div>
                </div>
            </div>`;
    },

    execSteps: [
        { text: 'Revertir conciliación bancaria', sub: 'Desaplicar el cobro en el sistema contable' },
        { text: 'Cancelar Complemento de Pago (si existe)', sub: 'REP debe cancelarse antes que la factura' },
        { text: 'Cancelar CFDI original (motivo 01)', sub: 'El receptor tiene 72 hrs para rechazar' },
        { text: 'Emitir nuevo CFDI corregido', sub: 'Con relación 01 al UUID original' },
        { text: 'Registrar póliza de reversión contable', sub: 'Débito Ingresos / Crédito CxC o Banco' },
        { text: 'Notificar a cobranza y contabilidad', sub: 'Confirmar re-aplicación del pago' },
    ],

    confirmChecks: [
        { id: 'chkConc', label: 'Conciliación bancaria revertida' },
        { id: 'chkCob', label: 'Área de cobranza notificada' },
        { id: 'chkCont', label: 'Área contable revisada y autorizada' },
        { id: 'chkGer', label: 'Autorización de gerencia obtenida' },
    ],

    // ── Tabs internos ─────────────────────────────────────────────────────────

    _buildTabPagada: function (el, cfdi) {
        el.innerHTML = `
            ${fiscalAlert('danger', 'fa-money-check-dollar',
            'Factura cobrada — Proceso crítico.',
            'Cancelar un CFDI por el cual ya se recibió pago implica: ' +
            '(1) El pago sigue siendo real e imputable. ' +
            '(2) Debes revertir los asientos contables de cobro. ' +
            '(3) Cancelar el Complemento de Pago (si existe). ' +
            '(4) El receptor tiene derecho a rechazar la cancelación hasta 72 horas.')}

            ${sectionTitle('Verificación previa obligatoria')}
            <div class="row g-2 mb-3">
                ${[
                { label: 'Conciliación bancaria revertida', id: 'chkConc' },
                { label: 'Área de cobranza notificada', id: 'chkCob' },
                { label: 'Área contable revisada y autorizada', id: 'chkCont' },
                { label: 'Autorización de gerencia obtenida', id: 'chkGer' },
            ].map(c => `
                    <div class="col-12">
                        <div class="checklist-item" style="cursor:pointer"
                            onclick="FacturaPagadaHandler._toggleCheck(this,'${c.id}')">
                            <input type="checkbox" id="${c.id}" style="accent-color:var(--accent-blue)">
                            <label for="${c.id}"
                                style="font-size:.85rem;cursor:pointer;margin:0">${c.label}</label>
                        </div>
                    </div>`).join('')}
            </div>

            ${sectionTitle('Información del pago recibido')}
            <div class="row g-3">
                <div class="col-md-3">
                    ${formRow('Monto pagado',
                `<input class="form-control-custom" type="number" id="pagMonto"
                            value="${cfdi.total}" step="0.01" readonly
                            style="background:var(--bg-gray)">`)}
                </div>
                <div class="col-md-3">
                    ${formRow('Fecha de cobro',
                    `<input class="form-control-custom" type="date" id="pagFecha">`)}
                </div>
                <div class="col-md-3">
                    ${formRow('Referencia bancaria',
                        fInput('pagRef', '', 'placeholder="No. transacción o cheque"'))}
                </div>
                <div class="col-md-3">
                    ${formRow('Forma en que se cobró',
                            fSelect('pagForma', catOpts(CAT_FORMA_PAGO, '03')))}
                </div>
            </div>`;
    },

    _buildTabRevContable: function (el, cfdi) {
        el.innerHTML = `
            ${fiscalAlert('info', 'fa-book-open',
            'Reversión contable requerida.',
            'La cancelación de un CFDI cobrado implica asientos de reversión: ' +
            'Se debita la cuenta de Ingresos y se acredita Cuentas por Cobrar (o Banco si ya se aplicó). ' +
            'Consulta con el contador.')}

            <div class="row g-3">
                <div class="col-md-4">
                    ${formRow('Póliza de reversión No.',
                fInput('revPoliza', '', 'placeholder="P-2024-0001"'))}
                </div>
                <div class="col-md-4">
                    ${formRow('Fecha de la póliza',
                    `<input class="form-control-custom" type="date" id="revFecha"
                            value="${new Date().toISOString().split('T')[0]}">`)}
                </div>
                <div class="col-md-4">
                    ${formRow('Concepto contable',
                        fInput('revConcepto', 'Cancelación CFDI por refacturación'))}
                </div>
                <div class="col-12">
                    ${formRow('Observaciones del contador',
                            `<textarea class="form-control-custom" id="revObs" rows="2"
                            placeholder="Notas sobre el impacto contable y fiscal..."></textarea>`)}
                </div>
            </div>`;
    },

    // ── Métodos internos ──────────────────────────────────────────────────────

    _toggleCheck: function (rowEl, id) {
        const chk = document.getElementById(id);
        if (chk) {
            chk.checked = !chk.checked;
            rowEl.classList.toggle('checked', chk.checked);
        }
    },
};
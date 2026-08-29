// handlers/forma-pago.js
// Depende de: utils.js, catalogos.js, state.js
// Base legal: Art. 29-A fracc VII-b CFF; Catálogo c_FormaPago

var FormaPagoHandler = {

    tipoRelacion: '04',

    tabs: [
        {
            id: 'formaPago',
            label: 'Forma de pago',
            build: function (el, cfdi) { FormaPagoHandler._buildTabFormaPago(el, cfdi); }
        },
    ],

    buildSummary: function (cfdi) {
        const nueva = document.getElementById('newFormaPago')?.value || '—';
        const metodo = cfdi.metodoPago || '—';
        return `
            <div class="row g-3">
                <div class="col-md-4">
                    <div class="form-label-custom">Forma de pago anterior</div>
                    <div style="font-weight:600;color:var(--danger-color)">${esc(cfdi.formaPago || '—')}</div>
                </div>
                <div class="col-md-4">
                    <div class="form-label-custom">Forma de pago nueva</div>
                    <div style="font-weight:700;color:var(--success-color)">${esc(nueva)}</div>
                </div>
                <div class="col-md-4">
                    <div class="form-label-custom">Método de pago</div>
                    <div style="font-weight:600">${esc(metodo)}</div>
                </div>
            </div>`;
    },

    execSteps: [
        { text: 'Verificar medio de pago real recibido', sub: 'La forma debe reflejar la realidad' },
        { text: 'Cancelar CFDI original (motivo 01)', sub: 'Envío al PAC certificado' },
        { text: 'Esperar aceptación del receptor', sub: 'Hasta 72 hrs hábiles' },
        { text: 'Emitir nuevo CFDI con forma de pago correcta', sub: 'Generación y sellado digital' },
        { text: 'Relacionar con UUID original (rel. 04)', sub: 'Nodo CfdiRelacionados' },
        { text: 'Notificar al receptor', sub: 'XML y PDF por correo' },
    ],

    confirmChecks: [
        { id: 'chkFPCompatible', label: 'Forma de pago compatible con el método (PPD → "99", PUE → específica)' },
        { id: 'chkFPReal', label: 'La forma de pago refleja el medio real de cobro' },
    ],

    // ── Tab interno ───────────────────────────────────────────────────────────

    _buildTabFormaPago: function (el, cfdi) {
        el.innerHTML = `
            ${fiscalAlert('info', 'fa-wallet',
            'Forma de pago.',
            'Debe reflejar el medio real mediante el cual se realizó o realizará el pago. ' +
            '"99 Por definir" solo es válido cuando el método de pago es PPD. ' +
            'El cambio de forma de pago no modifica obligaciones fiscales de retención.')}

            <div class="row g-3">
                <div class="col-md-5">
                    <label class="form-label-custom">Forma de pago actual</label>
                    <div style="background:rgba(220,38,38,0.06);border:1.5px solid rgba(220,38,38,0.25);
                        border-radius:8px;padding:.55rem .9rem;font-weight:700;color:var(--danger-color)">
                        ${esc(cfdi.formaPago || '—')}
                    </div>
                </div>
                <div class="col-md-2"
                    style="display:flex;align-items:flex-end;justify-content:center;padding-bottom:.6rem">
                    <i class="fas fa-arrow-right" style="font-size:1.1rem;color:var(--text-light)"></i>
                </div>
                <div class="col-md-5">
                    ${formRow('Nueva forma de pago *',
                fSelect('newFormaPago', catOpts(CAT_FORMA_PAGO, '03'),
                    'onchange="FormaPagoHandler._validateFormaPago()"'),
                'Catálogo c_FormaPago SAT')}
                </div>
            </div>

            <div id="formaPagoAlert" class="mt-3"></div>

            ${sectionTitle('Compatibilidad con método de pago')}
            <div id="formaPagoCompat" class="row g-2"></div>`;

        setTimeout(() => FormaPagoHandler._validateFormaPago(), 80);
    },

    // ── Métodos internos ──────────────────────────────────────────────────────

    _validateFormaPago: function () {
        const fp = document.getElementById('newFormaPago')?.value;
        const mp = AppState.selectedCFDI?.metodoPago || 'PUE';
        const alertEl = document.getElementById('formaPagoAlert');
        const compatEl = document.getElementById('formaPagoCompat');
        if (!alertEl) return;

        const isPPD = mp === 'PPD';
        const is99 = fp === '99';

        if (isPPD && !is99) {
            alertEl.innerHTML = fiscalAlert('warning', 'fa-triangle-exclamation',
                'Método PPD requiere "99 Por definir".',
                'SAT rechaza CFDIs con método PPD y forma de pago diferente a "99".');
        } else if (!isPPD && is99) {
            alertEl.innerHTML = fiscalAlert('danger', 'fa-xmark-circle',
                'Forma de pago "99" solo válida para PPD.',
                'CFDIs con método PUE deben tener una forma de pago específica.');
        } else {
            alertEl.innerHTML = fiscalAlert('success', 'fa-circle-check',
                'Combinación válida.',
                'La forma de pago es compatible con el método de pago.');
        }

        if (compatEl) {
            const ok = (isPPD && is99) || (!isPPD && !is99);
            compatEl.innerHTML = `
                <div class="col-12">
                    <div class="checklist-item ${ok ? 'checked' : 'error'}">
                        <div class="check-icon">
                            <i class="fas ${ok ? 'fa-check' : 'fa-xmark'}"></i>
                        </div>
                        <div style="font-size:.83rem;font-weight:500">
                            Forma de pago ${esc(fp)} compatible con método ${esc(mp)}
                        </div>
                    </div>
                </div>`;
        }
    },
};
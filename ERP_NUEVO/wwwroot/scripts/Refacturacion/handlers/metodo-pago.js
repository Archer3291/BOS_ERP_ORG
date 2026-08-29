// handlers/metodo-pago.js
// Depende de: utils.js, catalogos.js, state.js

var MetodoPagoHandler = {

    tipoRelacion: '04',

    // Recolecta los cambios para el back (MetodoPagoTypeHandler lee metodoPago, formaPago).
    collectChanges: function (cfdi) {
        const metodo = document.getElementById('mp-newMetodoPago')?.value || cfdi.metodoPago || 'PUE';
        let forma = document.getElementById('mp-newFormaPago')?.value || cfdi.formaPago || '';
        // PPD obliga forma '99'; para PUE nunca mandamos '99'.
        if (metodo === 'PPD') forma = '99';
        return { metodoPago: metodo, formaPago: forma };
    },

    tabs: [
        {
            id: 'metodo',
            label: 'Método de pago',
            build: function (el, cfdi) { MetodoPagoHandler._buildTabMetodo(el, cfdi); }
        },
        {
            id: 'complementos',
            label: 'Complementos de pago',
            build: function (el, cfdi) { MetodoPagoHandler._buildTabComplementos(el, cfdi); }
        },
    ],

    buildSummary: function (cfdi) {
        const nuevo = document.getElementById('mp-newMetodoPago')?.value || cfdi.metodoPago;
        const fp = document.getElementById('mp-newFormaPago')?.value || cfdi.formaPago || '—';
        return `
            <div class="row g-3">
                <div class="col-md-6">
                    <div class="form-label-custom">Método anterior</div>
                    <div style="font-weight:600">${esc(cfdi.metodoPago)}</div>
                </div>
                <div class="col-md-6">
                    <div class="form-label-custom">Método nuevo</div>
                    <div style="font-weight:700;color:var(--success-color)">${esc(nuevo)}</div>
                </div>
                <div class="col-md-6">
                    <div class="form-label-custom">Forma de pago nueva</div>
                    <div>${esc(fp)}</div>
                </div>
                <div class="col-md-6">
                    <div class="form-label-custom">Complementos activos</div>
                    <div>${cfdi.tieneComplementos ? `<span style="color:var(--danger-color)">${(cfdi.complementos || []).length} (requieren cancelación)</span>` : 'Ninguno'}</div>
                </div>
            </div>`;
    },

    execSteps: [
        { text: 'Cancelar complementos de pago (si existen)', sub: 'En orden inverso a su emisión' },
        { text: 'Cancelar CFDI original (motivo 01)', sub: 'Envío al PAC certificado' },
        { text: 'Esperar aceptación del receptor', sub: 'Hasta 72 hrs hábiles' },
        { text: 'Emitir nuevo CFDI con método correcto', sub: 'Generación y sellado digital' },
        { text: 'Relacionar con UUID original (rel. 04)', sub: 'Nodo CfdiRelacionados' },
        { text: 'Emitir complementos de pago nuevos (si aplica)', sub: 'Para método PPD' },
    ],

    confirmChecks: [
        { id: 'chkComplementos', label: 'Todos los complementos de pago anteriores cancelados' },
        { id: 'chkFormaPago99', label: 'Forma de pago "99" solo si el nuevo método es PPD' },
    ],

    // ── Tabs internos ─────────────────────────────────────────────────────────

    _buildTabMetodo: function (el, cfdi) {
        const esPPD = cfdi.metodoPago === 'PPD';
        el.innerHTML = `
            ${fiscalAlert(esPPD ? 'danger' : 'warning', 'fa-credit-card',
            esPPD ? 'CFDI PPD con complementos de pago.' : 'Cambio de método de pago.',
            esPPD
                ? 'Este CFDI es PPD. Para cambiarlo a PUE debes cancelar primero todos los complementos de pago relacionados.'
                : 'PUE = pago en una sola exhibición. PPD = se liquidará en parcialidades o en fecha posterior.')}

            <div class="row g-3">
                <div class="col-md-5">
                    <label class="form-label-custom">Método actual</label>
                    <div style="background:rgba(220,38,38,0.06);border:1.5px solid rgba(220,38,38,0.25);
                        border-radius:8px;padding:.55rem .9rem;font-weight:700;color:var(--danger-color)">
                        ${esc(cfdi.metodoPago)} — ${cfdi.metodoPago === 'PPD' ? 'Pago en parcialidades o diferido' : 'Pago en una sola exhibición'}
                    </div>
                </div>
                <div class="col-md-2" style="display:flex;align-items:flex-end;justify-content:center;padding-bottom:.6rem">
                    <i class="fas fa-arrow-right" style="font-size:1.1rem;color:var(--text-light)"></i>
                </div>
                <div class="col-md-5">
                    ${formRow('Nuevo método de pago *',
                    `<select class="form-control-custom" id="mp-newMetodoPago"
                            onchange="MetodoPagoHandler._onMetodoChange()">
                            <option value="PUE" ${cfdi.metodoPago !== 'PUE' ? 'selected' : ''}>PUE · Pago en una sola exhibición</option>
                            <option value="PPD" ${cfdi.metodoPago !== 'PPD' ? 'selected' : ''}>PPD · Pago en parcialidades o diferido</option>
                        </select>`)}
                </div>
            </div>

            <div id="mp-detalles" class="mt-3"></div>

            ${sectionTitle('Reglas de compatibilidad SAT')}
            <div class="row g-2" id="mp-rules">
                ${MetodoPagoHandler._buildRules(cfdi)}
            </div>`;

        setTimeout(() => MetodoPagoHandler._onMetodoChange(), 80);
    },

    _buildTabComplementos: function (el, cfdi) {
        if (!cfdi.tieneComplementos) {
            el.innerHTML = fiscalAlert('success', 'fa-circle-check',
                'Sin complementos de pago.',
                'Este CFDI no tiene complementos de pago activos. Puedes proceder con el cambio de método.');
            return;
        }

        el.innerHTML = `
            ${fiscalAlert('danger', 'fa-triangle-exclamation',
            'Complementos de pago vigentes.',
            'Deben cancelarse en orden inverso a su emisión (el más reciente primero). ' +
            'El saldo insoluto debe quedar en $0.00 antes de cancelar la factura.')}

            <div class="table-wrapper mb-3">
                <table class="table-custom">
                    <thead>
                        <tr><th>#</th><th>UUID complemento</th><th>Fecha pago</th>
                            <th>Monto pagado</th><th>Parcialidad</th><th>Estado</th><th>Acción</th></tr>
                    </thead>
                    <tbody>
                        ${(cfdi.complementos || []).map((c, i) => `
                            <tr>
                                <td style="font-size:.78rem;color:var(--text-light)">${i + 1}</td>
                                <td><code style="font-size:.7rem">${esc(c.uuid || c.folio_fiscal || '—')}</code></td>
                                <td>${fmtFecha(c.fecha_pago || c.fecha)}</td>
                                <td style="font-family:'JetBrains Mono',monospace;font-weight:600">
                                    $${fmtMoney(c.monto_pagado || c.monto || 0)}</td>
                                <td>${c.num_parcialidad || '1'}</td>
                                <td><span class="status-badge ${statusClass(c.estatus || 'vigente')}">${esc(c.estatus || 'Vigente')}</span></td>
                                <td>
                                    <button class="btn-danger-custom btn-sm-custom"
                                        onclick="MetodoPagoHandler._cancelarComplemento('${esc(c.uuid || '')}', ${i})">
                                        <i class="fas fa-ban"></i> Solicitar cancelación
                                    </button>
                                </td>
                            </tr>`).join('')}
                    </tbody>
                </table>
            </div>

            ${sectionTitle('Motivo de cancelación para complementos')}
            <div class="row g-3">
                <div class="col-md-6">
                    ${formRow('Motivo SAT',
                `<select class="form-control-custom" id="mp-motivoComp">
                            <option value="01">01 · Error con sustitución (se emitirá nuevo complemento)</option>
                            <option value="03">03 · No se llevó a cabo la operación</option>
                        </select>`,
                'Motivo 02 no es válido para complementos de pago.')}
                </div>
            </div>
            <div id="mp-compCancelLog" class="mt-3"></div>`;
    },

    // ── Métodos internos ──────────────────────────────────────────────────────

    _buildRules: function (cfdi) {
        const esPPD = cfdi.metodoPago === 'PPD';
        const rules = [
            { ok: !esPPD || !cfdi.tieneComplementos, label: 'Sin complementos de pago activos', msg: 'Cancela los complementos antes de cambiar el método.' },
            { ok: true, label: 'Forma de pago compatible (ver detalles abajo)', msg: '' },
            { ok: !(cfdi.metodoPago === 'PUE' && cfdi.formaPago === '99'), label: 'Forma de pago definida (no "99 Por definir") para PUE', msg: 'PUE requiere forma de pago específica.' },
        ];
        return rules.map(r => `
            <div class="col-12">
                <div class="checklist-item ${r.ok ? 'checked' : 'error'}">
                    <div class="check-icon"><i class="fas ${r.ok ? 'fa-check' : 'fa-xmark'}"></i></div>
                    <div>
                        <div style="font-size:.83rem;font-weight:500">${r.label}</div>
                        ${!r.ok ? `<div style="font-size:.73rem;color:var(--danger-color)">${r.msg}</div>` : ''}
                    </div>
                </div>
            </div>`).join('');
    },

    _onMetodoChange: function () {
        const nuevo = document.getElementById('mp-newMetodoPago')?.value;
        const el = document.getElementById('mp-detalles');
        if (!el) return;

        if (nuevo === 'PPD') {
            el.innerHTML = `
                ${fiscalAlert('info', 'fa-calendar-check',
                'Nuevo CFDI PPD.',
                'Deberás emitir Complementos de Pago (REP) cada vez que recibas un pago parcial o total. ' +
                'La forma de pago debe ser "99 Por definir".')}
                <div class="row g-3">
                    <div class="col-md-4">
                        ${formRow('Forma de pago para nuevo CFDI PPD',
                    `<select class="form-control-custom" id="mp-newFormaPago">
                                <option value="99" selected>99 · Por definir (obligatorio para PPD)</option>
                            </select>`,
                    'SAT obliga "99" cuando el método es PPD.')}
                    </div>
                    <div class="col-md-4">
                        ${formRow('Número de parcialidad inicial',
                        `<input class="form-control-custom" type="number" id="mp-parcInicial" value="1" min="1">`,
                        'En el primer complemento de pago.')}
                    </div>
                    <div class="col-md-4">
                        ${formRow('Fecha estimada de pago',
                            `<input class="form-control-custom" type="date" id="mp-fechaEstimada">`,
                            'Referencia interna. No va en el CFDI.')}
                    </div>
                </div>`;
        } else {
            el.innerHTML = `
                ${fiscalAlert('info', 'fa-check-circle',
                'Nuevo CFDI PUE.',
                'La forma de pago debe ser específica (no "99"). El pago se considera recibido en la fecha de emisión.')}
                <div class="row g-3">
                    <div class="col-md-6">
                        ${formRow('Forma de pago *',
                    fSelect('mp-newFormaPago', catOpts(CAT_FORMA_PAGO, '03')),
                    'Debe corresponder al medio de pago real recibido.')}
                    </div>
                    <div class="col-md-6">
                        ${formRow('Fecha de pago',
                        `<input class="form-control-custom" type="date" id="mp-fechaPago">`,
                        'Referencia contable. Generalmente igual a la fecha de emisión.')}
                    </div>
                </div>`;
        }
    },

    _cancelarComplemento: function (uuid, idx) {
        const log = document.getElementById('mp-compCancelLog');
        if (log) {
            log.innerHTML += `
                <div class="alert-custom alert-warning-custom mb-2">
                    <i class="fas fa-spinner fa-spin"></i>
                    <div>Solicitando cancelación del complemento ${idx + 1} — UUID: ${esc(uuid || '—')}</div>
                </div>`;
        }
        showToast('warning', `Cancelación solicitada para complemento ${idx + 1}.`);
    },
};
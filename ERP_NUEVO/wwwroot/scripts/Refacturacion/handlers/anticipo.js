// handlers/anticipo.js
// Base legal: Regla 2.7.1.26.1 RMF; Guía de llenado Anticipo SAT 2024
// Depende de: utils.js, catalogos.js, state.js

var AnticipoHandler = {

    tipoRelacion: '07',

    tabs: [
        {
            id: 'cadena',
            label: 'Cadena de anticipos',
            build: function (el, cfdi) { AnticipoHandler._buildTabAnticipo(el, cfdi); }
        },
        {
            id: 'aplicacion',
            label: 'CFDI Egreso (aplicación)',
            build: function (el, cfdi) { AnticipoHandler._buildTabAnticipoAplicacion(el, cfdi); }
        },
    ],

    buildSummary: function (cfdi) {
        const monto = document.getElementById('antMonto')?.value || '—';
        const rfc = document.getElementById('antRFC')?.value || cfdi.rfc;
        const fecha = document.getElementById('antFecha')?.value || '—';
        const usoCFDI = document.getElementById('antUsoCFDI')?.value || '—';
        const formaPago = document.getElementById('antFormaPago')?.value || '—';
        const saldo = parseFloat(cfdi.total) - parseFloat(monto) || 0;

        return `
            <div class="row g-3">
                <div class="col-md-4">
                    <div class="form-label-custom">Monto anticipo nuevo</div>
                    <div style="font-weight:700">$${fmtMoney(parseFloat(monto) || 0)}</div>
                </div>
                <div class="col-md-4">
                    <div class="form-label-custom">Saldo a cubrir</div>
                    <div style="font-weight:600;color:var(--warning-color)">$${fmtMoney(saldo)}</div>
                </div>
                <div class="col-md-4">
                    <div class="form-label-custom">Total operación</div>
                    <div>$${fmtMoney(cfdi.total)}</div>
                </div>
                <div class="col-md-4">
                    <div class="form-label-custom">RFC receptor</div>
                    <div style="font-family:'JetBrains Mono',monospace">${esc(rfc)}</div>
                </div>
                <div class="col-md-4">
                    <div class="form-label-custom">Fecha anticipo</div>
                    <div>${esc(fecha)}</div>
                </div>
                <div class="col-md-4">
                    <div class="form-label-custom">Uso CFDI / Forma pago</div>
                    <div>${esc(usoCFDI)} · ${esc(formaPago)}</div>
                </div>
            </div>`;
    },

    execSteps: [
        { text: 'Cancelar CFDI de ingreso final', sub: '(1) Primero el más dependiente' },
        { text: 'Cancelar CFDI de egreso (aplicación)', sub: '(2) Después el egreso' },
        { text: 'Cancelar CFDI de anticipo original', sub: '(3) Por último el anticipo' },
        { text: 'Emitir nuevo anticipo corregido', sub: 'Con monto y datos validados' },
        { text: 'Emitir nuevo CFDI Egreso (rel. 07)', sub: 'UUID del nuevo anticipo' },
        { text: 'Emitir nueva factura final (rel. 07)', sub: 'UUID del nuevo egreso' },
    ],

    confirmChecks: [
        { id: 'chkOrdenInv', label: 'Cancelación en orden inverso: ingreso → egreso → anticipo' },
        { id: 'chkMontoMenor', label: 'Monto del anticipo es menor al total de la factura final' },
        { id: 'chkRel07Egreso', label: 'CFDI Egreso relacionado con UUID del anticipo (rel. 07)' },
        { id: 'chkRel07Final', label: 'Factura final relacionada con UUID del egreso (rel. 07)' },
    ],

    // ── Tabs internos ─────────────────────────────────────────────────────────

    _buildTabAnticipo: function (el, cfdi) {
        el.innerHTML = `
            ${fiscalAlert('danger', 'fa-radiation',
            'Cadena de anticipos — Riesgo fiscal muy alto.',
            'El SAT establece que la cadena anticipo → egreso → factura final debe cancelarse en ORDEN INVERSO. ' +
            '(1) Cancelar CFDI de ingreso final. (2) Cancelar CFDI de egreso. (3) Cancelar CFDI de anticipo. ' +
            '(4) Emitir nuevo anticipo. (5) Nuevo egreso. (6) Nueva factura final. ' +
            'NUNCA cancelar el anticipo sin cancelar primero los CFDI que dependen de él.')}

            ${sectionTitle('Cadena identificada')}
            <div class="anticipo-chain mb-3">
                ${cfdi.anticipos.length > 0
                ? cfdi.anticipos.map((a, i) => `
                        <div class="chain-node">
                            <div style="font-size:.7rem;color:var(--text-light);font-weight:600">
                                ANTICIPO ${i + 1}
                            </div>
                            <div>${esc(a.anticipo_serie || 'S/N')}-${esc(String(a.anticipo_folio || ''))}</div>
                            <div style="font-family:'JetBrains Mono',monospace;font-size:.72rem;margin-top:.2rem">
                                $${fmtMoney(a.monto_aplicado || 0)}
                            </div>
                        </div>
                        <div class="chain-arrow"><i class="fas fa-arrow-right"></i></div>`).join('')
                : '<div style="font-size:.84rem;color:var(--text-light)">No se identificaron anticipos relacionados.</div>'
            }
                <div class="chain-node ${cfdi.anticipos.length > 0 ? '' : 'new-node'}">
                    <div style="font-size:.7rem;font-weight:600">EGRESO</div>
                    <div>Aplicación</div>
                </div>
                <div class="chain-arrow"><i class="fas fa-arrow-right"></i></div>
                <div class="chain-node">
                    <div style="font-size:.7rem;font-weight:600">INGRESO FINAL</div>
                    <div>${esc(cfdi.tipo)}-${esc(cfdi.folio)}</div>
                </div>
            </div>

            ${sectionTitle('Datos del anticipo correcto')}
            <div class="row g-3">
                <div class="col-md-4">
                    ${formRow('Monto anticipo correcto *',
                `<input class="form-control-custom" type="number" id="antMonto"
                            step="0.01" min="0.01" placeholder="0.00"
                            onchange="AnticipoHandler._validateAntMonto()">`,
                'Debe ser menor al total de la factura final.')}
                </div>
                <div class="col-md-4">
                    ${formRow('RFC receptor correcto *',
                    fInput('antRFC', cfdi.rfc,
                        'style="text-transform:uppercase" ' +
                        'oninput="this.value=this.value.toUpperCase()"'),
                    'Debe corresponder al mismo receptor.')}
                </div>
                <div class="col-md-4">
                    ${formRow('Fecha anticipo nuevo *',
                        `<input class="form-control-custom" type="date" id="antFecha"
                            value="${new Date().toISOString().split('T')[0]}">`)}
                </div>
                <div class="col-md-6">
                    ${formRow('Uso CFDI del anticipo',
                            fSelect('antUsoCFDI', catOpts(CAT_USO_CFDI, 'CP01')),
                            'Para anticipos generalmente "CP01 Pagos".')}
                </div>
                <div class="col-md-6">
                    ${formRow('Forma de pago del anticipo *',
                                fSelect('antFormaPago', catOpts(CAT_FORMA_PAGO, '03')))}
                </div>
            </div>
            <div id="antMontoPrev" class="mt-3"></div>`;
    },

    _buildTabAnticipoAplicacion: function (el, cfdi) {
        el.innerHTML = `
            ${fiscalAlert('info', 'fa-file-invoice-dollar',
            'CFDI de egreso — Aplicación de anticipo.',
            'Una vez emitido el nuevo anticipo, se debe emitir un CFDI de Egreso por el mismo monto (nodo "Anticipo") ' +
            'para aplicar el anticipo contra la factura final. TipoRelacionado = 07.')}

            <div class="row g-3">
                <div class="col-md-4">
                    ${formRow('Monto del CFDI Egreso (= anticipo)',
                `<input class="form-control-custom" type="number" id="antEgresoMonto"
                            step="0.01" readonly style="background:var(--bg-gray)"
                            placeholder="Se calcula automático">`,
                'Debe ser igual al monto del anticipo emitido.')}
                </div>
                <div class="col-md-4">
                    ${formRow('UUID del anticipo a aplicar',
                    fInput('antEgresoUUID', '',
                        'placeholder="UUID del nuevo anticipo" ' +
                        'style="font-family:JetBrains Mono,monospace"'),
                    'Se llena automáticamente al timbrar el anticipo.')}
                </div>
                <div class="col-md-4">
                    ${formRow('Tipo de relación en el Egreso',
                        fSelect('antEgresoRel', catOpts(CAT_TIPO_RELACION, '07')),
                        '07 = CFDI por aplicación de anticipos')}
                </div>
            </div>

            ${fiscalAlert('warning', 'fa-triangle-exclamation',
                            'Secuencia obligatoria.',
                            '(1) Timbrar anticipo nuevo → copiar UUID. ' +
                            '(2) Timbrar Egreso con UUID del anticipo. ' +
                            '(3) Timbrar factura final con UUID del Egreso (relación 07).')}`;

        // Sincronizar monto del egreso con el monto del anticipo
        const antMonto = document.getElementById('antMonto');
        const egresoMonto = document.getElementById('antEgresoMonto');
        if (antMonto && egresoMonto) {
            egresoMonto.value = antMonto.value || '';
        }
    },

    // ── Métodos internos ──────────────────────────────────────────────────────

    _validateAntMonto: function () {
        const cfdi = AppState.selectedCFDI;
        const el = document.getElementById('antMonto');
        const prev = document.getElementById('antMontoPrev');
        if (!el || !cfdi) return;

        const v = parseFloat(el.value);
        if (v >= cfdi.total) {
            markError('antMonto',
                `El anticipo debe ser MENOR al total de la factura final ($${fmtMoney(cfdi.total)}).`);
            if (prev) prev.innerHTML = fiscalAlert('danger', 'fa-xmark',
                'Monto inválido.',
                'El anticipo no puede ser igual o mayor al total de la operación.');
        } else {
            clearError('antMonto');
            const saldo = cfdi.total - v;
            if (prev) prev.innerHTML = fiscalAlert('info', 'fa-calculator',
                'Resumen anticipo + saldo.',
                `Anticipo: $${fmtMoney(v)} · Saldo a cubrir: $${fmtMoney(saldo)} · Total operación: $${fmtMoney(cfdi.total)}.`);

            // Sincronizar con el campo del egreso si ya está visible
            const egresoMonto = document.getElementById('antEgresoMonto');
            if (egresoMonto) egresoMonto.value = v.toFixed(2);
        }
    },
};
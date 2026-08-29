// handlers/refacturacion-parcial.js
// Depende de: utils.js, catalogos.js, state.js
// Base legal: Regla 2.7.1.26 RMF
// Nota: NO existe "cancelación parcial" en SAT — se cancela el total y se emiten nuevos CFDI.

var ParcialHandler = {

    tipoRelacion: '04',

    tabs: [
        {
            id: 'parcial',
            label: 'Redistribuir partidas',
            build: function (el, cfdi) { ParcialHandler._buildTabParcial(el, cfdi); }
        },
        {
            id: 'parcialRelacion',
            label: 'Relación entre CFDI',
            build: function (el, cfdi) { ParcialHandler._buildTabParcialRelacion(el, cfdi); }
        },
    ],

    buildSummary: function (cfdi) {
        const grupos = ParcialHandler._calcularGrupos(cfdi);
        const activos = Object.entries(grupos).filter(([, v]) => v > 0);
        return `
            <div class="row g-3">
                <div class="col-md-6">
                    <div class="form-label-custom">CFDI original</div>
                    <div style="font-weight:600;color:var(--danger-color)">
                        <code style="font-size:.75rem">${esc(cfdi.uuid || '—')}</code>
                    </div>
                </div>
                <div class="col-md-6">
                    <div class="form-label-custom">Nuevos CFDI a emitir</div>
                    <div style="font-weight:700;color:var(--success-color)">${activos.length}</div>
                </div>
                ${activos.map(([g, total]) => `
                    <div class="col-md-4">
                        <div class="form-label-custom">CFDI nuevo ${g}</div>
                        <div style="font-weight:600;font-family:'JetBrains Mono',monospace">
                            $${fmtMoney(total)} + IVA
                        </div>
                    </div>`).join('')}
            </div>`;
    },

    execSteps: [
        { text: 'Cancelar CFDI original (motivo 01)', sub: 'Envío al PAC certificado' },
        { text: 'Esperar aceptación del receptor', sub: 'Hasta 72 hrs hábiles' },
        { text: 'Emitir nuevo CFDI 1 con partidas seleccionadas', sub: 'Generación y sellado digital' },
        { text: 'Emitir nuevo CFDI 2 (si aplica)', sub: 'Generación y sellado digital' },
        { text: 'Emitir nuevo CFDI 3 (si aplica)', sub: 'Generación y sellado digital' },
        { text: 'Relacionar todos con UUID original (rel. 04)', sub: 'Nodo CfdiRelacionados en cada nuevo CFDI' },
    ],

    confirmChecks: [
        { id: 'chkSumaIgual', label: 'La suma de los nuevos CFDI es igual al total del CFDI original' },
        { id: 'chkRelacion04', label: 'Cada nuevo CFDI lleva TipoRelacionado = 04 apuntando al UUID cancelado' },
    ],

    // ── Tabs internos ─────────────────────────────────────────────────────────

    _buildTabParcial: function (el, cfdi) {
        el.innerHTML = `
            ${fiscalAlert('warning', 'fa-scissors',
            'No existe cancelación parcial de CFDI.',
            'El SAT no permite cancelar parcialmente un CFDI. El flujo correcto es: ' +
            '(1) Cancelar el CFDI original con motivo 01. ' +
            '(2) Emitir uno o más CFDI nuevos con los importes correctos. ' +
            'Selecciona las partidas que formarán cada nuevo CFDI.')}

            ${sectionTitle('Partidas del CFDI original')}
            <div id="parcialItems">
                ${(cfdi.conceptos || []).map((c, i) => `
                    <div class="partial-row" id="pi-${i}">
                        <input type="checkbox" class="partial-check" id="pichk-${i}" checked
                            onchange="ParcialHandler._updateParcialTotal()">
                        <label for="pichk-${i}"
                            style="flex:1;font-size:.85rem;cursor:pointer;font-weight:500">
                            ${esc(c.desc)}
                        </label>
                        <div style="font-size:.78rem;color:var(--text-light);margin:0 .75rem">
                            ${c.qty} × $${fmtMoney(c.pu)}
                        </div>
                        <div style="font-size:.88rem;font-weight:700;font-family:'JetBrains Mono',monospace;
                            color:var(--primary-blue);min-width:100px;text-align:right">
                            $${fmtMoney(c.importe)}
                        </div>
                        <div style="margin-left:.5rem">
                            <select
                                style="font-size:.75rem;border:1px solid var(--border-light);border-radius:6px;
                                    padding:2px 6px;background:var(--bg-light);color:var(--text-dark)"
                                id="pigroup-${i}"
                                onchange="ParcialHandler._updateParcialTotal()">
                                <option value="1">CFDI nuevo 1</option>
                                <option value="2">CFDI nuevo 2</option>
                                <option value="3">CFDI nuevo 3</option>
                            </select>
                        </div>
                    </div>`).join('')}
            </div>

            ${sectionTitle('Resumen por nuevo CFDI')}
            <div id="parcialResumen" class="row g-3 mt-1"></div>`;

        setTimeout(() => ParcialHandler._updateParcialTotal(), 80);
    },

    _buildTabParcialRelacion: function (el, cfdi) {
        el.innerHTML = `
            ${fiscalAlert('info', 'fa-link',
            'Relación entre CFDI.',
            'Los nuevos CFDI deben relacionarse con el original cancelado usando TipoRelacionado = 04 (Sustitución). ' +
            'Esto garantiza la trazabilidad fiscal ante el SAT.')}

            <div class="row g-3">
                <div class="col-md-6">
                    ${formRow('Tipo de relación para nuevos CFDI',
                fSelect('parcialTipoRel', catOpts(CAT_TIPO_RELACION, '04')),
                'Sustitución (04) es el más común para refacturación parcial.')}
                </div>
                <div class="col-md-6">
                    ${formRow('UUID del CFDI cancelado (a relacionar)',
                    fInput('parcialUUIDRel', cfdi.uuid,
                        'style="font-family:JetBrains Mono,monospace"'),
                    'Se incluye en el nodo CfdiRelacionados de cada nuevo CFDI.')}
                </div>
            </div>`;
    },

    // ── Métodos internos ──────────────────────────────────────────────────────

    _calcularGrupos: function (cfdi) {
        const grupos = { 1: 0, 2: 0, 3: 0 };
        (cfdi.conceptos || []).forEach((c, i) => {
            const chk = document.getElementById('pichk-' + i);
            const grp = parseInt(document.getElementById('pigroup-' + i)?.value || 1);
            if (chk?.checked) grupos[grp] = (grupos[grp] || 0) + (c.importe || 0);
        });
        return grupos;
    },

    _updateParcialTotal: function () {
        const cfdi = AppState.selectedCFDI;
        const grupos = ParcialHandler._calcularGrupos(cfdi);

        const el = document.getElementById('parcialResumen');
        if (!el) return;
        el.innerHTML = [1, 2, 3].filter(g => grupos[g] > 0).map(g => `
            <div class="col-md-4">
                <div class="kpi-card">
                    <div class="kpi-label">Nuevo CFDI ${g}</div>
                    <div class="kpi-value" style="font-size:1.2rem;color:var(--success-color)">
                        $${fmtMoney(grupos[g])}</div>
                    <div class="kpi-sub">+ IVA: $${fmtMoney(grupos[g] * 0.16)}</div>
                    <div style="font-size:.75rem;color:var(--text-light);margin-top:.25rem">
                        Total: $${fmtMoney(grupos[g] * 1.16)}</div>
                </div>
            </div>`).join('');
    },
};
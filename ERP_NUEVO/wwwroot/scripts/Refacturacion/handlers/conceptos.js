// handlers/conceptos.js
// Depende de: utils.js, catalogos.js, state.js

var ConceptosHandler = {

    tipoRelacion: '04',

    // Recolecta las partidas editadas para el back (ConceptosTypeHandler lee 'partidas').
    // Convierte la selección de IVA del select a la clave que espera el back.
    collectChanges: function (cfdi) {
        const partidas = [];
        document.querySelectorAll('#con-body tr.concept-editor-row').forEach((r) => {
            const i = r.id.replace('con-cr-', '');
            const g = id => document.getElementById(id + i);
            const noIdentEl = g('con-cnoident-');
            partidas.push({
                claveProdServ: g('con-cprod-')?.value.trim() || '',
                descripcion: g('con-cdesc-')?.value.trim() || '',
                claveUnidad: g('con-cunidad-')?.value || 'H87',
                cantidad: parseFloat(g('con-cqty-')?.value) || 0,
                precioUnit: parseFloat(g('con-cpu-')?.value) || 0,
                descuentoImporte: parseFloat(g('con-cdescAmt-')?.value) || 0,
                iva: g('con-civa-')?.value || '0.16',   // '0.16' | '0' | 'exento'
                numero: noIdentEl?.value.trim() || '',
                idProducto: parseInt(noIdentEl?.dataset.idproducto) || 0,
            });
        });

        // Qué hacer con las partidas de la remisión que quedaron pendientes de facturar.
        // 'descartar' | 'facturar' | 'nueva-remision'  (ver _panelPendientes)
        const est = document.querySelector('input[name="con-pend-estrategia"]:checked');
        return { partidas, pendientesEstrategia: est ? est.value : 'descartar' };
    },

    // ── Partidas PENDIENTES de facturar en las remisiones de origen ──
    // Solo aplica cuando la factura se generó facturando parte de su(s) remisión(es).
    // Los datos vienen del Step 3 (AppState.remisionesOrigen ← endpoint RemisionesOrigen).
    _pendientes: function () {
        const data = (typeof AppState !== 'undefined') ? AppState.remisionesOrigen : null;
        if (!data || !data.tieneParciales) return [];
        const out = [];
        (data.remisiones || []).forEach(r =>
            (r.pendientes || []).forEach(p => out.push(Object.assign({ _remision: r.folio || r.encabezado }, p))));
        return out;
    },

    _panelPendientes: function () {
        const pend = ConceptosHandler._pendientes();
        if (!pend.length) return '';

        const filas = pend.map(p => `<li>${esc(p.cveprod)} ${esc(p.descripcion || '')} —
            pendiente <strong>${p.pendiente}</strong> de ${p.cantidadoriginal}
            <span style="opacity:.7">(remisión ${esc(String(p._remision))})</span></li>`).join('');

        const op = (val, checked, titulo, desc) => `
            <label style="display:flex;gap:.5rem;align-items:flex-start;margin-bottom:.4rem;cursor:pointer">
                <input type="radio" name="con-pend-estrategia" value="${val}" ${checked ? 'checked' : ''}
                    onchange="ConceptosHandler._onEstrategiaPendientes()" style="margin-top:.25rem">
                <span><strong>${titulo}</strong><br><span style="opacity:.8;font-size:.85em">${desc}</span></span>
            </label>`;

        return `
            <div class="alert-custom alert-warning-custom mb-2" style="display:block">
                <div><i class="fas fa-triangle-exclamation"></i>
                    <strong>Hay partidas pendientes de facturar en la(s) remisión(es) de origen.</strong>
                    Al refacturar se cancela la remisión anterior, así que debes decidir qué pasa con ellas:</div>
                <ul style="margin:.4rem 0 .6rem 1.2rem">${filas}</ul>
                ${op('nueva-remision', true, 'Guardar una remisión nueva solo con las pendientes',
                    'Se crea otro documento de remisión con las cantidades pendientes para facturarlas después. No se pierde nada.')}
                ${op('facturar', false, 'Facturar en esta refacturación',
                    'Se agregan como conceptos al CFDI nuevo (puedes editarlos abajo antes de ejecutar).')}
                ${op('descartar', false, 'Descartar las pendientes',
                    'Las cantidades pendientes se dan por no entregadas: su stock regresa al almacén y ya no se podrán facturar.')}
            </div>`;
    },

    // "Facturar" agrega las pendientes como conceptos; las otras opciones las quitan.
    _onEstrategiaPendientes: function () {
        const est = document.querySelector('input[name="con-pend-estrategia"]:checked');
        const body = document.getElementById('con-body');
        if (!body) return;

        body.querySelectorAll('tr.con-pend-row').forEach(tr => tr.remove());

        if (est && est.value === 'facturar') {
            // El índice DEBE ser numérico: _rowHTML lo inyecta sin comillas en _calcRow(i)/_removeRow(i).
            ConceptosHandler._pendientes().forEach((p, n) => {
                const i = Date.now() + n;
                body.insertAdjacentHTML('beforeend', ConceptosHandler._rowHTML({
                    clave: p.claveprodserv || '',
                    desc: p.descripcion || p.cveprod,
                    unidad: p.claveunidad || 'H87',
                    qty: Number(p.pendiente) || 0,
                    pu: Number(p.precio) || 0,
                    importe: (Number(p.pendiente) || 0) * (Number(p.precio) || 0),
                    noIdentificacion: p.cveprod
                }, i));
                const tr = document.getElementById('con-cr-' + i);
                if (tr) tr.classList.add('con-pend-row');
            });
        }
        ConceptosHandler._recalc();
    },

    tabs: [
        {
            id: 'conceptos',
            label: 'Editar conceptos',
            build: function (el, cfdi) { ConceptosHandler._buildTabConceptos(el, cfdi); }
        },
        {
            id: 'impuestos-t',
            label: 'Impuestos',
            build: function (el, cfdi) { ConceptosHandler._buildTabImpuestos(el, cfdi); }
        },
    ],

    buildSummary: function (cfdi) {
        const totalEl = document.getElementById('summTotal');
        const subEl = document.getElementById('summSubtotal');
        const ivaEl = document.getElementById('summIVA16');
        return `
            <div class="row g-3">
                <div class="col-md-4">
                    <div class="form-label-custom">Conceptos</div>
                    <div style="font-weight:600">${cfdi.conceptos.length} partida(s)</div>
                </div>
                <div class="col-md-4">
                    <div class="form-label-custom">Subtotal nuevo</div>
                    <div style="font-weight:700;font-family:'JetBrains Mono',monospace">
                        ${subEl ? subEl.textContent : ('$' + fmtMoney(cfdi.subtotal))}
                    </div>
                </div>
                <div class="col-md-4">
                    <div class="form-label-custom">Total nuevo</div>
                    <div style="font-weight:700;color:var(--success-color);font-family:'JetBrains Mono',monospace">
                        ${totalEl ? totalEl.textContent : ('$' + fmtMoney(cfdi.total))}
                    </div>
                </div>
                <div class="col-md-4">
                    <div class="form-label-custom">IVA 16%</div>
                    <div style="font-family:'JetBrains Mono',monospace">
                        ${ivaEl ? ivaEl.textContent : ('$' + fmtMoney(cfdi.iva))}
                    </div>
                </div>
            </div>`;
    },

    execSteps: [
        { text: 'Guardar snapshot del CFDI original', sub: 'Respaldo antes de cancelar' },
        { text: 'Verificar impacto en inventario', sub: 'Ajuste de existencias si aplica' },
        { text: 'Cancelar CFDI original (motivo 01)', sub: 'Envío al PAC certificado' },
        { text: 'Esperar aceptación del receptor', sub: 'Hasta 72 hrs hábiles' },
        { text: 'Emitir nuevo CFDI con conceptos corregidos', sub: 'Generación y sellado digital' },
        { text: 'Ajustar inventario / costo promedio', sub: 'Si la operación afecta existencias' },
        { text: 'Ajuste contable', sub: 'Ingresos, CxC, IVA causado' },
        { text: 'Notificar al receptor', sub: 'XML y PDF por correo' },
    ],

    confirmChecks: [
        { id: 'chkInventario', label: 'Impacto en inventario revisado y autorizado' },
        { id: 'chkImpuestosConceptos', label: 'Impuestos recalculados y validados (cuadratura SAT)' },
    ],

    // ── Tabs internos ─────────────────────────────────────────────────────────

    _buildTabConceptos: function (el, cfdi) {
        el.innerHTML = `
            ${fiscalAlert('info', 'fa-list',
            'Reglas SAT para conceptos CFDI 4.0.',
            'Cantidad y precio unitario deben ser positivos. El importe = cantidad × precio − descuento. ' +
            'ClaveProdServ y ClaveUnidad son obligatorios y deben ser del catálogo SAT. ' +
            'El descuento no puede superar el importe del concepto.')}

            ${ConceptosHandler._panelPendientes()}

            <div class="mb-2 d-flex gap-2 flex-wrap align-items-center">
                <button class="btn-primary-custom btn-sm-custom" onclick="ConceptosHandler._openProductModal()">
                    <i class="fas fa-magnifying-glass"></i> Agregar producto del catálogo
                </button>
                <button class="btn-secondary-custom btn-sm-custom" onclick="ConceptosHandler._addRow()">
                    <i class="fas fa-plus"></i> Línea manual
                </button>
                <button class="btn-secondary-custom btn-sm-custom" onclick="ConceptosHandler._recalc()">
                    <i class="fas fa-calculator"></i> Recalcular totales
                </button>
                <button class="btn-secondary-custom btn-sm-custom" onclick="ConceptosHandler._validate()">
                    <i class="fas fa-shield-check"></i> Validar conceptos
                </button>
            </div>

            <div class="table-wrapper">
                <table class="table-custom" id="con-table" style="min-width:900px">
                    <thead>
                        <tr>
                            <th style="width:44px">Acc.</th>
                            <th style="width:120px">ClaveProdServ *</th>
                            <th style="min-width:200px">Descripción *</th>
                            <th style="width:90px">ClaveUnidad *</th>
                            <th style="width:80px">Cantidad *</th>
                            <th style="width:110px">P.U. *</th>
                            <th style="width:90px">Desc. $</th>
                            <th style="width:120px">Importe</th>
                            <th style="width:70px">IVA</th>
                            <th style="width:80px">No. Ident.</th>
                        </tr>
                    </thead>
                    <tbody id="con-body">
                        ${cfdi.conceptos.map((c, i) => ConceptosHandler._rowHTML(c, i)).join('')}
                    </tbody>
                </table>
            </div>

            <div id="con-errors" class="mt-2"></div>

            <div class="row g-3 mt-1">
                <div class="col-md-5">
                    <div class="amount-summary" id="con-summary">
                        ${ConceptosHandler._buildSummaryRows(cfdi)}
                    </div>
                </div>
                <div class="col-md-7">
                    ${fiscalAlert('warning', 'fa-scale-balanced',
                'Regla de cuadratura SAT.',
                'SubTotal = Σ(importes − descuentos). Total = SubTotal − DescuentoGlobal + IVA − Retenciones. ' +
                'Diferencias > $0.01 rechazan el CFDI en el PAC.')}
                </div>
            </div>`;

        ConceptosHandler._recalc();
    },

    _buildTabImpuestos: function (el /*, cfdi */) {
        el.innerHTML = `
            ${fiscalAlert('warning', 'fa-percent',
            'Reglas de traslado SAT CFDI 4.0.',
            'Todos los importes de impuestos en el nodo raíz deben ser la suma de los impuestos de cada concepto. ' +
            'IVA exento y tasa 0% son diferentes — exento no lleva nodo de traslado.')}

            ${sectionTitle('Impuestos trasladados')}
            <div class="row g-3 mb-3">
                <div class="col-md-4">
                    ${formRow('Impuesto',
                `<select class="form-control-custom" id="con-impTipo">
                            <option value="002">002 · IVA</option>
                            <option value="003">003 · IEPS</option>
                        </select>`,
                'Catálogo c_Impuesto SAT')}
                </div>
                <div class="col-md-4">
                    ${formRow('Tipo de factor',
                    `<select class="form-control-custom" id="con-tipoFactor"
                            onchange="ConceptosHandler._onTipoFactor()">
                            <option value="Tasa">Tasa</option>
                            <option value="Cuota">Cuota</option>
                            <option value="Exento">Exento</option>
                        </select>`,
                    'Si es Exento, no se captura tasa ni importe.')}
                </div>
                <div class="col-md-4" id="con-tasaRow">
                    ${formRow('Tasa o cuota',
                        `<select class="form-control-custom" id="con-tasa">
                            <option value="0.160000">0.160000 · IVA 16%</option>
                            <option value="0.080000">0.080000 · IVA 8% (Zona fronteriza)</option>
                            <option value="0.000000">0.000000 · IVA 0%</option>
                        </select>`,
                        'Hasta 6 decimales según catálogo SAT.')}
                </div>
            </div>

            ${sectionTitle('Retenciones')}
            ${fiscalAlert('info', 'fa-hand-holding',
                            'Retenciones obligatorias.',
                            'ISR: Honorarios 10%, Arrendamiento PF 10%. ' +
                            'IVA: Honorarios 10.666667%, Arrendamiento PF 10.666667%.')}
            <div class="row g-3 mb-3">
                <div class="col-md-3">
                    ${formRow('Retención ISR %',
                                `<input class="form-control-custom" type="number" id="con-retISRPct"
                            value="0" min="0" max="35" step="0.000001"
                            onchange="ConceptosHandler._calcRet()">`,
                                '0, 10% (honorarios/arrendamiento PF), 25% (extranjeros)')}
                </div>
                <div class="col-md-3">
                    ${formRow('Retención IVA %',
                                    `<input class="form-control-custom" type="number" id="con-retIVAPct"
                            value="0" min="0" max="16" step="0.000001"
                            onchange="ConceptosHandler._calcRet()">`,
                                    '0%, 10.666667% (honorarios) o 16% (comisiones)')}
                </div>
                <div class="col-md-3">
                    ${formRow('Importe ret. ISR',
                                        `<input class="form-control-custom" type="number" id="con-retISRMonto"
                            value="0" readonly style="background:var(--bg-gray)">`)}
                </div>
                <div class="col-md-3">
                    ${formRow('Importe ret. IVA',
                                            `<input class="form-control-custom" type="number" id="con-retIVAMonto"
                            value="0" readonly style="background:var(--bg-gray)">`)}
                </div>
            </div>

            ${sectionTitle('Configuraciones especiales')}
            <div class="row g-3">
                <div class="col-md-6">
                    ${formRow('IEPS (si aplica)',
                                                `<div style="display:flex;gap:.5rem">
                            <input class="form-control-custom" type="number" id="con-iepsPct"
                                value="0" min="0" step="0.01" placeholder="%" style="width:80px">
                            <span style="display:flex;align-items:center;font-size:.82rem;color:var(--text-light)">%</span>
                            <input class="form-control-custom" type="number" id="con-iepsMonto"
                                value="0" readonly style="background:var(--bg-gray)">
                        </div>`,
                                                'IEPS Art. 2 LIEPS. Tasas: 3%, 6%, 7%, 8%, 9%, 10.5%, 26.5%, 30%, 30.4%, 53%, 160%')}
                </div>
                <div class="col-md-6">
                    ${fiscalAlert('info', 'fa-map-marker',
                                                    'Zona fronteriza IVA 8%.',
                                                    'Decreto DOF 31/dic/2018. Aplica en municipios específicos. Requiere constancia de domicilio en zona fronteriza.')}
                </div>
            </div>`;

        setTimeout(() => { ConceptosHandler._onTipoFactor(); ConceptosHandler._calcRet(); }, 80);
    },

    // ── Fila de concepto ──────────────────────────────────────────────────────

    _rowHTML: function (c, i) {
        return `
            <tr class="concept-editor-row" id="con-cr-${i}">
                <td>
                    <button class="btn-danger-custom btn-sm-custom" style="padding:3px 8px"
                        onclick="ConceptosHandler._removeRow(${i})" title="Eliminar">
                        <i class="fas fa-trash"></i>
                    </button>
                </td>
                <td><input type="text" value="${esc(c.clave || '')}" placeholder="84111506" maxlength="8"
                    style="width:110px" id="con-cprod-${i}"></td>
                <td><input type="text" value="${esc(c.desc || '')}" placeholder="Descripción del producto o servicio"
                    style="width:100%" id="con-cdesc-${i}"></td>
                <td>
                    <select style="width:85px" id="con-cunidad-${i}">
                        <option value="H87" ${!c.unidad || c.unidad === 'H87' ? 'selected' : ''}>H87 · Pieza</option>
                        <option value="E48" ${c.unidad === 'E48' ? 'selected' : ''}>E48 · Servicio</option>
                        <option value="KGM" ${c.unidad === 'KGM' ? 'selected' : ''}>KGM · Kilogramo</option>
                        <option value="LTR" ${c.unidad === 'LTR' ? 'selected' : ''}>LTR · Litro</option>
                        <option value="MTR" ${c.unidad === 'MTR' ? 'selected' : ''}>MTR · Metro</option>
                        <option value="XBX" ${c.unidad === 'XBX' ? 'selected' : ''}>XBX · Caja</option>
                        <option value="ACT" ${c.unidad === 'ACT' ? 'selected' : ''}>ACT · Actividad</option>
                        <option value="MON" ${c.unidad === 'MON' ? 'selected' : ''}>MON · Mes</option>
                    </select>
                </td>
                <td><input type="number" value="${c.qty || 1}" min="0.000001" step="0.001"
                    style="width:75px" id="con-cqty-${i}"
                    onchange="ConceptosHandler._calcRow(${i})"></td>
                <td><input type="number" value="${c.pu ? c.pu.toFixed(6) : '0.000000'}" min="0" step="0.000001"
                    style="width:105px" id="con-cpu-${i}"
                    onchange="ConceptosHandler._calcRow(${i})"></td>
                <td><input type="number" value="0.00" min="0" step="0.01"
                    style="width:85px" id="con-cdescAmt-${i}"
                    onchange="ConceptosHandler._calcRow(${i})"></td>
                <td><input type="number" value="${c.importe ? c.importe.toFixed(2) : '0.00'}" readonly
                    style="width:115px;background:var(--bg-gray)" id="con-imp-${i}"></td>
                <td>
                    <select style="width:65px" id="con-civa-${i}"
                        onchange="ConceptosHandler._calcRow(${i})">
                        <option value="0.16">16%</option>
                        <option value="0">0%</option>
                        <option value="exento">Exento</option>
                    </select>
                </td>
                <td><input type="text" value="${esc(c.noIdentificacion || c.numero || '')}" placeholder="SKU"
                    style="width:75px" maxlength="100" id="con-cnoident-${i}"
                    data-idproducto="${esc(c.idProducto || '')}"></td>
            </tr>`;
    },

    _buildSummaryRows: function (cfdi) {
        const sub = cfdi?.subtotal ?? 0;
        const iva = cfdi?.iva ?? 0;
        const tot = cfdi?.total ?? 0;
        return `
            <div class="amount-row"><span class="label">Subtotal</span>
                <span class="value" id="summSubtotal">$${fmtMoney(sub)}</span></div>
            <div class="amount-row"><span class="label">Descuento total</span>
                <span class="value" id="summDesc">$0.00</span></div>
            <div class="amount-row"><span class="label">IVA trasladado (16%)</span>
                <span class="value" id="summIVA16">$${fmtMoney(iva)}</span></div>
            <div class="amount-row"><span class="label">IVA trasladado (0%)</span>
                <span class="value" id="summIVA0">$0.00</span></div>
            <div class="amount-row"><span class="label">Retención ISR</span>
                <span class="value" id="summRetISR">$0.00</span></div>
            <div class="amount-row"><span class="label">Retención IVA</span>
                <span class="value" id="summRetIVA">$0.00</span></div>
            <div class="amount-row amount-total"><span class="label">Total</span>
                <span class="value" id="summTotal">$${fmtMoney(tot)}</span></div>`;
    },

    // ── Lógica de filas y totales ─────────────────────────────────────────────

    _calcRow: function (i) {
        this._recalc();
    },

    _recalc: function () {
        let sub = 0, desc = 0, iva16 = 0;

        document.querySelectorAll('#con-body tr.concept-editor-row').forEach((r) => {
            const i = parseInt(r.id.replace('con-cr-', ''));
            const qty = parseFloat(document.getElementById('con-cqty-' + i)?.value || 0) || 0;
            const pu = parseFloat(document.getElementById('con-cpu-' + i)?.value || 0) || 0;
            const dsc = parseFloat(document.getElementById('con-cdescAmt-' + i)?.value || 0) || 0;
            const ivaSel = document.getElementById('con-civa-' + i)?.value || '0.16';
            const imp = Math.max(0, qty * pu - dsc);

            const impEl = document.getElementById('con-imp-' + i);
            if (impEl) impEl.value = imp.toFixed(2);

            sub += qty * pu;
            desc += dsc;
            if (ivaSel === '0.16') iva16 += imp * 0.16;
        });

        const base = sub - desc;
        const total = base + iva16;

        const set = (id, val) => {
            const el = document.getElementById(id);
            if (el) el.textContent = '$' + fmtMoney(val);
        };
        set('summSubtotal', base);
        set('summDesc', desc);
        set('summIVA16', iva16);
        set('summIVA0', 0);
        set('summTotal', total);
    },

    _removeRow: function (i) {
        const row = document.getElementById('con-cr-' + i);
        if (row) row.remove();
        this._recalc();
    },

    _addRow: function () {
        const body = document.getElementById('con-body');
        if (!body) return;
        const i = Date.now();
        const tr = document.createElement('tr');
        tr.className = 'concept-editor-row';
        tr.id = 'con-cr-' + i;
        tr.innerHTML = ConceptosHandler._rowHTML(
            { clave: '', desc: '', qty: 1, pu: 0, importe: 0 }, i
        );
        body.appendChild(tr);
    },

    // ── Modal de búsqueda de productos del catálogo ──
    // Endpoint: /DatosGeneralesRefacturacion/BuscarProductos?q= (catproductos + catrelacion).
    // Al seleccionar, arma la fila con clave SAT, unidad SAT, precio de lista e id de producto,
    // para que el usuario no capture a mano (menos errores) y el ajuste de inventario tenga idProducto.
    _prodSearchUrl: '/DatosGeneralesRefacturacion/BuscarProductos',
    _prodDebounce: null,

    _ensureProductModal: function () {
        if (document.getElementById('con-prod-modal')) return;
        const wrap = document.createElement('div');
        wrap.id = 'con-prod-modal';
        wrap.style.cssText =
            'position:fixed;inset:0;background:rgba(0,0,0,.45);z-index:20000;display:none;' +
            'align-items:flex-start;justify-content:center;padding:5vh 1rem';
        wrap.innerHTML = `
            <div style="background:var(--card-bg,#fff);color:inherit;border-radius:12px;width:min(820px,100%);
                        max-height:85vh;display:flex;flex-direction:column;box-shadow:0 20px 60px rgba(0,0,0,.35)">
                <div style="display:flex;align-items:center;justify-content:space-between;padding:1rem 1.25rem;
                            border-bottom:1px solid var(--border-color,#e5e7eb)">
                    <strong><i class="fas fa-magnifying-glass"></i> Buscar producto</strong>
                    <button class="btn-secondary-custom btn-sm-custom" onclick="ConceptosHandler._closeProductModal()">
                        <i class="fas fa-xmark"></i>
                    </button>
                </div>
                <div style="padding:.9rem 1.25rem">
                    <input type="text" id="con-prod-q" placeholder="Clave o descripción del producto…"
                        style="width:100%" oninput="ConceptosHandler._onProductQuery()"
                        onkeydown="if(event.key==='Enter'){event.preventDefault();ConceptosHandler._searchProducts();}">
                </div>
                <div style="overflow:auto;padding:0 1.25rem 1.25rem">
                    <table class="table-custom" style="min-width:100%">
                        <thead><tr>
                            <th style="width:110px">Clave</th>
                            <th>Descripción</th>
                            <th style="width:70px">Unidad</th>
                            <th style="width:100px;text-align:right">Precio</th>
                            <th style="width:90px"></th>
                        </tr></thead>
                        <tbody id="con-prod-results">
                            <tr><td colspan="5" style="text-align:center;opacity:.6;padding:1rem">
                                Escribe para buscar…</td></tr>
                        </tbody>
                    </table>
                </div>
            </div>`;
        document.body.appendChild(wrap);
        // Cerrar al hacer clic fuera de la tarjeta.
        wrap.addEventListener('click', (e) => { if (e.target === wrap) ConceptosHandler._closeProductModal(); });
    },

    _openProductModal: function () {
        ConceptosHandler._ensureProductModal();
        document.getElementById('con-prod-modal').style.display = 'flex';
        const q = document.getElementById('con-prod-q');
        q.value = '';
        document.getElementById('con-prod-results').innerHTML =
            '<tr><td colspan="5" style="text-align:center;opacity:.6;padding:1rem">Escribe para buscar…</td></tr>';
        setTimeout(() => q.focus(), 50);
    },

    _closeProductModal: function () {
        const m = document.getElementById('con-prod-modal');
        if (m) m.style.display = 'none';
    },

    _onProductQuery: function () {
        clearTimeout(ConceptosHandler._prodDebounce);
        ConceptosHandler._prodDebounce = setTimeout(() => ConceptosHandler._searchProducts(), 300);
    },

    _searchProducts: async function () {
        const q = (document.getElementById('con-prod-q')?.value || '').trim();
        const tbody = document.getElementById('con-prod-results');
        if (!tbody) return;
        if (q.length < 2) {
            tbody.innerHTML = '<tr><td colspan="5" style="text-align:center;opacity:.6;padding:1rem">Escribe al menos 2 caracteres…</td></tr>';
            return;
        }
        tbody.innerHTML = '<tr><td colspan="5" style="text-align:center;opacity:.6;padding:1rem"><i class="fas fa-spinner fa-spin"></i> Buscando…</td></tr>';

        let data;
        try {
            const resp = await fetch(`${ConceptosHandler._prodSearchUrl}?q=${encodeURIComponent(q)}`);
            data = await resp.json();
        } catch (e) {
            tbody.innerHTML = '<tr><td colspan="5" style="text-align:center;color:var(--danger,#dc2626);padding:1rem">Error de conexión.</td></tr>';
            return;
        }

        const rows = (data && data.success && data.productos) ? data.productos : [];
        if (!rows.length) {
            tbody.innerHTML = '<tr><td colspan="5" style="text-align:center;opacity:.6;padding:1rem">Sin resultados.</td></tr>';
            return;
        }

        ConceptosHandler._prodCache = {};
        tbody.innerHTML = rows.map((p, n) => {
            ConceptosHandler._prodCache[n] = p;
            const precio = Number(p.precio) || 0;
            const sinSat = !p.claveprodserv;
            return `<tr>
                <td>${esc(p.cveprod || '')}</td>
                <td>${esc(p.descripcion || '')}
                    ${sinSat ? '<br><small style="color:var(--warning,#d97706)">⚠ sin clave SAT en catrelacion</small>' : ''}</td>
                <td>${esc(p.claveunidad || 'H87')}</td>
                <td style="text-align:right">${precio.toFixed(2)}</td>
                <td><button class="btn-primary-custom btn-sm-custom"
                    onclick="ConceptosHandler._selectProduct(${n})">Elegir</button></td>
            </tr>`;
        }).join('');
    },

    _selectProduct: function (n) {
        const p = (ConceptosHandler._prodCache || {})[n];
        if (!p) return;

        const body = document.getElementById('con-body');
        if (!body) return;

        const i = Date.now();
        const tr = document.createElement('tr');
        tr.className = 'concept-editor-row';
        tr.id = 'con-cr-' + i;
        tr.innerHTML = ConceptosHandler._rowHTML({
            clave: p.claveprodserv || '',
            desc: p.descripcion || '',
            unidad: p.claveunidad || 'H87',
            qty: 1,
            pu: Number(p.precio) || 0,
            importe: Number(p.precio) || 0,
            noIdentificacion: p.cveprod || '',
            idProducto: p.idproducto || ''
        }, i);
        body.appendChild(tr);

        // Ajustes que el <select> no toma desde el value del HTML:
        const unidadSel = document.getElementById('con-cunidad-' + i);
        if (unidadSel && p.claveunidad &&
            [...unidadSel.options].some(o => o.value === p.claveunidad)) {
            unidadSel.value = p.claveunidad;
        }
        const ivaSel = document.getElementById('con-civa-' + i);
        if (ivaSel) ivaSel.value = (p.ivaexento || p.objetoimp === '01') ? 'exento' : '0.16';

        ConceptosHandler._calcRow(i);
        ConceptosHandler._closeProductModal();
    },

    _validate: function () {
        const errors = [];
        document.querySelectorAll('#con-body tr.concept-editor-row').forEach((r) => {
            const i = parseInt(r.id.replace('con-cr-', ''));
            const cprod = document.getElementById('con-cprod-' + i)?.value.trim();
            const desc = document.getElementById('con-cdesc-' + i)?.value.trim();
            const qty = parseFloat(document.getElementById('con-cqty-' + i)?.value || 0);
            const pu = parseFloat(document.getElementById('con-cpu-' + i)?.value || 0);
            const dsc = parseFloat(document.getElementById('con-cdescAmt-' + i)?.value || 0);
            const imp = qty * pu - dsc;
            const label = `Concepto ${i + 1}`;

            if (!cprod) errors.push(`${label}: ClaveProdServ es obligatoria.`);
            if (!desc) errors.push(`${label}: Descripción es obligatoria.`);
            if (qty <= 0) errors.push(`${label}: Cantidad debe ser mayor a 0.`);
            if (pu < 0) errors.push(`${label}: Precio unitario no puede ser negativo.`);
            if (dsc > qty * pu) errors.push(`${label}: Descuento no puede superar el importe.`);
            if (imp < 0) errors.push(`${label}: Importe no puede ser negativo.`);
        });

        const el = document.getElementById('con-errors');
        if (!el) return;
        if (errors.length === 0) {
            el.innerHTML = `
                <div class="alert-custom alert-success-custom">
                    <i class="fas fa-circle-check"></i>
                    <div>Todos los conceptos son válidos según las reglas SAT.</div>
                </div>`;
        } else {
            el.innerHTML = `
                <div class="alert-custom alert-danger-custom">
                    <i class="fas fa-triangle-exclamation"></i>
                    <div><strong>${errors.length} error(es) encontrado(s):</strong>
                        <ul style="margin:.4rem 0 0;padding-left:1.2rem">
                            ${errors.map(e => `<li style="font-size:.8rem">${e}</li>`).join('')}
                        </ul>
                    </div>
                </div>`;
        }
    },

    // ── Impuestos ─────────────────────────────────────────────────────────────

    _onTipoFactor: function () {
        const factor = document.getElementById('con-tipoFactor')?.value;
        const tasaRow = document.getElementById('con-tasaRow');
        if (tasaRow) tasaRow.style.display = factor === 'Exento' ? 'none' : '';
    },

    _calcRet: function () {
        const subEl = document.getElementById('summSubtotal');
        const sub = parseFloat(subEl?.textContent?.replace(/[$,]/g, '') || 0) || 0;
        const pctISR = parseFloat(document.getElementById('con-retISRPct')?.value || 0) / 100;
        const pctIVA = parseFloat(document.getElementById('con-retIVAPct')?.value || 0) / 100;
        const retISR = sub * pctISR;
        const retIVA = sub * pctIVA;

        const set = (id, val) => {
            const el = document.getElementById(id);
            if (el) el.value = val.toFixed(2);
        };
        set('con-retISRMonto', retISR);
        set('con-retIVAMonto', retIVA);

        const setSumm = (id, val) => {
            const el = document.getElementById(id);
            if (el) el.textContent = '$' + fmtMoney(val);
        };
        setSumm('summRetISR', retISR);
        setSumm('summRetIVA', retIVA);

        // Recalcular total con retenciones
        const iva16El = document.getElementById('summIVA16');
        const iva16 = parseFloat(iva16El?.textContent?.replace(/[$,]/g, '') || 0) || 0;
        setSumm('summTotal', sub + iva16 - retISR - retIVA);
    },
};
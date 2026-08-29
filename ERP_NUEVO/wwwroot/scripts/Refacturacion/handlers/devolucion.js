// handlers/devolucion.js
// Depende de: utils.js, catalogos.js, state.js
// Base legal: Art. 29-A CFF; Regla 2.7.1.26; CFDI Egreso (E)

var DevolucionHandler = {

    tipoRelacion: '01',

    tabs: [
        {
            id: 'devolucion',
            label: 'Tipo de devolución',
            build: function (el, cfdi) { DevolucionHandler._buildTabDevolucion(el, cfdi); }
        },
        {
            id: 'notaCredito',
            label: 'Nota de crédito',
            build: function (el, cfdi) { DevolucionHandler._buildTabNotaCredito(el, cfdi); }
        },
        {
            id: 'inventario',
            label: 'Inventario',
            build: function (el, cfdi) { DevolucionHandler._buildTabInventario(el, cfdi); }
        },
    ],

    buildSummary: function (cfdi) {
        const tipoSeleccionado = document
            .querySelector('input[name="devTipo"]:checked')?.value || 'nc';
        const etiquetas = {
            nc: 'Nota de Crédito (CFDI Egreso)',
            cancel: 'Cancelación total — motivo 03',
            'cancel-sust': 'Cancelación + sustitución parcial — motivo 01',
        };
        const importe = document.getElementById('devImporte')?.value
            || document.getElementById('sustImporte')?.value
            || '—';
        return `
            <div class="row g-3">
                <div class="col-md-6">
                    <div class="form-label-custom">Instrumento fiscal</div>
                    <div style="font-weight:600">${esc(etiquetas[tipoSeleccionado] || tipoSeleccionado)}</div>
                </div>
                <div class="col-md-6">
                    <div class="form-label-custom">Importe de devolución</div>
                    <div style="font-weight:700;font-family:'JetBrains Mono',monospace;color:var(--success-color)">
                        ${importe !== '—' ? '$' + fmtMoney(parseFloat(importe)) : '—'}
                    </div>
                </div>
                <div class="col-md-6">
                    <div class="form-label-custom">Total CFDI original</div>
                    <div style="font-family:'JetBrains Mono',monospace">$${fmtMoney(cfdi.total)}</div>
                </div>
                <div class="col-md-6">
                    <div class="form-label-custom">Tipo de relación</div>
                    <div>${tipoSeleccionado === 'nc' ? '01 · Nota de crédito' : '04 · Sustitución'}</div>
                </div>
            </div>`;
    },

    execSteps: [
        { text: 'Determinar instrumento fiscal correcto', sub: 'NC, cancelación 03 ó cancelación 01' },
        { text: 'Registrar devolución en inventario (si aplica)', sub: 'Revertir movimiento original' },
        { text: 'Emitir CFDI de Egreso (Nota de Crédito)', sub: 'TipoDeComprobante = E, rel. 01' },
        { text: 'Relacionar con CFDI de Ingreso original', sub: 'Nodo CfdiRelacionados' },
        { text: 'Notificar al receptor', sub: 'XML y PDF por correo' },
        { text: 'Registrar en contabilidad', sub: 'Asiento de devolución' },
    ],

    confirmChecks: [
        { id: 'chkDevInventario', label: 'Movimiento de inventario revertido (cantidad devuelta registrada)' },
        { id: 'chkDevRelacion', label: 'CFDI de Egreso relacionado con TipoRelacionado = 01 al CFDI de Ingreso' },
    ],

    // ── Tabs internos ─────────────────────────────────────────────────────────

    _buildTabDevolucion: function (el, cfdi) {
        el.innerHTML = `
            ${fiscalAlert('info', 'fa-rotate-left',
            'Devolución: instrumento fiscal correcto.',
            'Si la operación original fue correcta pero hubo devolución posterior, se emite un CFDI de Egreso ' +
            '(Nota de Crédito). Solo se cancela el CFDI de Ingreso cuando la operación no se llevó a cabo (motivo 03).')}

            ${sectionTitle('Tipo de instrumento fiscal')}
            <div class="row g-3 mb-3">
                <div class="col-12">
                    <div class="motivo-card selected"
                        onclick="DevolucionHandler._selectDevTipo(this, 'nc')" id="devTipoNC">
                        <input type="radio" name="devTipo" value="nc" checked>
                        <div>
                            <div class="motivo-text">Nota de Crédito (CFDI Egreso) — Devolución posterior</div>
                            <div class="motivo-sub">
                                La operación original sí ocurrió, pero después se devolvió mercancía o se otorgó descuento.
                                <strong>No cancela el CFDI original.</strong>
                            </div>
                        </div>
                    </div>
                    <div class="motivo-card"
                        onclick="DevolucionHandler._selectDevTipo(this, 'cancel')" id="devTipoCancel">
                        <input type="radio" name="devTipo" value="cancel">
                        <div>
                            <div class="motivo-text">Cancelación total (motivo 03) — Operación no realizada</div>
                            <div class="motivo-sub">
                                La operación nunca se llevó a cabo. Se cancela el CFDI y no se emite sustituto.
                            </div>
                        </div>
                    </div>
                    <div class="motivo-card"
                        onclick="DevolucionHandler._selectDevTipo(this, 'cancel-sust')" id="devTipoCancelSust">
                        <input type="radio" name="devTipo" value="cancel-sust">
                        <div>
                            <div class="motivo-text">Cancelación + sustitución parcial (motivo 01)</div>
                            <div class="motivo-sub">
                                La operación ocurrió pero el monto fue incorrecto.
                                Se cancela y se emite nuevo CFDI por el monto correcto.
                            </div>
                        </div>
                    </div>
                </div>
            </div>

            <div id="devTipoDetalles"></div>`;

        setTimeout(() => DevolucionHandler._renderDevTipoDetalles('nc'), 80);
    },

    _buildTabNotaCredito: function (el, cfdi) {
        el.innerHTML = `
            ${fiscalAlert('info', 'fa-file-invoice',
            'Nota de crédito (CFDI Egreso).',
            'TipoDeComprobante = E. Debe relacionarse con el CFDI de Ingreso original mediante TipoRelacionado = 01. ' +
            'Los conceptos de la NC deben coincidir con los del CFDI original que se está devolviendo.')}

            <div class="row g-3">
                <div class="col-md-4">
                    ${formRow('UUID del CFDI de ingreso relacionado',
                fInput('ncUUIDRel', cfdi.uuid,
                    'style="font-family:JetBrains Mono,monospace"'),
                'Relación tipo 01 en el nodo CfdiRelacionados')}
                </div>
                <div class="col-md-4">
                    ${formRow('Serie de la nota de crédito',
                    fInput('ncSerie', 'NC', 'maxlength="25"'),
                    'Recomendado usar serie diferente a facturas de ingreso.')}
                </div>
                <div class="col-md-4">
                    ${formRow('Fecha de la nota de crédito',
                        `<input class="form-control-custom" type="date" id="ncFecha"
                            value="${new Date().toISOString().split('T')[0]}">`)}
                </div>
            </div>`;
    },

    _buildTabInventario: function (el, cfdi) {
        el.innerHTML = `
            ${fiscalAlert('warning', 'fa-boxes-stacked',
            'Impacto en inventario.',
            'La devolución debe revertir el movimiento de inventario original. ' +
            'Registra la cantidad devuelta y el destino del producto.')}

            <div class="table-wrapper">
                <table class="table-custom">
                    <thead>
                        <tr>
                            <th>Producto / Servicio</th>
                            <th>Cant. original</th>
                            <th>Cant. devuelta *</th>
                            <th>Destino</th>
                            <th>Lote / Serie</th>
                        </tr>
                    </thead>
                    <tbody>
                        ${(cfdi.conceptos || []).map((c, i) => `
                            <tr>
                                <td style="font-size:.84rem;font-weight:500">${esc(c.desc)}</td>
                                <td style="font-family:'JetBrains Mono',monospace">${c.qty}</td>
                                <td>
                                    <input type="number" value="${c.qty}"
                                        min="0" max="${c.qty}" step="0.001"
                                        class="form-control-custom"
                                        style="width:90px;padding:.25rem .5rem">
                                </td>
                                <td>
                                    <select class="form-control-custom" style="padding:.25rem .5rem">
                                        <option>Almacén principal</option>
                                        <option>Cuarentena / revisión</option>
                                        <option>Merma / baja</option>
                                        <option>Remanufactura</option>
                                    </select>
                                </td>
                                <td>
                                    <input type="text" class="form-control-custom"
                                        style="padding:.25rem .5rem;width:100px"
                                        placeholder="Opcional">
                                </td>
                            </tr>`).join('')}
                    </tbody>
                </table>
            </div>`;
    },

    // ── Métodos internos ──────────────────────────────────────────────────────

    _selectDevTipo: function (el, val) {
        document.querySelectorAll('[id^="devTipo"]').forEach(c => c.classList.remove('selected'));
        el.classList.add('selected');
        el.querySelector('input[type=radio]').checked = true;
        DevolucionHandler._renderDevTipoDetalles(val);
    },

    _renderDevTipoDetalles: function (tipo) {
        const el = document.getElementById('devTipoDetalles');
        if (!el) return;

        if (tipo === 'nc') {
            el.innerHTML = `
                <div class="row g-3">
                    <div class="col-md-4">
                        ${formRow('Motivo de la devolución',
                `<select class="form-control-custom">
                                <option>Producto defectuoso / dañado</option>
                                <option>Error en el pedido</option>
                                <option>Insatisfacción del cliente</option>
                                <option>Devolución acordada</option>
                                <option>Garantía</option>
                                <option>Otro</option>
                            </select>`)}
                    </div>
                    <div class="col-md-4">
                        ${formRow('Importe de la devolución *',
                    `<input class="form-control-custom" type="number" id="devImporte"
                                step="0.01" min="0.01" placeholder="0.00"
                                onchange="DevolucionHandler._validateDevImporte()">`)}
                    </div>
                    <div class="col-md-4">
                        ${formRow('Tipo de relación',
                        fSelect('devTipoRel', catOpts(CAT_TIPO_RELACION, '01')),
                        '01 = Nota de crédito sobre el CFDI original')}
                    </div>
                    <div class="col-12">
                        <div id="devImporteWarn"></div>
                    </div>
                </div>`;

        } else if (tipo === 'cancel') {
            el.innerHTML = fiscalAlert('info', 'fa-ban',
                'Cancelación por operación no realizada.',
                'Motivo 03 · Sin sustitución. El CFDI se cancela ante el SAT sin emitir nuevo comprobante. ' +
                'El receptor tiene 72 horas para aceptar o rechazar la cancelación.');

        } else {
            el.innerHTML = `
                <div class="row g-3">
                    <div class="col-md-6">
                        ${formRow('Importe correcto del nuevo CFDI *',
                `<input class="form-control-custom" type="number"
                                id="sustImporte" step="0.01" min="0.01">`)}
                    </div>
                    <div class="col-md-6">
                        ${formRow('Diferencia vs original',
                    `<input class="form-control-custom" type="number"
                                id="sustDiferencia" readonly
                                style="background:var(--bg-gray)">`)}
                    </div>
                </div>`;
        }
    },

    _validateDevImporte: function () {
        const cfdi = AppState.selectedCFDI;
        const el = document.getElementById('devImporte');
        const warnEl = document.getElementById('devImporteWarn');
        if (!el || !cfdi) return;

        const v = parseFloat(el.value);
        const total = cfdi.total;

        if (v > total) {
            markError('devImporte',
                `El importe no puede superar el total del CFDI original ($${fmtMoney(total)}).`);
            if (warnEl) warnEl.innerHTML = fiscalAlert('danger', 'fa-xmark',
                'Importe inválido.', `Máximo: $${fmtMoney(total)}.`);
        } else {
            clearError('devImporte');
            if (warnEl) warnEl.innerHTML = '';
        }
    },
};

const NCRules = (() => {

    /* ──────────────────────────────────────────────────────────────
       REGLAS POR TIPO (sin cambios respecto a v1)
    ────────────────────────────────────────────────────────────── */
    const RULES = {
        devolucion:   { label:'Devolución de Mercancía',   color:'#7c3aed', requiresRef:true,  requiresPartidas:true,  allowNegativePrice:false, maxImporteFromRef:false, requiresAlmacen:true,  autoAuthLevel:'supervisor', partidaRules:{ requireClave:true,  requireDesc:true, cantMin:0.001, precioMin:0, warnIfCantExceedsRef:true  }, hints:{ partidas:'Las cantidades no deben superar las unidades originalmente facturadas.', fiscal:'El almacén de reingreso es obligatorio.' } },
        descuento:    { label:'Descuento / Bonificación',  color:'#0891b2', requiresRef:true,  requiresPartidas:true,  allowNegativePrice:false, maxImporteFromRef:true,  requiresAlmacen:false, autoAuthLevel:'supervisor', partidaRules:{ requireClave:false, requireDesc:true, cantMin:0.001, precioMin:0, warnIfCantExceedsRef:false }, hints:{ partidas:'El importe total de la NC no puede superar el importe de las facturas referenciadas.', fiscal:'Forma de aplicación recomendada: contra saldo del cliente.' } },
        precio:       { label:'Error en Precio',          color:'#d97706', requiresRef:true,  requiresPartidas:true,  allowNegativePrice:false, maxImporteFromRef:true,  requiresAlmacen:false, autoAuthLevel:'supervisor', requiresPrecioOriginal:true, requiresPrecioCorrecto:true, partidaRules:{ requireClave:false, requireDesc:true, cantMin:0.001, precioMin:0, warnIfCantExceedsRef:false }, hints:{ step1:'Ingresa el precio original y el correcto para calcular la diferencia.', partidas:'El precio unitario debe reflejar la diferencia.', fiscal:'El concepto debe especificar el error de precio.' } },
        cancelacion:  { label:'Cancelación Parcial',      color:'#dc2626', requiresRef:true,  requiresPartidas:true,  allowNegativePrice:false, maxImporteFromRef:true,  requiresAlmacen:false, autoAuthLevel:'gerencia',   partidaRules:{ requireClave:true,  requireDesc:true, cantMin:0.001, precioMin:0, warnIfCantExceedsRef:true  }, hints:{ partidas:'Solo incluye las partidas que se cancelan.', fiscal:'Nivel de autorización recomendado: Gerencia.' } },
        bonificacion: { label:'Bonificación / Rappel',    color:'#059669', requiresRef:false, requiresPartidas:true,  allowNegativePrice:false, maxImporteFromRef:false, requiresAlmacen:false, autoAuthLevel:'gerencia',   requiresBoniParams:true, partidaRules:{ requireClave:false, requireDesc:true, cantMin:0.001, precioMin:0, warnIfCantExceedsRef:false }, hints:{ partidas:'Describe el concepto del rappel.', fiscal:'Documenta el período de referencia y el % pactado.' } },
        standalone:   { label:'Sin Referencia (Libre)',   color:'#64748b', requiresRef:false, requiresPartidas:true,  allowNegativePrice:false, maxImporteFromRef:false, requiresAlmacen:false, autoAuthLevel:'direccion',  partidaRules:{ requireClave:false, requireDesc:true, cantMin:0.001, precioMin:0, warnIfCantExceedsRef:false }, hints:{ partidas:'NC independiente — documenta el concepto con detalle.', fiscal:'Sin referencia de factura. El nivel de autorización recomendado es Dirección.' } },
    };

    /* ──────────────────────────────────────────────────────────────
       ESTADO INTERNO
    ────────────────────────────────────────────────────────────── */
    let _activeType  = null;
    let _saldoInfo   = null;   // respuesta de ObtenerInfoSaldoFactura
    let _loadingSaldo = false;

    /* ──────────────────────────────────────────────────────────────
       HELPERS DOM
    ────────────────────────────────────────────────────────────── */
    function _ruleError(msg) {
        if (window.toastMixin) toastMixin.fire({ title: msg, icon: 'warning' });
        else if (window.toast) toast(msg, 'warn');
    }

    function _getRefImporteTotal() {
        return (STATE.refs || []).reduce((s, r) => s + (parseFloat(r.imp) || 0), 0);
    }

    function _getNCTotal() {
        return parseFloat(document.getElementById('nc-total')?.value || '0');
    }

    function _fmt(n) {
        const c = document.getElementById('nc-moneda')?.value || 'MXN';
        const sym = { MXN:'$', USD:'US$', EUR:'€', CAD:'CA$' };
        return (sym[c] || '$') + Number(n).toFixed(2).replace(/\B(?=(\d{3})+(?!\d))/g, ',');
    }

    /* ──────────────────────────────────────────────────────────────
       CARGAR INFO DE SALDO DESDE EL BACKEND
       Llamado desde onStepEnter(4) si hay referencias
    ────────────────────────────────────────────────────────────── */
    async function cargarInfoSaldo() {
        if (_loadingSaldo) return;
        const encId = STATE.refs?.[0]?.id;
        if (!encId) return;

        const ncTotal = _getNCTotal();
        _loadingSaldo = true;

        try {
            const url = `/VINotaCredito/ObtenerInfoSaldoFactura?encabezadoId=${encId}&ncTotal=${ncTotal}`;
            const res  = await fetch(url);
            if (!res.ok) throw new Error('HTTP ' + res.status);
            const data = await res.json();
            if (data.ok) {
                _saldoInfo = data;
                renderPanelAplicacion();
            }
        } catch (e) {
            console.warn('[NCRules] No se pudo obtener info de saldo:', e.message);
        } finally {
            _loadingSaldo = false;
        }
    }

    /* ──────────────────────────────────────────────────────────────
       RENDERIZAR EL PANEL DE APLICACIÓN EN EL PASO 4
       Reemplaza / actualiza el div #nc-panel-aplicacion
    ────────────────────────────────────────────────────────────── */
    function renderPanelAplicacion() {
        const anchor = document.getElementById('nc-aplicacion-anchor');
        if (!anchor) return;

        document.getElementById('nc-panel-aplicacion')?.remove();

        const panel = document.createElement('div');
        panel.id    = 'nc-panel-aplicacion';

        const ncTotal = _getNCTotal();
        const info    = _saldoInfo;

        /* ── Standalone ── */
        if (!STATE.refs?.length || _activeType === 'standalone') {
            panel.innerHTML = _htmlPanelSaldoFavor(
                ncTotal,
                'standalone',
                'NC sin referencia — se generará saldo a favor directo para el cliente.'
            );
            anchor.insertAdjacentElement('afterend', panel);
            _bindPanelEvents(panel);
            return;
        }

        if (!info) {
            panel.innerHTML = `<div class="section-card"><div class="section-body" style="font-size:12px;color:var(--ink-ghost);text-align:center;padding:20px;">
                <i class="fas fa-spinner fa-spin me-2"></i>Verificando estado de factura de referencia...</div></div>`;
            anchor.insertAdjacentElement('afterend', panel);
            return;
        }

        /* ── PUE / factura liquidada ── */
        if (info.scenario === 'pue_o_liquidada') {
            panel.innerHTML = _htmlPanelPUE(ncTotal, info);
            anchor.insertAdjacentElement('afterend', panel);
            _bindPanelEvents(panel);
            return;
        }

        /* ── PPD overflow ── */
        if (info.scenario === 'ppd_overflow') {
            panel.innerHTML = _htmlPanelSplit(ncTotal, info);
            anchor.insertAdjacentElement('afterend', panel);
            _bindPanelEvents(panel);
            return;
        }

        /* ── PPD normal (NC ≤ saldo pendiente) ── */
        panel.innerHTML = _htmlPanelPPDNormal(ncTotal, info);
        anchor.insertAdjacentElement('afterend', panel);
    }

    /* ── Plantillas HTML ─────────────────────────────────────────── */

    function _htmlPanelPPDNormal(ncTotal, info) {
        return `
        <div class="section-card" id="nc-panel-aplicacion-inner">
            <div class="section-head">
                <div class="section-icon ok"><i class="fas fa-check-circle"></i></div>
                <div>
                    <div class="section-title">Aplicación de la NC</div>
                    <div class="section-sub">Factura PPD — la NC reduce directamente el saldo pendiente</div>
                </div>
            </div>
            <div class="section-body">
                <div style="display:grid;grid-template-columns:1fr 1fr 1fr;gap:12px;font-size:12px;">
                    <div style="padding:10px 14px;border:1px solid var(--edge);border-radius:8px;background:var(--edge-soft);">
                        <div style="font-size:10px;color:var(--ink-ghost);text-transform:uppercase;letter-spacing:.08em;margin-bottom:4px;">Saldo actual</div>
                        <div style="font-family:var(--font-mono);font-weight:600;font-size:14px;color:var(--ink);">${_fmt(info.saldo_pendiente)}</div>
                    </div>
                    <div style="padding:10px 14px;border:1px solid var(--ok);border-radius:8px;background:var(--ok-bg);">
                        <div style="font-size:10px;color:var(--ok);text-transform:uppercase;letter-spacing:.08em;margin-bottom:4px;">Monto NC</div>
                        <div style="font-family:var(--font-mono);font-weight:700;font-size:14px;color:var(--ok);">−${_fmt(ncTotal)}</div>
                    </div>
                    <div style="padding:10px 14px;border:1px solid var(--accent-blue);border-radius:8px;background:var(--info-bg);">
                        <div style="font-size:10px;color:var(--accent-blue);text-transform:uppercase;letter-spacing:.08em;margin-bottom:4px;">Saldo nuevo</div>
                        <div style="font-family:var(--font-mono);font-weight:700;font-size:14px;color:var(--accent-blue);">${_fmt(Math.max(0, info.saldo_pendiente - ncTotal))}</div>
                    </div>
                </div>
                <input type="hidden" id="nc-decision-saldo" value="reducir_deuda"/>
                <div style="margin-top:12px;font-size:11px;color:var(--ink-ghost);">
                    <i class="fas fa-info-circle me-1"></i>
                    La NC se aplicará automáticamente contra el saldo de la factura
                    <strong>${STATE.refs?.[0]?.folio ?? ''}</strong>
                    al momento de timbrar.
                </div>
            </div>
        </div>`;
    }

    function _htmlPanelPUE(ncTotal, info) {
        return `
        <div class="section-card" id="nc-panel-aplicacion-inner">
            <div class="section-head">
                <div class="section-icon warn"><i class="fas fa-exclamation-triangle"></i></div>
                <div>
                    <div class="section-title">Factura ya liquidada — ¿qué hacemos con la NC?</div>
                    <div class="section-sub">PUE / saldo $0.00 — el cliente ya pagó esta factura</div>
                </div>
            </div>
            <div class="section-body">
                <p style="font-size:12px;color:var(--ink-soft);margin-bottom:14px;">
                    La factura de referencia ya fue cobrada en su totalidad.
                    El importe de la NC (<strong>${_fmt(ncTotal)}</strong>) no puede descontarse de ningún saldo pendiente.
                    Elige cómo manejar este crédito:
                </p>
                <div class="cxc-option selected" id="opt-saldo-favor" onclick="NCRulesUI.selDecision('saldo_favor')" style="margin-bottom:8px;">
                    <input type="radio" name="nc-decision-saldo-radio" value="saldo_favor" checked/>
                    <div>
                        <div class="cxc-option-label">Generar saldo a favor del cliente</div>
                        <div class="cxc-option-desc">El cliente acumula ${_fmt(ncTotal)} disponibles para aplicar en futuras facturas o cobros.</div>
                    </div>
                </div>
                <div class="cxc-option" id="opt-reembolso" onclick="NCRulesUI.selDecision('reembolso')" style="margin-bottom:8px;">
                    <input type="radio" name="nc-decision-saldo-radio" value="reembolso"/>
                    <div>
                        <div class="cxc-option-label">Devolver dinero al cliente (reembolso)</div>
                        <div class="cxc-option-desc">Se registra como reembolso pendiente. El área de tesorería procesa la devolución.</div>
                    </div>
                </div>
                <input type="hidden" id="nc-decision-saldo" value="saldo_favor"/>
                <div id="nc-reembolso-extra" style="display:none;margin-top:10px;padding:12px 14px;background:var(--warn-bg);border:1px solid #fde68a;border-radius:8px;">
                    <div class="field">
                        <label>Referencia de transferencia / cheque (opcional)</label>
                        <input type="text" id="nc-ref-reembolso" placeholder="SPEI-001, CHQ-A12..." style="font-size:12px;"/>
                    </div>
                </div>
            </div>
        </div>`;
    }

    function _htmlPanelSplit(ncTotal, info) {
        const saldo    = info.saldo_pendiente ?? 0;
        const deuda_s  = info.sugerido_deuda   ?? saldo;
        const cart_s   = info.sugerido_cartera ?? (ncTotal - saldo);
        return `
        <div class="section-card" id="nc-panel-aplicacion-inner">
            <div class="section-head">
                <div class="section-icon warn"><i class="fas fa-code-branch"></i></div>
                <div>
                    <div class="section-title">NC mayor al saldo pendiente — define el split</div>
                    <div class="section-sub">NC ${_fmt(ncTotal)} · Saldo pendiente ${_fmt(saldo)} · Excedente ${_fmt(ncTotal - saldo)}</div>
                </div>
            </div>
            <div class="section-body">
                <p style="font-size:12px;color:var(--ink-soft);margin-bottom:14px;">
                    La NC supera el saldo pendiente de la factura.
                    Indica cuánto va a reducir la deuda y cuánto se convierte en saldo a favor.
                    Los montos deben sumar exactamente <strong>${_fmt(ncTotal)}</strong>.
                </p>
                <div style="display:grid;grid-template-columns:1fr 1fr;gap:16px;">
                    <div class="field">
                        <label>Monto a reducir deuda <span style="font-size:10px;color:var(--ok);">(máx. ${_fmt(saldo)})</span></label>
                        <input type="number" id="nc-monto-deuda"
                            value="${deuda_s.toFixed(2)}"
                            min="0" max="${saldo}" step="0.01"
                            style="font-family:var(--font-mono);font-size:13px;"
                            oninput="NCRulesUI.syncSplit()"/>
                    </div>
                    <div class="field">
                        <label>Monto a saldo a favor del cliente</label>
                        <input type="number" id="nc-monto-cartera"
                            value="${cart_s.toFixed(2)}"
                            min="0" step="0.01"
                            style="font-family:var(--font-mono);font-size:13px;"
                            oninput="NCRulesUI.syncSplit(true)"/>
                    </div>
                </div>
                <div id="nc-split-error" style="display:none;font-size:11px;color:var(--danger-color);margin-top:8px;font-weight:600;">
                    <i class="fas fa-exclamation-circle me-1"></i>
                    Los montos no suman al total de la NC.
                </div>
                <div style="margin-top:12px;display:flex;align-items:center;gap:8px;font-size:12px;padding:10px 14px;background:var(--edge-soft);border-radius:8px;border:1px solid var(--edge);">
                    <i class="fas fa-equals" style="color:var(--ink-ghost)"></i>
                    <span style="color:var(--ink-soft);">Total verificado:</span>
                    <span id="nc-split-suma" style="font-family:var(--font-mono);font-weight:700;color:var(--ink);">${_fmt(ncTotal)}</span>
                    <span id="nc-split-ok-badge" class="badge ok" style="margin-left:auto;"><i class="fas fa-check"></i> OK</span>
                </div>
                <input type="hidden" id="nc-decision-saldo"  value="split"/>
                <input type="hidden" id="nc-hidden-deuda"    value="${deuda_s.toFixed(2)}"/>
                <input type="hidden" id="nc-hidden-cartera"  value="${cart_s.toFixed(2)}"/>
            </div>
        </div>`;
    }

    function _htmlPanelSaldoFavor(ncTotal, origen, nota) {
        return `
        <div class="section-card" id="nc-panel-aplicacion-inner">
            <div class="section-head">
                <div class="section-icon info"><i class="fas fa-wallet"></i></div>
                <div>
                    <div class="section-title">Saldo a favor</div>
                    <div class="section-sub">${nota}</div>
                </div>
            </div>
            <div class="section-body">
                <div style="font-family:var(--font-mono);font-size:24px;font-weight:700;color:var(--accent-blue);margin-bottom:8px;">${_fmt(ncTotal)}</div>
                <p style="font-size:12px;color:var(--ink-soft);">
                    El importe íntegro de la NC se abonará a la cartera del cliente como saldo disponible.
                </p>
                <input type="hidden" id="nc-decision-saldo" value="saldo_favor"/>
            </div>
        </div>`;
    }

    /* ── Bind de eventos en el panel recién renderizado ─────────── */
    function _bindPanelEvents(panel) {
        // Los botones del panel PUE usan NCRulesUI.selDecision()
        // Los inputs del split usan NCRulesUI.syncSplit()
        // Ambos están expuestos en el objeto NCRulesUI más abajo
    }

    /* ──────────────────────────────────────────────────────────────
       LEER DECISIÓN DEL USUARIO (para FormData en guardarNC)
    ────────────────────────────────────────────────────────────── */
    function leerDecisionAplicacion() {
        return {
            decisionSaldo : document.getElementById('nc-decision-saldo')?.value   || 'saldo_favor',
            montoADeuda   : document.getElementById('nc-hidden-deuda')?.value     || '0',
            montoACartera : document.getElementById('nc-hidden-cartera')?.value   || '0',
        };
    }

    /* ──────────────────────────────────────────────────────────────
       VALIDAR PASO 4 — incluye validación del split
    ────────────────────────────────────────────────────────────── */
    function validateStepRules(stepNum) {
        const rule = RULES[_activeType];
        if (!rule) return true;
        let valid = true;
        const errors = [];

        if (stepNum === 1) {
            // Validaciones de tipo precio y bonificación (step 1 del wizard)
            if (rule.requiresPrecioOriginal) {
                const orig = parseFloat(document.getElementById('nc-precio-original')?.value || '0');
                const corr = parseFloat(document.getElementById('nc-precio-correcto')?.value || '0');
                if (orig <= 0) { errors.push('Precio original debe ser mayor a 0'); valid = false; }
                if (corr <= 0) { errors.push('Precio correcto debe ser mayor a 0'); valid = false; }
                if (orig > 0 && corr > 0 && orig <= corr) {
                    errors.push('El precio original debe ser mayor al precio correcto'); valid = false;
                }
            }
            if (rule.requiresBoniParams) {
                const tipoBoni = document.getElementById('nc-tipo-boni')?.value;
                const pctBoni = parseFloat(document.getElementById('nc-pct-boni')?.value || '0');
                if (!tipoBoni) { errors.push('Selecciona el tipo de bonificación'); valid = false; }
                if (pctBoni <= 0) { errors.push('El % de bonificación debe ser mayor a 0'); valid = false; }
            }
        }

        if (stepNum === 2) {
            // Validación de partidas
            if (STATE.ncType === 'standalone') {
                const montoInput = document.getElementById('standalone-monto-total');
                const monto = parseFloat(montoInput?.value) || 0;
                if (monto <= 0) {
                    if (montoInput) montoInput.style.borderColor = 'var(--danger-color)';
                    errors.push('Ingresa el monto de la NC libre');
                    valid = false;
                } else {
                    if (montoInput) montoInput.style.borderColor = '#bfdbfe';
                    window.actualizarMontoStandalone(montoInput.value);
                }
            } else {
                pmSync();
                if (!STATE.partidas.length) {
                    errors.push('Agrega al menos una partida');
                    valid = false;
                } else {
                    const rowErr = STATE.partidas.some(p => !p.desc || p.cant <= 0 || p.precio < 0);
                    if (rowErr) {
                        errors.push('Revisa descripción, cantidad y precio de todas las partidas');
                        valid = false;
                    }
                }
            }
        }

        if (stepNum === 3) {
            // Validación de condiciones fiscales
            if (rule.requiresAlmacen && !document.getElementById('nc-almacen')?.value) {
                errors.push('El almacén de reingreso es obligatorio para Devoluciones');
                valid = false;
            }
            const decision = document.getElementById('nc-decision-saldo')?.value;
            if (decision === 'split') {
                const deuda = parseFloat(document.getElementById('nc-monto-deuda')?.value || '0');
                const cartera = parseFloat(document.getElementById('nc-monto-cartera')?.value || '0');
                const ncTotal = _getNCTotal();
                if (Math.abs((deuda + cartera) - ncTotal) > 0.01) {
                    errors.push(`El split debe sumar exactamente ${_fmt(ncTotal)}`);
                    valid = false;
                }
                if (deuda < 0 || cartera < 0) {
                    errors.push('Los montos del split no pueden ser negativos');
                    valid = false;
                }
            }
        }

        if (errors.length) {
            _ruleError(errors.length === 1 ? errors[0] : `${errors.length} errores: ${errors.join(' · ')}`);
        }
        return valid;
    }

    function validateAll() {
        const errs = [];
        if (!STATE.ncType) errs.push('Selecciona el tipo de nota de crédito (Paso 1)');
        if (!getVal('nc-cliente')) errs.push('Ingresa el cliente (Paso 2)');
        if (!getVal('nc-rfc')) errs.push('Ingresa el RFC del cliente (Paso 2)');
        if (!STATE.refs.length && STATE.ncType !== 'standalone')
            errs.push('Agrega al menos una factura de referencia (Paso 2)');
        if (!getVal('nc-concepto')) errs.push('Ingresa el concepto / motivo de la NC (Paso 2)');

        // ── Standalone: verificar monto en lugar de partidas ──
        if (STATE.ncType === 'standalone') {
            const monto = parseFloat(document.getElementById('standalone-monto-total')?.value) || 0;
            if (monto <= 0) errs.push('Ingresa el monto de la NC libre (Paso 3)');
        } else {
            if (!STATE.partidas.length) errs.push('Agrega al menos una partida (Paso 3)');
        }

        if (!getVal('nc-cp')) errs.push('Ingresa el código postal fiscal (Paso 4)');
        return errs;
    }

    /* ──────────────────────────────────────────────────────────────
       EVENTOS DE CICLO DE VIDA
    ────────────────────────────────────────────────────────────── */
    function onTypeSelected(type) {
        _activeType  = type;
        _saldoInfo   = null;  // resetear al cambiar tipo
        const rule   = RULES[type];
        if (!rule) return;
        _injectStyles();
        _renderHintBar(1);
        _applyPartidaTableRules();

        const placeholders = {
            devolucion:   'Devolución de mercancía — orden de retorno #...',
            descuento:    'Descuento comercial aplicado sobre factura #...',
            precio:       'Corrección de precio — diferencial entre precio facturado y pactado',
            cancelacion:  'Cancelación parcial de partidas de factura #...',
            bonificacion: 'Bonificación por rappel / volumen — período ...',
            standalone:   'Ajuste / crédito a favor del cliente — concepto libre',
        };
        const concepto = document.getElementById('nc-concepto');
        if (concepto && placeholders[type]) concepto.placeholder = placeholders[type];
        const stxRef = document.getElementById('ctx-standalone-ref');
        if (stxRef) {
            stxRef.classList.toggle('visible', type === 'standalone');
            if (type === 'standalone') {
                stxRef.innerHTML = `
        <div style="margin-top:12px; padding:12px 14px;
                    background:#eff6ff; border-radius:8px;
                    border:1px solid #bfdbfe; font-size:12px;">
            <i class="fas fa-wallet me-2" style="color:#3b82f6"></i>
            Esta NC generará un <strong>saldo a favor</strong> disponible
            en la cartera del cliente. Podrás aplicarlo contra facturas
            existentes o futuras desde el módulo de <strong>CxC</strong>.
        </div>`;
            }
        }

        if (type === 'standalone') {
            _inyectarPartidaVirtualStandalone();
        }


    }

    function _inyectarPartidaVirtualStandalone() {
        const aviso = document.getElementById('standalone-partida-aviso');
        if (aviso) return;

        const toolbar = document.querySelector('#panel-3 .partidas-toolbar');
        if (!toolbar) return;

        const div = document.createElement('div');
        div.id = 'standalone-partida-aviso';
        div.style.cssText = `
        margin: 12px 16px; padding: 14px 16px;
        background: #eff6ff; border: 1.5px solid #bfdbfe;
        border-radius: 8px; font-size: 12px; color: #1e40af;`;
        div.innerHTML = `
        <div style="font-weight:700; margin-bottom:6px;">
            <i class="fas fa-info-circle me-2"></i>
            NC Libre — concepto único generado automáticamente
        </div>
        <div style="color:#3b82f6; font-size:11px; margin-bottom:10px;">
            El SAT permite un concepto genérico <strong>84111506 / ACT</strong>
            para NCs sin referencia, igual que los anticipos.<br>
            El concepto que escribiste en el Paso 2 se usará como descripción.
        </div>
        <div style="display:flex; gap:8px; align-items:center; flex-wrap:wrap;">
            <span style="font-size:11px; color:#64748b; font-weight:600;">Monto base (sin IVA):</span>
            <input type="number" id="standalone-monto-total"
                style="width:150px; font-family:monospace; font-weight:700;
                       font-size:14px; padding:6px 10px; border:1.5px solid #bfdbfe;
                       border-radius:6px; color:#1e40af; outline:none;"
                placeholder="0.00" step="0.01" min="0"
                oninput="window.actualizarMontoStandalone(this.value)"
            />
            <span style="font-size:11px; color:#64748b;">+ IVA 16%</span>
            <span id="standalone-total-display"
                style="font-family:monospace; font-weight:700; font-size:14px;
                       color:#059669; padding:4px 10px; background:#f0fdf4;
                       border:1px solid #a7f3d0; border-radius:6px; display:none;">
            </span>
        </div>`;

        toolbar.insertAdjacentElement('afterend', div);
        document.querySelector('.partidas-wrap').style.display = 'none';
    }

    function actualizarMontoStandalone(val) {
        const subtotal = parseFloat(val) || 0;
        const iva = subtotal * 0.16;
        const total = subtotal + iva;

        // Inyectar una partida virtual en STATE para que recalc() y _syncPartidasHidden() funcionen
        STATE.partidas = [{
            _id: 1,
            clave: '',
            desc: document.getElementById('nc-concepto')?.value || 'Nota de crédito libre',
            cant: 1,
            precio: subtotal,   // precio SIN IVA — GenerarXml lo calcula
            descP: 0,
            ivaP: 16,
            iepsP: 0
        }];

        // Actualizar hiddens
        document.getElementById('nc-subtotal').value = subtotal.toFixed(2);
        document.getElementById('nc-iva').value = iva.toFixed(2);
        document.getElementById('nc-total').value = total.toFixed(2);
        document.getElementById('nc-descuentos').value = '0';
        document.getElementById('nc-base').value = subtotal.toFixed(2);

        // Sidebar
        setTxt('side-subtotal', fmt(subtotal));
        setTxt('side-iva', fmt(iva));
        setTxt('side-total', fmt(total));
        setTxt('side-base', fmt(subtotal));

        _syncPartidasHidden();
    }

    function onRecalc() {
        if (!_activeType) return;
        _renderImporteBar();
        _applyPartidaTableRules();
        // Si el panel de split ya está visible, recalcular el split sugerido
        if (_saldoInfo?.scenario === 'ppd_overflow') {
            const ncTotal  = _getNCTotal();
            const saldo    = _saldoInfo.saldo_pendiente ?? 0;
            const deudaIn  = document.getElementById('nc-monto-deuda');
            const cartaIn  = document.getElementById('nc-monto-cartera');
            if (deudaIn && cartaIn) {
                const deuda   = Math.min(saldo, ncTotal);
                const cartera = Math.max(0, ncTotal - deuda);
                deudaIn.value = deuda.toFixed(2);
                cartaIn.value = cartera.toFixed(2);
                NCRulesUI.syncSplit();
            }
        }
    }

    function onStepEnter(stepNum) {
        if (!_activeType) return;
        _renderHintBar(stepNum);
        if (stepNum === 2) { _applyPartidaTableRules(); _renderImporteBar(); } // paso 2 = partidas ✓
        if (stepNum === 3) {  // paso 3 = fiscal ✓
            _applySuggestedAuthLevel();
            if (STATE.refs?.length > 0 || _activeType === 'standalone') {
                cargarInfoSaldo().catch(() => {
                    if (_activeType === 'standalone') renderPanelAplicacion();
                });
            } else {
                renderPanelAplicacion();
            }
        }
    }
    /* ──────────────────────────────────────────────────────────────
       INTERNOS — igual que v1
    ────────────────────────────────────────────────────────────── */
    function _renderHintBar(stepNum) {
        const rule = RULES[_activeType];
        if (!rule) return;
        const hintKey = { 1: 'step1', 2: 'partidas', 3: 'fiscal' }[stepNum]; // antes era 1,2,3,4
        const hintText = rule.hints?.[hintKey];
        document.querySelectorAll('.nc-rule-hint').forEach(el => el.remove());
        if (!hintText) return;
        const panel = document.getElementById(`panel-${stepNum}`);
        if (!panel) return;
        const bar = document.createElement('div');
        bar.className = 'nc-rule-hint';
        bar.innerHTML = `<div class="nc-rule-hint-inner" style="--rule-color:${rule.color}">
            <span class="nc-rule-hint-icon"><i class="fas fa-lightbulb"></i></span>
            <span class="nc-rule-hint-text"><strong>${rule.label}:</strong> ${hintText}</span>
        </div>`;
        panel.insertBefore(bar, panel.firstChild);
    }

    function _renderImporteBar() {
        const rule = RULES[_activeType];
        document.getElementById('nc-importe-limit-bar')?.remove();
        if (!rule?.maxImporteFromRef || !STATE.refs?.length) return;
        const refTotal = _getRefImporteTotal();
        const ncTotal  = _getNCTotal();
        const pct      = refTotal > 0 ? Math.min((ncTotal / refTotal) * 100, 100) : 0;
        const pctReal  = refTotal > 0 ? (ncTotal / refTotal) * 100 : 0;
        const over     = pctReal > 100;
        const fillClass = pct < 80 ? 'ok' : pct < 100 ? 'warn' : 'over';
        const bar = document.createElement('div');
        bar.id = 'nc-importe-limit-bar';
        bar.className = 'nc-importe-limit-bar';
        bar.innerHTML = `<div class="lbl"><span>Límite disponible (facturas referenciadas)</span>
            <span class="val">${_fmt(ncTotal)} / ${_fmt(refTotal)}</span></div>
            <div class="nc-importe-progress-track">
                <div class="nc-importe-progress-fill ${fillClass}" style="width:${pct}%"></div>
            </div>
            <div class="nc-importe-over-msg ${over ? 'visible' : ''}">
                <i class="fas fa-exclamation-triangle me-1"></i>
                El importe de la NC supera el total de las facturas referenciadas.
            </div>`;
        const toolbar = document.querySelector('#panel-3 .partidas-toolbar');
        toolbar?.parentElement?.appendChild(bar);
    }

    function _applyPartidaTableRules() {
        const rule = RULES[_activeType];
        if (!rule) return;
        const pr = rule.partidaRules;
        const thClave = document.querySelector('.partidas-tbl thead th:nth-child(2)');
        if (thClave) {
            thClave.querySelectorAll('.nc-partida-rule-badge').forEach(b => b.remove());
            const b = document.createElement('span');
            b.className = 'nc-partida-rule-badge ' + (pr.requireClave ? 'req-sku' : 'no-inv');
            b.innerHTML = pr.requireClave ? '<i class="fas fa-asterisk"></i> obligatorio' : '<i class="fas fa-info"></i> opcional';
            thClave.appendChild(b);
        }
    }

    function _applySuggestedAuthLevel() {
        const rule = RULES[_activeType];
        if (!rule?.autoAuthLevel) return;
        const sel = document.getElementById('nc-auth-level');
        if (!sel || (sel.value && sel.value !== 'directo')) return;
        sel.value = rule.autoAuthLevel;
        if (window.updateApprovalTrack) updateApprovalTrack();
        document.querySelectorAll('.nc-auth-suggestion').forEach(el => el.remove());
        const lvlLabels = { directo:'Ninguno', supervisor:'Supervisor de ventas', gerencia:'Gerencia comercial', direccion:'Dirección' };
        const msg = document.createElement('div');
        msg.className = `nc-auth-suggestion ${rule.autoAuthLevel === 'direccion' ? 'warn' : 'info'}`;
        msg.innerHTML = `<i class="fas fa-shield-alt" style="margin-top:2px;flex-shrink:0"></i>
            <span>Nivel sugerido para <strong>${rule.label}</strong>: <strong>${lvlLabels[rule.autoAuthLevel]}</strong>.</span>`;
        document.getElementById('nc-auth-level')?.closest('.field')?.parentElement
            ?.insertBefore(msg, document.getElementById('nc-auth-level').closest('.field').parentElement.firstChild);
    }

    function _warnCantExceedsRef() {
        const refMap = {};
        STATE.refs.forEach(r => {
            (r.productos || []).forEach(p => {
                const k = (p.clave || p.id || '').toUpperCase();
                if (k) refMap[k] = (refMap[k] || 0) + parseFloat(p.cant || p.cantidad || 0);
            });
        });
        if (!Object.keys(refMap).length) return;
        STATE.partidas.forEach(p => {
            const k = (p.clave || '').toUpperCase();
            if (!k) return;
            const refCant = refMap[k] || 0;
            if (refCant > 0 && p.cant > refCant)
                toast?.(`Partida "${p.clave}": cantidad (${p.cant}) supera la facturada (${refCant})`, 'warn');
        });
    }

    function _injectStyles() {
        if (document.getElementById('nc-rules-styles')) return;
        const style = document.createElement('style');
        style.id = 'nc-rules-styles';
        style.textContent = `
.nc-rule-hint { margin-bottom:14px; animation:fadeIn .25s ease; }
.nc-rule-hint-inner { display:flex; align-items:flex-start; gap:10px; padding:11px 15px; border-radius:8px; background:color-mix(in srgb, var(--rule-color,#1e3a5f) 8%, #fff); border:1.5px solid color-mix(in srgb, var(--rule-color,#1e3a5f) 30%, transparent); font-size:12px; line-height:1.5; color:var(--ink,#0f172a); }
.nc-rule-hint-icon { color:var(--rule-color,#1e3a5f); font-size:14px; flex-shrink:0; margin-top:1px; }
.nc-rule-hint-text strong { color:var(--rule-color,#1e3a5f); }
.nc-importe-limit-bar { margin-top:10px; padding:10px 14px; border-radius:8px; background:#fff; border:1.5px solid var(--edge,#e2e8f0); font-size:11px; }
.nc-importe-limit-bar .lbl { display:flex; justify-content:space-between; color:var(--ink-soft); margin-bottom:6px; font-weight:600; text-transform:uppercase; letter-spacing:.04em; }
.nc-importe-progress-track { height:6px; border-radius:3px; background:var(--edge); overflow:hidden; }
.nc-importe-progress-fill { height:100%; border-radius:3px; transition:width .4s ease, background .3s; }
.nc-importe-progress-fill.ok { background:var(--ok); }
.nc-importe-progress-fill.warn { background:var(--warn); }
.nc-importe-progress-fill.over { background:var(--danger-color); }
.nc-importe-over-msg { margin-top:5px; color:var(--danger-color); font-weight:600; font-size:11px; display:none; }
.nc-importe-over-msg.visible { display:block; }
.nc-partida-rule-badge { display:inline-flex; align-items:center; gap:4px; font-size:9px; padding:2px 6px; border-radius:20px; font-weight:700; letter-spacing:.04em; text-transform:uppercase; margin-left:6px; }
.nc-partida-rule-badge.no-inv { background:#f0f9ff; color:#0891b2; border:1px solid #bae6fd; }
.nc-partida-rule-badge.req-sku { background:#faf5ff; color:#7c3aed; border:1px solid #e9d5ff; }
.nc-auth-suggestion { padding:10px 14px; border-radius:8px; font-size:12px; display:flex; align-items:flex-start; gap:8px; margin-bottom:10px; animation:fadeIn .3s ease; }
.nc-auth-suggestion.info { background:#eff6ff; border:1.5px solid #bfdbfe; color:#1e40af; }
.nc-auth-suggestion.warn { background:#fffbeb; border:1.5px solid #fde68a; color:#92400e; }`;
        document.head.appendChild(style);
    }

    /* ──────────────────────────────────────────────────────────────
       API PÚBLICA
    ────────────────────────────────────────────────────────────── */
    return {
        RULES,
        onTypeSelected,
        onRecalc,
        onStepEnter,
        validateStepRules,
        validateAll,
        cargarInfoSaldo,
        renderPanelAplicacion,
        leerDecisionAplicacion,
    };

})();

/* ══════════════════════════════════════════════════════════════════
   NCRulesUI — handlers de interacción del panel de aplicación
   Expuestos globalmente para los onclick del HTML generado
══════════════════════════════════════════════════════════════════ */
const NCRulesUI = {
    selDecision(valor) {
        document.getElementById('nc-decision-saldo').value = valor;
        document.querySelectorAll('#nc-panel-aplicacion .cxc-option').forEach(o => {
            o.classList.toggle('selected', o.id === `opt-${valor}`);
            const radio = o.querySelector('input[type=radio]');
            if (radio) radio.checked = (o.id === `opt-${valor}`);
        });
        const extra = document.getElementById('nc-reembolso-extra');
        if (extra) extra.style.display = valor === 'reembolso' ? '' : 'none';
    },

    syncSplit(fromCartera = false) {
        const ncTotal   = parseFloat(document.getElementById('nc-total')?.value || '0');
        const deudaIn   = document.getElementById('nc-monto-deuda');
        const cartaIn   = document.getElementById('nc-monto-cartera');
        const sumaEl    = document.getElementById('nc-split-suma');
        const errEl     = document.getElementById('nc-split-error');
        const badgeEl   = document.getElementById('nc-split-ok-badge');
        if (!deudaIn || !cartaIn) return;

        const maxDeuda = parseFloat(deudaIn.max || ncTotal);

        if (fromCartera) {
            // recalcular deuda desde cartera
            const cartera = Math.max(0, parseFloat(cartaIn.value) || 0);
            const deuda   = Math.max(0, Math.min(maxDeuda, ncTotal - cartera));
            deudaIn.value = deuda.toFixed(2);
        } else {
            // recalcular cartera desde deuda
            const deuda   = Math.max(0, Math.min(maxDeuda, parseFloat(deudaIn.value) || 0));
            const cartera = Math.max(0, ncTotal - deuda);
            cartaIn.value = cartera.toFixed(2);
        }

        const deuda   = parseFloat(deudaIn.value)  || 0;
        const cartera = parseFloat(cartaIn.value) || 0;
        const suma    = deuda + cartera;
        const ok      = Math.abs(suma - ncTotal) < 0.01;

        if (sumaEl) {
            const sym = { MXN:'$', USD:'US$', EUR:'€', CAD:'CA$' };
            const c   = document.getElementById('nc-moneda')?.value || 'MXN';
            sumaEl.textContent = (sym[c] || '$') + suma.toFixed(2).replace(/\B(?=(\d{3})+(?!\d))/g, ',');
        }
        if (errEl)   errEl.style.display   = ok ? 'none' : '';
        if (badgeEl) badgeEl.style.display  = ok ? '' : 'none';

        // Sincronizar hiddens
        const hd = document.getElementById('nc-hidden-deuda');
        const hc = document.getElementById('nc-hidden-cartera');
        if (hd) hd.value = deuda.toFixed(2);
        if (hc) hc.value = cartera.toFixed(2);
    }
};

// ══════════════════════════════════════════════════════
//  FUNCIÓN GLOBAL — actualizar monto standalone
//  Debe estar fuera del IIFE para ser accesible desde oninput
// ══════════════════════════════════════════════════════
window.actualizarMontoStandalone = function (val) {
    const subtotal = parseFloat(val) || 0;
    const iva = Math.round(subtotal * 0.16 * 100) / 100;
    const total = Math.round((subtotal + iva) * 100) / 100;

    // Mostrar total calculado junto al input
    const display = document.getElementById('standalone-total-display');
    if (display) {
        if (subtotal > 0) {
            display.textContent = '= ' + fmt(total);
            display.style.display = '';
        } else {
            display.style.display = 'none';
        }
    }

    // Inyectar partida virtual en STATE — necesaria para que
    // validateStep(3), _syncPartidasHidden() y recalc() no fallen
    const concepto = document.getElementById('nc-concepto')?.value?.trim()
        || 'Nota de crédito — ajuste a favor del cliente';

    STATE.partidas = [{
        _id: 1,
        clave: '',
        desc: concepto,
        cant: 1,
        precio: subtotal,  // precio SIN IVA
        descP: 0,
        ivaP: 16,
        iepsP: 0
    }];

    // Actualizar badge de partidas
    const badge = document.getElementById('partidas-count-badge');
    if (badge) badge.textContent = subtotal > 0 ? '1' : '0';

    // Actualizar hiddens de totales
    document.getElementById('nc-subtotal').value = subtotal.toFixed(2);
    document.getElementById('nc-descuentos').value = '0';
    document.getElementById('nc-base').value = subtotal.toFixed(2);
    document.getElementById('nc-iva').value = iva.toFixed(2);
    document.getElementById('nc-ieps').value = '0';
    document.getElementById('nc-total').value = total.toFixed(2);

    // Sidebar
    setTxt('side-subtotal', fmt(subtotal));
    setTxt('side-descuentos', '−' + fmt(0));
    setTxt('side-base', fmt(subtotal));
    setTxt('side-iva', fmt(iva));
    setTxt('side-total', fmt(total));

    const ccy = getVal('nc-moneda', 'MXN');
    const par = getVal('nc-paridad', '1');
    setTxt('side-moneda-note', `${ccy} · Paridad ${parseFloat(par).toFixed(4)}`);

    // Sincronizar hidden de partidas al backend
    _syncPartidasHidden();

    // Actualizar folio en topbar
    const f = getVal('nc-folio');
    if (f) setTxt('tb-folio', f);
};
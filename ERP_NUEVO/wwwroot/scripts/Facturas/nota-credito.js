let tomManager;
tomManager = new TomSelectManager();

/* ── Metadatos de pasos ──────────────────────────────────────── */
const SB2_STEPS = [
    { name: 'Tipo y origen', sub: 'Selecciona el tipo de NC' },
    { name: 'Cliente y referencia', sub: 'Datos del receptor' },
    { name: 'Partidas', sub: 'Artículos de la NC' },
    { name: 'Condiciones fiscales', sub: 'Configuración CFDI 4.0' },
    { name: 'Revisión y emisión', sub: 'Verificación final' },
];

/* ── Actualiza el strip de pasos ─────────────────────────────── */
function sb2UpdateSteps(currentStep) {
    document.querySelectorAll('.sb2-stp').forEach((el, i) => {
        const n = i + 1;
        el.classList.remove('done', 'active', 'pending');
        if (n < currentStep) el.classList.add('done');
        else if (n === currentStep) el.classList.add('active');
        else el.classList.add('pending');
    });

    const step = SB2_STEPS[(currentStep - 1)] || SB2_STEPS[0];
    const numEl = document.getElementById('sb2-paso-num');
    const nameEl = document.getElementById('sb2-paso-name');
    const subEl = document.getElementById('sb2-paso-sub');

    if (numEl) numEl.textContent = currentStep;
    if (nameEl) nameEl.textContent = step.name;
    if (subEl) subEl.textContent = step.sub;
}

/* ── Actualiza swatch y nombre del tipo ──────────────────────── */
function sb2UpdateType(type) {
    if (!type || !TYPE_META[type]) return;
    const meta = TYPE_META[type];
    const swatch = document.getElementById('side-type-dot');
    const nameEl = document.getElementById('side-type-name');
    if (swatch) swatch.style.background = meta.color;
    if (nameEl) nameEl.textContent = meta.label;
}

/* ── Actualiza pills de facturas referenciadas ───────────────── */
function sb2UpdateRefs() {
    const container = document.getElementById('side-refs-pills');
    if (!container) return;

    if (!STATE.refs || !STATE.refs.length) {
        // Recrea el span con id="side-refs" para que updateSideRefs lo encuentre
        container.innerHTML =
            '<span class="sb2-no-refs" id="side-refs" style="color:var(--ink-soft);">0 documentos</span>';
        sb2UpdateBar();
        return;
    }

    // Renderiza las pills Y al final un span oculto con id="side-refs"
    // para mantener compatibilidad con updateSideRefs() de nota-credito.js
    container.innerHTML = STATE.refs.map(r =>
        `<span class="sb2-ref-pill">
            <svg width="9" height="9" viewBox="0 0 9 9" fill="none">
                <rect x=".5" y=".5" width="8" height="8" rx="1.5"
                      stroke="currentColor" stroke-width="1"/>
            </svg>
            ${esc(r.folio)}
        </span>`
    ).join('') +
        // span invisible que actúa como ancla para updateSideRefs()
        `<span id="side-refs" style="display:none;">${STATE.refs.map(r => r.folio).join(', ')
        }</span>`;

    sb2UpdateBar();
}

/* ── Barra de límite vs referencias ─────────────────────────── */
function sb2UpdateBar() {
    const wrap = document.getElementById('sb2-bar-wrap');
    const fill = document.getElementById('sb2-bar-fill');
    const pctEl = document.getElementById('sb2-bar-pct');
    if (!wrap || !fill || !pctEl) return;

    const rule = (typeof NCRules !== 'undefined') ? NCRules.RULES[STATE.ncType] : null;

    if (!STATE.refs || !STATE.refs.length || !rule || !rule.maxImporteFromRef) {
        wrap.style.display = 'none';
        return;
    }

    const refTotal = STATE.refs.reduce((s, r) => s + (parseFloat(r.imp) || 0), 0);
    const ncTotal = parseFloat(document.getElementById('nc-total')?.value || '0');

    if (refTotal <= 0) { wrap.style.display = 'none'; return; }

    wrap.style.display = '';
    const pctReal = (ncTotal / refTotal) * 100;
    const pctCap = Math.min(pctReal, 100);

    pctEl.textContent = pctReal.toFixed(0) + '%';
    fill.style.width = pctCap + '%';
    fill.classList.remove('warn', 'over');
    if (pctReal >= 100) fill.classList.add('over');
    else if (pctReal >= 80) fill.classList.add('warn');
}

/* ── Badge de estado (BORRADOR / TIMBRADA) ───────────────────── */
function sb2SetEstado(estado) {
    const badge = document.getElementById('sb2-estado');
    if (!badge) return;
    badge.textContent = estado;
    badge.classList.toggle('timbrada', estado === 'TIMBRADA');
}

/* ── Sincroniza el folio en el sidebar ───────────────────────── */
function sb2SyncFolio() {
    const folio = document.getElementById('nc-folio')?.value?.trim() || '—';
    const el = document.getElementById('sb2-folio-lbl');
    if (el) el.textContent = folio;
}

/* ═══════════════════════════════════════════════════════════════
   MONKEY-PATCHING — envuelve las funciones existentes de
   nota-credito.js sin modificarlas, para alimentar el sidebar
═══════════════════════════════════════════════════════════════ */

/* gotoStep → actualizar strip de pasos */
(function () {
    const _orig = window.gotoStep;
    window.gotoStep = function (n) {
        _orig(n);
        sb2UpdateSteps(n);
        sb2UpdateRefs();
        sb2UpdateBar();
    };
})();

/* selectType → actualizar swatch de color */
(function () {
    const _orig = window.selectType;
    window.selectType = function (card) {
        _orig(card);
        sb2UpdateType(STATE.ncType);
        sb2UpdateBar();
    };
})();

/* recalc → actualizar barra de límite */
(function () {
    const _orig = window.recalc;
    window.recalc = function () {
        _orig();
        sb2UpdateBar();
    };
})();

/* renderRefs → actualizar pills de referencias */
(function () {
    const _orig = window.renderRefs;
    window.renderRefs = function () {
        _orig();
        sb2UpdateRefs();
    };
})();

/* updateSideRefs → también actualizar el nombre del cliente en sb2 */
(function () {
    const _orig = window.updateSideRefs;
    window.updateSideRefs = function () {
        _orig();
        sb2UpdateRefs();
    };
})();

/* ═══════════════════════════════════════════════════════════════
   INICIALIZACIÓN
═══════════════════════════════════════════════════════════════ */
document.addEventListener('DOMContentLoaded', function () {

    /* Paso inicial */
    sb2UpdateSteps(STATE.currentStep || 1);

    /* Sincronizar folio en tiempo real */
    const folioInput = document.getElementById('nc-folio');
    if (folioInput) {
        folioInput.addEventListener('input', sb2SyncFolio);
        sb2SyncFolio();
    }

    /* Sincronizar nombre del cliente en tiempo real
       El listener original de nota-credito.js usa setTxt('side-cliente', ...)
       lo cual actualiza el elemento sb2-cli-name porque tiene id="side-cliente" */

    /* Cuando el resultado del timbrado sea exitoso, marcar como TIMBRADA.
       mostrarResumenExitoso() se llama desde guardarNC() — lo envolvemos. */
    const _origMostrar = window.mostrarResumenExitoso;
    if (typeof _origMostrar === 'function') {
        window.mostrarResumenExitoso = function (r, cantDocs) {
            _origMostrar(r, cantDocs);
            sb2SetEstado('TIMBRADA');
        };
    }
});

/* ══════════════════════════════════════════
   ESTADO GLOBAL
══════════════════════════════════════════ */
const STATE = {
    currentStep: 1,
    ncType: null,
    refs: [],       // [{id, folio, cliente, rfc, fecha, imp, vdr, ccy, productos[]}]
    partidas: [],
    pidCounter: 0,
    clienteId: null,  // FIX: id numérico interno del cliente → nc.IdCliente en backend
};

/* ══════════════════════════════════════════
   UTILIDADES
══════════════════════════════════════════ */
function esc(t) {
    const d = document.createElement('div');
    d.textContent = String(t ?? '');
    return d.innerHTML;
}

function fmt(n, ccy) {
    const sym = { MXN: '$', USD: 'US$', EUR: '€', CAD: 'CA$' };
    const c = ccy || document.getElementById('nc-moneda')?.value || 'MXN';
    return (sym[c] || '$') + Number(n).toFixed(2).replace(/\B(?=(\d{3})+(?!\d))/g, ',');
}

function toast(msg, type = 'info') {
    const c = document.getElementById('toast-container');
    const t = document.createElement('div');
    t.className = `toast-msg ${type}`;
    t.innerHTML = `<i class="fas fa-${type === 'ok' ? 'check' : type === 'err' ? 'times' : 'info'}-circle me-2"></i>${esc(msg)}`;
    c.appendChild(t);
    requestAnimationFrame(() => t.classList.add('show'));
    setTimeout(() => {
        t.classList.remove('show');
        setTimeout(() => t.remove(), 300);
    }, 3500);
}

function getVal(id, def = '') { return document.getElementById(id)?.value?.trim() || def; }
function getTxt(id, def = '') {
    const e = document.getElementById(id);
    return e?.options?.[e.selectedIndex]?.text?.trim() || def;
}
function setTxt(id, v) {
    const e = document.getElementById(id);
    if (e) e.textContent = v;
}

/* ══════════════════════════════════════════
   VALIDACIÓN POR PASOS
══════════════════════════════════════════ */
function fieldError(id, msg) {
    const el = document.getElementById(id);
    if (!el) return;
    el.classList.remove('field-error');
    const prev = el.parentElement.querySelector('.field-error-msg');
    if (prev) prev.remove();
    if (msg) {
        el.classList.add('field-error');
        const span = document.createElement('span');
        span.className = 'field-error-msg';
        span.textContent = msg;
        el.parentElement.appendChild(span);
    }
}

function clearStepErrors(stepNum) {
    const panel = document.getElementById(`panel-${stepNum}`);
    if (!panel) return;
    panel.querySelectorAll('.field-error').forEach(el => el.classList.remove('field-error'));
    panel.querySelectorAll('.field-error-msg').forEach(el => el.remove());
    document.getElementById('nc-type-grid')?.classList.remove('field-error');
    panel.querySelectorAll('.ts-wrapper').forEach(w => w.classList.remove('field-error'));
}

function validateStep(stepNum) {
    clearStepErrors(stepNum);
    let valid = true;
    const errores = [];

    if (stepNum === 1) {
        if (!STATE.fromSolicitud && !STATE.ncType) {
            const grid = document.getElementById('nc-type-grid');
            if (grid) {
                grid.classList.add('field-error');
                const prev = grid.parentElement?.querySelector('.field-error-msg');
                if (prev) prev.remove();
                const sp = document.createElement('span');
                sp.className = 'field-error-msg';
                sp.textContent = 'Selecciona el tipo de nota de crédito';
                grid.insertAdjacentElement('afterend', sp);
            }
            errores.push('Tipo de NC requerido');
            valid = false;
        }
        if (!getVal('nc-folio')) {
            fieldError('nc-folio', 'El folio es obligatorio');
            errores.push('Folio requerido');
            valid = false;
        }
        if (!getVal('nc-fecha-emision')) {
            fieldError('nc-fecha-emision', 'La fecha de emisión es obligatoria');
            errores.push('Fecha de emisión requerida');
            valid = false;
        }
        if (!getVal('nc-cliente')) {
            fieldError('nc-cliente', 'El nombre del cliente es obligatorio');
            errores.push('Cliente requerido');
            valid = false;
        }
        if (!getVal('nc-rfc')) {
            fieldError('nc-rfc', 'El RFC es obligatorio');
            errores.push('RFC requerido');
            valid = false;
        }
        if (!getVal('nc-concepto')) {
            fieldError('nc-concepto', 'Ingresa el motivo / concepto de la NC');
            errores.push('Concepto / motivo requerido');
            valid = false;
        }
        if (STATE.ncType !== 'standalone' && !STATE.refs.length) {
            errores.push('Factura de referencia requerida');
            valid = false;
        }
    }

    else if (stepNum === 2) {
        pmSync();
        if (!STATE.partidas.length) {
            const toolbar = document.querySelector('#panel-3 .partidas-toolbar');
            const prev = toolbar?.parentElement.querySelector('.field-error-msg');
            if (prev) prev.remove();
            const span = document.createElement('span');
            span.className = 'field-error-msg';
            span.style.cssText = 'padding:8px 16px; display:block;';
            span.textContent = 'Agrega al menos una partida antes de continuar';
            toolbar?.insertAdjacentElement('afterend', span);
            errores.push('Se requiere al menos una partida');
            valid = false;
        } else {
            let rowErr = false;
            STATE.partidas.forEach(p => {
                if (!p.desc || p.cant <= 0 || p.precio < 0) rowErr = true;
            });
            if (rowErr) {
                errores.push('Revisa descripción, cantidad y precio de todas las partidas');
                valid = false;
            }
        }
    }

    else if (stepNum === 3) {
        if (!getVal('nc-cp')) {
            fieldError('nc-cp', 'El código postal fiscal es obligatorio');
            errores.push('Código postal fiscal requerido');
            valid = false;
        }
    }

    if (!valid) {
        const msg = errores.length === 1
            ? errores[0]
            : `${errores.length} campos requeridos: ${errores.join(' · ')}`;
        toastMixin.fire({ title: msg, icon: 'error' });
        return false;
    }

    if (!NCRules.validateStepRules(stepNum)) return false;
    return true;
}

/* ══════════════════════════════════════════
   WIZARD — PASOS
══════════════════════════════════════════ */
function gotoStep(n) {
    if (n > STATE.currentStep) {
        if (!validateStep(STATE.currentStep)) return;
    }
    if (n === 4) buildReview();

    document.querySelectorAll('.panel').forEach(p => p.classList.remove('active'));
    document.getElementById(`panel-${n}`).classList.add('active');

    document.querySelectorAll('.wizard-step').forEach(s => {
        const sn = +s.dataset.step;
        s.classList.remove('active', 'done');
        if (sn === n) s.classList.add('active');
        else if (sn < n) s.classList.add('done');
    });

    STATE.currentStep = n;
    window.scrollTo({ top: 0, behavior: 'smooth' });
    NCRules.onStepEnter(n);
}

/* ══════════════════════════════════════════
   TIPO DE NC
══════════════════════════════════════════ */
const TYPE_META = {
    devolucion: { label: 'Devolución de Mercancía', color: '#7c3aed' },
    descuento: { label: 'Descuento / Bonificación', color: '#0891b2' },
    precio: { label: 'Error en Precio', color: '#d97706' },
    cancelacion: { label: 'Cancelación Parcial', color: '#dc2626' },
    bonificacion: { label: 'Bonificación / Rappel', color: '#059669' },
    standalone: { label: 'Sin Referencia (Libre)', color: '#64748b' },
};

function selectType(card) {
    // Limpiar aviso standalone y restaurar tabla
    document.getElementById('standalone-partida-aviso')?.remove();
    document.querySelector('.partidas-wrap').style.display = '';

    // Si se venía de standalone, limpiar la partida virtual
    if (STATE.ncType === 'standalone') {
        STATE.partidas = [];
        pmRender();
        // Resetear totales
        ['nc-subtotal', 'nc-descuentos', 'nc-base', 'nc-iva', 'nc-ieps', 'nc-total']
            .forEach(id => { const el = document.getElementById(id); if (el) el.value = '0'; });
        setTxt('side-subtotal', fmt(0));
        setTxt('side-iva', fmt(0));
        setTxt('side-total', fmt(0));
    }
    document.querySelectorAll('.nc-type-card').forEach(c => c.classList.remove('selected'));
    card.classList.add('selected');
    const type = card.dataset.type;
    STATE.ncType = type;
    document.getElementById('nc-tipo-sel').value = type;

    document.querySelectorAll('.nc-ctx').forEach(c => c.classList.remove('visible'));
    const ctx = document.getElementById(`ctx-${type}`);
    if (ctx) ctx.classList.add('visible');

    const meta = TYPE_META[type];
    document.getElementById('side-type-dot').style.background = meta.color;
    setTxt('side-type-name', meta.label);

    const stxRef = document.getElementById('ctx-standalone-ref');
    if (stxRef) stxRef.classList.toggle('visible', type === 'standalone');

    const dp = document.getElementById('ctx-devolucion-partidas');
    if (dp) dp.classList.toggle('visible', type === 'devolucion');

    if (type === 'standalone' && STATE.refs.length) {
        if (confirm('¿Deseas quitar las facturas de referencia al cambiar a modo "Sin Referencia"?')) {
            clearAllRefs();
        }
    }

    toastMixin.fire({ icon: 'info', title: `Tipo: ${meta.label}` });
    NCRules.onTypeSelected(type);
}

/* ══════════════════════════════════════════
   MODALES GENÉRICOS
══════════════════════════════════════════ */
function openModal(id) { document.getElementById(id).classList.add('open'); }
function closeModal(id) { document.getElementById(id).classList.remove('open'); }

document.querySelectorAll('.modal-overlay').forEach(m => {
    m.addEventListener('click', e => { if (e.target === m) closeModal(m.id); });
});
document.addEventListener('keydown', e => {
    if (e.key === 'Escape') document.querySelectorAll('.modal-overlay.open').forEach(m => closeModal(m.id));
});

/* ══════════════════════════════════════════
   HELPERS INTERNOS — sincronización de hidden fields
══════════════════════════════════════════ */

/**
 * FIX: mantiene nc-refs-json y nc-encabezado-id sincronizados con STATE.refs.
 *  - nc-refs-json     → form["refs"]        → ObtenerUUIDsDeFacturas(List<int>)
 *  - nc-encabezado-id → form["encabezadoId"]→ nc.EncabezadoIdOrigen en INSERT
 */
function _syncRefsHidden() {
    const ids = STATE.refs.map(r => r.id);
    document.getElementById('nc-refs-json').value = JSON.stringify(ids);
    document.getElementById('nc-encabezado-id').value = ids.length ? ids[0] : '';
}

/**
 * FIX: serializa STATE.partidas al hidden nc-partidas-json.
 *  - nc-partidas-json → form["partidas"] → JsonConvert.DeserializeObject<List<PartidaItem>>()
 */
function _syncPartidasHidden() {
    pmSync();
    const lista = STATE.partidas.map(p => ({
        clave: p.clave,
        descripcion: p.desc,
        cantidad: p.cant,
        precio: p.precio,
        descuento: p.descP,
        iva: p.ivaP,
        ieps: p.iepsP,
    }));
    document.getElementById('nc-partidas-json').value = JSON.stringify(lista);
}

/* ══════════════════════════════════════════
   MODAL — FACTURAS / REFERENCIAS
══════════════════════════════════════════ */
function renderRefs() {
    const el = document.getElementById('multi-ref-list');
    const badge = document.getElementById('ref-count-badge');
    if (!STATE.refs.length) {
        el.innerHTML = '';
        badge.textContent = '0 referencias';
        return;
    }
    badge.textContent = `${STATE.refs.length} referencia${STATE.refs.length > 1 ? 's' : ''}`;
    el.innerHTML = STATE.refs.map(f => `
    <div class="multi-ref-item">
        <span class="folio">${esc(f.folio)}</span>
        <span class="cli">${esc(f.cliente)}</span>
        <span class="imp">${fmt(f.imp)}</span>
        <button class="row-delete" onclick="removeRef(${f.id})"><i class="fas fa-times"></i></button>
    </div>`).join('');

    // Sincronizar tarjeta con la última ref del array
    if (STATE.refs.length) {
        const last = STATE.refs[STATE.refs.length - 1];
        document.getElementById('nc-ref-folio').textContent = last.folio;
        document.getElementById('nc-ref-cliente').textContent = last.cliente;
        document.getElementById('nc-ref-fecha').textContent = last.fecha;
        document.getElementById('nc-ref-importe').textContent = fmt(last.imp);
        document.getElementById('nc-ref-card').classList.add('visible');
    }
}

function removeRef(id) {
    STATE.refs = STATE.refs.filter(r => r.id !== id);
    renderRefs();
    updateSideRefs();
    if (!STATE.refs.length) document.getElementById('nc-ref-card').classList.remove('visible');
    _syncRefsHidden();
}

function clearAllRefs() {
    STATE.refs = [];
    renderRefs();
    updateSideRefs();
    document.getElementById('nc-ref-card').classList.remove('visible');
    _syncRefsHidden();
    toast('Referencias removidas', 'warn');
}

function updateSideRefs() {
    // El elemento puede no existir si sb2UpdateRefs ya lo reemplazó
    const el = document.getElementById('side-refs');

    if (!STATE.refs.length) {
        // Sólo tocar si el elemento existe (puede haber sido eliminado por sb2)
        if (el) {
            el.textContent = '0 documentos';
            el.style.color = 'var(--ink-soft)';
        }
        return;
    }

    if (el) {
        el.textContent = STATE.refs.map(r => r.folio).join(', ');
        el.style.color = 'var(--accent-blue)';
    }
    // Si no existe, sb2UpdateRefs (llamado por el monkey-patch) lo gestiona
}

function applyClienteFromRef(f) {
    document.getElementById('nc-cliente').value = f.cliente || '';
    document.getElementById('nc-rfc').value = f.rfc || '';
    document.getElementById('nc-vendedor').value = f.vdr || '';
    if (f.ccy) document.getElementById('nc-moneda').value = f.ccy;
    setTxt('side-cliente', f.cliente || '—');
}

function importFromRef() {
    if (!STATE.refs.length) { toast('Agrega al menos una factura de referencia primero', 'warn'); return; }
    const allProds = STATE.refs.flatMap(r => r.productos || []);
    pmLoad(allProds);
    toast(`${allProds.length} partidas importadas`, 'ok');
}

/* ── FACTURAS paginación y búsqueda ── */
const FACTURAS_STATE = { page: 1, pageSize: 50, total: 0, query: '', clienteRfc: '', loading: false };
let _facturasDebounceTimer = null;

function filterFacturasDebounce(q) {
    clearTimeout(_facturasDebounceTimer);
    _facturasDebounceTimer = setTimeout(() => filterFacturas(q), 300);
}
function filterFacturas(q) { FACTURAS_STATE.query = q; FACTURAS_STATE.page = 1; fetchFacturas(); }
function facturasPrevPage() { if (FACTURAS_STATE.page > 1) { FACTURAS_STATE.page--; fetchFacturas(); } }
function facturasNextPage() {
    const tp = Math.ceil(FACTURAS_STATE.total / FACTURAS_STATE.pageSize);
    if (FACTURAS_STATE.page < tp) { FACTURAS_STATE.page++; fetchFacturas(); }
}

async function fetchFacturas() {
    if (FACTURAS_STATE.loading) return;
    FACTURAS_STATE.loading = true;
    const el = document.getElementById('list-facturas');
    el.innerHTML = `<div style="text-align:center;padding:30px;color:var(--ink-ghost);font-size:12px;">
        <i class="fas fa-spinner fa-spin fa-2x" style="opacity:.5;display:block;margin-bottom:8px;"></i>Buscando...</div>`;
    try {
        const params = new URLSearchParams({
            nombre: FACTURAS_STATE.query || '',
            page: FACTURAS_STATE.page,
            pageSize: FACTURAS_STATE.pageSize,
            rfc: FACTURAS_STATE.clienteRfc || ''
        });
        const res = await fetch(`/DatosGenerales/BuscarFacturasSolicitud?${params}`);
        if (!res.ok) throw new Error('HTTP ' + res.status);
        const data = await res.json();
        FACTURAS_STATE.total = data.total || 0;
        renderFacturasModal(data.items || []);
    } catch (e) {
        el.innerHTML = `<div style="text-align:center;padding:30px;color:var(--danger-color);font-size:12px;">
            <i class="fas fa-exclamation-circle fa-2x" style="display:block;margin-bottom:8px;"></i>Error: ${esc(e.message)}</div>`;
    } finally { FACTURAS_STATE.loading = false; }
}

function renderFacturasModal(items) {
    const el = document.getElementById('list-facturas');
    const tp = Math.ceil(FACTURAS_STATE.total / FACTURAS_STATE.pageSize);
    const prevBtn = document.getElementById('btn-facturas-prev');
    const nextBtn = document.getElementById('btn-facturas-next');
    const pageTxt = document.getElementById('facturas-page-txt');
    if (tp > 1) { prevBtn.style.display = ''; nextBtn.style.display = ''; pageTxt.textContent = `Pág. ${FACTURAS_STATE.page} / ${tp}`; }
    else { prevBtn.style.display = 'none'; nextBtn.style.display = 'none'; pageTxt.textContent = ''; }
    prevBtn.disabled = FACTURAS_STATE.page <= 1;
    nextBtn.disabled = FACTURAS_STATE.page >= tp;
    document.getElementById('facturas-count-txt').textContent =
        FACTURAS_STATE.total > 0 ? `${FACTURAS_STATE.total} factura${FACTURAS_STATE.total !== 1 ? 's' : ''}` : 'Sin resultados';
    if (!items.length) {
        el.innerHTML = `<div style="text-align:center;padding:30px;color:var(--ink-ghost);font-size:12px;">
            <i class="fas fa-file-slash fa-2x" style="opacity:.3;display:block;margin-bottom:8px;"></i>Sin resultados</div>`;
        return;
    }
    el.innerHTML = items.map(f => `
    <div class="modal-item">
        <div class="modal-item-icon"><i class="fas fa-file-invoice"></i></div>
        <div class="modal-item-main">
            <div class="modal-item-title">${esc(f.folio)} — ${esc(f.n_cli || f.cli_prov)}</div>
            <div class="modal-item-sub">${esc(f.cli_prov)} &nbsp;|&nbsp; ${esc(dateFormatter(f.fch?.substring(0, 10)))} &nbsp;|&nbsp; ${fmt(f.imp)}</div>
        </div>
        <button class="modal-item-action" onclick="addRef(${f.id_encabezado})">Cargar</button>
    </div>`).join('');
}

async function addRef(id) {
    if (STATE.refs.find(r => r.id === id)) {
        toast('Esta factura ya está referenciada', 'warn');
        return;
    }
    try {
        const res = await fetch(`/DatosGenerales/BuscarFacturaDetalleNC?id=${id}`);
        if (!res.ok) throw new Error('HTTP ' + res.status);
        const data = await res.json();
        if (!data || !data.length) {
            toast('No se encontró el detalle de la factura', 'err');
            return;
        }
        const d = data[0];
        const ref = {
            id: d.id_encabezado,
            folio: d.folio,
            cliente: d.n_cli || d.cli_prov,
            rfc: d.rfc,
            fecha: dateFormatter(d.fch0?.substring(0, 10)) ?? '',
            imp: d.total ?? 0,
            vdr: d.vdr_cpr ?? '',
            ccy: d.ccy ?? 'MXN',
            productos: (d.productos || []).map(p => ({
                id: p.producto_id,
                clave: p.producto_id,
                desc: p.descripcion,
                cant: p.cantidad,
                precio: p.precio,
                desc_p: p.descuento,
                iva: p.iva,
                ieps: p.ieps,
            })),
        };

        STATE.refs.push(ref);
        renderRefs();
        if (!getVal('nc-cliente')) applyClienteFromRef(ref);

        // Actualizar tarjeta de referencia
        document.getElementById('nc-ref-folio').textContent = ref.folio;
        document.getElementById('nc-ref-cliente').textContent = ref.cliente;
        document.getElementById('nc-ref-fecha').textContent = dateFormatter(ref.fecha);
        document.getElementById('nc-ref-importe').textContent = fmt(ref.imp);
        document.getElementById('nc-ref-card').classList.add('visible');

        _syncRefsHidden();
        closeModal('modal-facturas');
        updateSideRefs();
        toast(`Factura ${ref.folio} agregada`, 'ok');

        // ── NUEVO: buscar solicitud NC ligada a este encabezado ──────────────
        await _autoSelectTipoDesdeFactura(d.id_encabezado);

        await verificarAnticiposDeRef(id, ref.folio);

    } catch (e) {
        toast('Error al cargar factura: ' + e.message, 'err');
    }
}


async function _autoSelectTipoDesdeFactura(encabezadoId) {
    try {
        const res = await fetch(
            `/VINotaCredito/ObtenerSolicitudNC?encabezadoId=${encabezadoId}`
        );
        if (!res.ok) return;

        const data = await res.json();
        if (!data.found) return;   // sin solicitud → nada que hacer

        const tipo = data.tipoNC || data.motivoTipo || 'standalone';

        // Mapeo motivo_tipo → tipo de card (por si difieren en nomenclatura)
        const mapa = {
            devolucion: 'devolucion',
            descuento: 'descuento',
            precio: 'precio',
            cancelacion: 'cancelacion',
            bonificacion: 'bonificacion',
            otro: 'standalone',
            standalone: 'standalone',
        };
        const tipoFinal = mapa[tipo] || tipo;

        // ── Solo auto-seleccionar si aún no hay tipo elegido ──────────────
        // (respetamos si el usuario ya seleccionó algo antes de agregar la ref)
        if (STATE.ncType && STATE.ncType !== tipoFinal) {
            _mostrarBannerConflicto(tipoFinal, data);
            return;
        }

        // ── Intentar click en la card visible ────────────────────────────
        const card = document.querySelector(
            `.nc-type-card[data-type="${tipoFinal}"]`
        );

        if (card) {
            selectType(card);  // dispara toda la lógica de selectType()
        } else {
            // El selector está oculto (viene de otra solicitud previa)
            _autoSelectTipo(tipoFinal);
        }

        // ── Banner informativo ────────────────────────────────────────────
        _mostrarBannerSolicitud(tipoFinal, data);

    } catch (e) {
        // Error de red — no bloquear flujo
        console.warn('[NC] No se pudo verificar solicitud NC:', e.message);
    }
}


function _mostrarBannerSolicitud(tipo, data) {
    // Eliminar banner anterior si existe
    document.getElementById('nc-solicitud-banner')?.remove();

    const meta = TYPE_META[tipo] || { label: tipo, color: '#64748b' };
    const prior = { normal: '', alta: '⚡ Alta', urgente: '🔴 Urgente' };

    const banner = document.createElement('div');
    banner.id = 'nc-solicitud-banner';
    banner.style.cssText = `
        margin: 0 0 16px 0;
        padding: 12px 16px;
        background: #f0fdf4;
        border: 1.5px solid #86efac;
        border-radius: 8px;
        font-size: 12px;
        display: flex;
        align-items: flex-start;
        gap: 10px;
        animation: fadeIn .3s ease;`;

    banner.innerHTML = `
        <i class="fas fa-clipboard-check"
           style="color:#16a34a;font-size:16px;margin-top:1px;flex-shrink:0;"></i>
        <div style="flex:1;min-width:0;">
            <div style="font-weight:700;color:#15803d;margin-bottom:3px;">
                Tipo auto-seleccionado desde solicitud registrada
            </div>
            <div style="color:#166534;">
                <strong style="color:${meta.color};">${meta.label}</strong>
                ${data.comentario
            ? `— <em style="color:#4b5563;">"${_truncar(data.comentario, 80)}"</em>`
            : ''}
                ${data.prioridad && data.prioridad !== 'normal'
            ? `<span style="margin-left:6px;font-size:10px;
                            background:#fef9c3;color:#854d0e;
                            border:1px solid #fde047;border-radius:4px;
                            padding:1px 6px;">${prior[data.prioridad] || data.prioridad}</span>`
            : ''}
            </div>
            ${data.responsable
            ? `<div style="margin-top:3px;color:#6b7280;font-size:11px;">
                       Responsable: <strong>${data.responsable}</strong>
                   </div>`
            : ''}
        </div>
        <button type="button"
                onclick="document.getElementById('nc-solicitud-banner')?.remove()"
                style="background:none;border:none;color:#6b7280;cursor:pointer;
                       font-size:14px;padding:0;line-height:1;flex-shrink:0;">
            <i class="fas fa-times"></i>
        </button>`;

    // Insertar antes del selector de tipo o antes del primer section-card del panel 1
    const selectorTipo = document.getElementById('nc-tipo-selector')
        || document.querySelector('#panel-1 .section-card');
    selectorTipo?.insertAdjacentElement('beforebegin', banner);
}

function _mostrarBannerConflicto(tipoSugerido, data) {
    document.getElementById('nc-solicitud-banner')?.remove();

    const metaSug = TYPE_META[tipoSugerido] || { label: tipoSugerido, color: '#64748b' };
    const metaCur = TYPE_META[STATE.ncType] || { label: STATE.ncType, color: '#64748b' };

    const banner = document.createElement('div');
    banner.id = 'nc-solicitud-banner';
    banner.style.cssText = `
        margin: 0 0 16px 0;
        padding: 12px 16px;
        background: #fffbeb;
        border: 1.5px solid #fde68a;
        border-radius: 8px;
        font-size: 12px;
        display: flex;
        align-items: flex-start;
        gap: 10px;`;

    banner.innerHTML = `
        <i class="fas fa-exclamation-triangle"
           style="color:#d97706;font-size:16px;margin-top:1px;flex-shrink:0;"></i>
        <div style="flex:1;">
            <div style="font-weight:700;color:#92400e;margin-bottom:4px;">
                La solicitud registrada sugiere un tipo diferente
            </div>
            <div style="color:#78350f;">
                Solicitud: <strong style="color:${metaSug.color};">${metaSug.label}</strong>
                &nbsp;·&nbsp;
                Seleccionado: <strong style="color:${metaCur.color};">${metaCur.label}</strong>
            </div>
            <div style="margin-top:6px;display:flex;gap:8px;">
                <button type="button"
                        onclick="_aplicarTipoSolicitud('${tipoSugerido}')"
                        style="font-size:11px;padding:3px 10px;
                               background:#fef3c7;border:1px solid #f59e0b;
                               border-radius:4px;cursor:pointer;color:#92400e;">
                    Usar tipo de la solicitud
                </button>
                <button type="button"
                        onclick="document.getElementById('nc-solicitud-banner')?.remove()"
                        style="font-size:11px;padding:3px 10px;
                               background:#fff;border:1px solid #d1d5db;
                               border-radius:4px;cursor:pointer;color:#6b7280;">
                    Mantener mi selección
                </button>
            </div>
        </div>`;

    const selectorTipo = document.getElementById('nc-tipo-selector')
        || document.querySelector('#panel-1 .section-card');
    selectorTipo?.insertAdjacentElement('beforebegin', banner);
}

/** Aplica el tipo sugerido desde el banner de conflicto */
function _aplicarTipoSolicitud(tipo) {
    document.getElementById('nc-solicitud-banner')?.remove();
    const card = document.querySelector(`.nc-type-card[data-type="${tipo}"]`);
    if (card) selectType(card);
    else _autoSelectTipo(tipo);
}

/** Trunca texto para mostrar en el banner */
function _truncar(texto, max) {
    return texto.length > max ? texto.substring(0, max) + '…' : texto;
}
async function verificarAnticiposDeRef(encabezadoId, folioRef) {
    try {
        const res = await fetch(
            `/DatosGenerales/BuscarAnticipoDeFact?encabezadoId=${encabezadoId}`
        );
        if (!res.ok) return;
        const data = await res.json();

        if (!data.tieneAnticipos) return;

        // Guardar anticipos detectados en STATE para usarlos al emitir
        STATE.anticiposDetectados = STATE.anticiposDetectados || [];
        data.anticipos.forEach(a => {
            if (!STATE.anticiposDetectados.find(x => x.anticipoId === a.anticipoId)) {
                STATE.anticiposDetectados.push({ ...a, encabezadoId, montoAplicarNC: 0 });
            }
        });

        renderPanelAnticipos(folioRef, data.anticipos);
    } catch (e) {
        console.warn('No se pudieron verificar anticipos:', e);
    }
}

function renderPanelAnticipos(folioRef, anticipos) {
    // Eliminar panel anterior si existe
    document.getElementById('panel-anticipos-nc')?.remove();

    const container = document.getElementById('nc-ref-card')?.parentElement;
    if (!container) return;

    const total = anticipos.reduce((s, a) => s + parseFloat(a.maxRecuperable || 0), 0);
    const fmt2 = n => '$' + Number(n).toFixed(2).replace(/\B(?=(\d{3})+(?!\d))/g, ',');

    const panel = document.createElement('div');
    panel.id = 'panel-anticipos-nc';
    panel.style.cssText = `
        margin-top: 12px; padding: 14px 16px;
        background: #fffbeb; border: 1.5px solid #fde68a;
        border-radius: 8px; font-size: 12px;`;

    panel.innerHTML = `
        <div style="font-weight:700; color:#92400e; margin-bottom:10px;">
            <i class="fas fa-exclamation-triangle me-2"></i>
            Factura <strong>${esc(folioRef)}</strong> tiene
            ${anticipos.length} anticipo(s) aplicado(s)
            — máximo recuperable: <strong>${fmt2(total)}</strong>
        </div>
        <div style="color:#78350f; margin-bottom:12px; font-size:11px;">
            Al emitir esta NC puedes recuperar parte o todo el anticipo,
            o dejarlo sin cambios. Indica cuánto aplicar por cada anticipo:
        </div>
        ${anticipos.map(a => `
        <div style="display:grid; grid-template-columns:1fr auto; gap:8px;
                    align-items:center; margin-bottom:8px; padding:8px;
                    background:#fff; border-radius:6px; border:1px solid #fde68a;">
            <div>
                <div style="font-weight:600; color:#0f172a;">
                    ${esc(a.anticipoFolio)}
                    <span style="font-weight:400; color:#64748b; font-size:10px;">
                        — aplicado: ${fmt2(a.montoAplicado)}
                        · saldo actual: ${fmt2(a.saldoActual)}
                    </span>
                </div>
                <div style="font-size:10px; color:#64748b; margin-top:2px;">
                    Máximo a recuperar: <strong>${fmt2(a.maxRecuperable)}</strong>
                </div>
            </div>
            <div style="display:flex; align-items:center; gap:6px;">
                <input
                    type="number"
                    id="antic-monto-${a.anticipoId}"
                    data-anticipo-id="${a.anticipoId}"
                    data-max="${a.maxRecuperable}"
                    data-fa-id="${a.faId}"
                    value="0"
                    min="0"
                    max="${a.maxRecuperable}"
                    step="0.01"
                    style="width:110px; padding:5px 8px; border:1px solid #fde68a;
                           border-radius:6px; font-family:monospace; font-size:12px;"
                    oninput="validarMontoAnticipo(this)"
                    placeholder="0.00"
                />
                <button type="button"
                    onclick="aplicarMaxAnticipo(${a.anticipoId}, ${a.maxRecuperable})"
                    style="font-size:10px; padding:4px 8px; border:1px solid #d97706;
                           background:#fef3c7; border-radius:4px; cursor:pointer; color:#92400e;">
                    Máx
                </button>
                <button type="button"
                    onclick="document.getElementById('antic-monto-${a.anticipoId}').value=0;
                             sincronizarMontosAnticipo();"
                    style="font-size:10px; padding:4px 8px; border:1px solid #e2e8f0;
                           background:#f8fafc; border-radius:4px; cursor:pointer; color:#64748b;">
                    Omitir
                </button>
            </div>
        </div>`).join('')}
        <div style="margin-top:10px; padding:8px 12px; background:#fef3c7;
                    border-radius:6px; display:flex; justify-content:space-between;
                    align-items:center; font-size:12px;">
            <span style="color:#92400e; font-weight:600;">
                Total a recuperar en esta NC:
            </span>
            <span id="antic-total-recuperar" style="font-family:monospace;
                  font-weight:700; color:#92400e; font-size:14px;">
                ${fmt2(0)}
            </span>
        </div>`;

    container.appendChild(panel);
}

function validarMontoAnticipo(input) {
    const max = parseFloat(input.dataset.max || 0);
    let val = parseFloat(input.value) || 0;
    if (val > max) { input.value = max; val = max; }
    if (val < 0) { input.value = 0; val = 0; }
    sincronizarMontosAnticipo();
}

function aplicarMaxAnticipo(anticipoId, max) {
    const input = document.getElementById(`antic-monto-${anticipoId}`);
    if (input) { input.value = parseFloat(max).toFixed(2); }
    sincronizarMontosAnticipo();
}

function sincronizarMontosAnticipo() {
    // Actualizar totales del panel
    let total = 0;
    document.querySelectorAll('[id^="antic-monto-"]').forEach(inp => {
        total += parseFloat(inp.value) || 0;
    });

    const el = document.getElementById('antic-total-recuperar');
    if (el) el.textContent = '$' + total.toFixed(2)
        .replace(/\B(?=(\d{3})+(?!\d))/g, ',');

    // Sincronizar al STATE
    if (!STATE.anticiposDetectados) return;
    document.querySelectorAll('[id^="antic-monto-"]').forEach(inp => {
        const antId = parseInt(inp.dataset.anticipoId);
        const ant = STATE.anticiposDetectados.find(a => a.anticipoId === antId);
        if (ant) ant.montoAplicarNC = parseFloat(inp.value) || 0;
    });
}

function abrirModalFacturas() {
    // Tomar el RFC del campo visible y sincronizarlo al estado de búsqueda
    FACTURAS_STATE.clienteRfc = getVal('nc-rfc') || '';
    FACTURAS_STATE.page = 1;
    openModal('modal-facturas');
    fetchFacturas();
}

/* ══════════════════════════════════════════
   MODAL — CLIENTES
══════════════════════════════════════════ */
const CLIENTES_STATE = { page: 1, pageSize: 50, total: 0, query: '', loading: false };
let _clientesDebounceTimer = null;

function filterClientesDebounce(q) {
    clearTimeout(_clientesDebounceTimer);
    _clientesDebounceTimer = setTimeout(() => filterClientes(q), 300);
}
function filterClientes(q) { CLIENTES_STATE.query = q; CLIENTES_STATE.page = 1; fetchClientes(); }
function clientesPrevPage() { if (CLIENTES_STATE.page > 1) { CLIENTES_STATE.page--; fetchClientes(); } }
function clientesNextPage() {
    const tp = Math.ceil(CLIENTES_STATE.total / CLIENTES_STATE.pageSize);
    if (CLIENTES_STATE.page < tp) { CLIENTES_STATE.page++; fetchClientes(); }
}

async function fetchClientes() {
    if (CLIENTES_STATE.loading) return;
    CLIENTES_STATE.loading = true;
    const el = document.getElementById('list-clientes');
    el.innerHTML = `<div style="text-align:center;padding:30px;color:var(--ink-ghost);font-size:12px;">
        <i class="fas fa-spinner fa-spin fa-2x" style="opacity:.5;display:block;margin-bottom:8px;"></i>Buscando...</div>`;
    try {
        const params = new URLSearchParams({ nombre: CLIENTES_STATE.query || '', page: CLIENTES_STATE.page, pageSize: CLIENTES_STATE.pageSize });
        const res = await fetch(`/DatosGenerales/BuscarC?${params}`);
        if (!res.ok) throw new Error('HTTP ' + res.status);
        const data = await res.json();
        CLIENTES_STATE.total = data.total || 0;
        renderClientesModal(data.items || []);
    } catch (e) {
        el.innerHTML = `<div style="text-align:center;padding:30px;color:var(--danger-color);font-size:12px;">
            <i class="fas fa-exclamation-circle fa-2x" style="display:block;margin-bottom:8px;"></i>Error: ${esc(e.message)}</div>`;
    } finally { CLIENTES_STATE.loading = false; }
}

function renderClientesModal(items) {
    const el = document.getElementById('list-clientes');
    const tp = Math.ceil(CLIENTES_STATE.total / CLIENTES_STATE.pageSize);
    const prevBtn = document.getElementById('btn-clientes-prev');
    const nextBtn = document.getElementById('btn-clientes-next');
    const pageTxt = document.getElementById('clientes-page-txt');
    if (tp > 1) { prevBtn.style.display = ''; nextBtn.style.display = ''; pageTxt.textContent = `Pág. ${CLIENTES_STATE.page} / ${tp}`; }
    else { prevBtn.style.display = 'none'; nextBtn.style.display = 'none'; pageTxt.textContent = ''; }
    prevBtn.disabled = CLIENTES_STATE.page <= 1;
    nextBtn.disabled = CLIENTES_STATE.page >= tp;
    document.getElementById('clientes-count-txt').textContent =
        CLIENTES_STATE.total > 0 ? `${CLIENTES_STATE.total} cliente${CLIENTES_STATE.total !== 1 ? 's' : ''}` : 'Sin resultados';
    if (!items.length) {
        el.innerHTML = `<div style="text-align:center;padding:30px;color:var(--ink-ghost);font-size:12px;">
            <i class="fas fa-user-slash fa-2x" style="opacity:.3;display:block;margin-bottom:8px;"></i>Sin resultados</div>`;
        return;
    }
    el.innerHTML = items.map(c => `
    <div class="modal-item">
        <div class="modal-item-icon" style="background:var(--ok-bg);color:var(--ok);"><i class="fas fa-user"></i></div>
        <div class="modal-item-main">
            <div class="modal-item-title">${esc(c.descripcion)}</div>
            <div class="modal-item-sub font-mono">${esc(c.id)}</div>
        </div>
        <button class="modal-item-action" onclick="selectCliente('${esc(c.id)}')">Seleccionar</button>
    </div>`).join('');
}

async function selectCliente(id) {
    try {
        const res = await fetch(`/DatosGenerales/BuscarCliente?id=${encodeURIComponent(id)}`);
        if (!res.ok) throw new Error('HTTP ' + res.status);
        const data = await res.json();
        if (!data || !data.length) { toast('No se encontraron datos del cliente', 'err'); return; }
        const c = data[0];

        document.getElementById('nc-cliente').value = c.descripcion || '';
        document.getElementById('nc-rfc').value = c.rfc || '';
        document.getElementById('nc-clave-cli').value = c.id || '';
        document.getElementById('nc-cp').value = c.codigo_postal || c.cp || '';
        document.getElementById('nc-vendedor').value = c.cve_vdr || '';

        const partes = [c.calle, c.no_exterior ? `#${c.no_exterior}` : '', c.colonia, c.municipio, c.estado].filter(Boolean);
        document.getElementById('nc-dir-cliente').value = partes.join(', ') || c.dir || '';

        // FIX: guardar id numérico → hidden nc-id-cliente → form["idCliente"] → nc.IdCliente en INSERT
        STATE.clienteId = c.id_num ?? c.id_cliente ?? c.id ?? null;
        document.getElementById('nc-id-cliente').value = STATE.clienteId ?? '';

        if (c.forma_pago) {
            const fpSel = document.getElementById('nc-forma-pago');
            if (fpSel) {
                const opt = [...fpSel.options].find(o => o.value === c.forma_pago);
                if (opt) fpSel.value = c.forma_pago;
            }
        }

        // FIX: actualiza el select visible Y el hidden de régimen
        if (c.regimen_fiscal) {
            const rfSel = document.getElementById('nc-regimen');
            if (rfSel) {
                const opt = [...rfSel.options].find(o => o.value === String(c.regimen_fiscal));
                if (opt) {
                    rfSel.value = String(c.regimen_fiscal);
                    document.getElementById('nc-regimen-hidden').value = String(c.regimen_fiscal);
                }
            }
        }

        setTxt('side-cliente', c.descripcion || '—');
        closeModal('modal-clientes');
        toast(`Cliente: ${c.descripcion}`, 'ok');
    } catch (e) { toast('Error al cargar detalle del cliente: ' + e.message, 'err'); }
}

/* ══════════════════════════════════════════
   GESTIÓN DE PARTIDAS
══════════════════════════════════════════ */
function pmLoad(prods) {
    STATE.partidas = [];
    STATE.pidCounter = 0;
    prods.forEach(p => pmPush({
        clave: p.id || p.clave || '',
        desc: p.desc || p.descripcion || '',
        cant: parseFloat(p.cant || p.cantidad) || 0,
        precio: parseFloat(p.precio) || 0,
        descP: parseFloat(p.desc_p || p.descuento) || 0,
        ivaP: parseFloat(p.iva) || 16,
        iepsP: 0,
    }));
    pmRender();
    recalc();
}

function pmPush(d) { STATE.pidCounter++; STATE.partidas.push({ _id: STATE.pidCounter, ...d }); }
function pmAddRow() { pmPush({ clave: '', desc: '', cant: 1, precio: 0, descP: 0, ivaP: 16, iepsP: 0 }); pmRender(); recalc(); }
function pmDelete(id) { STATE.partidas = STATE.partidas.filter(p => p._id !== id); pmRender(); recalc(); }

function pmSync() {
    document.querySelectorAll('#partidas-body tr[data-pid]').forEach(tr => {
        const id = +tr.dataset.pid;
        const p = STATE.partidas.find(x => x._id === id);
        if (!p) return;
        const inp = tr.querySelectorAll('input,select');
        p.clave = inp[0]?.value || '';
        p.desc = inp[1]?.value || '';
        p.cant = parseFloat(inp[2]?.value) || 0;
        p.precio = parseFloat(inp[3]?.value) || 0;
        p.descP = parseFloat(inp[4]?.value) || 0;
        p.ivaP = parseFloat(inp[5]?.value) || 16;
        p.iepsP = parseFloat(inp[6]?.value) || 0;
    });
}

function pmRender() {
    const tbody = document.getElementById('partidas-body');
    const badge = document.getElementById('partidas-count-badge');
    badge.textContent = STATE.partidas.length;
    if (!STATE.partidas.length) {
        tbody.innerHTML = `<tr class="empty-state-row"><td colspan="10">
            <span class="empty-state-icon"><i class="fas fa-box-open"></i></span>
            Sin partidas — importa de la factura de referencia o agrega manualmente</td></tr>`;
        renderDevolucion();
        return;
    }
    tbody.innerHTML = '';
    STATE.partidas.forEach((p, i) => {
        const brutoBase = p.cant * p.precio;
        const neto = brutoBase * (1 - p.descP / 100);
        const iva = neto * (p.ivaP / 100);
        const ieps = neto * (p.iepsP / 100);
        const sub = neto + iva + ieps;
        const tr = document.createElement('tr');
        tr.dataset.pid = p._id;
        tr.innerHTML = `
    <td style="color:var(--ink-ghost);font-family:var(--font-mono);font-size:11px;">${String(i + 1).padStart(2, '0')}</td>
    <td><input type="text"   value="${esc(p.clave)}"         placeholder="SKU"         oninput="recalc()" /></td>
    <td><input type="text"   value="${esc(p.desc)}"          placeholder="Descripción" oninput="recalc()" /></td>
    <td><input type="number" value="${p.cant}"               min="0" step="any"         oninput="recalc()" /></td>
    <td><input type="number" value="${p.precio.toFixed(2)}"  min="0" step="0.01"        oninput="recalc()" /></td>
    <td><input type="number" value="${p.descP}"              min="0" max="100" step="0.01" oninput="recalc()" /></td>
    <td><input type="number" value="${p.ivaP}"               min="0" max="100" step="0.01" oninput="recalc()" /></td>
    <td><input type="number" value="${p.iepsP}"              min="0" max="100" step="0.01" oninput="recalc()" /></td>
    <td class="td-importe"  id="imp-${p._id}">${fmt(sub)}</td>
    <td><button type="button" class="row-delete" onclick="pmDelete(${p._id})"><i class="fas fa-trash"></i></button></td>`;
        tbody.appendChild(tr);
    });
    renderDevolucion();
}

function renderDevolucion() {
    if (STATE.ncType !== 'devolucion') return;
    const tbody = document.getElementById('devolucion-body');
    if (!STATE.partidas.length) {
        tbody.innerHTML = `<tr class="empty-state-row"><td colspan="7">
            <span class="empty-state-icon"><i class="fas fa-pallet"></i></span>
            Las partidas del paso anterior aparecerán aquí</td></tr>`;
        return;
    }
    tbody.innerHTML = '';
    STATE.partidas.forEach(p => {
        const tr = document.createElement('tr');
        tr.innerHTML = `
    <td><span class="font-mono text-sm">${esc(p.clave)}</span></td>
    <td>${esc(p.desc)}</td>
    <td class="font-mono">${p.cant}</td>
    <td><input type="number" value="${p.cant}" min="0" step="any" style="width:70px;font-family:var(--font-mono);padding:4px 6px;border:1px solid var(--edge);border-radius:4px;" /></td>
    <td><input type="text"   value="PZA" style="width:60px;padding:4px 6px;border:1px solid var(--edge);border-radius:4px;font-size:11px;" /></td>
    <td><input type="text"   placeholder="N/A" style="width:90px;padding:4px 6px;border:1px solid var(--edge);border-radius:4px;font-size:11px;" /></td>
    <td><select style="font-size:11px;padding:4px 6px;border:1px solid var(--edge);border-radius:4px;">
        <option>Buen estado</option><option>Requiere QC</option><option>Con daño</option><option>Destrucción</option>
    </select></td>`;
        tbody.appendChild(tr);
    });
}

/* ══════════════════════════════════════════
   RECALCULAR TOTALES
══════════════════════════════════════════ */
function recalc() {
    pmSync();
    let subtotal = 0, descTotal = 0, ivaTotal = 0, iepsTotal = 0;

    document.querySelectorAll('#partidas-body tr[data-pid]').forEach(tr => {
        const inp = tr.querySelectorAll('input');
        const cant = parseFloat(inp[2]?.value) || 0;
        const prec = parseFloat(inp[3]?.value) || 0;
        const descP = parseFloat(inp[4]?.value) || 0;
        const ivaP = parseFloat(inp[5]?.value) || 16;
        const iepsP = parseFloat(inp[6]?.value) || 0;
        const id = tr.dataset.pid;

        const brutoBase = cant * prec;
        const desc = brutoBase * (descP / 100);
        const neto = brutoBase - desc;
        const iva = neto * (ivaP / 100);
        const ieps = neto * (iepsP / 100);
        const sub = neto + iva + ieps;

        subtotal += brutoBase;
        descTotal += desc;
        ivaTotal += iva;
        iepsTotal += ieps;

        const cell = document.getElementById(`imp-${id}`);
        if (cell) cell.textContent = fmt(sub);
    });

    const base = subtotal - descTotal;
    const total = base + ivaTotal + iepsTotal;

    setTxt('side-subtotal', fmt(subtotal));
    setTxt('side-descuentos', '−' + fmt(descTotal));
    setTxt('side-base', fmt(base));
    setTxt('side-iva', fmt(ivaTotal));
    setTxt('side-ieps', fmt(iepsTotal));
    setTxt('side-total', fmt(total));

    const iepsLine = document.getElementById('ieps-line');
    if (iepsLine) iepsLine.style.display = iepsTotal > 0 ? '' : 'none';

    const ccy = getVal('nc-moneda');
    const par = getVal('nc-paridad', '1');
    setTxt('side-moneda-note', `${ccy} · Paridad ${parseFloat(par).toFixed(4)}`);

    // Actualizar todos los hiddens de totales
    document.getElementById('nc-subtotal').value = subtotal.toFixed(2);
    document.getElementById('nc-descuentos').value = descTotal.toFixed(2);
    document.getElementById('nc-base').value = base.toFixed(2);
    document.getElementById('nc-iva').value = ivaTotal.toFixed(2);
    document.getElementById('nc-ieps').value = iepsTotal.toFixed(2);
    document.getElementById('nc-total').value = total.toFixed(2);

    // FIX: mantener hidden de partidas sincronizado en tiempo real
    _syncPartidasHidden();

    const f = getVal('nc-folio');
    if (f) setTxt('tb-folio', f);

    NCRules.onRecalc();
}

/* ══════════════════════════════════════════
   HELPERS DE TIPO
══════════════════════════════════════════ */
function calcDiffPrecio() {
    const orig = parseFloat(document.getElementById('nc-precio-original')?.value) || 0;
    const corr = parseFloat(document.getElementById('nc-precio-correcto')?.value) || 0;
    const diff = orig - corr;
    const el = document.getElementById('nc-precio-diff');
    if (el) {
        el.value = (orig > 0 || corr > 0) ? fmt(Math.abs(diff)) : '';
        el.style.color = diff > 0 ? 'var(--ok)' : 'var(--danger-color)';
    }
    if (orig > 0 && corr > 0 && orig <= corr) {
        fieldError('nc-precio-correcto', 'El precio correcto debe ser menor al original para generar diferencial positivo');
    } else {
        fieldError('nc-precio-correcto', null);
    }
}

function calcBoni() {
    const base = parseFloat(document.getElementById('nc-base-boni')?.value) || 0;
    const pct = parseFloat(document.getElementById('nc-pct-boni')?.value) || 0;
    toast(`Bonificación calculada: ${fmt(base * pct / 100)}`, 'info');
}

/* ══════════════════════════════════════════
   CxC
══════════════════════════════════════════ */
function selectCxC(el) {
    document.querySelectorAll('.cxc-option').forEach(c => c.classList.remove('selected'));
    el.classList.add('selected');
    const radio = el.querySelector('input[type=radio]');
    if (radio) radio.checked = true;
}

/* ══════════════════════════════════════════
   FLUJO DE APROBACIÓN
══════════════════════════════════════════ */
function updateApprovalTrack() {
    const val = getVal('nc-auth-level');
    const lbl = document.getElementById('auth-step-lbl');
    const map = {
        directo: 'Emisión directa sin autorización',
        supervisor: 'Esperando aprobación — Supervisor',
        gerencia: 'Esperando aprobación — Gerencia',
        direccion: 'Esperando aprobación — Dirección',
    };
    if (lbl) lbl.textContent = map[val] || 'Pendiente';
}

/* ══════════════════════════════════════════
   REVISAR NC (PASO 5)
══════════════════════════════════════════ */
function buildReview() {
    const rb = document.getElementById('review-body');
    pmSync();
    const type = TYPE_META[STATE.ncType] || { label: 'No seleccionado', color: '#64748b' };
    const partidas = STATE.partidas;

    rb.innerHTML = `
    <div style="display:grid;grid-template-columns:1fr 1fr;gap:16px;">
        <div>
            <div style="font-size:10px;font-weight:700;text-transform:uppercase;color:var(--ink-ghost);letter-spacing:.08em;margin-bottom:8px;">Documento</div>
            <div class="total-line"><span class="lbl">Folio</span><span class="val font-mono">${esc(getVal('nc-folio', '—'))}</span></div>
            <div class="total-line"><span class="lbl">Tipo</span><span style="color:${type.color};font-weight:600;font-size:12px;">${esc(type.label)}</span></div>
            <div class="total-line"><span class="lbl">Fecha</span><span class="val font-mono">${esc(getVal('nc-fecha-emision', '—'))}</span></div>
            <div class="total-line"><span class="lbl">Moneda</span><span class="val">${esc(getVal('nc-moneda'))} · ${esc(getVal('nc-paridad', '1'))}</span></div>
            <div class="total-line"><span class="lbl">Refs. factura</span><span class="val">${STATE.refs.map(r => r.folio).join(', ') || 'Sin referencia'}</span></div>
        </div>
        <div>
            <div style="font-size:10px;font-weight:700;text-transform:uppercase;color:var(--ink-ghost);letter-spacing:.08em;margin-bottom:8px;">Cliente</div>
            <div class="total-line"><span class="lbl">Razón social</span><span class="val">${esc(getVal('nc-cliente', '—'))}</span></div>
            <div class="total-line"><span class="lbl">RFC</span><span class="val font-mono">${esc(getVal('nc-rfc', '—'))}</span></div>
            <div class="total-line"><span class="lbl">Vendedor</span><span class="val">${esc(getVal('nc-vendedor', '—'))}</span></div>
            <div class="total-line"><span class="lbl">Concepto</span><span class="val">${esc(getVal('nc-concepto', '—'))}</span></div>
            <div class="total-line"><span class="lbl">Autorizado por</span><span class="val">${esc(getVal('nc-autorizado-por', '—'))}</span></div>
        </div>
    </div>
    <hr class="total-sep" style="margin:16px 0"/>
    <div style="font-size:10px;font-weight:700;text-transform:uppercase;color:var(--ink-ghost);letter-spacing:.08em;margin-bottom:8px;">Partidas (${partidas.length})</div>
    <div style="border:1px solid var(--edge);border-radius:var(--radius);overflow:hidden;font-size:12px;">
        <table style="width:100%;border-collapse:collapse;">
            <thead><tr style="background:var(--primary-blue);color:#fff;">
                <th style="padding:7px 10px;text-align:left;font-family:var(--font-mono);font-size:10px;">Clave</th>
                <th style="padding:7px 10px;text-align:left;font-family:var(--font-mono);font-size:10px;">Descripción</th>
                <th style="padding:7px 10px;text-align:right;font-family:var(--font-mono);font-size:10px;">Cant</th>
                <th style="padding:7px 10px;text-align:right;font-family:var(--font-mono);font-size:10px;">Precio</th>
                <th style="padding:7px 10px;text-align:right;font-family:var(--font-mono);font-size:10px;">Importe</th>
            </tr></thead>
            <tbody>
                ${partidas.map(p => {
                    const bruto = p.cant * p.precio;
                    const base = bruto * (1 - p.descP / 100);
                    const sub = base * (1 + p.ivaP / 100 + p.iepsP / 100);
        return `<tr style="border-bottom:1px solid var(--edge);">
                        <td style="padding:6px 10px;font-family:var(--font-mono);">${esc(p.clave)}</td>
                        <td style="padding:6px 10px;">${esc(p.desc)}</td>
                        <td style="padding:6px 10px;text-align:right;font-family:var(--font-mono);">${p.cant}</td>
                        <td style="padding:6px 10px;text-align:right;font-family:var(--font-mono);">${fmt(p.precio)}</td>
                        <td style="padding:6px 10px;text-align:right;font-family:var(--font-mono);font-weight:600;">${fmt(sub)}</td>
                    </tr>`;
    }).join('') || '<tr><td colspan="5" style="padding:14px;text-align:center;color:var(--ink-ghost);">Sin partidas</td></tr>'}
            </tbody>
        </table>
    </div>
    <div style="display:flex;justify-content:flex-end;margin-top:16px;">
        <div style="text-align:right;">
            <div class="total-line"><span class="lbl" style="margin-right:40px;">Subtotal</span><span class="val font-mono">${fmt(getVal('nc-subtotal', '0'))}</span></div>
            <div class="total-line disc"><span class="lbl">Descuentos</span><span class="val font-mono">−${fmt(getVal('nc-descuentos', '0'))}</span></div>
            <div class="total-line"><span class="lbl">IVA</span><span class="val font-mono">${fmt(getVal('nc-iva', '0'))}</span></div>
            <hr class="total-sep"/>
            <div style="display:flex;justify-content:space-between;align-items:center;background:var(--primary-blue);color:#fff;padding:10px 14px;border-radius:var(--radius);margin-top:6px;">
                <span style="font-size:11px;font-weight:700;letter-spacing:.08em;text-transform:uppercase;color:rgba(255,255,255,.6);">Total NC</span>
                <span style="font-family:var(--font-mono);font-size:18px;font-weight:700;">${fmt(getVal('nc-total', '0'))}</span>
            </div>
        </div>
    </div>`;
}

/* ══════════════════════════════════════════
   VALIDACIÓN GLOBAL
══════════════════════════════════════════ */
function validar() {
    const errs = [];
    if (!STATE.ncType) errs.push('Selecciona el tipo de nota de crédito (Paso 1)');
    if (!getVal('nc-cliente')) errs.push('Ingresa el cliente (Paso 2)');
    if (!getVal('nc-rfc')) errs.push('Ingresa el RFC del cliente (Paso 2)');
    if (!STATE.refs.length && STATE.ncType !== 'standalone') errs.push('Agrega al menos una factura de referencia (Paso 2)');
    if (!getVal('nc-concepto')) errs.push('Ingresa el concepto / motivo de la NC (Paso 2)');
    if (!STATE.partidas.length) errs.push('Agrega al menos una partida (Paso 3)');
    if (!getVal('nc-cp')) errs.push('Ingresa el código postal fiscal (Paso 4)');
    return errs;
}

/* ══════════════════════════════════════════════════════════════
   GUARDAR / EMITIR
   ─────────────────────────────────────────────────────────────
   FIX PRINCIPAL: construye un FormData PLANO cuyas keys coinciden
   EXACTAMENTE con lo que lee form["key"] en MapearFormulario().

   NO se envía JSON anidado ni application/json — IFormCollection
   de ASP.NET MVC no deserializa objetos anidados.
══════════════════════════════════════════════════════════════ */
async function guardarNC() {
    const errs = validar();
    const ruleErrs = NCRules.validateAll();
    const allErrs = [...errs, ...ruleErrs];

    if (allErrs.length) {
        alert('Corrija los siguientes errores:\n\n• ' + allErrs.join('\n• '));
        return;
    }

    // Sincronizar estado completo antes de leer valores
    pmSync();
    _syncPartidasHidden();
    _syncRefsHidden();

    // El select #nc-regimen NO tiene name="" (para evitar doble envío).
    // Leemos su valor y lo copiamos al hidden justo antes de enviar.
    const regimenVal = document.getElementById('nc-regimen')?.value || '601';
    document.getElementById('nc-regimen-hidden').value = regimenVal;

    // ── FormData plano ────────────────────────────────────────────────────────
    const fd = new FormData();

    // Identificación del documento
    fd.append('folio', getVal('nc-folio'));
    fd.append('serie', getVal('nc-serie', 'NC'));
    fd.append('fechaEmision', getVal('nc-fecha-emision'));
    fd.append('tipoNC', STATE.ncType || '');

    // Moneda
    fd.append('moneda', getVal('nc-moneda', 'MXN'));
    fd.append('paridad', getVal('nc-paridad', '1'));

    // Receptor
    fd.append('cliente', getVal('nc-cliente'));
    fd.append('rfc', getVal('nc-rfc'));
    fd.append('email', getVal('nc-email'));
    fd.append('cp', getVal('nc-cp'));

    // regimen — leído del hidden (el select visible no tiene name)
    fd.append('regimen', regimenVal);

    // idCliente — id numérico interno → nc.IdCliente en INSERT
    fd.append('idCliente', document.getElementById('nc-id-cliente')?.value || '0');

    // Condiciones fiscales
    fd.append('metodoPago', getVal('nc-metodo-pago', 'PUE'));
    fd.append('formaPago', getVal('nc-forma-pago', '99'));
    fd.append('usoCFDI', getVal('nc-uso-cfdi', 'G03'));

    // Campos que solo tenían id pero NO name
    fd.append('formaAplicacion', getVal('nc-forma-aplicacion', 'saldo'));
    fd.append('alcance', getVal('nc-alcance', 'externo'));
    fd.append('nivelAutorizacion', getVal('nc-auth-level', 'directo'));
    fd.append('envEmail', getVal('nc-env-email', 'no'));
    fd.append('genPdf', getVal('nc-gen-pdf', 'no'));
    fd.append('timbrar', getVal('nc-timbrar', 'si'));

    // Concepto y datos de referencia
    fd.append('concepto', getVal('nc-concepto'));
    fd.append('ticket', getVal('nc-ticket'));
    fd.append('almacen', getVal('nc-almacen'));

    // Totales (el controller los recalcula internamente desde partidas,
    // pero los enviamos como respaldo para el INSERT directo)
    fd.append('subtotal', getVal('nc-subtotal', '0'));
    fd.append('descuentos', getVal('nc-descuentos', '0'));
    fd.append('iva', getVal('nc-iva', '0'));
    fd.append('ieps', getVal('nc-ieps', '0'));
    fd.append('total', getVal('nc-total', '0'));

    // refs — JSON array de IDs numéricos de encabezado
    fd.append('refs', document.getElementById('nc-refs-json').value || '[]');

    // encabezadoId — primer encabezado referenciado
    fd.append('encabezadoId', document.getElementById('nc-encabezado-id').value || '0');

    // partidas — JSON array de PartidaItem
    fd.append('partidas', document.getElementById('nc-partidas-json').value || '[]');

    fd.append('__RequestVerificationToken', document.querySelector('input[name="__RequestVerificationToken"]')?.value || '');

    // Decisión de aplicación de saldo
    const decApl = NCRules.leerDecisionAplicacion();
    fd.append('decisionSaldo', decApl.decisionSaldo);
    const inputReembolso = document.getElementById('nc-ref-reembolso');

    if (inputReembolso && inputReembolso.value?.trim()) {
        fd.append('nc-ref-reembolso', inputReembolso.value.trim());
    }
    fd.append('montoADeuda', decApl.montoADeuda);
    fd.append('montoACartera', decApl.montoACartera);
    
      // Sincronizar hiddens del formulario también
    document.getElementById('nc-decision-saldo-hidden').value = decApl.decisionSaldo;
    document.getElementById('nc-monto-deuda-hidden').value    = decApl.montoADeuda;
    document.getElementById('nc-monto-cartera-hidden').value  = decApl.montoACartera;

    const anticiposAplicar = (STATE.anticiposDetectados || [])
        .filter(a => a.montoAplicarNC > 0)
        .map(a => ({
            faId: a.faId,
            anticipoId: a.anticipoId,
            encabezadoId: a.encabezadoId,
            montoAplicar: a.montoAplicarNC
        }));

    fd.append('anticiposAplicar', JSON.stringify(anticiposAplicar));
    // ── Deshabilitar botones durante el envío ─────────────────────────────────
    const btns = document.querySelectorAll('[onclick="guardarNC()"]');
    btns.forEach(b => {
        b.disabled = true;
        b.innerHTML = '<i class="fas fa-spinner fa-spin me-1"></i>Emitiendo...';
    });

    try {
        // SIN Content-Type manual — el browser lo pone con el boundary correcto
        const res = await fetch('/VINotaCredito/ProcesarNCAsync', {
            method: 'POST',
            body: fd,
        });

        // ── Error HTTP (4xx / 5xx) ────────────────────────────────────────────
        if (!res.ok) {
            const errorText = await res.text().catch(() => '');
            mostrarErrorServidor(res.status, errorText);
            return;
        }

        // ── JSON malformado ───────────────────────────────────────────────────
        let r;
        try {
            r = await res.json();
        } catch {
            mostrarErrorFormato();
            return;
        }

        // ── Respuesta del servidor ────────────────────────────────────────────
        if (r.success) {
            // Éxito: abre el modal detallado con UUID, descarga, correo, etc.
            const cantDocs = STATE.partidas.length;
            mostrarResumenExitoso(r, cantDocs);

            //// Si el usuario pidió generar PDF también lo descargamos localmente
            //if (getVal('nc-gen-pdf') === 'si') downloadPDF();

        } else if (r.step) {
            // Error en un paso específico del backend
            // (GuardarDocumentoNC, GenerarNotaCredito, etc.)
            // mostrarErrorPorPaso muestra código SAT, rollback info y copiado
            mostrarErrorPorPaso(r, STATE.partidas.length);

        } else {
            // Error genérico del servidor sin información de paso
            mostrarErrorServidor(res.status, r.message || 'El servidor rechazó la solicitud');
        }

    } catch (e) {
        // ── Error de red / timeout / CORS / fetch rechazado ───────────────────
        mostrarErrorConexion(e);

    } finally {
        // Siempre rehabilitar botones
        btns.forEach(b => {
            b.disabled = false;
            b.innerHTML = '<i class="fas fa-paper-plane me-1"></i>Emitir NC';
        });
    }
}

/* ══════════════════════════════════════════
   VISTA PREVIA
══════════════════════════════════════════ */
function openPreview() {
    pmSync();
    const type = TYPE_META[STATE.ncType] || { label: '—', color: '#1e3a5f' };
    const pp = document.getElementById('preview-paper');
    const partidas = STATE.partidas;
    pp.innerHTML = `
    <div class="preview-header-strip"></div>
    <div class="preview-title-row">
        <div>
            <div class="preview-nc-title">NOTA DE CRÉDITO</div>
            <div style="font-size:13px;color:#666;margin-top:4px;"><span style="color:${type.color};font-weight:700;">${esc(type.label)}</span></div>
        </div>
        <div class="preview-meta">
            <strong style="font-size:16px;">Folio: ${esc(getVal('nc-folio', '—'))}</strong>
            <span>Fecha: ${esc(getVal('nc-fecha-emision', '—'))}</span>
            <span>Refs: ${STATE.refs.map(r => r.folio).join(', ') || 'S/R'}</span>
        </div>
    </div>
    <div class="preview-section-title">CLIENTE</div>
    <div class="preview-grid-2">
        <div>
            <div class="preview-field"><div class="lbl">Razón Social</div><div class="val">${esc(getVal('nc-cliente', '—'))}</div></div>
            <div class="preview-field" style="margin-top:6px;"><div class="lbl">RFC</div><div class="val">${esc(getVal('nc-rfc', '—'))}</div></div>
            <div class="preview-field" style="margin-top:6px;"><div class="lbl">Dirección</div><div class="val">${esc(getVal('nc-dir-cliente', '—'))}</div></div>
        </div>
        <div>
            <div class="preview-field"><div class="lbl">Vendedor</div><div class="val">${esc(getVal('nc-vendedor', '—'))}</div></div>
            <div class="preview-field" style="margin-top:6px;"><div class="lbl">Concepto</div><div class="val">${esc(getVal('nc-concepto', '—'))}</div></div>
            <div class="preview-field" style="margin-top:6px;"><div class="lbl">Moneda</div><div class="val">${esc(getVal('nc-moneda'))} · ${esc(getVal('nc-paridad', '1'))}</div></div>
        </div>
    </div>
    <div class="preview-section-title">PARTIDAS</div>
    <table class="preview-tbl">
        <thead><tr>
            <th>Clave</th><th>Descripción</th>
            <th style="text-align:center">Cant</th><th style="text-align:right">Precio</th>
            <th style="text-align:center">Desc%</th><th style="text-align:center">IVA%</th>
            <th style="text-align:right">Importe</th>
        </tr></thead>
        <tbody>
            ${partidas.map(p => {
                const bruto = p.cant * p.precio;
                const base = bruto * (1 - p.descP / 100);
                const sub = base * (1 + p.ivaP / 100 + p.iepsP / 100);
        return `<tr>
                    <td>${esc(p.clave)}</td><td>${esc(p.desc)}</td>
                    <td style="text-align:center">${p.cant}</td>
                    <td style="text-align:right">${fmt(p.precio)}</td>
                    <td style="text-align:center">${p.descP}%</td>
                    <td style="text-align:center">${p.ivaP}%</td>
                    <td style="text-align:right;font-weight:700;">${fmt(sub)}</td>
                </tr>`;
    }).join('') || '<tr><td colspan="7" style="text-align:center;color:#999;padding:12px;">Sin partidas</td></tr>'}
        </tbody>
    </table>
    <div class="clearfix" style="margin-top:14px;">
        <div class="preview-totals-box">
            <div class="preview-totals-row"><span>Subtotal</span><strong>${fmt(getVal('nc-subtotal', '0'))}</strong></div>
            <div class="preview-totals-row" style="color:#dc2626;"><span>Descuentos</span><strong>−${fmt(getVal('nc-descuentos', '0'))}</strong></div>
            <div class="preview-totals-row"><span>Base gravable</span><strong>${fmt(getVal('nc-base', '0'))}</strong></div>
            <div class="preview-totals-row"><span>IVA</span><strong>${fmt(getVal('nc-iva', '0'))}</strong></div>
            <div class="preview-totals-row grand"><span>TOTAL NC</span><strong>${fmt(getVal('nc-total', '0'))}</strong></div>
        </div>
        <div style="margin-top:8px;font-size:10px;color:#666;">
            <strong>Condiciones fiscales:</strong><br>
            Método de pago: ${esc(getTxt('nc-metodo-pago', '—'))}<br>
            Forma de pago: ${esc(getTxt('nc-forma-pago', '—'))}<br>
            Uso CFDI: ${esc(getTxt('nc-uso-cfdi', '—'))}<br>
            Autorizado por: ${esc(getVal('nc-autorizado-por', '—'))}
        </div>
    </div>
    <div style="clear:both;margin-top:20px;padding-top:10px;border-top:1px solid #eee;text-align:center;color:#aaa;font-size:9px;">
        Nota de Crédito generada por el sistema ERP · ${new Date().toLocaleString('es-MX')}
    </div>`;
    openModal('modal-preview');
}

/* ══════════════════════════════════════════
   DESCARGA PDF
══════════════════════════════════════════ */
//async function downloadPDF() {
//    pmSync();
//    const { jsPDF } = window.jspdf;
//    const doc = new jsPDF('p', 'mm', 'a4');
//    const pw = doc.internal.pageSize.getWidth();
//    const ph = doc.internal.pageSize.getHeight();
//    const m = 12;
//    const R = [30, 58, 95]; const ACC = [59, 130, 246]; const INK = [15, 23, 42];
//    const W = [255, 255, 255]; const G = [248, 250, 252]; const BRD = [226, 232, 240]; const SOFT = [100, 116, 139];
//    const type = TYPE_META[STATE.ncType] || { label: 'Sin tipo' };

//    doc.setFillColor(...INK); doc.rect(0, 0, pw, 16, 'F');
//    doc.setFillColor(...ACC); doc.rect(0, 14, pw, 2, 'F');
//    doc.setFont('helvetica', 'bold'); doc.setFontSize(16); doc.setTextColor(...W);
//    doc.text('NOTA DE CRÉDITO', m, 9);
//    doc.setFont('helvetica', 'normal'); doc.setFontSize(7);
//    doc.text(`Folio: ${getVal('nc-folio', '—')}  |  ${new Date().toLocaleDateString('es-MX')}`, pw - m, 5, { align: 'right' });
//    doc.text(`Tipo: ${type.label}`, pw - m, 9, { align: 'right' });
//    doc.text(`Refs: ${STATE.refs.map(r => r.folio).join(', ') || 'Sin referencia'}`, pw - m, 13, { align: 'right' });

//    let y = 21;
//    const cw = (pw - 3 * m) / 2;
//    doc.setFillColor(...G); doc.setDrawColor(...BRD); doc.setLineWidth(.4);
//    doc.roundedRect(m, y, cw, 26, 1.5, 1.5, 'FD');
//    doc.setFont('helvetica', 'bold'); doc.setFontSize(7.5); doc.setTextColor(...R); doc.text('CLIENTE', m + 3, y + 5);
//    doc.setFont('helvetica', 'normal'); doc.setFontSize(7); doc.setTextColor(60, 70, 85);
//    doc.text(getVal('nc-cliente', '—').substring(0, 45), m + 3, y + 10);
//    doc.text(`RFC: ${getVal('nc-rfc', '—')}`, m + 3, y + 14);
//    doc.text(`Vendedor: ${getVal('nc-vendedor', '—')}`, m + 3, y + 18);
//    doc.text(`Concepto: ${getVal('nc-concepto', '—').substring(0, 38)}`, m + 3, y + 22);
//    const cx = m + cw + m;
//    doc.roundedRect(cx, y, cw, 26, 1.5, 1.5, 'FD');
//    doc.setFont('helvetica', 'bold'); doc.setFontSize(7.5); doc.setTextColor(...R); doc.text('CONDICIONES', cx + 3, y + 5);
//    doc.setFont('helvetica', 'normal'); doc.setFontSize(7); doc.setTextColor(60, 70, 85);
//    doc.text(`Moneda: ${getVal('nc-moneda')} · ${getVal('nc-paridad', '1')}`, cx + 3, y + 10);
//    doc.text(`Método: ${getTxt('nc-metodo-pago', '—')}`, cx + 3, y + 14);
//    doc.text(`Forma pago: ${getTxt('nc-forma-pago', '—').substring(0, 30)}`, cx + 3, y + 18);
//    doc.text(`CFDI: ${getTxt('nc-uso-cfdi', '—').substring(0, 35)}`, cx + 3, y + 22);
//    y += 30;

//    const rows = STATE.partidas.map(p => {
//        const sub = p.cant * p.precio * (1 - p.descP / 100) * (1 + p.ivaP / 100);
//        return [p.clave, p.desc.substring(0, 40), p.cant, fmt(p.precio), `${p.descP}%`, `${p.ivaP}%`, fmt(sub)];
//    });
//    if (!rows.length) rows.push(['Sin partidas', '', '', '', '', '', '']);

//    doc.autoTable({
//        startY: y,
//        head: [['Clave', 'Descripción', 'Cant.', 'Precio', 'Desc%', 'IVA%', 'Importe']],
//        body: rows, theme: 'grid',
//        headStyles: { fillColor: INK, textColor: W, fontSize: 7, fontStyle: 'bold', cellPadding: 2.5 },
//        bodyStyles: { fontSize: 7, textColor: [60, 70, 85], cellPadding: 2 },
//        alternateRowStyles: { fillColor: [225, 231, 239] },
//        columnStyles: { 0: { cellWidth: 20 }, 2: { halign: 'center', cellWidth: 12 }, 3: { halign: 'right', cellWidth: 18 }, 4: { halign: 'center', cellWidth: 12 }, 5: { halign: 'center', cellWidth: 12 }, 6: { halign: 'right', fontStyle: 'bold', cellWidth: 22 } },
//        margin: { left: m, right: m },
//    });

//    y = doc.lastAutoTable.finalY + 6;
//    const sx = pw - m - 55, sw = 51;
//    doc.setFillColor(...G); doc.setDrawColor(...BRD); doc.setLineWidth(.4);
//    doc.roundedRect(sx - 1.5, y - 1, sw + 3, 38, 1.5, 1.5, 'FD');
//    doc.setFont('helvetica', 'normal'); doc.setFontSize(7);
//    let sy = y + 3;
//    [['Subtotal:', 'nc-subtotal'], ['Descuentos:', 'nc-descuentos'], ['Base:', 'nc-base'], ['IVA:', 'nc-iva']].forEach(([l, id]) => {
//        doc.setTextColor(...SOFT); doc.text(l, sx + 1.5, sy);
//        doc.setTextColor(...INK); doc.text(fmt(+getVal(id, '0')), sx + sw - 1.5, sy, { align: 'right' });
//        sy += 3.5;
//    });
//    doc.setDrawColor(...ACC); doc.setLineWidth(.7); doc.line(sx + .5, sy - .5, sx + sw - .5, sy - .5); sy += 1.5;
//    doc.setFillColor(...R); doc.roundedRect(sx - .5, sy - 3, sw + 1, 6, .5, .5, 'F');
//    doc.setTextColor(...W); doc.setFont('helvetica', 'bold'); doc.setFontSize(9.5);
//    doc.text('TOTAL NC:', sx + 1.5, sy);
//    doc.text(fmt(+getVal('nc-total', '0')), sx + sw - 1.5, sy, { align: 'right' });

//    if (getVal('nc-autorizado-por')) {
//        y = doc.lastAutoTable.finalY + 12;
//        doc.setFont('helvetica', 'normal'); doc.setFontSize(7); doc.setTextColor(...SOFT);
//        doc.text(`Autorizado por: ${getVal('nc-autorizado-por')}  ·  Auth#: ${getVal('nc-num-auth', '—')}`, m, y + 3);
//    }
//    doc.setDrawColor(...ACC); doc.setLineWidth(.6); doc.line(0, ph - 8, pw, ph - 8);
//    doc.setFont('helvetica', 'normal'); doc.setFontSize(6); doc.setTextColor(...SOFT);
//    doc.text('Nota de Crédito — Sistema ERP', m, ph - 4);
//    doc.text(new Date().toLocaleString('es-MX'), pw - m, ph - 4, { align: 'right' });
//    doc.save(`NC-${getVal('nc-folio', 'SN')}-${Date.now()}.pdf`);
//}

/* ══════════════════════════════════════════
   INICIALIZACIÓN
══════════════════════════════════════════ */
document.addEventListener('DOMContentLoaded', () => {

    // ── 1. Fecha default ────────────────────────────────────────
    document.getElementById('nc-fecha-emision').value = new Date().toISOString().slice(0, 10);
    document.getElementById('tb-fecha').textContent = new Date().toLocaleDateString('es-MX');

    // ── 2. Sidebar en tiempo real ───────────────────────────────
    document.getElementById('nc-cliente').addEventListener('input', e =>
        setTxt('side-cliente', e.target.value || '—'));
    document.getElementById('nc-folio').addEventListener('input', e =>
        setTxt('tb-folio', e.target.value));

    // Sincronizar hidden de régimen cuando el usuario cambia el select visible
    document.getElementById('nc-regimen')?.addEventListener('change', e => {
        document.getElementById('nc-regimen-hidden').value = e.target.value;
    });

    // ── 3. Cerrar modales con click fuera / Escape ──────────────
    document.querySelectorAll('.modal-overlay').forEach(m => {
        m.addEventListener('click', e => { if (e.target === m) closeModal(m.id); });
    });
    document.addEventListener('keydown', e => {
        if (e.key === 'Escape')
            document.querySelectorAll('.modal-overlay.open').forEach(m => closeModal(m.id));
    });

    document.getElementById('nc-rfc').addEventListener('input', e => {
        const hint = document.getElementById('ref-cliente-hint');
        if (hint) hint.style.display = e.target.value.trim() ? 'none' : 'inline';
    });

    // ── 5. Cargar catálogos (moneda) y DESPUÉS auto-seleccionar ─
    //    Es crítico esperar a que tomManager inicialice el select
    //    antes de llamar a selectType(), porque selectType() puede
    //    disparar recalc() → getVal('nc-moneda') que necesita TomSelect.
    GetData({ path: '/DatosGenerales/DatosSelect' }).then((_res) => {
        const monedas = _res.monedas;
        const tasas = _res.tasas;
        const usocfdi = _res.usocfdi;

        tomManager.create('nc-moneda', {
            selector: '#nc-moneda',
            options: monedas,
            valueField: 'id',
            displayField: 'nombre',
            searchField: ['id', 'nombre'],
            placeholder: 'Seleccione una moneda...',
            maxItems: 1,
            create: false,
            render: {
                option: (data, escape) =>
                    `<div style="color:var(--text-dark);">${escape(data.nombre)} (ID: ${escape(data.id)})</div>`,
                item: (data, escape) =>
                    `<div style="color:var(--text-dark);">${escape(data.nombre)}</div>`,
            },
            onDropdownOpen(dropdown) {
                const rect = this.control.getBoundingClientRect();
                dropdown.style.position = 'fixed';
                dropdown.style.top = `${rect.bottom + 2}px`;
                dropdown.style.left = `${rect.left}px`;
                dropdown.style.width = `${rect.width}px`;
                dropdown.style.zIndex = '9999';
            },
            onChange: (value) => {
                let v;
                if (value === 'PESOS') v = 'peso';
                else if (value === 'DLLS') v = 'dolar';
                else if (value === 'EURO') v = 'euro';
                const tasaData = tasas?.[0];
                const parInput = document.getElementById('nc-paridad');
                if (tasaData && v && tasaData[v] !== undefined) parInput.value = tasaData[v];
                else parInput.value = '';
                recalc();
            },
        });

        tomManager.create('nc-uso-cfdi', {
            selector: '#nc-uso-cfdi',
            options: usocfdi,
            valueField: 'id',
            displayField: 'nombre',
            searchField: ['id', 'nombre'],
            placeholder: 'Seleccione una moneda...',
            maxItems: 1,
            create: false,
            render: {
                option: (data, escape) =>
                    `<div style="color:var(--text-dark);">${escape(data.nombre)} (ID: ${escape(data.id)})</div>`,
                item: (data, escape) =>
                    `<div style="color:var(--text-dark);">${escape(data.nombre)}</div>`,
            },
            onDropdownOpen(dropdown) {
                const rect = this.control.getBoundingClientRect();
                dropdown.style.position = 'fixed';
                dropdown.style.top = `${rect.bottom + 2}px`;
                dropdown.style.left = `${rect.left}px`;
                dropdown.style.width = `${rect.width}px`;
                dropdown.style.zIndex = '9999';
            },
           
        });

        const monedaTs = tomManager.getInstance('nc-moneda');
        if (monedaTs) monedaTs.setValue('PESOS', false);

    }).catch(err => {
        console.error('[NC] Error cargando catálogos:', err);
    });

    // ── 7. Restaurar borrador si existe y es reciente ───────────
    try {
        const saved = localStorage.getItem('nc_borrador');
        if (saved) {
            const d = JSON.parse(saved);
            const mins = (Date.now() - d.ts) / 60000;
            if (mins < 120) {
                toastMixin.fire({
                    title: `Hay un borrador de NC de hace ${Math.round(mins)} min. ¿Restaurar?`,
                    showCancelButton: true,
                    confirmButtonText: 'Sí, restaurar',
                }).then(r => { if (r.isConfirmed) restaurarBorrador(d); });
            }
        }
    } catch (e) { /* storage no disponible */ }

});


// ══════════════════════════════════════════════════════════════
//  _autoSelectTipo — sin cambios de lógica, solo más defensivo
// ══════════════════════════════════════════════════════════════
function _autoSelectTipo(tipoNC) {
    const mapa = {
        devolucion: 'devolucion',
        descuento: 'descuento',
        precio: 'precio',
        cancelacion: 'cancelacion',
        bonificacion: 'bonificacion',
        otro: 'standalone',
    };
    const tipo = mapa[tipoNC] || tipoNC;

    const card = document.querySelector(`.nc-type-card[data-type="${tipo}"]`);
    if (card) {
        // El selector está visible — selectType hace todo
        selectType(card);
    } else {
        // El selector está oculto (viene de solicitud) — aplicar estado mínimo
        // sin llamar a selectType() que depende de la card para efectos visuales
        STATE.ncType = tipo;
        document.getElementById('nc-tipo-sel').value = tipo;

        // Limpiar contextos anteriores de otros tipos
        document.querySelectorAll('.nc-ctx').forEach(c => c.classList.remove('visible'));
        const ctx = document.getElementById(`ctx-${tipo}`);
        if (ctx) ctx.classList.add('visible');

        // Actualizar sidebar
        const meta = TYPE_META[tipo];
        if (meta) {
            const swatch = document.getElementById('side-type-dot');
            if (swatch) swatch.style.background = meta.color;
            setTxt('side-type-name', meta.label);
        }

        // Disparar reglas de negocio sin depender del DOM del selector
        NCRules.onTypeSelected(tipo);
    }
}

// Mapea el tipo al ícono de Font Awesome correcto para el banner
function _getIconoTipo(tipo) {
    const iconos = {
        devolucion: 'fa-undo-alt',
        descuento: 'fa-percent',
        precio: 'fa-dollar-sign',
        cancelacion: 'fa-ban',
        bonificacion: 'fa-gift',
        standalone: 'fa-file-alt',
    };
    return iconos[tipo] || 'fa-tag';
}

// Después de cada cambio significativo:
function persistirBorrador() {
    try {
        localStorage.setItem('nc_borrador', JSON.stringify({
            ncType: STATE.ncType,
            refs: STATE.refs,
            partidas: STATE.partidas,
            cliente: getVal('nc-cliente'),
            rfc: getVal('nc-rfc'),
            folio: getVal('nc-folio'),
            ts: Date.now()
        }));
    } catch (e) { /* storage lleno, ignorar */ }
}

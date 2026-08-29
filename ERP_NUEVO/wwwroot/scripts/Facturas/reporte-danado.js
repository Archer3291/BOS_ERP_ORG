/* ══════════════════════════════════════════════════════════════
   ESTADO GLOBAL
══════════════════════════════════════════════════════════════ */
const RMD_STATE = {
    seleccionadas: [],      // [{id, folio, cliente, rfc, imp, ccy, fecha}]
    tipoDanio: null,        // clave del tipo de daño seleccionado
    currentTab: 'nueva',
    partidasDanadas: [],    // [{encabezadoId, productoId, descripcion, cantTotal, cantDanada}]
};

/* ══════════════════════════════════════════════════════════════
   CATÁLOGO DE TIPOS DE DAÑO
══════════════════════════════════════════════════════════════ */
const TIPOS_DANIO = {
    fisico: { label: 'Daño físico', icon: 'fa-box-open', color: '#dc2626', estatusId: 40 },
    incompleto: { label: 'Incompleto', icon: 'fa-puzzle-piece', color: '#d97706', estatusId: 41 },
    caducado: { label: 'Caducado / Vencido', icon: 'fa-calendar-times', color: '#7c3aed', estatusId: 42 },
    calidad: { label: 'Problema de calidad', icon: 'fa-ban', color: '#0891b2', estatusId: 43 },
    mojado: { label: 'Humedad / Mojado', icon: 'fa-tint', color: '#059669', estatusId: 44 },
    otro: { label: 'Otro', icon: 'fa-ellipsis-h', color: '#64748b', estatusId: 45 },
};

/* ══════════════════════════════════════════════════════════════
   UTILIDADES
══════════════════════════════════════════════════════════════ */
function rmdEsc(t) {
    const d = document.createElement('div');
    d.textContent = String(t ?? '');
    return d.innerHTML;
}

function rmdFmt(n, ccy) {
    const sym = { MXN: '$', USD: 'US$', EUR: '€', CAD: 'CA$' };
    return (sym[ccy || 'MXN'] || '$') +
        Number(n).toFixed(2).replace(/\B(?=(\d{3})+(?!\d))/g, ',');
}

function rmdToast(msg, type = 'info') {
    const c = document.getElementById('rmd-toast-container');
    if (!c) return;
    const t = document.createElement('div');
    t.className = `rmd-toast ${type}`;
    const icon = type === 'ok' ? 'check' : type === 'err' ? 'times' : type === 'warn' ? 'exclamation-triangle' : 'info';
    t.innerHTML = `<i class="fas fa-${icon}-circle me-2"></i>${rmdEsc(msg)}`;
    c.appendChild(t);
    requestAnimationFrame(() => t.classList.add('show'));
    setTimeout(() => {
        t.classList.remove('show');
        setTimeout(() => t.remove(), 300);
    }, 3500);
}

function rmdGetVal(id, def = '') {
    return document.getElementById(id)?.value?.trim() || def;
}

function rmdFieldError(id, msg) {
    const el = document.getElementById(id);
    if (!el) return;
    el.classList.remove('field-error');
    el.parentElement.querySelector('.field-error-msg')?.remove();
    if (msg) {
        el.classList.add('field-error');
        const span = document.createElement('span');
        span.className = 'field-error-msg';
        span.textContent = msg;
        el.parentElement.appendChild(span);
    }
}

/* ══════════════════════════════════════════════════════════════
   TABS
══════════════════════════════════════════════════════════════ */
function rmdSwitchTab(tab) {
    RMD_STATE.currentTab = tab;
    document.querySelectorAll('.rmd-tab').forEach(t =>
        t.classList.toggle('active', t.dataset.tab === tab));
    document.querySelectorAll('.rmd-tab-panel').forEach(p =>
        p.classList.toggle('active', p.id === `rmd-panel-${tab}`));
    if (tab === 'historial') rmdLoadHistorial();
}

/* ══════════════════════════════════════════════════════════════
   TABLA DE FACTURAS — createTable
══════════════════════════════════════════════════════════════ */
let rmdTblInstance = null;

function rmdInitTabla() {
    rmdTblInstance = createTable({
        selector: '#rmd-facturas-table',
        path: '/ReporteDanado/ObtenerFacturasVendedor',
        searchPlaceholder: 'Buscar por folio, cliente o RFC...',
        defaultPageSize: 10,

        // Datos extra que se mandan en cada POST (fechas del filtro externo)
        data: () => ({
            desde: document.getElementById('rmd-filter-desde')?.value ?? '',
            hasta: document.getElementById('rmd-filter-hasta')?.value ?? '',
        }),

        // Selección múltiple de filas
        selectable: {
            enabled: true,
            rowKey: 'id_encabezado',
            condition: row => !row.rmd_activo,   // no seleccionar si ya tiene reporte activo
            onChange: filas => {
                // Sincronizar con RMD_STATE.seleccionadas
                RMD_STATE.seleccionadas = filas.map(f => ({
                    id: f.id_encabezado,
                    folio: f.folio,
                    cliente: f.n_cli || f.cli_prov || '—',
                    rfc: f.rfc || '—',
                    imp: f.imp || 0,
                    ccy: f.ccy || 'MXN',
                    fecha: f.fch || '',
                }));
                rmdUpdateSidebar();
            },
        },

        rowKey: 'id_encabezado',

        columns: [
            {
                title: 'Folio',
                data: 'folio',
                formatter: (v, row) => {
                    const badge = row.rmd_activo
                        ? `<span class="rmd-badge pending ms-1" style="font-size:9px;">
                               <i class="fas fa-clock" style="font-size:8px;"></i> Reporte activo
                           </span>`
                        : '';
                    return `<span class="font-mono" style="font-weight:600;color:var(--accent-blue);">
                                ${rmdEsc(v)}
                            </span>${badge}`;
                },
            },
            {
                title: 'Cliente',
                data: 'n_cli',
                formatter: (v, row) => rmdEsc(v || row.cli_prov || '—'),
            },
            {
                title: 'RFC',
                data: 'rfc',
                className: 'd-none d-md-table-cell',
                formatter: v => `<span class="font-mono" style="font-size:11px;color:var(--ink-soft);">
                                     ${rmdEsc(v || '—')}
                                 </span>`,
            },
            {
                title: 'Importe',
                data: 'imp',
                formatter: (v, row) =>
                    `<span class="font-mono" style="font-weight:600;color:var(--ok);">
                         ${rmdFmt(v, row.ccy)}
                     </span>`,
            },
            {
                title: 'Fecha',
                data: 'fch',
                className: 'd-none d-sm-table-cell',
                formatter: v => `<span class="font-mono" style="font-size:11px;">
                                     ${typeof dateFormatter === 'function' ? rmdEsc(dateFormatter(v)) : rmdEsc(v)}
                                 </span>`,
            },
            {
                title: 'Estado',
                data: 'estatus_txt',
                className: 'd-none d-lg-table-cell',
                formatter: (v, row) =>
                    `<span class="rmd-badge ${_rmdMapStatus(row.estatus_id)}">
                         ${rmdEsc(v || 'Activa')}
                     </span>`,
            },
        ],

        actions: [
            {
                icon: 'fa-eye',
                color: 'btn-outline-secondary',
                title: 'Ver detalle',
                onClick: row => rmdVerDetalle(row.id_encabezado),
            },
        ],

        GlobalEvents: {
            onDataLoaded: () => {
                // Después de cada carga, sincronizar checkboxes con la selección actual
                // (createTable ya lo hace vía selectable.initialSelected si lo configuras,
                //  pero aquí lo dejamos simple)
            },
        },
    });
}

// Mantenemos _rmdMapStatus que usan las columnas de la tabla y el historial
function _rmdMapStatus(estatusId) {
    if (!estatusId) return 'gray';
    const map = { 1: 'ok', 11: 'info', 2: 'warn' };
    return map[estatusId] || 'gray';
}
/* ══════════════════════════════════════════════════════════════
   SELECCIÓN DE FILAS
══════════════════════════════════════════════════════════════ */
function rmdToggleRow(id, trEl, facturaData) {
    const idx = RMD_STATE.seleccionadas.findIndex(s => s.id === id);
    if (idx >= 0) {
        RMD_STATE.seleccionadas.splice(idx, 1);
        trEl?.classList.remove('selected');
        document.getElementById(`rmd-chk-${id}`)?.classList.remove('checked');
    } else {
        RMD_STATE.seleccionadas.push({
            id: facturaData.id_encabezado,
            folio: facturaData.folio,
            cliente: facturaData.n_cli || facturaData.cli_prov || '—',
            rfc: facturaData.rfc || '—',
            imp: facturaData.imp || 0,
            ccy: facturaData.ccy || 'MXN',
            fecha: facturaData.fch || '',
        });
        trEl?.classList.add('selected');
        document.getElementById(`rmd-chk-${id}`)?.classList.add('checked');
    }
    rmdUpdateSidebar();
    rmdUpdateSelectAll();
}

function rmdDeseleccionarDeSidebar(id) {
    RMD_STATE.seleccionadas = RMD_STATE.seleccionadas.filter(s => s.id !== id);
    document.getElementById(`rmd-row-${id}`)?.classList.remove('selected');
    document.getElementById(`rmd-chk-${id}`)?.classList.remove('checked');
    rmdUpdateSidebar();
    rmdUpdateSelectAll();
}

function rmdSelectAll() {
    const rows = document.querySelectorAll('#rmd-tbody tr[id^="rmd-row-"]');
    const allSelected = [...rows].every(r => r.classList.contains('selected'));

    rows.forEach(tr => {
        const id = parseInt(tr.id.replace('rmd-row-', ''));
        if (allSelected) {
            RMD_STATE.seleccionadas = RMD_STATE.seleccionadas.filter(s => s.id !== id);
            tr.classList.remove('selected');
            document.getElementById(`rmd-chk-${id}`)?.classList.remove('checked');
        } else {
            if (!RMD_STATE.seleccionadas.find(s => s.id === id)) {
                const celdas = tr.querySelectorAll('td');
                RMD_STATE.seleccionadas.push({
                    id,
                    folio: celdas[1]?.querySelector('.font-mono')?.textContent?.trim() || '',
                    cliente: celdas[2]?.textContent?.trim() || '',
                    rfc: celdas[3]?.textContent?.trim() || '',
                    imp: parseFloat(celdas[4]?.textContent?.replace(/[^0-9.-]/g, '') || '0'),
                    ccy: 'MXN',
                });
            }
            tr.classList.add('selected');
            document.getElementById(`rmd-chk-${id}`)?.classList.add('checked');
        }
    });

    rmdUpdateSidebar();
    rmdUpdateSelectAll();
}

function rmdUpdateSelectAll() {
    const allChk = document.getElementById('rmd-chk-all');
    if (!allChk) return;
    const rows = document.querySelectorAll('#rmd-tbody tr[id^="rmd-row-"]');
    const selected = [...rows].filter(r => r.classList.contains('selected'));
    allChk.classList.remove('checked', 'indeterminate');
    if (selected.length === 0) { /* vacío */ }
    else if (selected.length === rows.length) allChk.classList.add('checked');
    else allChk.classList.add('indeterminate');
}

/* ══════════════════════════════════════════════════════════════
   SIDEBAR
══════════════════════════════════════════════════════════════ */
function rmdUpdateSidebar() {
    const list = document.getElementById('rmd-sb-list');
    const badge = document.getElementById('rmd-sb-badge');
    const count = document.getElementById('rmd-sb-count');
    const total = RMD_STATE.seleccionadas.reduce((s, f) => s + (parseFloat(f.imp) || 0), 0);
    const n = RMD_STATE.seleccionadas.length;

    if (badge) {
        badge.textContent = n > 0 ? `${n} sel.` : 'Sin selección';
        badge.classList.toggle('ready', n > 0);
    }
    if (count) count.textContent = `${n} factura${n !== 1 ? 's' : ''}`;

    document.getElementById('rmd-sb-total-val').textContent = n > 0 ? rmdFmt(total) : '$0.00';

    const btn = document.getElementById('rmd-btn-procesar');
    const btnHead = document.getElementById('rmd-btn-procesar-head');
    if (btn) btn.disabled = n === 0;
    if (btnHead) {
        btnHead.style.display = n > 0 ? 'inline-flex' : 'none';
        const span = document.getElementById('rmd-sel-count-head');
        if (span) span.textContent = n;
    }

    if (!list) return;

    if (!n) {
        list.innerHTML = `<div class="rmd-sb-empty">
            <i class="fas fa-hand-pointer me-2" style="opacity:.4;"></i>
            Selecciona facturas de la tabla
        </div>`;
        return;
    }

    list.innerHTML = RMD_STATE.seleccionadas.map(f => `
    <div class="rmd-sel-pill" id="rmd-pill-${f.id}">
        <div style="min-width:0">
            <div class="rmd-sel-pill-folio">${rmdEsc(f.folio)}</div>
            <div style="font-size:10px;color:var(--ink-ghost);white-space:nowrap;overflow:hidden;text-overflow:ellipsis;max-width:140px;">
                ${rmdEsc(f.cliente)}
            </div>
        </div>
        <div style="display:flex;align-items:center;gap:6px;flex-shrink:0;">
            <span class="rmd-sel-pill-imp">${rmdFmt(f.imp, f.ccy)}</span>
            <button class="rmd-sel-pill-del" onclick="rmdDeseleccionarDeSidebar(${f.id})" title="Quitar">
                <i class="fas fa-times"></i>
            </button>
        </div>
    </div>`).join('');
}


/* ══════════════════════════════════════════════════════════════
   MODAL — VER DETALLE
══════════════════════════════════════════════════════════════ */
async function rmdVerDetalle(encabezadoId) {
    openRmdModal('rmd-modal-detalle');
    const body = document.getElementById('rmd-detalle-body');
    body.innerHTML = `<div style="text-align:center;padding:30px;color:var(--ink-ghost);">
        <i class="fas fa-spinner fa-spin fa-2x" style="opacity:.4;"></i></div>`;

    try {
        const res = await fetch(`/DatosGenerales/BuscarFacturaDetalle?id=${encabezadoId}`);
        if (!res.ok) throw new Error('HTTP ' + res.status);
        const data = await res.json();
        if (!data?.length) throw new Error('Sin datos');

        const d = data[0];
        const prods = d.productos || [];

        body.innerHTML = `
        <div style="display:grid;grid-template-columns:1fr 1fr;gap:14px;margin-bottom:16px;">
            <div>
                <div class="field-section-label">Encabezado</div>
                <div style="font-size:12px;display:flex;flex-direction:column;gap:5px;">
                    <div><span style="color:var(--ink-soft);">Folio:</span> <strong class="font-mono">${rmdEsc(d.folio)}</strong></div>
                    <div><span style="color:var(--ink-soft);">Cliente:</span> ${rmdEsc(d.n_cli || d.cli_prov)}</div>
                    <div><span style="color:var(--ink-soft);">RFC:</span> <span class="font-mono">${rmdEsc(d.rfc || '—')}</span></div>
                    <div><span style="color:var(--ink-soft);">Fecha:</span> <span class="font-mono">${rmdEsc((typeof dateFormatter === 'function' ? dateFormatter(d.fch0) : d.fch0) || '—')}</span></div>
                </div>
            </div>
            <div>
                <div class="field-section-label">Importes</div>
                <div style="font-size:12px;display:flex;flex-direction:column;gap:5px;">
                    <div><span style="color:var(--ink-soft);">Moneda:</span> ${rmdEsc(d.ccy || 'MXN')}</div>
                    <div><span style="color:var(--ink-soft);">Importe:</span> <strong class="font-mono text-ok">${rmdFmt(d.total, d.ccy)}</strong></div>
                </div>
            </div>
        </div>
        <div class="field-section-label">Partidas (${prods.length})</div>
        <div class="rmd-partidas-detalle-wrap">
            <table class="rmd-partidas-tbl">
                <thead><tr>
                    <th>Clave</th><th>Descripción</th>
                    <th style="text-align:right">Cant.</th>
                    <th style="text-align:right">Precio</th>
                    <th style="text-align:right">Importe</th>
                </tr></thead>
                <tbody>
                    ${prods.map(p => `
                    <tr>
                        <td class="font-mono" style="font-size:11px;">${rmdEsc(p.producto_id || '—')}</td>
                        <td>${rmdEsc(p.descripcion)}</td>
                        <td class="font-mono" style="text-align:right;">${p.cantidad}</td>
                        <td class="font-mono" style="text-align:right;">${rmdFmt(p.precio, d.ccy)}</td>
                        <td class="font-mono" style="text-align:right;font-weight:600;">${rmdFmt(p.precio * p.cantidad, d.ccy)}</td>
                    </tr>`).join('') || '<tr><td colspan="5" style="text-align:center;padding:14px;color:var(--ink-ghost);">Sin partidas</td></tr>'}
                </tbody>
            </table>
        </div>`;
    } catch (e) {
        body.innerHTML = `<div style="text-align:center;padding:30px;color:var(--danger-color);">
            Error: ${rmdEsc(e.message)}</div>`;
    }
}

/* ══════════════════════════════════════════════════════════════
   MODAL — REGISTRAR REPORTE
   Al abrir, carga las partidas de las facturas seleccionadas
   para que el vendedor indique cuáles y cuántas están dañadas
══════════════════════════════════════════════════════════════ */
async function rmdAbrirModalReporte() {
    if (!RMD_STATE.seleccionadas.length) {
        rmdToast('Selecciona al menos una factura', 'warn');
        return;
    }

    // Reset
    RMD_STATE.tipoDanio = null;
    document.getElementById('rmd-tipo-hidden').value = '';
    document.getElementById('rmd-descripcion').value = '';
    document.getElementById('rmd-urgencia').value = 'normal';
    document.getElementById('rmd-referencia').value = '';
    document.getElementById('rmd-ubicacion').value = '';
    document.querySelectorAll('.tipo-card').forEach(c => c.classList.remove('selected'));
    RMD_STATE.partidasDanadas = [];

    // Resumen en el modal
    const total = RMD_STATE.seleccionadas.reduce((s, f) => s + f.imp, 0);
    document.getElementById('rmd-modal-resumen').innerHTML = `
    <div style="background:var(--danger-soft);border:1px solid var(--danger-border);border-radius:8px;padding:12px 14px;font-size:12px;">
        <div style="font-weight:600;color:var(--ink);margin-bottom:8px;">
            <i class="fas fa-file-invoice me-2" style="color:var(--danger-color);"></i>
            ${RMD_STATE.seleccionadas.length} factura${RMD_STATE.seleccionadas.length !== 1 ? 's' : ''} seleccionada${RMD_STATE.seleccionadas.length !== 1 ? 's' : ''}
            — Total: <span class="font-mono" style="color:var(--danger-color);">${rmdFmt(total)}</span>
        </div>
        <div style="display:flex;flex-wrap:wrap;gap:5px;">
            ${RMD_STATE.seleccionadas.map(f =>
        `<span style="background:#fff;border:1px solid var(--danger-border);border-radius:5px;padding:2px 9px;font-family:var(--font-mono);font-size:11px;font-weight:600;color:var(--danger-color);">
                    ${rmdEsc(f.folio)}
                </span>`
    ).join('')}
        </div>
    </div>`;

    // Reset — añadir estas dos líneas a las ya existentes
    const descSelect = document.getElementById('rmd-descripcion-tipo');
    if (descSelect) descSelect.value = '';
    document.getElementById('rmd-descripcion-otro-wrap').style.display = 'none';
    document.getElementById('rmd-descripcion').value = '';

    openRmdModal('rmd-modal-reporte');

    // Cargar partidas de forma asíncrona
    await rmdCargarPartidasModal();
}

async function rmdCargarPartidasModal() {
    const wrap = document.getElementById('rmd-partidas-wrap');
    const lista = document.getElementById('rmd-partidas-lista');
    wrap.style.display = 'block';
    lista.innerHTML = `<div style="text-align:center;padding:20px;color:var(--ink-ghost);">
        <i class="fas fa-spinner fa-spin me-2"></i> Cargando partidas...</div>`;

    const todasPartidas = [];

    for (const f of RMD_STATE.seleccionadas) {
        try {
            const res = await fetch(`/DatosGenerales/BuscarFacturaDetalle?id=${f.id}`);
            if (!res.ok) continue;
            const data = await res.json();
            if (!data?.length) continue;
            const d = data[0];
            (d.productos || []).forEach(p => {
                todasPartidas.push({
                    encabezadoId: f.id,
                    folio: f.folio,
                    productoId: p.producto_id || '',
                    descripcion: p.descripcion || '',
                    cantTotal: p.cantidad || 0,
                    precio: p.precio || 0,
                    ccy: d.ccy || 'MXN',
                });
            });
        } catch { /* continuar con la siguiente */ }
    }

    if (!todasPartidas.length) {
        lista.innerHTML = `<div style="text-align:center;padding:16px;color:var(--ink-ghost);font-size:12px;">
            No se encontraron partidas</div>`;
        return;
    }

    lista.innerHTML = todasPartidas.map((p, idx) => `
    <div class="rmd-partida-row" id="rmd-prow-${idx}">
        <div class="rmd-partida-chk-wrap">
            <div class="rmd-check" id="rmd-pchk-${idx}"
                 onclick="rmdTogglePartida(${idx}, this)"></div>
        </div>
        <div class="rmd-partida-info">
            <div class="rmd-partida-folio-badge">${rmdEsc(p.folio)}</div>
            <div class="rmd-partida-clave font-mono">${rmdEsc(p.productoId || '—')}</div>
            <div class="rmd-partida-desc">${rmdEsc(p.descripcion)}</div>
        </div>
        <div class="rmd-partida-cant-wrap">
            <div style="font-size:10px;color:var(--ink-ghost);margin-bottom:4px;">
                Disponible: <strong>${p.cantTotal}</strong>
            </div>
            <div style="display:flex;align-items:center;gap:6px;">
                <label style="font-size:10px;color:var(--ink-soft);">Cant. dañada:</label>
                <input type="number" class="rmd-cant-input" id="rmd-cant-${idx}"
                       min="1" max="${p.cantTotal}" step="1" value="1"
                       disabled
                       onchange="rmdUpdateCantidad(${idx}, this.value)"
                       style="width:70px;" />
            </div>
        </div>
        <div class="rmd-partida-precio font-mono" style="font-size:11px;color:var(--ink-soft);flex-shrink:0;">
            ${rmdFmt(p.precio, p.ccy)} c/u
        </div>
    </div>`).join('');

    // Guardar referencia a las partidas para poder leerlas al guardar
    window._rmdTodasPartidas = todasPartidas;
}

function rmdTogglePartida(idx, chkEl) {
    const isChecked = chkEl.classList.contains('checked');
    const cantInput = document.getElementById(`rmd-cant-${idx}`);
    const row = document.getElementById(`rmd-prow-${idx}`);

    if (isChecked) {
        chkEl.classList.remove('checked');
        cantInput.disabled = true;
        row.classList.remove('selected-partida');
    } else {
        chkEl.classList.add('checked');
        cantInput.disabled = false;
        row.classList.add('selected-partida');
    }
}

function rmdUpdateCantidad(idx, val) {
    const p = (window._rmdTodasPartidas || [])[idx];
    if (!p) return;
    const max = p.cantTotal;
    const num = Math.min(Math.max(1, parseInt(val) || 1), max);
    document.getElementById(`rmd-cant-${idx}`).value = num;
}

function rmdGetPartidasSeleccionadas() {
    const partidas = window._rmdTodasPartidas || [];
    const result = [];
    partidas.forEach((p, idx) => {
        const chk = document.getElementById(`rmd-pchk-${idx}`);
        if (chk?.classList.contains('checked')) {
            result.push({
                ...p,
                cantDanada: parseInt(document.getElementById(`rmd-cant-${idx}`)?.value || '1'),
            });
        }
    });
    return result;
}

/* ══════════════════════════════════════════════════════════════
   SELECCIÓN DE TIPO DE DAÑO
══════════════════════════════════════════════════════════════ */
function rmdSelectTipo(card, tipo) {
    document.querySelectorAll('.tipo-card').forEach(c => c.classList.remove('selected'));
    card.classList.add('selected');
    RMD_STATE.tipoDanio = tipo;
    document.getElementById('rmd-tipo-hidden').value = tipo;
}

/* ══════════════════════════════════════════════════════════════
   VALIDACIÓN
══════════════════════════════════════════════════════════════ */
function rmdValidarReporte() {
    let valid = true;

    // Tipo de daño (las tarjetas)
    if (!RMD_STATE.tipoDanio) {
        const grid = document.getElementById('rmd-tipo-grid');
        grid?.classList.add('field-error');
        const prev = grid?.parentElement.querySelector('.field-error-msg');
        if (prev) prev.remove();
        const sp = document.createElement('span');
        sp.className = 'field-error-msg';
        sp.textContent = 'Selecciona el tipo de daño';
        grid?.insertAdjacentElement('afterend', sp);
        valid = false;
    } else {
        document.getElementById('rmd-tipo-grid')?.classList.remove('field-error');
        document.querySelectorAll('#rmd-modal-reporte .field-error-msg').forEach(e => {
            if (e.previousElementSibling?.id === 'rmd-tipo-grid') e.remove();
        });
    }

    // Descripción — select obligatorio
    const descTipo = rmdGetVal('rmd-descripcion-tipo');
    if (!descTipo) {
        rmdFieldError('rmd-descripcion-tipo', 'Selecciona una descripción del daño');
        valid = false;
    } else {
        rmdFieldError('rmd-descripcion-tipo', null);
    }

    // Texto libre — solo obligatorio si eligió "otro"
    if (descTipo === 'otro') {
        const descLibre = rmdGetVal('rmd-descripcion');
        if (!descLibre || descLibre.length < 10) {
            rmdFieldError('rmd-descripcion', 'Describe el daño con al menos 10 caracteres');
            valid = false;
        } else {
            rmdFieldError('rmd-descripcion', null);
        }
    }

    // Partidas seleccionadas
    const partidas = rmdGetPartidasSeleccionadas();
    if (!partidas.length) {
        const lista = document.getElementById('rmd-partidas-lista');
        if (lista) {
            lista.querySelector('.rmd-partidas-error')?.remove();
            const div = document.createElement('div');
            div.className = 'rmd-partidas-error';
            div.textContent = 'Selecciona al menos una partida dañada';
            lista.prepend(div);
        }
        valid = false;
    }

    return valid;
}

/* ══════════════════════════════════════════════════════════════
   GUARDAR REPORTE — POST al controller
══════════════════════════════════════════════════════════════ */
async function rmdGuardarReporte() {
    if (!rmdValidarReporte()) return;

    const btn = document.getElementById('rmd-btn-confirmar');
    const btnOrig = btn.innerHTML;
    btn.disabled = true;
    btn.innerHTML = '<i class="fas fa-spinner fa-spin me-1"></i>Enviando...';

    // Construir descripción final
    const descTipo = rmdGetVal('rmd-descripcion-tipo');
    const descLibre = rmdGetVal('rmd-descripcion');
    const descripcionFinal = descTipo === 'otro'
        ? descLibre
        : descTipo + (descLibre ? ` — ${descLibre}` : '');

    try {
        const partidas = rmdGetPartidasSeleccionadas();
        const payload = {
            encabezadoIds: RMD_STATE.seleccionadas.map(f => f.id),
            tipoDanio: RMD_STATE.tipoDanio,
            estatusId: TIPOS_DANIO[RMD_STATE.tipoDanio]?.estatusId || 40,
            descripcion: descripcionFinal,
            urgencia: rmdGetVal('rmd-urgencia', 'normal'),
            referencia: rmdGetVal('rmd-referencia'),
            ubicacion: rmdGetVal('rmd-ubicacion'),
            partidasDanadas: partidas,
        };

        const fd = new FormData();
        Object.entries(payload).forEach(([k, v]) => {
            fd.append(k, Array.isArray(v) || typeof v === 'object'
                ? JSON.stringify(v)
                : v);
        });
        const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
        if (token) fd.append('__RequestVerificationToken', token);

        const res = await fetch('/ReporteDanado/RegistrarReporte', {
            method: 'POST',
            body: fd,
        });
        if (!res.ok) throw new Error('HTTP ' + res.status);
        const data = await res.json();

        if (data.success) {
            closeRmdModal('rmd-modal-reporte');
            rmdToast(`Reporte enviado a inventario — ${RMD_STATE.seleccionadas.length} factura(s) marcadas`, 'ok');
            RMD_STATE.seleccionadas = [];
            rmdUpdateSidebar();
            if (rmdTblInstance) rmdTblInstance.reload();
        } else {
            throw new Error(data.message || 'Error al guardar');
        }
    } catch (e) {
        rmdToast('Error: ' + e.message, 'err');
    } finally {
        btn.disabled = false;
        btn.innerHTML = btnOrig;
    }
}

/* ══════════════════════════════════════════════════════════════
   HISTORIAL — solo del vendedor logueado
══════════════════════════════════════════════════════════════ */
const RMD_HIST = { page: 1, pageSize: 25, total: 0, estatus: '' };

async function rmdLoadHistorial() {
    const tbody = document.getElementById('rmd-hist-tbody');
    if (!tbody) return;
    tbody.innerHTML = `<tr><td colspan="7" style="text-align:center;padding:24px;color:var(--ink-ghost);">
        <i class="fas fa-spinner fa-spin fa-lg" style="opacity:.4;display:block;margin-bottom:8px;"></i>
        Cargando tus reportes...</td></tr>`;

    try {
        const p = new URLSearchParams({
            estatus: RMD_HIST.estatus || '',
            page: RMD_HIST.page,
            pageSize: RMD_HIST.pageSize,
        });
        const res = await fetch(`/ReporteDanado/ObtenerHistorialVendedor?${p}`);
        if (!res.ok) throw new Error('HTTP ' + res.status);
        const data = await res.json();
        RMD_HIST.total = data.total || 0;
        rmdRenderHistorial(data.items || []);
    } catch (e) {
        tbody.innerHTML = `<tr><td colspan="7" style="text-align:center;padding:24px;color:var(--danger-color);">
            Error: ${rmdEsc(e.message)}</td></tr>`;
    }
}

function rmdRenderHistorial(items) {
    const tbody = document.getElementById('rmd-hist-tbody');
    if (!items.length) {
        tbody.innerHTML = `<tr><td colspan="7">
            <div class="rmd-empty">
                <span class="rmd-empty-icon"><i class="fas fa-inbox"></i></span>
                No tienes reportes registrados
            </div></td></tr>`;
        return;
    }

    tbody.innerHTML = items.map(s => {
        const tipo = TIPOS_DANIO[s.tipo_danio] || { label: s.tipo_danio, color: '#64748b', icon: 'fa-tag' };
        const estatusBadge = _rmdHistEstatus(s.estatus_actual);

        return `
        <tr>
            <td class="font-mono" style="font-size:11px;color:var(--ink-soft);">
                ${rmdEsc((typeof dateFormatter === 'function' ? dateFormatter(s.fecha_reporte) : s.fecha_reporte) || '—')}
            </td>
            <td>
                <div style="display:flex;flex-wrap:wrap;gap:3px;">
                    ${(s.folios || '').split(',').map(f => f.trim()).filter(Boolean).map(f =>
            `<span class="font-mono" style="background:var(--danger-soft);color:var(--danger-color);border-radius:4px;padding:1px 7px;font-size:10px;font-weight:600;">${rmdEsc(f)}</span>`
        ).join('')}
                </div>
            </td>
            <td class="d-none d-md-table-cell">
                <div style="display:flex;align-items:center;gap:6px;">
                    <span style="color:${tipo.color}"><i class="fas ${tipo.icon}"></i></span>
                    <span style="font-size:12px;">${rmdEsc(tipo.label)}</span>
                </div>
            </td>
            <td class="d-none d-lg-table-cell" style="max-width:200px;">
                <div style="font-size:11px;color:var(--ink-soft);overflow:hidden;text-overflow:ellipsis;white-space:nowrap;"
                     title="${rmdEsc(s.descripcion)}">
                    ${rmdEsc(s.descripcion || '—')}
                </div>
            </td>
            <td><span class="rmd-badge ${estatusBadge.class}">${rmdEsc(estatusBadge.label)}</span></td>
            <td class="d-none d-sm-table-cell" style="font-size:11px;color:var(--ink-soft);">
                ${rmdEsc(s.usuario_inventario || '—')}
            </td>
            <td>
                ${s.doc_folio
                ? `<span class="rmd-badge ok font-mono"><i class="fas fa-check-circle" style="font-size:9px;"></i> ${rmdEsc(s.doc_folio)}</span>`
                : `<span style="font-size:11px;color:var(--ink-ghost);">Pendiente</span>`
            }
            </td>
        </tr>`;
    }).join('');
}

function _rmdHistEstatus(estatus) {
    const map = {
        'PENDIENTE': { class: 'pending', label: 'Pendiente revisión' },
        'RECIBIDO': { class: 'info', label: 'Material recibido' },
        'DOC_GENERADO': { class: 'ok', label: 'Documento generado' },
        'CANCELADO': { class: 'danger', label: 'Cancelado' },
    };
    return map[estatus] || { class: 'gray', label: estatus || 'Desconocido' };
}

/* ══════════════════════════════════════════════════════════════
   MODALES — HELPERS
══════════════════════════════════════════════════════════════ */
function openRmdModal(id) { document.getElementById(id)?.classList.add('open'); }
function closeRmdModal(id) { document.getElementById(id)?.classList.remove('open'); }

function rmdOnDescTipoChange(sel) {
    const wrap = document.getElementById('rmd-descripcion-otro-wrap');
    const isOtro = sel.value === 'otro';
    wrap.style.display = isOtro ? 'block' : 'none';
    if (!isOtro) {
        // Limpia el campo libre al cambiar de opción
        const ta = document.getElementById('rmd-descripcion');
        if (ta) ta.value = '';
        rmdFieldError('rmd-descripcion', null);
    }
    // Quitar error del select si ya eligió algo
    rmdFieldError('rmd-descripcion-tipo', null);
}

/* ══════════════════════════════════════════════════════════════
   INICIALIZACIÓN
══════════════════════════════════════════════════════════════ */
document.addEventListener('DOMContentLoaded', () => {
    rmdInitTabla();         
    rmdUpdateSidebar();

    document.addEventListener('keydown', e => {
        if (e.key === 'Escape')
            document.querySelectorAll('.modal-overlay.open').forEach(m => m.classList.remove('open'));
    });

    document.querySelectorAll('.modal-overlay').forEach(m => {
        m.addEventListener('click', e => { if (e.target === m) m.classList.remove('open'); });
    });

    rmdSwitchTab('nueva');
});

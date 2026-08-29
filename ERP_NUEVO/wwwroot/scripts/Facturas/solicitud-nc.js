/* ══════════════════════════════════════════════════════════════
   ESTADO GLOBAL
══════════════════════════════════════════════════════════════ */
const SNC_STATE = {
    seleccionadas: [],      // [{id, folio, cliente, rfc, imp, ccy, fecha}]
    motivoTipo: null,       // clave del motivo seleccionado
    currentTab: 'nueva',    // 'nueva' | 'historial'
};

/* ══════════════════════════════════════════════════════════════
   CATÁLOGO DE MOTIVOS (mapea a tipoNC del módulo de NC)
   Estos valores deben coincidir con TYPE_META en nota-credito.js
══════════════════════════════════════════════════════════════ */
const MOTIVOS = {
    devolucion: {
        label: 'Mercancía devuelta',
        desc: 'Producto regresa a almacén',
        icon: 'fa-undo-alt',
        color: '#7c3aed',
        tipoNC: 'devolucion',
        estatusId: 20,          // estatus en encabezadomov para "Solicitud NC - Devolución"
    },
    descuento: {
        label: 'Descuento / Error comercial',
        desc: 'Ajuste sobre importe facturado',
        icon: 'fa-percent',
        color: '#0891b2',
        tipoNC: 'descuento',
        estatusId: 21,
    },
    precio: {
        label: 'Error en precio',
        desc: 'Diferencia vs. precio pactado',
        icon: 'fa-dollar-sign',
        color: '#d97706',
        tipoNC: 'precio',
        estatusId: 22,
    },
    cancelacion: {
        label: 'Cancelación parcial',
        desc: 'Partidas específicas a cancelar',
        icon: 'fa-ban',
        color: '#dc2626',
        tipoNC: 'cancelacion',
        estatusId: 23,
    },
    bonificacion: {
        label: 'Bonificación / Rappel',
        desc: 'Abono por acuerdo comercial',
        icon: 'fa-gift',
        color: '#059669',
        tipoNC: 'bonificacion',
        estatusId: 24,
    },
    otro: {
        label: 'Otro motivo',
        desc: 'Especificar en comentarios',
        icon: 'fa-ellipsis-h',
        color: '#64748b',
        tipoNC: 'standalone',
        estatusId: 25,
    },
};

/* ══════════════════════════════════════════════════════════════
   UTILIDADES
══════════════════════════════════════════════════════════════ */
function sncEsc(t) {
    const d = document.createElement('div');
    d.textContent = String(t ?? '');
    return d.innerHTML;
}

function sncFmt(n, ccy) {
    const sym = { MXN: '$', USD: 'US$', EUR: '€', CAD: 'CA$' };
    return (sym[ccy || 'MXN'] || '$') +
        Number(n).toFixed(2).replace(/\B(?=(\d{3})+(?!\d))/g, ',');
}

function sncToast(msg, type = 'info') {
    const c = document.getElementById('snc-toast-container');
    if (!c) return;
    const t = document.createElement('div');
    t.className = `snc-toast ${type}`;
    const icon = type === 'ok' ? 'check' : type === 'err' ? 'times' : type === 'warn' ? 'exclamation-triangle' : 'info';
    t.innerHTML = `<i class="fas fa-${icon}-circle me-2"></i>${sncEsc(msg)}`;
    c.appendChild(t);
    requestAnimationFrame(() => t.classList.add('show'));
    setTimeout(() => {
        t.classList.remove('show');
        setTimeout(() => t.remove(), 300);
    }, 3500);
}

function sncGetVal(id, def = '') {
    return document.getElementById(id)?.value?.trim() || def;
}

function sncFieldError(id, msg) {
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
function sncSwitchTab(tab) {
    SNC_STATE.currentTab = tab;
    document.querySelectorAll('.snc-tab').forEach(t => {
        t.classList.toggle('active', t.dataset.tab === tab);
    });
    document.querySelectorAll('.snc-tab-panel').forEach(p => {
        p.classList.toggle('active', p.id === `snc-panel-${tab}`);
    });
    if (tab === 'historial') sncLoadHistorial();
}

/* ══════════════════════════════════════════════════════════════
   BÚSQUEDA DE FACTURAS
══════════════════════════════════════════════════════════════ */
const SNC_FACTURAS = {
    page: 1, pageSize: 30, total: 0,
    query: '', rfc: '', desde: '', hasta: '',
    loading: false,
    _timer: null,
};

function sncFilterDebounce() {
    clearTimeout(SNC_FACTURAS._timer);
    SNC_FACTURAS._timer = setTimeout(() => {
        SNC_FACTURAS.page = 1;
        sncFetchFacturas();
    }, 300);
}

function sncApplyFilters() {
    SNC_FACTURAS.query = sncGetVal('snc-search');
    SNC_FACTURAS.rfc = sncGetVal('snc-filter-rfc');
    SNC_FACTURAS.desde = sncGetVal('snc-filter-desde');
    SNC_FACTURAS.hasta = sncGetVal('snc-filter-hasta');
    SNC_FACTURAS.page = 1;
    sncFetchFacturas();
}

function sncClearFilters() {
    ['snc-search', 'snc-filter-rfc', 'snc-filter-desde', 'snc-filter-hasta']
        .forEach(id => { const el = document.getElementById(id); if (el) el.value = ''; });
    SNC_FACTURAS.query = SNC_FACTURAS.rfc = SNC_FACTURAS.desde = SNC_FACTURAS.hasta = '';
    SNC_FACTURAS.page = 1;
    sncFetchFacturas();
}

async function sncFetchFacturas() {
    if (SNC_FACTURAS.loading) return;
    SNC_FACTURAS.loading = true;

    const tbody = document.getElementById('snc-tbody');
    tbody.innerHTML = `<tr><td colspan="8" style="text-align:center;padding:30px;color:var(--ink-ghost);">
        <i class="fas fa-spinner fa-spin fa-lg" style="opacity:.4;display:block;margin-bottom:8px;"></i>
        Buscando facturas...</td></tr>`;

    try {
        const p = new URLSearchParams({
            nombre: SNC_FACTURAS.query || '',
            rfc: SNC_FACTURAS.rfc || '',
            desde: SNC_FACTURAS.desde || '',
            hasta: SNC_FACTURAS.hasta || '',
            page: SNC_FACTURAS.page,
            pageSize: SNC_FACTURAS.pageSize,
        });

        const res = await fetch(`/DatosGenerales/BuscarFacturasActivas?${p}`);
        if (!res.ok) throw new Error('HTTP ' + res.status);
        const data = await res.json();

        SNC_FACTURAS.total = data.total || 0;
        sncRenderFacturas(data.items || []);
        sncRenderPaginacion();
    } catch (e) {
        tbody.innerHTML = `<tr><td colspan="8" style="text-align:center;padding:30px;color:var(--danger-color);">
            <i class="fas fa-exclamation-circle fa-lg" style="display:block;margin-bottom:8px;"></i>
            Error: ${sncEsc(e.message)}</td></tr>`;
    } finally {
        SNC_FACTURAS.loading = false;
    }
}

function sncRenderFacturas(items) {
    const tbody = document.getElementById('snc-tbody');
    const countEl = document.getElementById('snc-count-txt');

    if (countEl) {
        countEl.textContent = SNC_FACTURAS.total > 0
            ? `${SNC_FACTURAS.total} factura${SNC_FACTURAS.total !== 1 ? 's' : ''}`
            : 'Sin resultados';
    }

    if (!items.length) {
        tbody.innerHTML = `<tr><td colspan="8">
            <div class="snc-empty">
                <span class="snc-empty-icon"><i class="fas fa-file-slash"></i></span>
                No se encontraron facturas con los filtros actuales
            </div></td></tr>`;
        return;
    }

    tbody.innerHTML = items.map(f => {
        const selIdx = SNC_STATE.seleccionadas.findIndex(s => s.id === f.id_encabezado);
        const isSelected = selIdx >= 0;
        const imp = sncFmt(f.imp, f.ccy);
        const fecha = f.fch ? f.fch.substring(0, 10) : '—';

        // Badge de estado de la solicitud si ya tiene una pendiente
        let estadoBadge = '';
        if (f.snc_estatus_id && f.snc_estatus_id !== 1) {
            estadoBadge = `<span class="snc-badge pending" style="margin-left:6px;">
                <i class="fas fa-clock" style="font-size:9px;"></i> Solicitud pendiente
            </span>`;
        }

        return `
        <tr class="${isSelected ? 'selected' : ''}" id="snc-row-${f.id_encabezado}"
            onclick="sncToggleRow(${f.id_encabezado}, this, ${JSON.stringify(f).replace(/"/g, '&quot;')})">
            <td onclick="event.stopPropagation()">
                <div class="snc-check ${isSelected ? 'checked' : ''}" id="snc-chk-${f.id_encabezado}"
                     onclick="sncToggleRow(${f.id_encabezado}, document.getElementById('snc-row-${f.id_encabezado}'), ${JSON.stringify(f).replace(/"/g, '&quot;')})">
                </div>
            </td>
            <td>
                <span class="font-mono" style="font-weight:600;color:var(--accent-blue);">${sncEsc(f.folio)}</span>
                ${estadoBadge}
            </td>
            <td>${sncEsc(f.n_cli || f.cli_prov || '—')}</td>
            <td class="font-mono" style="font-size:11px;color:var(--ink-soft);">${sncEsc(f.rfc || '—')}</td>
            <td class="font-mono" style="text-align:right;font-weight:600;color:var(--ok);">${sncEsc(imp)}</td>
            <td class="font-mono" style="font-size:11px;">${sncEsc(dateFormatter(fecha))}</td>
            <td>
                <span class="snc-badge ${_sncMapStatus(f.estatus_id)}">
                    ${sncEsc(f.estatus_txt || 'Activa')}
                </span>
            </td>
            <td onclick="event.stopPropagation()">
                <button class="btn-icon" style="padding:4px 9px;font-size:10px;"
                    onclick="sncVerDetalle(${f.id_encabezado})">
                    <i class="fas fa-eye"></i> Ver
                </button>
            </td>
        </tr>`;
    }).join('');
}

function _sncMapStatus(estatusId) {
    if (!estatusId) return 'gray';
    const map = { 1: 'ok', 11: 'info', 2: 'warn' };
    return map[estatusId] || 'gray';
}

/* ══════════════════════════════════════════════════════════════
   SELECCIÓN DE FILAS
══════════════════════════════════════════════════════════════ */
function sncToggleRow(id, trEl, facturaData) {
    const idx = SNC_STATE.seleccionadas.findIndex(s => s.id === id);

    if (idx >= 0) {
        // Deseleccionar
        SNC_STATE.seleccionadas.splice(idx, 1);
        trEl?.classList.remove('selected');
        document.getElementById(`snc-chk-${id}`)?.classList.remove('checked');
    } else {
        // Seleccionar
        SNC_STATE.seleccionadas.push({
            id: facturaData.id_encabezado,
            folio: facturaData.folio,
            cliente: facturaData.n_cli || facturaData.cli_prov || '—',
            rfc: facturaData.rfc || '—',
            imp: facturaData.imp || 0,
            ccy: facturaData.ccy || 'MXN',
            fecha: facturaData.fch || '',
        });
        trEl?.classList.add('selected');
        document.getElementById(`snc-chk-${id}`)?.classList.add('checked');
    }

    sncUpdateSidebar();
    sncUpdateSelectAll();
}

function sncDeseleccionarDesidebar(id) {
    SNC_STATE.seleccionadas = SNC_STATE.seleccionadas.filter(s => s.id !== id);
    // Actualizar la fila en la tabla si es visible
    const tr = document.getElementById(`snc-row-${id}`);
    const chk = document.getElementById(`snc-chk-${id}`);
    tr?.classList.remove('selected');
    chk?.classList.remove('checked');
    sncUpdateSidebar();
    sncUpdateSelectAll();
}

function sncSelectAll() {
    const allChk = document.getElementById('snc-chk-all');
    const rows = document.querySelectorAll('#snc-tbody tr[id^="snc-row-"]');

    // Si todos están seleccionados, deseleccionar todos los visibles
    const allSelected = [...rows].every(r => r.classList.contains('selected'));

    rows.forEach(tr => {
        const id = parseInt(tr.id.replace('snc-row-', ''));
        if (allSelected) {
            SNC_STATE.seleccionadas = SNC_STATE.seleccionadas.filter(s => s.id !== id);
            tr.classList.remove('selected');
            document.getElementById(`snc-chk-${id}`)?.classList.remove('checked');
        } else {
            // Solo agregar si no está ya en la lista
            if (!SNC_STATE.seleccionadas.find(s => s.id === id)) {
                // Extraer datos del dataset de la fila (guardados en data-* al render, o refetch)
                // Solución simple: disparar click en la fila para usar la data ya cargada
                // Para select-all necesitamos los datos — los leemos del DOM
                const celdas = tr.querySelectorAll('td');
                SNC_STATE.seleccionadas.push({
                    id,
                    folio: celdas[1]?.querySelector('.font-mono')?.textContent?.trim() || '',
                    cliente: celdas[2]?.textContent?.trim() || '',
                    rfc: celdas[3]?.textContent?.trim() || '',
                    imp: parseFloat(celdas[4]?.textContent?.replace(/[^0-9.-]/g, '') || '0'),
                    ccy: 'MXN',
                });
            }
            tr.classList.add('selected');
            document.getElementById(`snc-chk-${id}`)?.classList.add('checked');
        }
    });

    sncUpdateSidebar();
    sncUpdateSelectAll();
}

function sncUpdateSelectAll() {
    const allChk = document.getElementById('snc-chk-all');
    if (!allChk) return;
    const rows = document.querySelectorAll('#snc-tbody tr[id^="snc-row-"]');
    const selected = [...rows].filter(r => r.classList.contains('selected'));
    allChk.classList.remove('checked', 'indeterminate');
    if (selected.length === 0) { /* vacío */ }
    else if (selected.length === rows.length) allChk.classList.add('checked');
    else allChk.classList.add('indeterminate');
}

/* ══════════════════════════════════════════════════════════════
   SIDEBAR — RESUMEN SELECCIÓN
══════════════════════════════════════════════════════════════ */
function sncUpdateSidebar() {
    const list = document.getElementById('snc-sb-list');
    const badge = document.getElementById('snc-sb-badge');
    const count = document.getElementById('snc-sb-count');
    const total = SNC_STATE.seleccionadas.reduce((s, f) => s + (parseFloat(f.imp) || 0), 0);

    if (badge) {
        badge.textContent = SNC_STATE.seleccionadas.length > 0
            ? `${SNC_STATE.seleccionadas.length} sel.`
            : 'Sin selección';
        badge.classList.toggle('ready', SNC_STATE.seleccionadas.length > 0);
    }

    if (count) {
        count.textContent = `${SNC_STATE.seleccionadas.length} factura${SNC_STATE.seleccionadas.length !== 1 ? 's' : ''}`;
    }

    document.getElementById('snc-sb-total-val').textContent =
        SNC_STATE.seleccionadas.length > 0
            ? sncFmt(total)
            : '$0.00';

    const btnProcesar = document.getElementById('snc-btn-procesar');
    if (btnProcesar) btnProcesar.disabled = SNC_STATE.seleccionadas.length === 0;

    if (!list) return;

    if (!SNC_STATE.seleccionadas.length) {
        list.innerHTML = `<div class="snc-sb-empty">
            <i class="fas fa-hand-pointer me-2" style="opacity:.4;"></i>
            Selecciona facturas de la tabla
        </div>`;
        return;
    }

    list.innerHTML = SNC_STATE.seleccionadas.map(f => `
    <div class="snc-sel-pill" id="snc-pill-${f.id}">
        <div style="min-width:0">
            <div class="snc-sel-pill-folio">${sncEsc(f.folio)}</div>
            <div style="font-size:10px;color:var(--ink-ghost);white-space:nowrap;overflow:hidden;text-overflow:ellipsis;max-width:140px;">
                ${sncEsc(f.cliente)}
            </div>
        </div>
        <div style="display:flex;align-items:center;gap:6px;flex-shrink:0;">
            <span class="snc-sel-pill-imp">${sncFmt(f.imp, f.ccy)}</span>
            <button class="snc-sel-pill-del" onclick="sncDeseleccionarDesidebar(${f.id})" title="Quitar">
                <i class="fas fa-times"></i>
            </button>
        </div>
    </div>`).join('');
}

/* ══════════════════════════════════════════════════════════════
   PAGINACIÓN
══════════════════════════════════════════════════════════════ */
function sncRenderPaginacion() {
    const wrap = document.getElementById('snc-paginacion');
    if (!wrap) return;

    const totalPages = Math.ceil(SNC_FACTURAS.total / SNC_FACTURAS.pageSize);
    if (totalPages <= 1) { wrap.innerHTML = ''; return; }

    const cur = SNC_FACTURAS.page;
    let pages = [];

    // Siempre mostrar 1, última y páginas cercanas
    for (let i = 1; i <= totalPages; i++) {
        if (i === 1 || i === totalPages || (i >= cur - 1 && i <= cur + 1)) pages.push(i);
        else if (pages[pages.length - 1] !== '...') pages.push('...');
    }

    wrap.innerHTML = `
    <div class="snc-pagination">
        <span style="font-size:11px;color:var(--ink-ghost);">
            ${SNC_FACTURAS.total} resultado${SNC_FACTURAS.total !== 1 ? 's' : ''}
        </span>
        <div class="snc-pag-btns">
            <button class="snc-pag-btn" onclick="sncChangePage(${cur - 1})" ${cur <= 1 ? 'disabled' : ''}>
                <i class="fas fa-chevron-left"></i>
            </button>
            ${pages.map(p => p === '...'
        ? `<span style="padding:5px 4px;color:var(--ink-ghost);">…</span>`
        : `<button class="snc-pag-btn ${p === cur ? 'active' : ''}" onclick="sncChangePage(${p})">${p}</button>`
    ).join('')}
            <button class="snc-pag-btn" onclick="sncChangePage(${cur + 1})" ${cur >= totalPages ? 'disabled' : ''}>
                <i class="fas fa-chevron-right"></i>
            </button>
        </div>
    </div>`;
}

function sncChangePage(p) {
    const tp = Math.ceil(SNC_FACTURAS.total / SNC_FACTURAS.pageSize);
    if (p < 1 || p > tp) return;
    SNC_FACTURAS.page = p;
    sncFetchFacturas();
}

/* ══════════════════════════════════════════════════════════════
   MODAL — VER DETALLE DE FACTURA
══════════════════════════════════════════════════════════════ */
async function sncVerDetalle(encabezadoId) {
    openSncModal('snc-modal-detalle');

    const body = document.getElementById('snc-detalle-body');
    body.innerHTML = `<div style="text-align:center;padding:30px;color:var(--ink-ghost);">
        <i class="fas fa-spinner fa-spin fa-2x" style="opacity:.4;"></i>
    </div>`;

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
                <div style="font-size:10px;font-weight:700;text-transform:uppercase;color:var(--ink-ghost);letter-spacing:.08em;margin-bottom:8px;">Encabezado</div>
                <div style="font-size:12px;display:flex;flex-direction:column;gap:5px;">
                    <div><span style="color:var(--ink-soft);">Folio:</span> <strong class="font-mono">${sncEsc(d.folio)}</strong></div>
                    <div><span style="color:var(--ink-soft);">Cliente:</span> ${sncEsc(d.n_cli || d.cli_prov)}</div>
                    <div><span style="color:var(--ink-soft);">RFC:</span> <span class="font-mono">${sncEsc(d.rfc || '—')}</span></div>
                    <div><span style="color:var(--ink-soft);">Fecha:</span> <span class="font-mono">${sncEsc(dateFormatter(d.fch0) || '—')}</span></div>
                </div>
            </div>
            <div>
                <div style="font-size:10px;font-weight:700;text-transform:uppercase;color:var(--ink-ghost);letter-spacing:.08em;margin-bottom:8px;">Importes</div>
                <div style="font-size:12px;display:flex;flex-direction:column;gap:5px;">
                    <div><span style="color:var(--ink-soft);">Moneda:</span> ${sncEsc(d.ccy || 'MXN')}</div>
                    <div><span style="color:var(--ink-soft);">Importe:</span> <strong class="font-mono text-ok">${sncFmt(d.total, d.ccy)}</strong></div>
                    <div><span style="color:var(--ink-soft);">Saldo pendiente:</span> <span class="font-mono">${sncFmt(d.saldo ?? d.total, d.ccy)}</span></div>
                </div>
            </div>
        </div>
        <div style="font-size:10px;font-weight:700;text-transform:uppercase;color:var(--ink-ghost);letter-spacing:.08em;margin-bottom:8px;">
            Partidas (${prods.length})
        </div>
        <div class="snc-partidas-wrap">
            <table class="snc-partidas-tbl">
                <thead><tr>
                    <th>Clave</th><th>Descripción</th>
                    <th style="text-align:right">Cant.</th>
                    <th style="text-align:right">Precio</th>
                    <th style="text-align:right">Importe</th>
                </tr></thead>
                <tbody>
                    ${prods.map(p => `
                    <tr>
                        <td class="font-mono" style="font-size:11px;">${sncEsc(p.producto_id || '—')}</td>
                        <td>${sncEsc(p.descripcion)}</td>
                        <td class="font-mono" style="text-align:right;">${p.cantidad}</td>
                        <td class="font-mono" style="text-align:right;">${sncFmt(p.precio, d.ccy)}</td>
                        <td class="font-mono" style="text-align:right;font-weight:600;">${sncFmt(p.precio * p.cantidad, d.ccy)}</td>
                    </tr>`).join('') || '<tr><td colspan="5" style="text-align:center;padding:14px;color:var(--ink-ghost);">Sin partidas</td></tr>'}
                </tbody>
            </table>
        </div>`;
    } catch (e) {
        body.innerHTML = `<div style="text-align:center;padding:30px;color:var(--danger-color);">
            <i class="fas fa-exclamation-circle"></i> Error: ${sncEsc(e.message)}
        </div>`;
    }
}

/* ══════════════════════════════════════════════════════════════
   MODAL — REGISTRAR SOLICITUD DE NC
══════════════════════════════════════════════════════════════ */
async function sncAbrirModalSolicitud() {
    if (!SNC_STATE.seleccionadas.length) {
        sncToast('Selecciona al menos una factura', 'warn');
        return;
    }

    // Reset del modal
    SNC_STATE.motivoTipo = null;
    document.getElementById('snc-motivo-tipo-hidden').value = '';
    document.getElementById('snc-comentario').value = '';
    document.getElementById('snc-prioridad').value = 'normal';
    document.getElementById('snc-responsable').value = '';
    document.querySelectorAll('.motivo-card').forEach(c => c.classList.remove('selected'));
    sncFieldError('snc-comentario', null);
    // Asegurar campos editables (por si venían bloqueados de una apertura anterior)
    _sncRestaurarCamposModal();

    // Resumen de facturas
    const resumen = document.getElementById('snc-modal-resumen');
    const total = SNC_STATE.seleccionadas.reduce((s, f) => s + f.imp, 0);
    resumen.innerHTML = `
    <div style="background:var(--edge-soft);border:1px solid var(--edge);border-radius:8px;padding:12px 14px;font-size:12px;">
        <div style="font-weight:600;color:var(--ink);margin-bottom:8px;">
            <i class="fas fa-file-invoice me-2" style="color:var(--accent-blue);"></i>
            ${SNC_STATE.seleccionadas.length} factura${SNC_STATE.seleccionadas.length !== 1 ? 's' : ''} seleccionada${SNC_STATE.seleccionadas.length !== 1 ? 's' : ''}
            — Total: <span class="font-mono text-ok">${sncFmt(total)}</span>
        </div>
        <div style="display:flex;flex-wrap:wrap;gap:5px;">
            ${SNC_STATE.seleccionadas.map(f =>
        `<span style="background:#fff;border:1px solid var(--edge);border-radius:5px;padding:2px 9px;
                              font-family:var(--font-mono);font-size:11px;font-weight:600;color:var(--accent-blue);">
                    ${sncEsc(f.folio)}
                </span>`
    ).join('')}
        </div>
    </div>`;

    openSncModal('snc-modal-solicitud');

    // Consultar si alguna factura tiene reporte de daño activo
    await _sncBuscarReporteActivo();
}

async function _sncBuscarReporteActivo() {
    // Tomar la primera factura seleccionada para buscar (si hay varias del mismo reporte todas tienen el mismo)
    const primeraId = SNC_STATE.seleccionadas[0]?.id;
    if (!primeraId) return;

    try {
        const res = await fetch(`/SolicitudNC/ObtenerReportePorFactura?encabezadoId=${primeraId}`);
        if (!res.ok) return;
        const json = await res.json();
        if (!json.found) return;

        const r = json.data;

        const TIPO_A_MOTIVO = {
            fisico: 'devolucion',
            mojado: 'devolucion',
            incompleto: 'devolucion',
            caducado: 'cancelacion',
            calidad: 'descuento',
            otro: 'otro',
        };

        const motivo = TIPO_A_MOTIVO[r.tipo_danio] || 'devolucion';

        // Preseleccionar tarjeta de motivo
        const card = document.querySelector(`.motivo-card[data-tipo="${motivo}"]`);
        if (card) sncSelectMotivo(card, motivo);

        // Precargar descripción y bloquear
        const textarea = document.getElementById('snc-comentario');
        if (textarea) {
            textarea.value = r.descripcion || '';
            textarea.readOnly = true;
            textarea.style.background = 'var(--color-background-secondary)';
            textarea.style.color = 'var(--color-text-secondary)';
            textarea.style.cursor = 'not-allowed';
            textarea.title = 'Tomado del reporte de material dañado';
        }

        // Bloquear tarjetas de motivo
        document.querySelectorAll('.motivo-card').forEach(c => {
            c.style.pointerEvents = 'none';
            c.style.opacity = c.classList.contains('selected') ? '1' : '0.4';
        });

        // Precargar urgencia mapeada
        const urgenciaMap = { normal: 'normal', alta: 'alta', critica: 'urgente' };
        const urgEl = document.getElementById('snc-prioridad');
        if (urgEl) urgEl.value = urgenciaMap[r.urgencia] || 'normal';

        // Banner informativo encima del resumen
        const resumen = document.getElementById('snc-modal-resumen');
        const banner = document.createElement('div');
        banner.id = 'snc-rmd-banner';
        banner.style.cssText = `
            background:var(--color-background-info);
            border:1px solid var(--color-border-info);
            border-radius:8px;
            padding:10px 14px;
            font-size:11px;
            color:var(--color-text-info);
            margin-bottom:12px;
            display:flex;
            align-items:center;
            gap:8px;
        `;
        banner.innerHTML = `
            <i class="fas fa-link" style="flex-shrink:0;"></i>
            <span>
                Esta factura tiene un <strong>Reporte de Material Dañado #${r.reporte_id}</strong> activo.
                El motivo y descripción se tomaron del reporte y no pueden modificarse.
            </span>
        `;
        resumen.insertAdjacentElement('afterbegin', banner);

    } catch {
        // Si falla la consulta, el modal sigue funcionando normalmente
    }
}

function _sncRestaurarCamposModal() {
    const textarea = document.getElementById('snc-comentario');
    if (textarea) {
        textarea.readOnly = false;
        textarea.style.background = '';
        textarea.style.color = '';
        textarea.style.cursor = '';
        textarea.title = '';
    }
    document.querySelectorAll('.motivo-card').forEach(c => {
        c.style.pointerEvents = '';
        c.style.opacity = '';
    });
    document.getElementById('snc-rmd-banner')?.remove();
}


function sncSelectMotivo(card, tipo) {
    document.querySelectorAll('.motivo-card').forEach(c => c.classList.remove('selected'));
    card.classList.add('selected');
    SNC_STATE.motivoTipo = tipo;
    document.getElementById('snc-motivo-tipo-hidden').value = tipo;
    sncFieldError('snc-motivo-tipo-hidden', null);

    // Mostrar/ocultar campo de precio si es error de precio
    const precioCampos = document.getElementById('snc-campos-precio');
    if (precioCampos) precioCampos.style.display = tipo === 'precio' ? 'grid' : 'none';
}

function sncValidarSolicitud() {
    let valid = true;

    if (!SNC_STATE.motivoTipo) {
        // Marcar el grid de motivos
        const grid = document.getElementById('snc-motivo-grid');
        grid?.classList.add('field-error');
        const prev = grid?.parentElement.querySelector('.field-error-msg');
        if (prev) prev.remove();
        const sp = document.createElement('span');
        sp.className = 'field-error-msg';
        sp.textContent = 'Selecciona el motivo de la solicitud';
        grid?.insertAdjacentElement('afterend', sp);
        valid = false;
    } else {
        document.getElementById('snc-motivo-grid')?.classList.remove('field-error');
        document.querySelectorAll('#snc-modal-solicitud .field-error-msg').forEach(e => {
            if (e.previousElementSibling?.id === 'snc-motivo-grid') e.remove();
        });
    }

    const comentario = sncGetVal('snc-comentario');
    if (!comentario || comentario.length < 10) {
        sncFieldError('snc-comentario', 'Describe el motivo con al menos 10 caracteres');
        valid = false;
    } else {
        sncFieldError('snc-comentario', null);
    }

    return valid;
}

async function sncGuardarSolicitud() {
    if (!sncValidarSolicitud()) return;

    const btn = document.getElementById('snc-btn-confirmar');
    const btnOrig = btn.innerHTML;
    btn.disabled = true;
    btn.innerHTML = '<i class="fas fa-spinner fa-spin me-1"></i>Guardando...';

    try {
        const payload = {
            encabezadoIds: SNC_STATE.seleccionadas.map(f => f.id),
            motivoTipo: SNC_STATE.motivoTipo,
            tipoNC: MOTIVOS[SNC_STATE.motivoTipo]?.tipoNC || 'standalone',
            estatusId: MOTIVOS[SNC_STATE.motivoTipo]?.estatusId || 20,
            comentario: sncGetVal('snc-comentario'),
            prioridad: sncGetVal('snc-prioridad', 'normal'),
            responsable: sncGetVal('snc-responsable')
        };

        const fd = new FormData();
        Object.entries(payload).forEach(([k, v]) => {
            fd.append(k, Array.isArray(v) ? JSON.stringify(v) : v);
        });
        const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
        if (token) fd.append('__RequestVerificationToken', token);
        const res = await fetch('/SolicitudNC/RegistrarSolicitud', {
            method: 'POST',
            body: fd,
        });

        if (!res.ok) throw new Error('HTTP ' + res.status);
        const data = await res.json();

        if (data.success) {
            closeSncModal('snc-modal-solicitud');
            sncToast(`Solicitud registrada — ${SNC_STATE.seleccionadas.length} factura(s) marcadas`, 'ok');

            // Preguntar si ir directamente al módulo de NC
            if (SNC_STATE.seleccionadas.length > 0) {
                const firstId = SNC_STATE.seleccionadas[0].id;
                const tipoNC = MOTIVOS[SNC_STATE.motivoTipo]?.tipoNC || 'standalone';

                setTimeout(() => {
                    toastMixin?.fire({
                        title: '¿Ir al módulo de Nota de Crédito ahora?',
                        text: 'Las facturas marcadas ya están listas para procesar.',
                        icon: 'question',
                        showCancelButton: true,
                        confirmButtonText: 'Sí, ir ahora',
                        cancelButtonText: 'Después',
                    }).then(r => {
                        if (r.isConfirmed) {
                            // Redirigir pasando el encabezado y tipo preseleccionados
                            const params = new URLSearchParams({
                                encabezadoIds: SNC_STATE.seleccionadas.map(f => f.id).join(','),
                                tipoNC,
                            });
                            window.location.href = `/VINotaCredito/Index?${params}`;
                        }
                    });
                }, 800);
            }

            // Limpiar selección y recargar
            SNC_STATE.seleccionadas = [];
            sncUpdateSidebar();
            sncFetchFacturas();
            if (SNC_STATE.currentTab === 'historial') sncLoadHistorial();
        } else {
            throw new Error(data.message || 'Error al guardar');
        }
    } catch (e) {
        sncToast('Error: ' + e.message, 'err');
    } finally {
        btn.disabled = false;
        btn.innerHTML = btnOrig;
    }
}

/* ══════════════════════════════════════════════════════════════
   HISTORIAL DE SOLICITUDES
══════════════════════════════════════════════════════════════ */
const SNC_HIST = { page: 1, pageSize: 25, total: 0, query: '', estatus: '' };

async function sncLoadHistorial() {
    const tbody = document.getElementById('snc-hist-tbody');
    if (!tbody) return;

    tbody.innerHTML = `<tr><td colspan="7" style="text-align:center;padding:24px;color:var(--ink-ghost);">
        <i class="fas fa-spinner fa-spin fa-lg" style="opacity:.4;display:block;margin-bottom:8px;"></i>
        Cargando historial...</td></tr>`;

    try {
        const p = new URLSearchParams({
            query: SNC_HIST.query || '',
            estatus: SNC_HIST.estatus || '',
            page: SNC_HIST.page,
            pageSize: SNC_HIST.pageSize,
        });

        const res = await fetch(`/SolicitudNC/ObtenerHistorial?${p}`);
        if (!res.ok) throw new Error('HTTP ' + res.status);
        const data = await res.json();

        SNC_HIST.total = data.total || 0;
        sncRenderHistorial(data.items || []);
    } catch (e) {
        tbody.innerHTML = `<tr><td colspan="7" style="text-align:center;padding:24px;color:var(--danger-color);">
            Error: ${sncEsc(e.message)}</td></tr>`;
    }
}

function sncRenderHistorial(items) {
    const tbody = document.getElementById('snc-hist-tbody');

    if (!items.length) {
        tbody.innerHTML = `<tr><td colspan="7">
            <div class="snc-empty">
                <span class="snc-empty-icon"><i class="fas fa-inbox"></i></span>
                No hay solicitudes de NC registradas
            </div></td></tr>`;
        return;
    }

    tbody.innerHTML = items.map(s => {
        const motivo = MOTIVOS[s.motivo_tipo] || { label: s.motivo_tipo, color: '#64748b', icon: 'fa-tag' };
        const estadoBadge = _sncHistEstatus(s.estatus_actual);

        return `
        <tr>
            <td class="font-mono" style="font-size:11px;color:var(--ink-soft);">${sncEsc(dateFormatter(s.fecha_solicitud) || '—')}</td>
            <td>
                <div style="display:flex;flex-wrap:wrap;gap:3px;">
                    ${(s.folios || '').split(',').map(f => f.trim()).filter(Boolean).map(f =>
            `<span class="font-mono" style="background:var(--info-bg);color:var(--accent-blue);border-radius:4px;padding:1px 7px;font-size:10px;font-weight:600;">${sncEsc(f)}</span>`
        ).join('')}
                </div>
            </td>
            <td>
                <div style="display:flex;align-items:center;gap:6px;">
                    <span style="color:${motivo.color}"><i class="fas ${motivo.icon}"></i></span>
                    <span style="font-size:12px;">${sncEsc(motivo.label || s.motivo_tipo)}</span>
                </div>
            </td>
            <td style="max-width:200px;">
                <div style="font-size:11px;color:var(--ink-soft);overflow:hidden;text-overflow:ellipsis;white-space:nowrap;" title="${sncEsc(s.comentario)}">
                    ${sncEsc(s.comentario || '—')}
                </div>
            </td>
            <td><span class="snc-badge ${estadoBadge.class}">${sncEsc(estadoBadge.label)}</span></td>
            <td style="font-size:11px;color:var(--ink-soft);">${sncEsc(s.usuario_solicitud || '—')}</td>
           
        </tr>`;
    }).join('');
}
//Boton de accion
//<td>
//    ${s.nc_folio
//        ? `<span class="snc-badge ok font-mono"><i class="fas fa-check-circle" style="font-size:9px;"></i> NC ${sncEsc(s.nc_folio)}</span>`
//        : `<button class="btn-icon warn" style="padding:3px 9px;font-size:10px;"
//                            onclick="sncIrANC(${sncEsc(s.encabezado_ids)}, '${sncEsc(s.motivo_tipo)}')">
//                            <i class="fas fa-arrow-right"></i> Generar NC
//                        </button>`
//    }
//</td>
function _sncHistEstatus(estatus) {
    const map = {
        'PENDIENTE': { class: 'pending', label: 'Pendiente' },
        'EN_PROCESO': { class: 'info', label: 'En proceso' },
        'NC_EMITIDA': { class: 'ok', label: 'NC emitida' },
        'CANCELADA': { class: 'danger', label: 'Cancelada' },
        'RECHAZADA': { class: 'danger', label: 'Rechazada' },
    };
    return map[estatus] || { class: 'gray', label: estatus || 'Desconocido' };
}

function sncIrANC(encabezadoIds, tipoNC) {
    const params = new URLSearchParams({
        encabezadoIds: Array.isArray(encabezadoIds) ? encabezadoIds.join(',') : encabezadoIds,
        tipoNC: MOTIVOS[tipoNC]?.tipoNC || tipoNC || 'standalone',
    });
    window.location.href = `/VINotaCredito/Index?${params}`;
}

/* ══════════════════════════════════════════════════════════════
   MODALES — HELPERS
══════════════════════════════════════════════════════════════ */
function openSncModal(id) {
    document.getElementById(id)?.classList.add('open');
}

function closeSncModal(id) {
    document.getElementById(id)?.classList.remove('open');
    if (id === 'snc-modal-solicitud') _sncRestaurarCamposModal();
}

/* ══════════════════════════════════════════════════════════════
   INICIALIZACIÓN
══════════════════════════════════════════════════════════════ */
document.addEventListener('DOMContentLoaded', () => {

    // Carga inicial de facturas
    sncFetchFacturas();

    // Sidebar inicial
    sncUpdateSidebar();

    // Cerrar modales con Escape
    document.addEventListener('keydown', e => {
        if (e.key === 'Escape') {
            document.querySelectorAll('.modal-overlay.open').forEach(m => {
                m.classList.remove('open');
            });
        }
    });

    // Cerrar modales con click fuera
    document.querySelectorAll('.modal-overlay').forEach(m => {
        m.addEventListener('click', e => {
            if (e.target === m) m.classList.remove('open');
        });
    });

    // Si se recibe desde el historial o desde otra pantalla con facturas preseleccionadas
    const urlParams = new URLSearchParams(window.location.search);
    const presel = urlParams.get('preselect');
    if (presel) {
        // Cargar y preseleccionar automáticamente — se resuelve tras el primer fetch
        SNC_FACTURAS._preselect = presel.split(',').map(Number).filter(Boolean);
    }

    // Tab por defecto
    sncSwitchTab('nueva');
});

// ============================================================
// rack-view.js — Renderizado de vista rack/estante
// ============================================================

// ─────────────────────────────────────────────────────────
// Punto de entrada: renderiza la vista activa
// ─────────────────────────────────────────────────────────

function renderView() {
    const container = document.getElementById('rackViewContainer');
    if (!container) return;

    if (currentView === 'rack') {
        container.innerHTML = buildRackViewHTML();
    } else {
        container.innerHTML = buildListViewHTML();
    }

    bindCellEvents();
    updateViewButtons();
}

function updateViewButtons() {
    const btnRack = document.getElementById('btnViewRack');
    const btnList = document.getElementById('btnViewList');
    if (btnRack) btnRack.classList.toggle('active', currentView === 'rack');
    if (btnList) btnList.classList.toggle('active', currentView === 'list');
}

// ─────────────────────────────────────────────────────────
// Vista RACK — árbol: sucursal > almacén > pasillo > rack > cols
// ─────────────────────────────────────────────────────────

function buildRackViewHTML() {
    let html = '';

    warehouseData.sucursales.forEach(suc => {
        if (!suc.almacenes || !suc.almacenes.length) return;

        html += `
        <div class="rv-suc-block">
            <div class="rv-suc-title">
                <i class="ti ti-building" aria-hidden="true"></i>
                ${esc(suc.descripcion)}
                <span class="rv-badge">${esc(suc.cve)}</span>
            </div>`;

        suc.almacenes.forEach(alm => {
            html += `<div class="rv-alm-block">
                <div class="rv-alm-title">
                    <i class="ti ti-building-warehouse" aria-hidden="true"></i>
                    ${esc(alm.descripcion || alm.cve)}
                    <span class="rv-badge-type">${esc(alm.tipo || '')}</span>
                </div>`;

            let anyContent = false;

            (alm.pasillos || []).forEach(pas => {
                if (!pas.racks || !pas.racks.length) return;
                const racksHTML = buildRacksHTML(pas.racks, alm);
                if (!racksHTML) return;
                html += `
                <div class="rv-pas-block">
                    <div class="rv-pas-label">
                        <i class="ti ti-road" aria-hidden="true"></i>
                        Pasillo ${esc(pas.cve || pas.num_pasillo)}
                    </div>
                    <div class="rv-racks-row">${racksHTML}</div>
                </div>`;
                anyContent = true;
            });

            if (alm.racks_sin_pasillo && alm.racks_sin_pasillo.length) {
                const racksHTML = buildRacksHTML(alm.racks_sin_pasillo, alm);
                if (racksHTML) {
                    html += `
                    <div class="rv-pas-block">
                        <div class="rv-pas-label">
                            <i class="ti ti-layout-grid" aria-hidden="true"></i>
                            Sin pasillo
                        </div>
                        <div class="rv-racks-row">${racksHTML}</div>
                    </div>`;
                    anyContent = true;
                }
            }

            if (!anyContent) {
                html += `<div class="rv-empty">Sin racks configurados</div>`;
            }

            html += `</div>`;
        });

        html += `</div>`;
    });

    return html || '<div class="rv-empty" style="text-align:center;padding:40px">Sin datos disponibles</div>';
}

function buildRacksHTML(racks, alm) {
    let out = '';

    racks.forEach(rack => {
        if (!rack.columnas || !rack.columnas.length) return;

        let colsHTML = '';
        let anyVisible = false;

        rack.columnas.forEach(col => {
            // Ordenar niveles de mayor a menor (visualización: N3 arriba, N1 abajo)
            const niveles = [...(col.niveles || [])].reverse();
            let nivsHTML = '';

            niveles.forEach(niv => {
                if (!niv.ulocation) return;
                const key = `${alm.id}::${niv.ulocation}`;
                const loc = inventoryData[key];
                if (!loc) return;

                const visible = locMatchesSearch(loc);
                if (visible) anyVisible = true;

                const t = occupancyTier(loc.stock, loc.capacity);
                const p = occupancyPct(loc.stock, loc.capacity);
                const pctLabel = loc.stock > 0 ? p + '%' : '—';
                const selClass = selectedKey === key ? ' rv-cell-selected' : '';
                const dimStyle = (!searchQuery || visible) ? '' : 'opacity:.2;pointer-events:none';

                nivsHTML += `
                <div class="rv-nivel-cell rv-tier-${t}${selClass}"
                     style="${dimStyle}"
                     data-key="${key}"
                     role="button"
                     tabindex="0"
                     aria-label="Nivel ${esc(niv.ulocation)}, ocupación ${pctLabel}">
                    <span class="rv-cell-pct">${pctLabel}</span>
                    <span class="rv-cell-loc">${esc(niv.ulocation)}</span>
                </div>`;
            });

            if (nivsHTML) {
                colsHTML += `
                <div class="rv-col-block">
                    <div class="rv-col-header">${esc(col.nombre)}</div>
                    ${nivsHTML}
                </div>`;
            }
        });

        if (colsHTML) {
            out += `
            <div class="rv-rack-card">
                <div class="rv-rack-header">
                    <i class="ti ti-layout-rows" aria-hidden="true"></i>
                    ${esc(rack.nombre)}
                    <span class="rv-rack-tipo">${esc(rack.tipo || '')}</span>
                </div>
                <div class="rv-rack-cols">${colsHTML}</div>
            </div>`;
        }
    });

    return out;
}

// ─────────────────────────────────────────────────────────
// Vista LISTA
// ─────────────────────────────────────────────────────────

function buildListViewHTML() {
    const locs = Object.values(inventoryData).filter(locMatchesSearch);

    if (!locs.length) {
        return '<div class="rv-empty" style="text-align:center;padding:40px">Sin resultados para la búsqueda</div>';
    }

    const rows = locs.map(loc => {
        const t = occupancyTier(loc.stock, loc.capacity);
        const p = occupancyPct(loc.stock, loc.capacity);
        const barColor = tierColor(t);
        const textColor = tierTextColor(t);
        const selBorder = selectedKey === loc.key
            ? 'border-left:3px solid var(--rv-accent)'
            : 'border-left:3px solid transparent';

        return `
        <div class="rv-list-row" style="${selBorder}" data-key="${loc.key}" role="button" tabindex="0">
            <div class="rv-list-loc">${esc(loc.ulocation)}</div>
            <div class="rv-list-alm">${esc(loc.almacen)}</div>
            <div class="rv-list-ruta">${esc(loc.rack)} › ${esc(loc.columna)} › ${esc(loc.nivel)}</div>
            <div class="rv-list-sku">${loc.productos.length} SKU(s)</div>
            <div class="rv-list-bar-wrap">
                <div class="rv-list-bar-bg">
                    <div class="rv-list-bar-fill" style="width:${p}%;background:${barColor}"></div>
                </div>
                <span class="rv-list-pct" style="color:${textColor}">${loc.stock > 0 ? p + '%' : '—'}</span>
            </div>
        </div>`;
    }).join('');

    return `
    <div class="rv-list-table">
        <div class="rv-list-header">
            <div class="rv-list-loc">Ubicación</div>
            <div class="rv-list-alm">Almacén</div>
            <div class="rv-list-ruta">Ruta</div>
            <div class="rv-list-sku">SKUs</div>
            <div class="rv-list-bar-wrap">Ocupación</div>
        </div>
        ${rows}
    </div>`;
}

// ─────────────────────────────────────────────────────────
// Colores por tier (deben coincidir con las clases CSS)
// ─────────────────────────────────────────────────────────

function tierColor(t) {
    return { high: 'var(--rv-high)', medium: 'var(--rv-medium)', low: 'var(--rv-low)', empty: 'var(--rv-empty)' }[t] || 'var(--rv-empty)';
}

function tierTextColor(t) {
    return { high: 'var(--rv-high-text)', medium: 'var(--rv-medium-text)', low: 'var(--rv-low-text)', empty: 'var(--rv-empty-text)' }[t] || 'var(--rv-empty-text)';
}

// ─────────────────────────────────────────────────────────
// Filtrado de búsqueda
// ─────────────────────────────────────────────────────────

function locMatchesSearch(loc) {
    if (!searchQuery) return true;
    const q = searchQuery.toLowerCase();
    if (loc.ulocation.toLowerCase().includes(q)) return true;
    if (loc.almacen.toLowerCase().includes(q)) return true;
    if (loc.rack.toLowerCase().includes(q)) return true;
    if (loc.productos.some(p =>
        p.cve.toLowerCase().includes(q) ||
        (p.descripcion || '').toLowerCase().includes(q)
    )) return true;
    return false;
}

// ─────────────────────────────────────────────────────────
// Eventos de las celdas
// ─────────────────────────────────────────────────────────

function bindCellEvents() {
    const tooltip = document.getElementById('rvTooltip');

    document.querySelectorAll('[data-key]').forEach(el => {
        el.addEventListener('mouseenter', e => showCellTooltip(e, el.dataset.key, tooltip));
        el.addEventListener('mousemove', e => moveCellTooltip(e, tooltip));
        el.addEventListener('mouseleave', () => { if (tooltip) tooltip.style.display = 'none'; });
        el.addEventListener('click', () => openDetailPanel(el.dataset.key));
        el.addEventListener('keydown', e => {
            if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); openDetailPanel(el.dataset.key); }
        });
    });
}

function showCellTooltip(e, key, tooltip) {
    if (!tooltip) return;
    const loc = inventoryData[key];
    if (!loc) return;

    const t = occupancyTier(loc.stock, loc.capacity);
    const p = occupancyPct(loc.stock, loc.capacity);
    const lbl = { high: 'Alto', medium: 'Medio', low: 'Bajo', empty: 'Vacío' }[t];

    tooltip.innerHTML = `
        <strong>${esc(loc.ulocation)}</strong>
        <div class="rv-tt-row"><span>Almacén</span><span>${esc(loc.almacen)}</span></div>
        <div class="rv-tt-row"><span>Ruta</span><span>${esc(loc.rack)} › ${esc(loc.columna)} › ${esc(loc.nivel)}</span></div>
        <div class="rv-tt-row"><span>Stock</span><span>${loc.stock.toLocaleString()} / ${loc.capacity.toLocaleString()} uds</span></div>
        <div class="rv-tt-row"><span>Ocupación</span><span>${loc.stock > 0 ? p + '%' : '—'} (${lbl})</span></div>
        <div class="rv-tt-row"><span>SKUs</span><span>${loc.productos.length}</span></div>`;
    tooltip.style.display = 'block';
    moveCellTooltip(e, tooltip);
}

function moveCellTooltip(e, tooltip) {
    if (!tooltip) return;
    let x = e.clientX + 14, y = e.clientY - 10;
    if (x + 270 > window.innerWidth) x = e.clientX - 274;
    tooltip.style.left = x + 'px';
    tooltip.style.top = y + 'px';
}

// ─────────────────────────────────────────────────────────
// Panel de detalle lateral
// ─────────────────────────────────────────────────────────

function openDetailPanel(key) {
    const loc = inventoryData[key];
    if (!loc) return;

    selectedKey = key;
    renderView(); // refrescar highlight

    const t = occupancyTier(loc.stock, loc.capacity);
    const p = occupancyPct(loc.stock, loc.capacity);
    const barColor = tierColor(t);
    const tierLabel = { high: 'Alto ≥70%', medium: 'Medio 30–69%', low: 'Bajo <30%', empty: 'Vacío' }[t];

    const path = [loc.sucursal, loc.almacen,
    loc.pasillo !== 'Sin pasillo' ? loc.pasillo : null,
    loc.rack, loc.columna, loc.nivel]
        .filter(Boolean)
        .map(s => `<span class="rv-dp-path-step">${esc(s)}</span>`)
        .join(' › ');

    const prodsHTML = loc.productos.length === 0
        ? `<div class="rv-dp-empty">Sin productos en este nivel</div>`
        : loc.productos.map(pr => `
            <div class="rv-dp-prod">
                <div class="rv-dp-prod-cve">${esc(pr.cve)}</div>
                <div class="rv-dp-prod-desc">${esc(pr.descripcion || 'Sin descripción')}</div>
                <div class="rv-dp-prod-meta">
                    <span class="rv-dp-prod-qty">${pr.cantidad.toLocaleString()} ${esc(pr.unidad || 'uds')}</span>
                    <span>Línea: ${esc(pr.linea || '—')}</span>
                    <span>Tarima: ${esc(pr.tarimaCod)}</span>
                </div>
            </div>`).join('');

    document.getElementById('rvDetailContent').innerHTML = `
        <div class="rv-dp-title">
            <i class="ti ti-map-pin" aria-hidden="true"></i>
            ${esc(loc.ulocation)}
        </div>
        <div class="rv-dp-path">${path}</div>

        <div class="rv-dp-bar-section">
            <div class="rv-dp-bar-header">
                <span>Ocupación</span>
                <span style="font-weight:500">${loc.stock.toLocaleString()} / ${loc.capacity.toLocaleString()} uds</span>
            </div>
            <div class="rv-dp-bar-bg">
                <div class="rv-dp-bar-fill" style="width:${p}%;background:${barColor}"></div>
            </div>
            <div class="rv-dp-tier-label" style="color:${barColor}">${tierLabel}</div>
        </div>

        <div class="rv-dp-section">
            <div class="rv-dp-section-title">Información del nivel</div>
            <div class="rv-dp-row"><span>Almacén</span><span>${esc(loc.almacen)} (${esc(loc.almacenCve)})</span></div>
            <div class="rv-dp-row"><span>Tipo almacén</span><span>${esc(loc.almacenTipo || '—')}</span></div>
            <div class="rv-dp-row"><span>Pasillo</span><span>${esc(loc.pasillo)}</span></div>
            <div class="rv-dp-row"><span>Rack</span><span>${esc(loc.rack)} — ${esc(loc.rackTipo || '—')}</span></div>
            <div class="rv-dp-row"><span>Columna</span><span>${esc(loc.columna)}</span></div>
            <div class="rv-dp-row"><span>Nivel</span><span>${esc(loc.nivel)}</span></div>
            <div class="rv-dp-row"><span>Capacidad</span><span>${loc.capacity.toLocaleString()} uds</span></div>
            <div class="rv-dp-row"><span>Actualización</span><span>${esc(loc.lastUpdate)}</span></div>
        </div>

        <div class="rv-dp-section">
            <div class="rv-dp-section-title">Productos — ${loc.productos.length} SKU(s)</div>
            ${prodsHTML}
        </div>`;

    document.getElementById('rvDetailPanel').classList.add('open');
}

function closeDetailPanel() {
    document.getElementById('rvDetailPanel').classList.remove('open');
    selectedKey = null;
    renderView();
}

// ─────────────────────────────────────────────────────────
// Acciones de la toolbar
// ─────────────────────────────────────────────────────────

function switchView(v) {
    currentView = v;
    renderView();
}

function handleSearchInput(val) {
    searchQuery = val.trim();
    renderView();
}

// ─────────────────────────────────────────────────────────
// Utilidad HTML escape
// ─────────────────────────────────────────────────────────

function esc(s) {
    return String(s || '').replace(/[&<>"']/g, c => (
        { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]
    ));
}
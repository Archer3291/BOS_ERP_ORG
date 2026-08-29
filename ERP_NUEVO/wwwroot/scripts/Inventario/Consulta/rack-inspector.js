/**
 * rack-inspector.js
 * Renderizado isométrico SVG del rack + paneles del modal (Resumen / Niveles / Tarimas)
 * + sub-modal de detalle de producto.
 */

'use strict';

const RackInspector = (() => {

    const ISO = {
        cellW: 48,
        cellH: 28,
        depthW: 16,
        depthH: 9,
        gap: 3,
        padX: 40,
        padY: 30,
    };

    const COLOR = {
        rackFrame: '#0d1f38',
        rackStroke: '#1e3a5f',
        nivelEmpty: '#0f1f35',
        nivelPartial: '#0e3050',
        nivelFull: '#0a3d2e',
        nivelLow: '#0a2d1e',
        tarimaBg: '#1a4a7a',
        tarimaTapa: '#1e5490',
        tarimaSide: '#143d66',
        accentCian: '#00d4ff',
        accentGreen: '#10b981',
        accentAmber: '#f59e0b',
        accentRed: '#ef4444',
        text: '#e2e8f0',
        textMuted: '#64748b',
        stroke: 'rgba(0,212,255,0.2)',
    };

    let _rackActual = null;   // último rack cargado, para el sub-modal de producto
    let _nivelActual = null;   // { nivel, rack, col } del nivel abierto en el panel
    let _tarimaActual = null;   // { tarima, nivel, columna, rack } del modal de tarima

    // ── Parsear capacidad — acepta string descriptivo o número ────
    function _parsearCapacidadNivel(nivel) {
        const tarimas = nivel.tarimas || [];
        const capRaw = (nivel.capacidad ?? '').toString().trim();

        if (capRaw === '') {
            return { pct: null, capNum: null, esString: false };
        }

        const capNorm = capRaw.toLowerCase()
            .normalize('NFD')
            .replace(/[\u0300-\u036f]/g, '');

        const MAPA = {
            lleno: 1.0, full: 1.0, alto: 1.0, completo: 1.0, ocupado: 1.0,
            medio: 0.55, medium: 0.55, med: 0.55, parcial: 0.55, mitad: 0.55,
            bajo: 0.2, low: 0.2, poco: 0.2, escaso: 0.2,
            vacio: 0, empty: 0, libre: 0, disponible: 0,
        };

        if (MAPA[capNorm] !== undefined) {
            return { pct: MAPA[capNorm], capNum: null, esString: true };
        }

        const pctMatch = capNorm.match(/^(\d+(?:\.\d+)?)\s*%$/);
        if (pctMatch) {
            return { pct: Math.min(1, parseFloat(pctMatch[1]) / 100), capNum: null, esString: false };
        }

        const capNum = parseFloat(capRaw.replace(',', '.'));
        if (!isNaN(capNum) && capNum > 0) {
            return { pct: Math.min(1, tarimas.length / capNum), capNum, esString: false };
        }

        return { pct: null, capNum: null, esString: false };
    }

    function _ocupToColor(pct) {
        if (pct >= 0.9) return { fill: COLOR.nivelFull, bar: COLOR.accentRed, barClass: 'full' };
        if (pct >= 0.5) return { fill: COLOR.nivelPartial, bar: COLOR.accentAmber, barClass: 'warn' };
        if (pct > 0) return { fill: COLOR.nivelLow, bar: COLOR.accentGreen, barClass: '' };
        return { fill: COLOR.nivelEmpty, bar: COLOR.accentCian, barClass: '' };
    }

    // ════════════════════════════════════════════════════════════
    // RENDER PRINCIPAL — rack 3D navegable (panel "Niveles")
    // Si el visor 3D no está disponible se cae al SVG isométrico.
    // ════════════════════════════════════════════════════════════
    /**
     * @param {{destacarTarimaId?:number}} [opts]  al venir de una búsqueda,
     *        señala en el rack 3D dónde está la tarima encontrada.
     */
    function render(rack, containerId, opts = {}) {
        _rackActual = rack;

        // Cada rack rehace sus tarjetas: el registro anterior ya no sirve
        _ctxTarimas.clear();
        _ctxSeq = 0;

        const container = document.getElementById(containerId);
        if (!container) return;

        if (window.Rack3D) {
            document.getElementById('av-rack-3d-placeholder')?.classList.add('hide');
            window.Rack3D.render(containerId, rack, {
                onNivel: (nivel, col) => _mostrarDetalleNivel(nivel, rack, col),
                onTarima: (tarima, nivel, col) =>
                    abrirTarima({ tarima, nivel, columna: col, rack }),
            }, { destacarTarimaId: opts.destacarTarimaId });
            return;
        }

        _renderIso(rack, containerId);
    }

    // ════════════════════════════════════════════════════════════
    // FALLBACK — SVG isométrico (sin WebGL / sin three.js)
    // ════════════════════════════════════════════════════════════
    function _renderIso(rack, containerId) {
        const container = document.getElementById(containerId);
        if (!container) return;

        const cols = rack.columnas || [];
        const numCol = Math.max(cols.length, 1);
        const numNiv = Math.max(...cols.map(c => (c.niveles || []).length), 3);

        const svgW = ISO.padX * 2 + numCol * (ISO.cellW + ISO.gap) + ISO.depthW + 20;
        const svgH = ISO.padY * 2 + numNiv * (ISO.cellH + ISO.gap) + ISO.depthH + 60;

        let svg = `<svg id="av-rack-iso-svg" viewBox="0 0 ${svgW} ${svgH}"
                       xmlns="http://www.w3.org/2000/svg"
                       style="width:100%;overflow:visible;display:block;">
            <defs>${_defs()}</defs>
            <g id="rack-iso-root">`;

        svg += _rackFrame(numCol, numNiv, svgW, svgH);

        cols.forEach((col, ci) => {
            const sorted = [...(col.niveles || [])].sort((a, b) => b.num_nivel - a.num_nivel);

            sorted.forEach((nivel, ni) => {
                const x = ISO.padX + ci * (ISO.cellW + ISO.gap);
                const y = ISO.padY + ni * (ISO.cellH + ISO.gap);
                svg += _renderNivel(nivel, ci, ni, x, y, numNiv);
            });

            const lblX = ISO.padX + ci * (ISO.cellW + ISO.gap) + ISO.cellW / 2;
            const lblY = ISO.padY + numNiv * (ISO.cellH + ISO.gap) + 16;
            svg += `<text x="${lblX}" y="${lblY}" text-anchor="middle"
                          font-size="9" font-family="Segoe UI,sans-serif"
                          fill="${COLOR.textMuted}">C${col.num_col || (ci + 1)}</text>`;
        });

        for (let ni = 0; ni < numNiv; ni++) {
            const y = ISO.padY + ni * (ISO.cellH + ISO.gap) + ISO.cellH / 2 + 4;
            svg += `<text x="${ISO.padX - 8}" y="${y}" text-anchor="end"
                          font-size="8" font-family="Segoe UI,sans-serif"
                          fill="${COLOR.textMuted}">N${numNiv - ni}</text>`;
        }

        svg += `</g></svg>`;
        container.innerHTML = svg;
        _bindNivelClicks(rack);
    }

    function _rackFrame(numCol, numNiv, svgW, svgH) {
        const x1 = ISO.padX - 4, y1 = ISO.padY - 4;
        const x2 = ISO.padX + numCol * (ISO.cellW + ISO.gap) + 4;
        const y2 = ISO.padY + numNiv * (ISO.cellH + ISO.gap) + 4;
        const dw = ISO.depthW, dh = ISO.depthH;

        let s = `<rect x="${x1}" y="${y1}" width="${x2 - x1}" height="${y2 - y1}"
                       fill="${COLOR.rackFrame}" stroke="${COLOR.rackStroke}"
                       stroke-width="1.5" rx="3"/>`;

        s += `<polygon points="${x1},${y1} ${x2},${y1} ${x2 + dw},${y1 - dh} ${x1 + dw},${y1 - dh}"
                       fill="#0d2540" stroke="${COLOR.rackStroke}" stroke-width="1"/>`;
        s += `<polygon points="${x2},${y1} ${x2 + dw},${y1 - dh} ${x2 + dw},${y2 - dh} ${x2},${y2}"
                       fill="#0a1e33" stroke="${COLOR.rackStroke}" stroke-width="1"/>`;

        for (let ci = 0; ci <= numCol; ci++) {
            const rx = x1 + ci * (ISO.cellW + ISO.gap) + (ci > 0 ? ISO.gap : 0);
            s += `<line x1="${rx}" y1="${y1}" x2="${rx}" y2="${y2}"
                        stroke="${COLOR.rackStroke}" stroke-width="1" opacity="0.6"/>`;
        }
        for (let ni = 0; ni <= numNiv; ni++) {
            const ry = y1 + ni * (ISO.cellH + ISO.gap);
            s += `<line x1="${x1}" y1="${ry}" x2="${x2}" y2="${ry}"
                        stroke="${COLOR.rackStroke}" stroke-width="0.8" opacity="0.5"/>`;
        }
        return s;
    }

    function _renderNivel(nivel, ci, ni, x, y, numNiv) {
        const tarimas = nivel.tarimas || [];
        const ocup = _parsearCapacidadNivel(nivel);
        const colors = _ocupToColor(ocup.pct);

        let s = `<g class="rack-nivel"
                    data-nivel-id="${nivel.id}"
                    data-col="${ci}" data-ni="${ni}"
                    style="cursor:pointer">`;

        s += `<rect x="${x}" y="${y}" width="${ISO.cellW}" height="${ISO.cellH}"
                    fill="${colors.fill}" stroke="${COLOR.stroke}" stroke-width="0.8" rx="2"/>`;

        if (ocup.pct > 0) {
            const barH = Math.max(2, Math.round(ISO.cellH * ocup.pct));
            s += `<rect x="${x}" y="${y + ISO.cellH - barH}"
                        width="3" height="${barH}"
                        fill="${colors.bar}" opacity="0.85" rx="1"/>`;
        }

        tarimas.slice(0, 3).forEach((tarima, ti) => {
            s += _renderTarimaIso(x + 6 + ti * 13, y + ISO.cellH - 16, tarima);
        });

        if (tarimas.length > 0) {
            s += `<text x="${x + ISO.cellW - 4}" y="${y + 10}"
                        text-anchor="end" font-size="7"
                        font-family="Cascadia Code,Consolas,monospace"
                        fill="${COLOR.accentCian}" font-weight="bold">${tarimas.length}T</text>`;
        }

        if (ocup.pct > 0) {
            s += `<circle cx="${x + 6}" cy="${y + 6}" r="2.5"
                          fill="${colors.bar}" opacity="0.9"/>`;
        }

        s += `<rect x="${x}" y="${y}" width="${ISO.cellW}" height="${ISO.cellH}"
                    fill="transparent" stroke="transparent" stroke-width="2"
                    rx="2" class="nivel-hover-rect"/>`;

        s += `</g>`;
        return s;
    }

    function _renderTarimaIso(x, y, tarima) {
        const w = 10, h = 8, dw = 5, dh = 3;
        const hasProd = (tarima.productos || []).length > 0;
        const topFill = hasProd ? COLOR.tarimaTapa : 'rgba(26,74,122,0.4)';

        let s = `<rect x="${x}" y="${y}" width="${w}" height="${h}"
                       fill="${COLOR.tarimaBg}" stroke="rgba(0,212,255,0.3)"
                       stroke-width="0.5" rx="1"/>`;
        s += `<polygon points="${x},${y} ${x + w},${y} ${x + w + dw},${y - dh} ${x + dw},${y - dh}"
                       fill="${topFill}" stroke="rgba(0,212,255,0.25)" stroke-width="0.5"/>`;
        s += `<polygon points="${x + w},${y} ${x + w + dw},${y - dh} ${x + w + dw},${y + h - dh} ${x + w},${y + h}"
                       fill="${COLOR.tarimaSide}" stroke="rgba(0,212,255,0.2)" stroke-width="0.5"/>`;
        if (hasProd) {
            s += `<circle cx="${x + w / 2}" cy="${y + h / 2}" r="1.5"
                          fill="${COLOR.accentGreen}" opacity="0.8"/>`;
        }
        return s;
    }

    function _defs() {
        return `
        <filter id="rack-glow" x="-20%" y="-20%" width="140%" height="140%">
            <feGaussianBlur stdDeviation="2" result="blur"/>
            <feMerge><feMergeNode in="blur"/><feMergeNode in="SourceGraphic"/></feMerge>
        </filter>
        <linearGradient id="rack-shine" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%"   stop-color="rgba(0,212,255,0.12)"/>
            <stop offset="100%" stop-color="rgba(0,212,255,0)"/>
        </linearGradient>`;
    }

    function _bindNivelClicks(rack) {
        const svgEl = document.getElementById('av-rack-iso-svg');
        if (!svgEl) return;

        svgEl.querySelectorAll('.rack-nivel').forEach(g => {
            const colIdx = parseInt(g.dataset.col);
            const niIdx = parseInt(g.dataset.ni);

            g.addEventListener('mouseenter', () => {
                const hr = g.querySelector('.nivel-hover-rect');
                if (hr) hr.setAttribute('stroke', '#00d4ff');
            });
            g.addEventListener('mouseleave', () => {
                const hr = g.querySelector('.nivel-hover-rect');
                if (hr) hr.setAttribute('stroke', 'transparent');
            });
            g.addEventListener('click', () => {
                const col = (rack.columnas || [])[colIdx];
                const sorted = [...(col?.niveles || [])].sort((a, b) => b.num_nivel - a.num_nivel);
                const nivel = sorted[niIdx];
                if (nivel) _mostrarDetalleNivel(nivel, rack);
            });
        });
    }

    // ── Detalle del nivel seleccionado (dentro del panel "Niveles") ─
    function _mostrarDetalleNivel(nivel, rack, col) {
        const container = document.getElementById('av-nivel-detail');
        if (!container) return;

        _nivelActual = { nivel, rack, col };
        const tarimas = nivel.tarimas || [];
        const totalProd = tarimas.reduce((s, t) => s + (t.productos || []).length, 0);
        const ocup = _parsearCapacidadNivel(nivel);

        const capDisplay = ocup.esString
            ? (nivel.capacidad || '—')
            : (ocup.capNum != null ? `${ocup.capNum} tarimas` : '—');

        const colors = ocup.pct !== null ? _ocupToColor(ocup.pct) : null;
        const pct = ocup.pct !== null ? Math.round(ocup.pct * 100) : null;
        const pctText = ocup.pct !== null
            ? (ocup.esString ? nivel.capacidad : pct + '%')
            : null;

        let html = `
    <div class="av-detail-section">
        <div class="av-detail-title">
            <i class="fas fa-layer-group"></i>
            Nivel ${nivel.num_nivel}${nivel.nombre ? ' — ' + nivel.nombre : ''}
        </div>
        <div class="av-info-grid">
            <div class="av-info-item">
                <label>Ubicación</label>
                <span>${nivel.ulocation || '—'}</span>
            </div>
            <div class="av-info-item">
                <label>Capacidad</label>
                <span ${colors ? `style="color:${colors.bar}"` : ''}>${capDisplay}</span>
            </div>
            <div class="av-info-item">
                <label>Tarimas</label>
                <span>${tarimas.length}</span>
            </div>
            <div class="av-info-item">
                <label>Productos</label>
                <span>${totalProd} líneas</span>
            </div>
        </div>

        ${colors !== null ? `
        <div style="margin-top:10px;">
            <div style="display:flex;justify-content:space-between;
                        font-size:9px;color:var(--av-text-muted);margin-bottom:3px;">
                <span>Ocupación</span>
                <span style="color:${colors.bar};font-weight:600;">${pctText}</span>
            </div>
            <div class="av-ocupacion-bar">
                <div class="av-ocupacion-fill ${colors.barClass}"
                     style="width:${pct}%"></div>
            </div>
        </div>` : ''}
    </div>`;

        if (tarimas.length > 0) {
            html += `<div class="av-detail-section">
            <div class="av-detail-title"><i class="fas fa-pallet"></i> Tarimas</div>`;
            html += _renderTarimasCards(tarimas, { nivel, col, rack });
            html += `</div>`;
        } else {
            html += `
        <div class="av-detail-section">
            <div style="text-align:center;padding:20px;color:var(--av-text-muted);">
                <i class="fas fa-inbox"
                   style="font-size:28px;opacity:0.3;display:block;margin-bottom:8px;"></i>
                <span style="font-size:11px;">Nivel vacío</span>
            </div>
        </div>`;
        }

        container.innerHTML = html;
        _bindProductRowClicks(container);
    }

    // ════════════════════════════════════════════════════════════
    // PANEL "RESUMEN"
    // ════════════════════════════════════════════════════════════
    function renderDetalle(rack, containerId) {
        _rackActual = rack;

        const container = document.getElementById(containerId);
        if (!container) return;

        const cols = rack.columnas || [];
        const totalTarimas = cols.reduce((s, c) =>
            s + (c.niveles || []).reduce((s2, n) => s2 + (n.tarimas || []).length, 0), 0);
        const totalProds = cols.reduce((s, c) =>
            s + (c.niveles || []).reduce((s2, n) =>
                s2 + (n.tarimas || []).reduce((s3, t) => s3 + (t.productos || []).length, 0), 0), 0);
        const totalNiveles = cols.reduce((s, c) => s + (c.niveles || []).length, 0);

        const capRaw = (rack.capacidad ?? '').toString().trim();
        const capNorm = capRaw.toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g, '');

        const MAPA_STR = {
            lleno: 1.0, full: 1.0, alto: 1.0, completo: 1.0, ocupado: 1.0,
            medio: 0.55, medium: 0.55, med: 0.55, parcial: 0.55, mitad: 0.55,
            bajo: 0.2, low: 0.2, poco: 0.2, escaso: 0.2,
            vacio: 0, empty: 0, libre: 0, disponible: 0,
        };

        let globalPct = null, esString = false, capDisplay = '—';

        if (capRaw === '' || capRaw === null) {
            globalPct = null;
            capDisplay = '—';
        } else if (MAPA_STR[capNorm] !== undefined) {
            globalPct = MAPA_STR[capNorm];
            esString = true;
            capDisplay = rack.capacidad;
        } else {
            const capNum = parseFloat(capRaw.replace(',', '.'));
            if (!isNaN(capNum) && capNum > 0) {
                globalPct = Math.min(1, totalTarimas / capNum);
                capDisplay = `${capNum} tarimas`;
            } else {
                globalPct = null;
                capDisplay = capRaw || '—';
            }
        }

        const colors = globalPct !== null ? _ocupToColor(globalPct) : _ocupToColor(0);
        const pctText = globalPct !== null
            ? (esString ? rack.capacidad : Math.round(globalPct * 100) + '%')
            : null;

        container.innerHTML = `
    <div class="av-im-stats-grid">
        <div class="av-im-stat-card" style="--stat-color:${COLOR.accentCian}">
            <i class="fas fa-grip-lines-vertical av-im-stat-icon"></i>
            <div class="av-im-stat-value">${cols.length}</div>
            <div class="av-im-stat-label">Columnas</div>
        </div>
        <div class="av-im-stat-card" style="--stat-color:${COLOR.accentGreen}">
            <i class="fas fa-layer-group av-im-stat-icon"></i>
            <div class="av-im-stat-value">${totalNiveles}</div>
            <div class="av-im-stat-label">Niveles</div>
        </div>
        <div class="av-im-stat-card" style="--stat-color:${COLOR.accentAmber}">
            <i class="fas fa-pallet av-im-stat-icon"></i>
            <div class="av-im-stat-value">${totalTarimas}</div>
            <div class="av-im-stat-label">Tarimas</div>
        </div>
        <div class="av-im-stat-card" style="--stat-color:${COLOR.accentCian}">
            <i class="fas fa-box av-im-stat-icon"></i>
            <div class="av-im-stat-value">${totalProds}</div>
            <div class="av-im-stat-label">Líneas prod.</div>
        </div>
    </div>

    <div class="av-detail-section">
        <div class="av-detail-title"><i class="fas fa-info-circle"></i> Información general</div>
        <div class="av-info-grid">
            <div class="av-info-item">
                <label>Tipo de rack</label>
                <span>${rack.tipo || '—'}</span>
            </div>
            <div class="av-info-item">
                <label>Lado</label>
                <span>${rack.lado || '—'}</span>
            </div>
            <div class="av-info-item">
                <label>Capacidad</label>
                <span style="${globalPct !== null ? 'color:' + colors.bar : ''}">${capDisplay}</span>
            </div>
            <div class="av-info-item">
                <label>Núm. rack</label>
                <span>${rack.num_rack || '—'}</span>
            </div>
        </div>

        ${globalPct !== null ? `
        <div style="margin-top:14px;">
            <div style="display:flex;justify-content:space-between;
                        font-size:9px;color:var(--av-text-muted);margin-bottom:3px;">
                <span>Ocupación global</span>
                <span style="color:${colors.bar};font-weight:600;">${pctText}</span>
            </div>
            <div class="av-ocupacion-bar">
                <div class="av-ocupacion-fill ${colors.barClass}"
                     style="width:${Math.round(globalPct * 100)}%"></div>
            </div>
        </div>` : ''}
    </div>

    <p style="font-size:11px;color:var(--av-text-muted);text-align:center;margin-top:10px;">
        <i class="fas fa-hand-pointer" style="margin-right:4px;"></i>
        Ve a la pestaña <strong style="color:var(--av-accent)">Niveles</strong> para inspeccionar el rack en 3D,
        o a <strong style="color:var(--av-accent)">Tarimas</strong> para ver todos los productos.
    </p>`;

        const badge = document.getElementById('av-im-tab-tarimas-count');
        if (badge) badge.textContent = totalTarimas;

        const sub = document.getElementById('av-inspector-modal-subtitle');
        if (sub) {
            sub.textContent = `${cols.length} columnas · ${totalNiveles} niveles · ${totalTarimas} tarimas · ${totalProds} productos`;
        }

        _renderPanelTarimas(rack);
    }

    // ════════════════════════════════════════════════════════════
    // PANEL "TARIMAS" — todas las tarimas del rack
    // ════════════════════════════════════════════════════════════
    function _renderPanelTarimas(rack) {
        const container = document.getElementById('av-im-panel-tarimas');
        if (!container) return;

        const cols = rack.columnas || [];
        let html = '';
        let totalTarimas = 0;

        cols.forEach(col => {
            const sorted = [...(col.niveles || [])].sort((a, b) => b.num_nivel - a.num_nivel);
            sorted.forEach(nivel => {
                const tarimas = nivel.tarimas || [];
                if (tarimas.length === 0) return;
                totalTarimas += tarimas.length;

                html += `
                <div class="av-detail-section">
                    <div class="av-detail-title">
                        <i class="fas fa-map-pin"></i>
                        Col ${col.num_col || '—'} · Nivel ${nivel.num_nivel || '—'}
                        ${nivel.ulocation ? ` <span style="color:var(--av-text-muted);font-weight:400;">(${nivel.ulocation})</span>` : ''}
                    </div>
                    ${_renderTarimasCards(tarimas, { nivel, col, rack })}
                </div>`;
            });
        });

        if (totalTarimas === 0) {
            html = `
            <div style="text-align:center;padding:60px 20px;color:var(--av-text-muted);">
                <i class="fas fa-inbox" style="font-size:32px;opacity:0.3;display:block;margin-bottom:10px;"></i>
                <span style="font-size:12px;">Este rack no tiene tarimas registradas</span>
            </div>`;
        }

        container.innerHTML = html;
        _bindProductRowClicks(container);
    }

    // Registro de contextos de tarima: las tarjetas se construyen como texto,
    // así que el objeto se guarda aparte y la tarjeta solo lleva su clave.
    const _ctxTarimas = new Map();
    let _ctxSeq = 0;

    // ── Helper: cards de tarimas (reutilizado en Niveles y Tarimas) ─
    function _renderTarimasCards(tarimas, ctx) {
        let html = '';
        tarimas.forEach(tarima => {
            const prods = tarima.productos || [];

            const clave = ++_ctxSeq;
            _ctxTarimas.set(String(clave), {
                tarima,
                nivel: ctx?.nivel,
                columna: ctx?.col,
                rack: ctx?.rack || _rackActual,
            });

            html += `
            <div class="av-tarima-card">
                <div class="av-tarima-header"
                     onclick="this.nextElementSibling.classList.toggle('open')">
                    <div class="av-tarima-icon"><i class="fas fa-pallet"></i></div>
                    <div class="av-tarima-meta">
                        <strong>${tarima.codigo || 'Tarima ' + tarima.id}</strong>
                        <span>${prods.length} producto${prods.length !== 1 ? 's' : ''}
                              · ${_formatDate(tarima.fecha)}</span>
                    </div>
                    <button class="av-tarima-3d-btn" data-ctx="${clave}"
                            title="Ver la tarima en 3D">
                        <i class="fas fa-cube"></i>
                    </button>
                    <i class="fas fa-chevron-down"
                       style="font-size:9px;color:var(--av-text-muted)"></i>
                </div>
                <div class="av-tarima-body">`;

            if (prods.length === 0) {
                html += `<p style="font-size:11px;color:var(--av-text-muted);
                               text-align:center;padding:8px 0;">
                        Sin productos registrados</p>`;
            } else {
                html += `<table class="av-prod-table">
                        <thead><tr>
                            <th>Clave</th><th>Descripción</th>
                            <th>Cant.</th><th>UDM</th>
                        </tr></thead><tbody>`;
                prods.forEach(p => {
                    html += `<tr class="av-prod-row-click" data-prod-id="${p.id}" data-tarima-codigo="${_escapeAttr(tarima.codigo || ('Tarima ' + tarima.id))}">
                    <td style="font-family:var(--av-font-mono);font-size:10px;
                               color:var(--av-accent)">${p.cve_prod || '—'}</td>
                    <td title="${_escapeAttr(p.descr_prod || '')}"
                        style="max-width:160px;overflow:hidden;
                               text-overflow:ellipsis;white-space:nowrap;">
                        ${p.descr_prod || '—'}</td>
                    <td><span class="av-qty-badge">${_fmtNum(p.cantidad)}</span></td>
                    <td style="color:var(--av-text-muted)">
                        ${p.udm || p.unidad || '—'}</td>
                </tr>`;
                });
                html += `</tbody></table>`;
            }
            html += `</div></div>`;
        });
        return html;
    }

    // ── Bind: click en fila de producto abre el sub-modal ───────────
    function _bindProductRowClicks(scopeEl) {
        scopeEl.querySelectorAll('.av-prod-row-click').forEach(tr => {
            tr.addEventListener('click', () => {
                const prodId = tr.dataset.prodId;
                const tarimaCodigo = tr.dataset.tarimaCodigo;
                _abrirModalProducto(prodId, tarimaCodigo);
            });
        });

        // Botón "ver en 3D" de cada tarjeta de tarima
        scopeEl.querySelectorAll('.av-tarima-3d-btn').forEach(btn => {
            btn.addEventListener('click', (e) => {
                e.stopPropagation();   // no plegar/desplegar la tarjeta
                const ctx = _ctxTarimas.get(btn.dataset.ctx);
                if (ctx) abrirTarima(ctx);
            });
        });
    }

    // ════════════════════════════════════════════════════════════
    // MODAL DE TARIMA — visor 3D + productos
    // ════════════════════════════════════════════════════════════

    /** Paleta con la que Tarima3D colorea cada caja (para que la lista coincida). */
    function _paletaProd() {
        return window.Tarima3D?.paleta
            || ['#38bdf8', '#f59e0b', '#a78bfa', '#34d399',
                '#f472b6', '#fbbf24', '#60a5fa', '#4ade80'];
    }

    /**
     * Abre el detalle de una tarima.
     * @param {{tarima:Object, nivel:Object, columna:Object, rack:Object, almacen:Object}} ctx
     */
    function abrirTarima(ctx) {
        if (!ctx?.tarima) return;
        _tarimaActual = ctx;

        const { tarima, nivel, columna, rack, almacen } = ctx;
        const prods = tarima.productos || [];
        const paleta = _paletaProd();

        const overlay = document.getElementById('av-tarima-modal-overlay');
        const titulo = document.getElementById('av-tarima-modal-title');
        const subtitulo = document.getElementById('av-tarima-modal-subtitle');
        const side = document.getElementById('av-tarima-modal-side');
        if (!overlay || !side) return;

        if (titulo) titulo.textContent = tarima.codigo || ('Tarima ' + tarima.id);
        if (subtitulo) {
            const partes = [];
            if (rack?.num_rack) partes.push('Rack ' + rack.num_rack);
            if (columna?.num_col != null) partes.push('Col ' + columna.num_col);
            if (nivel?.num_nivel != null) partes.push('Nivel ' + nivel.num_nivel);
            if (tarima.fecha) partes.push(_formatDate(tarima.fecha));
            subtitulo.textContent = partes.join('  ·  ') || '—';
        }

        const totalCant = prods.reduce((s, p) => s + (parseFloat(p.cantidad) || 0), 0);

        let html = `
            <div class="av-tm-resumen">
                <div class="av-tm-stat"><b>${prods.length}</b><span>Productos</span></div>
                <div class="av-tm-stat"><b>${_fmtNum(totalCant)}</b><span>Cantidad total</span></div>
            </div>

            <div class="av-tm-trail">
                <i class="fas fa-warehouse"></i>
                ${almacen ? `<span><strong>${_escapeHtml(almacen.cve_almacen || almacen.cve || almacen.descripcion || '')}</strong></span>
                             <i class="fas fa-chevron-right"></i>` : ''}
                <span>Rack <strong>${rack?.num_rack ?? '—'}</strong></span>
                <i class="fas fa-chevron-right"></i>
                <span>Col <strong>${columna?.num_col ?? '—'}</strong></span>
                <i class="fas fa-chevron-right"></i>
                <span>Nivel <strong>${nivel?.num_nivel ?? '—'}</strong></span>
                ${nivel?.ulocation ? `<i class="fas fa-chevron-right"></i>
                    <span><strong>${_escapeHtml(nivel.ulocation)}</strong></span>` : ''}
            </div>`;

        if (!prods.length) {
            html += `<div class="av-tm-vacio">
                        <i class="fas fa-inbox"></i>
                        Esta tarima no tiene productos registrados
                     </div>`;
        } else {
            html += `<div class="av-tm-lista-title">
                        <i class="fas fa-boxes-stacked"></i> Contenido
                     </div>`;
            prods.forEach((p, i) => {
                const color = paleta[i % paleta.length];
                html += `
                    <button class="av-tm-prod" data-idx="${i}" style="--prod-color:${color}">
                        <div class="av-tm-prod-main">
                            <strong>${_escapeHtml(p.cve_prod || '—')}</strong>
                            <span title="${_escapeAttr(p.descr_prod || '')}">${_escapeHtml(p.descr_prod || '—')}</span>
                        </div>
                        <div class="av-tm-prod-qty">
                            ${_fmtNum(p.cantidad)}
                            <small>${_escapeHtml(p.udm || p.unidad || '')}</small>
                        </div>
                    </button>`;
            });
        }

        side.innerHTML = html;
        side.querySelectorAll('.av-tm-prod').forEach(btn => {
            btn.addEventListener('click', () => {
                const p = prods[parseInt(btn.dataset.idx)];
                if (p) _abrirModalProducto(null, null, _ctxProducto(p));
            });
        });

        // La partida buscada queda marcada también en la lista
        if (ctx.destacarProductoId != null) {
            const i = prods.findIndex(p => p.id === ctx.destacarProductoId);
            if (i >= 0) {
                const fila = side.querySelector(`.av-tm-prod[data-idx="${i}"]`);
                fila?.classList.add('activo');
                fila?.scrollIntoView({ block: 'nearest' });
            }
        }

        overlay.classList.add('open');

        // El visor necesita que el contenedor ya tenga medidas
        requestAnimationFrame(() => {
            if (!window.Tarima3D) return;
            window.Tarima3D.render('av-tarima-3d-container', tarima, {
                onProducto: (p) => _abrirModalProducto(null, null, _ctxProducto(p)),
            }, { destacarProductoId: ctx.destacarProductoId });
            _pintarModosTarima(prods.length);
        });
    }

    /**
     * Barra para elegir cómo se disponen los productos en el 3D. Se genera
     * desde `Tarima3D.MODOS` para no repetir la lista en dos sitios.
     */
    function _pintarModosTarima(numProds) {
        const cont = document.getElementById('av-tarima-3d-modos');
        if (!cont || !window.Tarima3D?.MODOS) return;

        // Con una sola partida no hay nada que reorganizar
        cont.classList.toggle('av-off', numProds < 2);
        if (numProds < 2) { cont.innerHTML = ''; return; }

        const actual = window.Tarima3D.getModo();
        cont.innerHTML = window.Tarima3D.MODOS.map(m => `
            <button class="av-tm-modo${m.id === actual ? ' active' : ''}"
                    data-modo="${m.id}" title="${_escapeAttr(m.ayuda)}">
                <i class="fas ${m.icono}"></i><span>${_escapeHtml(m.nombre)}</span>
            </button>`).join('');

        cont.querySelectorAll('.av-tm-modo').forEach(btn => {
            btn.addEventListener('click', () => {
                window.Tarima3D.setModo(btn.dataset.modo);
                _sincronizarModos();
            });
        });
    }

    /** El botón activo se deriva del visor, nunca se asume por el clic. */
    function _sincronizarModos() {
        const cont = document.getElementById('av-tarima-3d-modos');
        if (!cont || !window.Tarima3D) return;
        const actual = window.Tarima3D.getModo();
        cont.querySelectorAll('.av-tm-modo')
            .forEach(b => b.classList.toggle('active', b.dataset.modo === actual));
    }

    /** Contexto de ubicación de un producto dentro de la tarima abierta. */
    function _ctxProducto(p) {
        const t = _tarimaActual;
        return {
            producto: p,
            ubicacion: {
                colNum: t?.columna?.num_col,
                nivelNum: t?.nivel?.num_nivel,
                ulocation: t?.nivel?.ulocation,
                tarimaCodigo: t?.tarima?.codigo || ('Tarima ' + (t?.tarima?.id ?? '')),
            },
        };
    }

    function _cerrarTarima() {
        document.getElementById('av-tarima-modal-overlay')?.classList.remove('open');
        window.Tarima3D?.desmontar();
    }

    function _bindTarimaModal() {
        document.getElementById('av-tarima-modal-close')?.addEventListener('click', _cerrarTarima);
        document.getElementById('av-tarima-modal-overlay')?.addEventListener('click', (e) => {
            if (e.target.id === 'av-tarima-modal-overlay') _cerrarTarima();
        });
        document.getElementById('av-rack-3d-fit')?.addEventListener('click', () => {
            window.Rack3D?.encuadrar();
        });
    }

    // ════════════════════════════════════════════════════════════
    // SUB-MODAL: Detalle de Producto
    // ════════════════════════════════════════════════════════════
    function _buscarProductoPorId(prodId) {
        if (!_rackActual) return null;
        const cols = _rackActual.columnas || [];
        for (const col of cols) {
            for (const nivel of (col.niveles || [])) {
                for (const tarima of (nivel.tarimas || [])) {
                    for (const p of (tarima.productos || [])) {
                        if (String(p.id) === String(prodId)) {
                            return {
                                producto: p,
                                ubicacion: {
                                    colNum: col.num_col,
                                    nivelNum: nivel.num_nivel,
                                    ulocation: nivel.ulocation,
                                    tarimaCodigo: tarima.codigo || ('Tarima ' + tarima.id),
                                }
                            };
                        }
                    }
                }
            }
        }
        return null;
    }

    /**
     * @param {*} prodId  id del producto (se busca dentro del rack cargado)
     * @param {*} tarimaCodigoFallback  sin uso cuando se pasa `ctxDirecto`
     * @param {{producto:Object, ubicacion:Object}} [ctxDirecto]
     *        Contexto ya resuelto. Necesario cuando la tarima se abre desde
     *        el plano 3D: ahí el producto puede no pertenecer a `_rackActual`.
     */
    function _abrirModalProducto(prodId, tarimaCodigoFallback, ctxDirecto) {
        const found = ctxDirecto || _buscarProductoPorId(prodId);
        const overlay = document.getElementById('av-product-modal-overlay');
        const title = document.getElementById('av-product-modal-title');
        const subtitle = document.getElementById('av-product-modal-subtitle');
        const body = document.getElementById('av-product-modal-body');
        if (!overlay || !body) return;

        if (!found) {
            title.textContent = 'Producto no encontrado';
            subtitle.textContent = '';
            body.innerHTML = `<p style="color:var(--av-text-muted);font-size:12px;text-align:center;padding:20px;">
                No se pudo localizar la información de este producto.</p>`;
            overlay.classList.add('open');
            return;
        }

        const { producto: p, ubicacion } = found;

        title.textContent = p.descr_prod || p.cve_prod || 'Producto';
        subtitle.textContent = p.cve_prod || '';

        const field = (label, value) => `
            <div class="av-pm-field">
                <label>${label}</label>
                <span class="${value ? '' : 'empty'}">${value || 'Sin dato'}</span>
            </div>`;

        body.innerHTML = `
            <div class="av-pm-location-trail">
                <i class="fas fa-warehouse"></i>
                <span>Col <strong>${ubicacion.colNum ?? '—'}</strong></span>
                <i class="fas fa-chevron-right"></i>
                <span>Nivel <strong>${ubicacion.nivelNum ?? '—'}</strong></span>
                ${ubicacion.ulocation ? `<i class="fas fa-chevron-right"></i><span><strong>${_escapeHtml(ubicacion.ulocation)}</strong></span>` : ''}
                <i class="fas fa-chevron-right"></i>
                <span><i class="fas fa-pallet" style="margin-right:3px;"></i>${_escapeHtml(ubicacion.tarimaCodigo)}</span>
            </div>

            <div class="av-pm-qty-banner">
                <div>
                    <div class="av-pm-qty-value">${_fmtNum(p.cantidad)}</div>
                    <div class="av-pm-qty-unit">${p.udm || p.unidad || 'unidades'}</div>
                </div>
                <i class="fas fa-cubes" style="font-size:28px;color:var(--av-accent);opacity:0.5;"></i>
            </div>

            <div class="av-pm-field-grid">
                ${field('Clave producto', p.cve_prod)}
                ${field('Línea', p.lin_prod)}
                ${field('Tipo', p.tp)}
                ${field('Grupo', p.gpo)}
                ${field('Código proveedor', p.cod_prov)}
                ${field('Código de barras', p.cbr)}
                ${field('Proveedor principal', p.cve_prov_ppal)}
                ${field('Proveedor secundario', p.cve_prov_sec)}
                <div class="av-pm-field full">
                    <label>Ubicación de almacén (catálogo)</label>
                    <span class="${p.cve_ub_alm ? '' : 'empty'}">${p.cve_ub_alm || 'Sin dato'}</span>
                </div>
                <div class="av-pm-field full">
                    <label>Descripción completa</label>
                    <span class="${p.descr_prod ? '' : 'empty'}">${p.descr_prod || 'Sin dato'}</span>
                </div>
            </div>

        `;
        body.innerHTML += `
            <div style="margin-top:16px;padding-top:14px;border-top:1px solid var(--av-border);">
                <button class="av-pm-historial-btn" id="av-pm-btn-historial"
                        data-prod-id="${p.producto_id || p.id}"
                        data-prod-label="${_escapeAttr(p.descr_prod || p.cve_prod || 'Producto')}">
                    <i class="fas fa-history"></i>
                    Ver historial de movimientos
                </button>
            </div>`;

        // Bind inmediato al botón recién inyectado
        document.getElementById('av-pm-btn-historial')?.addEventListener('click', (e) => {
            const btn = e.currentTarget;
            HistorialProducto.abrir(
                parseInt(btn.dataset.prodId),
                btn.dataset.prodLabel
            );
        });
        overlay.classList.add('open');
    }

    function _cerrarModalProducto() {
        document.getElementById('av-product-modal-overlay')?.classList.remove('open');
    }

    function _bindProductModalEvents() {
        document.getElementById('av-product-modal-close')?.addEventListener('click', _cerrarModalProducto);
        document.getElementById('av-product-modal-overlay')?.addEventListener('click', (e) => {
            if (e.target.id === 'av-product-modal-overlay') _cerrarModalProducto();
        });
        // Escape cierra solo el modal más superficial que esté abierto
        document.addEventListener('keydown', (e) => {
            if (e.key !== 'Escape') return;
            const prod = document.getElementById('av-product-modal-overlay');
            if (prod?.classList.contains('open')) { _cerrarModalProducto(); return; }
            const tar = document.getElementById('av-tarima-modal-overlay');
            if (tar?.classList.contains('open')) _cerrarTarima();
        });
    }
    if (typeof document !== 'undefined') {
        document.addEventListener('DOMContentLoaded', () => {
            _bindProductModalEvents();
            _bindTarimaModal();
        });
    }

    // ── Helpers ───────────────────────────────────────────────────
    function _formatDate(dt) {
        if (!dt) return '—';
        try { return new Date(dt).toLocaleDateString('es-MX'); }
        catch { return dt; }
    }

    function _fmtNum(n) {
        if (n === null || n === undefined) return '—';
        return parseFloat(n).toLocaleString('es-MX', { maximumFractionDigits: 2 });
    }

    function _escapeHtml(s) {
        return (s || '').toString()
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
    }

    function _escapeAttr(s) {
        return _escapeHtml(s).replace(/"/g, '&quot;');
    }

    return { render, renderDetalle, abrirTarima, cerrarTarima: _cerrarTarima };

})();
/**
 * almacen-virtual.js
 * Motor principal del plano aéreo — Konva.js
 * Maneja: pan/zoom, drag&drop de almacenes/racks,
 *         modos (ver / editar), snap a grid, guardado.
 */

'use strict';

const AlmacenVirtual = (() => {

    // ── Estado global ────────────────────────────────────────────
    let _stage, _layerBg, _layerAlmacenes, _layerRacks, _layerUI;
    let _plano      = null;   // datos del servidor
    let _inventario = null;   // árbol completo de inventario
    let _sucursalId = null;
    let _modo       = 'ver';  // 'ver' | 'editar'
    let _vista      = '3d';   // '3d' (Three.js) | '2d' (Konva)
    let _snapGrid   = 10;     // px de snap
    let _snapActivo = true;
    let _selectedId = null;
    let _scaleMin   = 0.15;
    let _scaleMax   = 4;

    // Mapas id→shape para acceso rápido
    const _almacenShapes = new Map();
    const _rackShapes = new Map();


    // Minimapa
    let _minimapStage = null;
    let _minimapLayer = null;

    // ── Helper: lee una variable CSS calculada del :root (para Konva/canvas) ──
    function _cssVar(name, fallback) {
        const v = getComputedStyle(document.documentElement).getPropertyValue(name).trim();
        return v || fallback;
    }

    // Paleta resuelta a valores reales — se recalcula cada vez que se (re)dibuja el plano,
    // así si el usuario cambia de tema y vuelve a cargar/renderizar, toma los colores nuevos.
    function _paleta() {
        return {
            accent: _cssVar('--av-accent', '#00d4ff'),
            success: _cssVar('--av-success', '#10b981'),
            warn: _cssVar('--av-warn', '#f59e0b'),
            danger: _cssVar('--av-danger', '#ef4444'),
            text: _cssVar('--av-text', '#e2e8f0'),
            textMuted: _cssVar('--av-text-muted', '#4a6080'),
            accent2: _cssVar('--av-accent2', '#0066cc'),
            border: _cssVar('--av-border', '#1e3a5f'),
        };
    }

    // ── Inicialización ───────────────────────────────────────────
    function init() {
        const container = document.getElementById('av-konva-container');
        if (!container) return;

        // El contenedor puede arrancar oculto (la vista 3D es la de entrada).
        // Konva lanza InvalidStateError al dibujar sobre un canvas de 0 px,
        // así que el stage nunca debe quedarse sin medidas.
        _stage = new Konva.Stage({
            container: 'av-konva-container',
            width:  Math.max(container.clientWidth,  600),
            height: Math.max(container.clientHeight, 400),
            draggable: false,
        });

        _layerBg        = new Konva.Layer({ listening: false });
        _layerAlmacenes = new Konva.Layer();
        _layerRacks     = new Konva.Layer();
        _layerUI        = new Konva.Layer({ listening: false });

        _stage.add(_layerBg, _layerAlmacenes, _layerRacks, _layerUI);

        _dibujarGrid();
        _bindZoom();
        _bindEvents();
        _initMinimap();
        _bindBuscador();
        _bindLector();
        _bind3D();
        _bindMonito();

        // Resize
        window.addEventListener('resize', _onResize);
    }

    // ── Grid de fondo ────────────────────────────────────────────
    function _dibujarGrid(color = '#1a2235') {
        _layerBg.destroyChildren();
        const w = _stage.width();
        const h = _stage.height();
        const g = _snapGrid;

        // Grid menor
        for (let x = 0; x < w * 4; x += g) {
            _layerBg.add(new Konva.Line({
                points: [x, 0, x, h * 4],
                stroke: color, strokeWidth: 0.5, opacity: 0.4
            }));
        }
        for (let y = 0; y < h * 4; y += g) {
            _layerBg.add(new Konva.Line({
                points: [0, y, w * 4, y],
                stroke: color, strokeWidth: 0.5, opacity: 0.4
            }));
        }
        // Grid mayor cada 5 celdas
        for (let x = 0; x < w * 4; x += g * 5) {
            _layerBg.add(new Konva.Line({
                points: [x, 0, x, h * 4],
                stroke: '#1e3a5f', strokeWidth: 1, opacity: 0.6
            }));
        }
        for (let y = 0; y < h * 4; y += g * 5) {
            _layerBg.add(new Konva.Line({
                points: [0, y, w * 4, y],
                stroke: '#1e3a5f', strokeWidth: 1, opacity: 0.6
            }));
        }
        _layerBg.batchDraw();
    }

    // ── Zoom con rueda ───────────────────────────────────────────
    function _bindZoom() {
        _stage.on('wheel', (e) => {
            e.evt.preventDefault();
            const oldScale = _stage.scaleX();
            const pointer  = _stage.getPointerPosition();
            const mousePointTo = {
                x: (pointer.x - _stage.x()) / oldScale,
                y: (pointer.y - _stage.y()) / oldScale,
            };
            const direction = e.evt.deltaY < 0 ? 1.08 : 0.92;
            const newScale  = Math.min(_scaleMax, Math.max(_scaleMin, oldScale * direction));
            _stage.scale({ x: newScale, y: newScale });
            _stage.position({
                x: pointer.x - mousePointTo.x * newScale,
                y: pointer.y - mousePointTo.y * newScale,
            });
            _stage.batchDraw();
            _actualizarZoomLabel(newScale);
            _actualizarMinimap();
        });

        // Botones zoom
        document.getElementById('av-zoom-in')?.addEventListener('click', () => _zoomDelta(1.2));
        document.getElementById('av-zoom-out')?.addEventListener('click', () => _zoomDelta(0.8));
        document.getElementById('av-zoom-fit')?.addEventListener('click', zoomFit);
    }

    function _zoomDelta(factor) {
        const center = { x: _stage.width() / 2, y: _stage.height() / 2 };
        const oldScale = _stage.scaleX();
        const newScale  = Math.min(_scaleMax, Math.max(_scaleMin, oldScale * factor));
        const mousePointTo = {
            x: (center.x - _stage.x()) / oldScale,
            y: (center.y - _stage.y()) / oldScale,
        };
        _stage.scale({ x: newScale, y: newScale });
        _stage.position({
            x: center.x - mousePointTo.x * newScale,
            y: center.y - mousePointTo.y * newScale,
        });
        _stage.batchDraw();
        _actualizarZoomLabel(newScale);
        _actualizarMinimap();
    }

    function _actualizarZoomLabel(scale) {
        const el = document.getElementById('av-zoom-label');
        if (el) el.textContent = Math.round(scale * 100) + '%';
    }

    // ── Pan con click medio / espacio ────────────────────────────
    function _bindEvents() {
        let isPanning = false;
        let lastPos   = null;

        _stage.on('mousedown touchstart', (e) => {
            if (e.evt.button === 1 || (e.evt.buttons === 4)) {
                isPanning = true;
                lastPos   = _stage.getPointerPosition();
                document.getElementById('av-konva-container')?.classList.add('panning');
            }
            if (_modo === 'ver' && e.evt.button === 0 && e.target === _stage) {
                isPanning = true;
                lastPos   = _stage.getPointerPosition();
                document.getElementById('av-konva-container')?.classList.add('panning');
            }
        });

        _stage.on('mousemove touchmove', (e) => {
            // Coordenadas
            const pos = _stage.getRelativePointerPosition();
            const coordEl = document.getElementById('av-coords');
            if (coordEl && pos) {
                coordEl.textContent = `X: ${Math.round(pos.x)}  Y: ${Math.round(pos.y)}`;
            }

            if (!isPanning || !lastPos) return;
            const curr = _stage.getPointerPosition();
            _stage.position({
                x: _stage.x() + (curr.x - lastPos.x),
                y: _stage.y() + (curr.y - lastPos.y),
            });
            lastPos = curr;
            _stage.batchDraw();
            _actualizarMinimap();
        });

        _stage.on('mouseup touchend', () => {
            isPanning = false;
            lastPos = null;
            document.getElementById('av-konva-container')?.classList.remove('panning');
        });

        // Click vacío: deseleccionar
        _stage.on('click', (e) => {
            if (e.target === _stage) {
                _seleccionar(null);
            }
        });

        // Modo editar activa drag en shapes
        document.getElementById('av-mode-edit')?.addEventListener('click', () => setModo('editar'));
        document.getElementById('av-mode-view')?.addEventListener('click', () => setModo('ver'));

        // Config panel
        document.getElementById('av-config-btn')?.addEventListener('click', () => {
            document.getElementById('av-config-panel')?.classList.toggle('open');
        });

        // Guardar config plano
        document.getElementById('av-config-save')?.addEventListener('click', _guardarConfigPlano);

        // Guardar posiciones
        document.getElementById('av-save-btn')?.addEventListener('click', guardarPosiciones);

        // Snap toggle
        document.getElementById('av-snap-btn')?.addEventListener('click', () => {
            _snapActivo = !_snapActivo;
            document.getElementById('av-snap-btn')?.classList.toggle('active', _snapActivo);
        });

        // Sucursal selector
        document.getElementById('av-sucursal-sel')?.addEventListener('change', (e) => {
            const id = parseInt(e.target.value);
            if (id) cargarPlano(id);
        });

        // Inspector close
        // Inspector close (modal)
        document.getElementById('av-inspector-modal-close')?.addEventListener('click', () => {
            _cerrarInspector();
        });

        // Cerrar modal al hacer click fuera (en el overlay)
        document.getElementById('av-inspector-modal-overlay')?.addEventListener('click', (e) => {
            if (e.target.id === 'av-inspector-modal-overlay') {
                _cerrarInspector();
            }
        });

        document.querySelectorAll('.av-im-tab').forEach(tab => {
            tab.addEventListener('click', () => {
                const target = tab.dataset.tab;
                document.querySelectorAll('.av-im-tab').forEach(t => t.classList.toggle('active', t === tab));
                document.querySelectorAll('.av-im-panel').forEach(p => {
                    p.classList.toggle('active', p.id === `av-im-panel-${target}`);
                });
            });
        });

        // Toggle sidebar
        document.getElementById('av-sidebar-toggle')?.addEventListener('click', () => {
            document.getElementById('av-sidebar')?.classList.toggle('collapsed');
            // Forzar resize del stage después de la transición
            setTimeout(_onResize, 220);
        });

        // Expandir sidebar al hacer clic en el ícono de sucursal (modo colapsado)
        document.getElementById('av-sidebar-expand-btn')?.addEventListener('click', () => {
            document.getElementById('av-sidebar')?.classList.remove('collapsed');
            setTimeout(_onResize, 220);
        });

        // Evento delegado para racks en sidebar (en lugar de onclick inline)
        document.getElementById('av-sidebar-body')?.addEventListener('click', (e) => {
            const item = e.target.closest('.av-rack-item[data-rack-id]');
            if (item) _onSidebarRackClick(parseInt(item.dataset.rackId));

            // Toggle del árbol
            const header = e.target.closest('.av-tree-node-header');
            if (header) {
                const children = header.nextElementSibling;
                if (children?.classList.contains('av-tree-children')) {
                    children.classList.toggle('open');
                    header.classList.toggle('open');
                }
            }
        });
    }

    // ── Snap a grid ──────────────────────────────────────────────
    function _snap(v) {
        if (!_snapActivo) return v;
        return Math.round(v / _snapGrid) * _snapGrid;
    }

    // ── Resize ───────────────────────────────────────────────────
    function _onResize() {
        const container = document.getElementById('av-konva-container');
        if (!container || !_stage) return;

        const w = container.clientWidth;
        const h = container.clientHeight;
        // Oculto (vista 3D activa): se conservan las medidas anteriores; poner
        // 0 haría que Konva reventara al siguiente batchDraw().
        if (w < 2 || h < 2) return;

        _stage.width(w);
        _stage.height(h);
        _stage.batchDraw();
        _actualizarMinimap();
    }

    // ── Cargar plano desde servidor ──────────────────────────────
    async function cargarPlano(sucursalId) {
        _sucursalId = sucursalId;
        _mostrarLoading(true);
        try {
            // 1. Plano con posiciones
            const rPlano = await fetch(`/Consulta/GetPlanoSucursal?sucursalId=${sucursalId}`);
            const dPlano = await rPlano.json();
            if (!dPlano.ok) throw new Error(dPlano.msg);

            // RunQuery a veces devuelve array de objetos, a veces string
            let rawPlano = dPlano.data;
            if (Array.isArray(rawPlano) && rawPlano.length > 0) {
                const val = Object.values(rawPlano[0])[0];
                rawPlano = typeof val === 'string' ? JSON.parse(val) : val;
            } else if (typeof rawPlano === 'string') {
                rawPlano = JSON.parse(rawPlano);
            }
            _plano = rawPlano;
            console.log('[AV] plano cargado:', _plano);
            // 2. Inventario completo
            const fd = new FormData();
            fd.append('sucursalId', sucursalId);
            const rInv = await fetch('/Consulta/GetAllData', { method: 'POST', body: fd });


            const dInv = await rInv.json();

            if (dInv?.length && dInv[0]?.jsonb_pretty) {
                _inventario = JSON.parse(dInv[0].jsonb_pretty);
            } else {
                _inventario = {};
            }

            _renderizarPlano();
            _actualizarSidebar();
            _sincronizar3D(true);
            _onResize();
            requestAnimationFrame(() => requestAnimationFrame(zoomFit));
        } catch (err) {
            _toast('Error cargando plano: ' + err.message, 'error');
        } finally {
            _mostrarLoading(false);
        }
        console.log('[AV] _plano keys:', _plano ? Object.keys(_plano) : 'null');
        console.log('[AV] _inventario keys:', _inventario ? Object.keys(_inventario) : 'null');
        console.log('[AV] almacenes count:', _inventario?.sucursales?.[0]?.almacenes?.length ?? 0);
    }

    // ── Renderizar plano completo ────────────────────────────────
    function _renderizarPlano() {
        if (!_plano || !_inventario) return;

        _layerAlmacenes.destroyChildren();
        _layerRacks.destroyChildren();
        _almacenShapes.clear();
        _rackShapes.clear();

        const posAlmacenes = _indexarPor(_plano.almacenes || [], 'almacen_id');
        const posRacks     = _indexarPor(_plano.racks     || [], 'rack_id');

        const sucursales = _inventario.sucursales || [];
        const sucursal   = sucursales.find(s => s.id === _sucursalId)
                        || sucursales[0];
        if (!sucursal) return;

        (sucursal.almacenes || []).forEach((alm, ai) => {
            const posA = posAlmacenes[alm.id] || _defaultPosAlmacen(ai);
            _crearAlmacenShape(alm, posA);

            (alm.pasillos || []).forEach(pasillo => {
                (pasillo.racks || []).forEach((rack, ri) => {
                    const posR = posRacks[rack.id] || _defaultPosRack(alm, posA, ri);
                    _crearRackShape(rack, alm.id, posR);
                });
            });
            (alm.racks_sin_pasillo || []).forEach((rack, ri) => {
                const posR = posRacks[rack.id]
                          || _defaultPosRack(alm, posA, (alm.pasillos?.length || 0) + ri);
                _crearRackShape(rack, alm.id, posR);
            });
        });

        _layerAlmacenes.batchDraw();
        _layerRacks.batchDraw();
        _construirIndiceBusqueda();
    }
    function _defaultPosAlmacen(index) {
        const cols = 2;
        const col = index % cols;
        const row = Math.floor(index / cols);
        return {
            x: 60 + col * 480,
            y: 60 + row * 360,
            ancho: 420,
            alto: 300,
            rotacion: 0,
            color_fondo: '#071628',
            color_borde: '#1e6cb5',
        };
    }

    function _defaultPosRack(alm, posA, index) {
        const cols = 6;
        const col = index % cols;
        const row = Math.floor(index / cols);
        const padX = 24;
        const padY = 50;
        const gapX = 8;
        const gapY = 10;
        const rackW = Math.floor((posA.ancho - padX * 2 - gapX * (cols - 1)) / cols);
        const rackH = 52;
        return {
            x: posA.x + padX + col * (rackW + gapX),
            y: posA.y + padY + row * (rackH + gapY),
            ancho: rackW,
            alto: rackH,
            rotacion: 0,
            color_fondo: '#071e38',
            color_borde: '#0080cc',
        };
    }

    // ── Shape de Almacén ─────────────────────────────────────────
    function _crearAlmacenShape(alm, pos) {
        const group = new Konva.Group({
            x: pos.x, y: pos.y,
            rotation: pos.rotacion || 0,
            draggable: false,
            id: 'alm_' + alm.id,
        });

        const W = Math.max(pos.ancho, 80);
        const H = Math.max(pos.alto, 50);
        const bc = pos.color_borde || '#1e6cb5';
        const R = 8;  // cornerRadius fijo y seguro

        // Glow exterior
        group.add(new Konva.Rect({
            x: -4, y: -4, width: W + 8, height: H + 8,
            fill: 'transparent',
            shadowColor: bc, shadowBlur: 28, shadowOpacity: 0.3,
            cornerRadius: R + 2,
        }));

        // Fondo
        group.add(new Konva.Rect({
            width: W, height: H,
            fill: pos.color_fondo || '#071628',
            stroke: bc, strokeWidth: 1.5,
            cornerRadius: R,
        }));

        // Gradiente superior simulado
        const gradH = Math.max(10, Math.floor(H / 2.5));
        group.add(new Konva.Rect({
            x: 1, y: 1, width: W - 2, height: gradH,
            fillLinearGradientStartPoint: { x: 0, y: 0 },
            fillLinearGradientEndPoint: { x: 0, y: gradH },
            fillLinearGradientColorStops: [0, 'rgba(255,255,255,0.05)', 1, 'rgba(255,255,255,0)'],
            cornerRadius: R,
        }));

        // Barra título
        const titleH = Math.min(36, Math.floor(H * 0.25));
        group.add(new Konva.Rect({
            x: 0, y: 0, width: W, height: titleH,
            fill: bc, opacity: 0.13,
            cornerRadius: R,
        }));

        // Línea acento top
        group.add(new Konva.Rect({
            x: 0, y: 0, width: W, height: 2,
            fill: bc, opacity: 0.9,
            cornerRadius: R,
        }));

        // Ícono
        group.add(new Konva.Text({
            x: 10, y: Math.floor(titleH / 2) - 7,
            text: '\uf49e',
            fontSize: 13,
            fontFamily: '"Font Awesome 6 Free"',
            fontStyle: '900',
            fill: bc, opacity: 0.9,
        }));

        // Clave almacén
        const lblY = Math.max(4, Math.floor(titleH / 2) - 8);
        group.add(new Konva.Text({
            x: 28, y: lblY,
            text: (alm.cve_almacen || alm.cve || '').toUpperCase(),
            fontSize: 11, fontStyle: 'bold',
            fontFamily: 'Segoe UI, sans-serif',
            fill: bc,
            width: Math.max(10, W - 90), ellipsis: true,
            letterSpacing: 1,
        }));

        // Descripción
        if (titleH >= 28) {
            group.add(new Konva.Text({
                x: 28, y: lblY + 14,
                text: alm.descripcion || '',
                fontSize: 8,
                fontFamily: 'Segoe UI, sans-serif',
                fill: 'rgba(226,232,240,0.4)',
                width: Math.max(10, W - 90), ellipsis: true,
            }));
        }

        // Badge racks
        const totalRacks = _contarRacks(alm);
        const badgeW = 52;
        if (W > badgeW + 20) {
            group.add(new Konva.Rect({
                x: W - badgeW - 8, y: Math.floor(titleH / 2) - 10,
                width: badgeW, height: 20,
                fill: bc, opacity: 0.15,
                cornerRadius: 10,
                stroke: bc, strokeWidth: 0.5,
            }));
            group.add(new Konva.Text({
                x: W - badgeW - 8, y: Math.floor(titleH / 2) - 6,
                text: `${totalRacks} racks`,
                fontSize: 9, fontFamily: 'Segoe UI, sans-serif',
                fill: bc, width: badgeW, align: 'center',
            }));
        }

        // Líneas decorativas de pasillo
        if (H > 60) {
            for (let i = 1; i < 4; i++) {
                group.add(new Konva.Line({
                    points: [(W / 4) * i, titleH + 4, (W / 4) * i, H - 8],
                    stroke: bc, strokeWidth: 0.4, opacity: 0.1,
                    dash: [5, 5],
                }));
            }
        }

        // Línea bottom accent
        group.add(new Konva.Line({
            points: [12, H - 1, W - 12, H - 1],
            stroke: bc, strokeWidth: 1, opacity: 0.25,
        }));

        _addResizeHandles(group, { ancho: W, alto: H }, 'almacen', alm.id);

        group.on('click tap', (e) => {
            e.cancelBubble = true;
            _seleccionar('alm_' + alm.id);
        });

        _layerAlmacenes.add(group);
        _almacenShapes.set(alm.id, { group, pos: { ...pos, ancho: W, alto: H }, alm });
    }
    function _contarRacks(alm) {
        const desPasillos = (alm.pasillos || []).reduce((s, p) => s + (p.racks || []).length, 0);
        return desPasillos + (alm.racks_sin_pasillo || []).length;
    }

    // ── Calcular ocupación desde string o número ─────────────────
    // ── Parsear ocupación desde string o número ──────────────────────
    function _parsearOcupacion(rack, cols) {
        const capRaw = (rack.capacidad ?? '').toString().trim();
        const capStr = capRaw.toLowerCase()
            .normalize('NFD')
            .replace(/[\u0300-\u036f]/g, ''); // quitar acentos: lleno, vacio, etc.

        // ── Caso 1: string descriptivo ───────────────────────────────
        const MAP_LLENO = ['lleno', 'full', 'alto', 'completo', 'ocupado', 'high'];
        const MAP_MEDIO = ['medio', 'medium', 'med', 'parcial', 'partial', 'mitad', 'half'];
        const MAP_BAJO = ['bajo', 'low', 'poco', 'escaso', 'minimo'];
        const MAP_VACIO = ['vacio', 'empty', 'libre', 'disponible', 'free', 'sin uso', 'nulo'];

        const _p = _paleta();
        if (MAP_LLENO.includes(capStr)) {
            return { pct: 1.0, color: _p.danger, label: 'Lleno', clase: 'full' };
        }
        if (MAP_MEDIO.includes(capStr)) {
            return { pct: 0.55, color: _p.warn, label: 'Medio', clase: 'partial' };
        }
        if (MAP_BAJO.includes(capStr)) {
            return { pct: 0.2, color: _p.success, label: 'Bajo', clase: 'low' };
        }
        if (MAP_VACIO.includes(capStr)) {
            return { pct: 0, color: _p.accent2, label: 'Vacío', clase: 'empty' };
        }

        // ── Caso 2: porcentaje explícito "75%" ───────────────────────
        const pctMatch = capStr.match(/^(\d+(?:\.\d+)?)\s*%$/);
        if (pctMatch) {
            const pct = Math.min(1, parseFloat(pctMatch[1]) / 100);
            return _pctToOcup(pct);
        }

        // ── Caso 3: fracción "3/5" ───────────────────────────────────
        const fracMatch = capStr.match(/^(\d+)\s*\/\s*(\d+)$/);
        if (fracMatch) {
            const num = parseFloat(fracMatch[1]);
            const den = parseFloat(fracMatch[2]);
            const pct = den > 0 ? Math.min(1, num / den) : 0;
            return _pctToOcup(pct);
        }

        // ── Caso 4: número puro — es la capacidad máxima del rack ────
        // Calcula usedSlots desde tarimas reales en columnas
        const numCap = parseFloat(capRaw.replace(',', '.'));
        if (!isNaN(numCap) && numCap > 0) {
            let usedSlots = 0;
            cols.forEach(c => (c.niveles || []).forEach(n => {
                usedSlots += (n.tarimas || []).length;
            }));
            return _pctToOcup(Math.min(1, usedSlots / numCap));
        }

        // ── Caso 5: sin capacidad en rack — sumar desde niveles ──────
        let totalSlots = 0, usedSlots = 0;
        cols.forEach(c => (c.niveles || []).forEach(n => {
            const nCap = parseFloat((n.capacidad ?? '4').toString().replace(',', '.'));
            totalSlots += isNaN(nCap) ? 4 : Math.max(1, nCap);
            usedSlots += (n.tarimas || []).length;
        }));

        if (totalSlots === 0) {
            return { pct: 0, color: '#1e6cb5', label: '—', clase: 'empty' };
        }

        return _pctToOcup(Math.min(1, usedSlots / totalSlots));
    }

    // Helper: convierte pct (0-1) a objeto de ocupación completo
    function _pctToOcup(pct) {
        const p = _paleta();
        if (pct >= 0.9) return { pct, color: p.danger, label: `${Math.round(pct * 100)}%`, clase: 'full' };
        if (pct >= 0.5) return { pct, color: p.warn, label: `${Math.round(pct * 100)}%`, clase: 'partial' };
        if (pct > 0) return { pct, color: p.success, label: `${Math.round(pct * 100)}%`, clase: 'low' };
        return { pct: 0, color: p.accent2, label: '0%', clase: 'empty' };
    }

    // ── Shape de Rack ────────────────────────────────────────────
    function _crearRackShape(rack, almacenId, pos) {
        const group = new Konva.Group({
            x: pos.x, y: pos.y,
            rotation: pos.rotacion || 0,
            draggable: false,
            id: 'rack_' + rack.id,
        });

        const W = Math.max(pos.ancho, 20);
        const H = Math.max(pos.alto, 24);
        const bc = pos.color_borde || '#0080cc';
        const R = Math.min(4, Math.floor(W / 6), Math.floor(H / 6));  // radius seguro

        const cols = rack.columnas || [];
        const numCols = Math.max(cols.length, 1);
        const hasAnyProd = cols.some(c =>
            (c.niveles || []).some(n => (n.tarimas || []).length > 0));

        const ocup = _parsearOcupacion(rack, cols);
        const ocupPct = ocup.pct;
        const ocupColor = ocup.color;
        const activeColor = ocupPct > 0 ? ocupColor : (bc || '#1e6cb5');

        // Glow si tiene productos
        if (hasAnyProd) {
            group.add(new Konva.Rect({
                x: -2, y: -2, width: W + 4, height: H + 4,
                fill: 'transparent',
                shadowColor: ocupColor,
                shadowBlur: 12, shadowOpacity: 0.45,
                cornerRadius: R + 1,
            }));
        }

        // Fondo base
        const bg = new Konva.Rect({
            width: W, height: H,
            fill: pos.color_fondo || '#071e38',
            stroke: activeColor,
            strokeWidth: hasAnyProd ? 1.2 : 0.7,
            cornerRadius: R,
        });
        group.add(bg);

        // Cabecera coloreada
        const headerH = Math.max(2, Math.min(16, Math.floor(H * 0.3)));
        group.add(new Konva.Rect({
            x: 0, y: 0, width: W, height: headerH,
            fill: activeColor, opacity: 0.22,
            cornerRadius: R,
        }));

        // Línea de acento top
        group.add(new Konva.Rect({
            x: 0, y: 0, width: W, height: Math.max(1, Math.floor(headerH * 0.12)),
            fill: activeColor, opacity: 0.8,
            cornerRadius: R,
        }));

        // Columnas (barras de ocupación)
        const bodyY = headerH + 2;
        const bodyH = Math.max(2, H - bodyY - 2);
        const colW = Math.max(2, (W - 4) / numCols);

        cols.forEach((col, i) => {
            const colUsed = (col.niveles || []).reduce((s, n) => s + (n.tarimas || []).length, 0);
            const colTotal = (col.niveles || []).reduce((s, n) => s + (n.capacidad || 4), 0);
            const colPct = colTotal > 0 ? colUsed / colTotal : 0;
            const colFill = colPct >= 0.9 ? 'rgba(239,68,68,0.28)'
                : colPct >= 0.5 ? 'rgba(245,158,11,0.22)'
                    : colPct > 0 ? 'rgba(16,185,129,0.18)'
                        : 'rgba(10,42,74,0.5)';

            const cx = 2 + i * colW;
            const cw = Math.max(1, colW - 1);
            const cr = Math.min(2, Math.floor(cw / 3));

            // Fondo columna
            group.add(new Konva.Rect({
                x: cx, y: bodyY,
                width: cw, height: bodyH,
                fill: colFill, cornerRadius: cr,
            }));

            // Barra de nivel (crece desde abajo)
            if (colPct > 0 && bodyH > 4) {
                const barH = Math.max(2, Math.floor(bodyH * colPct));
                const barColor = colPct >= 0.9 ? '#ef4444'
                    : colPct >= 0.5 ? '#f59e0b'
                        : '#10b981';
                group.add(new Konva.Rect({
                    x: cx + 1, y: bodyY + bodyH - barH,
                    width: Math.max(1, cw - 2), height: barH,
                    fill: barColor, opacity: 0.6,
                    cornerRadius: Math.min(cr, Math.floor(barH / 2)),
                }));
            }
        });

        // Etiqueta num_rack (solo si hay espacio)
        // Etiqueta num_rack
        if (headerH >= 10) {
            const lblRack = rack.num_rack ? `R${rack.num_rack}` : `#${rack.id}`;
            group.add(new Konva.Text({
                x: 0, y: Math.floor(headerH / 2) - 4,
                text: lblRack,
                fontSize: Math.min(9, Math.max(6, headerH - 4)),
                fontStyle: 'bold',
                fontFamily: 'Segoe UI, sans-serif',
                fill: activeColor,
                width: ocupPct > 0 ? W - 12 : W,
                align: 'center',
            }));
        }

        // Label de ocupación debajo del cuerpo (si hay suficiente espacio)
        if (H > 40 && ocup.label !== '—') {
            const labelColor = ocupPct >= 0.9 ? '#ef4444'
                : ocupPct >= 0.5 ? '#f59e0b'
                    : '#10b981';
            group.add(new Konva.Text({
                x: 0, y: H + 4,
                text: ocup.label,
                fontSize: 7,
                fontFamily: 'Segoe UI, sans-serif',
                fill: labelColor,
                opacity: 0.8,
                width: W, align: 'center',
            }));
        }
        // Punto indicador esquina sup derecha
        if (ocupPct > 0 && W >= 12 && H >= 12) {
            const dotR = Math.min(3.5, Math.floor(Math.min(W, H) / 8));
            group.add(new Konva.Circle({
                x: W - dotR - 2, y: dotR + 2,
                radius: dotR,
                fill: ocupColor, opacity: 0.9,
            }));
        }

        // Eventos
        group.on('mouseenter', () => {
            bg.stroke('#00d4ff');
            bg.strokeWidth(1.8);
            _layerRacks.batchDraw();
        });
        group.on('mouseleave', () => {
            if (_selectedId !== 'rack_' + rack.id) {
                bg.stroke(activeColor);
                bg.strokeWidth(hasAnyProd ? 1.2 : 0.7);
                _layerRacks.batchDraw();
            }
        });
        group.on('click tap', (e) => {
            e.cancelBubble = true;
            _seleccionar('rack_' + rack.id);
            _abrirInspectorRack(rack.id);
        });
        group.on('dragend', () => {
            group.position({ x: _snap(group.x()), y: _snap(group.y()) });
            _layerRacks.batchDraw();
            _actualizarMinimap();
        });

        _layerRacks.add(group);
        _rackShapes.set(rack.id, { group, pos: { ...pos, ancho: W, alto: H }, rack, almacenId });
    }

    // ── Handles de resize ────────────────────────────────────────
    function _addResizeHandles(group, pos, tipo, id) {
        const handleDefs = [
            { x: pos.ancho, y: pos.alto, cursor: 'se-resize', corner: 'se' },
        ];
        handleDefs.forEach(hd => {
            const h = new Konva.Circle({
                x: hd.x, y: hd.y,
                radius: 5,
                fill: '#00d4ff',
                stroke: '#000',
                strokeWidth: 1,
                draggable: true,
                visible: false,
                name: 'resize-handle',
            });
            h.on('mouseenter',  () => { document.body.style.cursor = hd.cursor; });
            h.on('mouseleave',  () => { document.body.style.cursor = 'default'; });
            h.on('dragmove', () => {
                const newW = Math.max(60, _snap(h.x()));
                const newH = Math.max(40, _snap(h.y()));
                const rect = group.findOne('Rect');
                if (rect) { rect.width(newW); rect.height(newH); }
                h.x(newW); h.y(newH);
                group.getLayer()?.batchDraw();
            });
            group.add(h);
        });
    }

    // ── Selección ────────────────────────────────────────────────
    function _seleccionar(id) {
        // Reset anterior
        if (_selectedId) {
            const prev = _stage.findOne('#' + _selectedId);
            if (prev) {
                prev.find('.resize-handle').forEach(h => h.visible(false));
                const rect = prev.findOne('Rect');
                if (rect) rect.strokeWidth(1);
            }
        }
        _selectedId = id;
        if (!id) {
            _layerAlmacenes.batchDraw();
            _layerRacks.batchDraw();
            return;
        }
        const shape = _stage.findOne('#' + id);
        if (shape) {
            shape.find('.resize-handle').forEach(h => h.visible(_modo === 'editar'));
            const rect = shape.findOne('Rect');
            if (rect) rect.strokeWidth(2);
        }
        _layerAlmacenes.batchDraw();
        _layerRacks.batchDraw();
    }

    // ── Modo editar/ver ──────────────────────────────────────────
    function setModo(modo) {
        _modo = modo;
        const isEdit = modo === 'editar';

        document.getElementById('av-mode-edit')?.classList.toggle('active', isEdit);
        document.getElementById('av-mode-view')?.classList.toggle('active', !isEdit);
        document.getElementById('av-save-btn')?.classList.toggle('d-none', !isEdit);
        document.getElementById('av-konva-container')?.classList.toggle('editing', isEdit);

        // Activar/desactivar draggable en shapes
        _almacenShapes.forEach(({ group }) => group.draggable(isEdit));
        _rackShapes.forEach(({ group }) => group.draggable(isEdit));

        // Ocultar handles si volvemos a ver
        if (!isEdit) {
            _seleccionar(null);
        }

        _layerAlmacenes.batchDraw();
        _layerRacks.batchDraw();
    }

    // ── Zoom fit ─────────────────────────────────────────────────
    function zoomFit() {
        // En 3D el encuadre lo resuelve el motor de Three.js
        if (_vista === '3d') { window.AV3D?.zoomFit(); return; }

        if (!_plano) return;
        const pad = 60;
        const sw  = _stage.width();
        const sh  = _stage.height();
        // El stage puede medir 0 si el contenedor está oculto (vista 3D activa):
        // en ese caso no hay nada que encuadrar todavía.
        if (sw < 2 || sh < 2) return;
        // Determinar bounding box de todo el contenido
        let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
        _almacenShapes.forEach(({ group, pos }) => {
            minX = Math.min(minX, group.x());
            minY = Math.min(minY, group.y());
            maxX = Math.max(maxX, group.x() + pos.ancho);
            maxY = Math.max(maxY, group.y() + pos.alto);
        });
        if (minX === Infinity) { minX = 0; minY = 0; maxX = 800; maxY = 600; }
        const contentW = maxX - minX + pad * 2;
        const contentH = maxY - minY + pad * 2;
        const scale    = Math.min(sw / contentW, sh / contentH, _scaleMax);
        _stage.scale({ x: scale, y: scale });
        _stage.position({
            x: (sw - (maxX + minX) * scale) / 2,
            y: (sh - (maxY + minY) * scale) / 2,
        });
        _stage.batchDraw();
        _actualizarZoomLabel(scale);
        _actualizarMinimap();
    }

    // ── Minimap ──────────────────────────────────────────────────
    // ── Minimap ──────────────────────────────────────────────────
    function _initMinimap() {
        const mmContainer = document.getElementById('av-minimap');
        if (!mmContainer) return;

        // Crear un div interno para el stage del minimapa
        // (sin tocar el span de label ni el div de viewport, que ya existen en el HTML)
        let mmStageDiv = document.getElementById('av-minimap-stage');
        if (!mmStageDiv) {
            mmStageDiv = document.createElement('div');
            mmStageDiv.id = 'av-minimap-stage';
            mmStageDiv.style.position = 'absolute';
            mmStageDiv.style.inset = '0';
            mmStageDiv.style.zIndex = '0';
            // Insertarlo antes del label/viewport para que quede debajo visualmente
            mmContainer.insertBefore(mmStageDiv, mmContainer.firstChild);
        }

        // Igual que el stage principal: nunca 0 px (el minimapa está oculto
        // mientras la vista 3D esté activa). _actualizarMinimap lo remide.
        _minimapStage = new Konva.Stage({
            container: 'av-minimap-stage',
            width: Math.max(mmContainer.offsetWidth, 160),
            height: Math.max(mmContainer.offsetHeight, 120),
            listening: false,
        });
        _minimapLayer = new Konva.Layer({ listening: false });
        _minimapStage.add(_minimapLayer);
    }

    function _actualizarMinimap() {
        const mm = document.getElementById('av-minimap');
        if (!mm || !_stage) return;
        const mmW = mm.offsetWidth;
        const mmH = mm.offsetHeight;
        // Oculto (vista 3D activa) → nada que dibujar
        if (mmW < 2 || mmH < 2) return;

        // Si el mini-stage no existe todavía (primera carga), créalo ahora
        if (!_minimapStage) _initMinimap();
        if (!_minimapStage) return;

        // Pudo crearse mientras el contenedor estaba oculto: reajustar
        if (_minimapStage.width() !== mmW || _minimapStage.height() !== mmH) {
            _minimapStage.width(mmW);
            _minimapStage.height(mmH);
        }

        // Bounding box de todo el contenido real (igual que en zoomFit)
        let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
        _almacenShapes.forEach(({ group, pos }) => {
            minX = Math.min(minX, group.x());
            minY = Math.min(minY, group.y());
            maxX = Math.max(maxX, group.x() + pos.ancho);
            maxY = Math.max(maxY, group.y() + pos.alto);
        });
        if (minX === Infinity) { minX = 0; minY = 0; maxX = 800; maxY = 600; }

        const pad = 20;
        const contentW = (maxX - minX) + pad * 2;
        const contentH = (maxY - minY) + pad * 2;

        // Escala para que el contenido completo entre en el minimapa
        const mmScale = Math.min(mmW / contentW, mmH / contentH);
        const offsetX = (mmW - contentW * mmScale) / 2 - minX * mmScale + pad * mmScale;
        const offsetY = (mmH - contentH * mmScale) / 2 - minY * mmScale + pad * mmScale;

        // Redibujar miniaturas de almacenes y racks
        _minimapLayer.destroyChildren();

        _almacenShapes.forEach(({ group, pos }) => {
            _minimapLayer.add(new Konva.Rect({
                x: group.x() * mmScale + offsetX,
                y: group.y() * mmScale + offsetY,
                width: Math.max(1, pos.ancho * mmScale),
                height: Math.max(1, pos.alto * mmScale),
                fill: pos.color_fondo || '#071628',
                stroke: pos.color_borde || '#1e6cb5',
                strokeWidth: 0.5,
            }));
        });

        _rackShapes.forEach(({ group, pos }) => {
            _minimapLayer.add(new Konva.Rect({
                x: group.x() * mmScale + offsetX,
                y: group.y() * mmScale + offsetY,
                width: Math.max(1, pos.ancho * mmScale),
                height: Math.max(1, pos.alto * mmScale),
                fill: pos.color_borde || '#0080cc',
                opacity: 0.7,
            }));
        });

        _minimapLayer.batchDraw();

        // Viewport indicator (recuadro que ya existía) — usa la misma transformación
        const vp = document.getElementById('av-minimap-viewport');
        if (vp) {
            const scale = _stage.scaleX();
            const stagePos = _stage.position();

            // Coordenadas del viewport visible, en el sistema de coordenadas del plano
            const viewX = -stagePos.x / scale;
            const viewY = -stagePos.y / scale;
            const viewW = _stage.width() / scale;
            const viewH = _stage.height() / scale;

            vp.style.left = (viewX * mmScale + offsetX) + 'px';
            vp.style.top = (viewY * mmScale + offsetY) + 'px';
            vp.style.width = (viewW * mmScale) + 'px';
            vp.style.height = (viewH * mmScale) + 'px';
        }
    }

    // ── Inspector del rack ───────────────────────────────────────
    async function _abrirInspectorRack(rackId, opts = {}) {
        const delayMs = opts.delayMs || 0;

        const overlay = document.getElementById('av-inspector-modal-overlay');
        const modalLoading = document.getElementById('av-inspector-modal-loading');
        const nivelDetail = document.getElementById('av-nivel-detail');

        overlay?.classList.add('open');

        // Resetear siempre a la pestaña "Niveles" al abrir
        document.querySelectorAll('.av-im-tab').forEach(t => t.classList.toggle('active', t.dataset.tab === 'niveles'));
        document.querySelectorAll('.av-im-panel').forEach(p => p.classList.toggle('active', p.id === 'av-im-panel-niveles'));

        // Si viene de búsqueda, mostramos un loading breve para dejar
        // que la animación de spotlight/radar en el plano se aprecie
        // ANTES de tapar la vista con el modal.
        if (delayMs > 0) {
            if (nivelDetail) nivelDetail.style.display = 'none';
            modalLoading?.classList.add('show');
            await new Promise(res => setTimeout(res, delayMs));
        }

        // El contenedor del visor aloja el canvas WebGL: NUNCA se le toca el
        // innerHTML. Los estados de carga/error van en su capa hermana.
        _estadoVisorRack('cargando');
        if (nivelDetail) nivelDetail.style.display = '';
        modalLoading?.classList.remove('show');

        try {
            const r = await fetch(`/Consulta/GetRackDetalle?rackId=${rackId}`);
            const d = await r.json();

            if (!d.ok) throw new Error(d.msg);

            let rack = d.data;

            if (typeof rack === 'string') {
                rack = JSON.parse(rack);
            } else if (Array.isArray(rack) && rack.length > 0) {
                const valor = Object.values(rack[0])[0];
                rack = typeof valor === 'string' ? JSON.parse(valor) : valor;
            }

            // Título
            const h5 = document.getElementById('av-inspector-modal-title');
            if (h5) h5.textContent = `Rack ${rack.num_rack || ''} — ${rack.nombre || ''}`;

            // Rack 3D navegable (o SVG isométrico si no hay visor)
            RackInspector.render(rack, 'av-rack-3d-container', {
                destacarTarimaId: opts.destacarTarimaId,
            });
            _estadoVisorRack('listo');

            // Detalle niveles + tarimas
            RackInspector.renderDetalle(rack, 'av-nivel-detail');

        } catch (err) {
            _estadoVisorRack('error', err.message);
        }
    }

    /** Capa de estado sobre el visor 3D del rack (no toca el canvas). */
    function _estadoVisorRack(estado, msg) {
        const ph = document.getElementById('av-rack-3d-placeholder');
        if (!ph) return;

        if (estado === 'listo') { ph.classList.add('hide'); return; }

        ph.classList.remove('hide');
        ph.innerHTML = estado === 'cargando'
            ? `<div class="av-spinner"><div class="av-spinner-ring"></div><span>Cargando rack…</span></div>`
            : `<i class="fas fa-triangle-exclamation" style="color:var(--av-danger)"></i>
               <span style="color:var(--av-danger);max-width:260px;text-align:center;">${_escapeHtml(msg || 'Error')}</span>`;
    }

    function _cerrarInspector() {
        document.getElementById('av-inspector-modal-overlay')?.classList.remove('open');
        window.Rack3D?.desmontar();
        RackInspector.cerrarTarima?.();
        _seleccionar(null);
        if (_vista === '3d') window.AV3D?.seleccionarRack(null);
    }

    // ── Guardar posiciones ───────────────────────────────────────
    async function guardarPosiciones() {
        const planoId = _plano?.id_plano || _plano?.idPlano;

        if (!planoId) {
            _toast('No hay plano activo. Selecciona una sucursal primero.', 'error');
            return;
        }

        const almacenes = [];
        _almacenShapes.forEach(({ group, pos, alm }) => {
            almacenes.push({
                almacenId: alm.id,
                x: Math.round(group.x()),
                y: Math.round(group.y()),
                ancho: Math.round(pos.ancho),
                alto: Math.round(pos.alto),
                rotacion: group.rotation(),
                colorFondo: pos.color_fondo,
                colorBorde: pos.color_borde,
            });
        });

        const racks = [];
        _rackShapes.forEach(({ group, pos, rack, almacenId }) => {
            racks.push({
                rackId: rack.id,
                almacenId: almacenId,
                x: Math.round(group.x()),
                y: Math.round(group.y()),
                ancho: Math.round(pos.ancho),
                alto: Math.round(pos.alto),
                rotacion: group.rotation(),
                colorFondo: pos.color_fondo,
                colorBorde: pos.color_borde,
            });
        });

        _mostrarLoading(true);
        try {
            const fd = new FormData();
            fd.append('planoId', planoId);
            fd.append('almacenes', JSON.stringify(almacenes));
            fd.append('racks', JSON.stringify(racks));

            const r = await fetch('/Consulta/GuardarPosicionesPlano', { method: 'POST', body: fd });
            const d = await r.json();
            if (!d.ok) throw new Error(d.msg);
            _toast('Plano guardado correctamente', 'success');
        } catch (err) {
            _toast('Error al guardar: ' + err.message, 'error');
        } finally {
            _mostrarLoading(false);
        }
    }

    // ── Guardar config del plano ─────────────────────────────────
    async function _guardarConfigPlano() {
        const planoId = _plano?.id_plano || _plano?.idPlano;
        if (!planoId) {
            _toast('Carga un plano primero', 'error');
            return;
        }

        const anchoM = parseFloat(document.getElementById('av-cfg-ancho')?.value || 100);
        const altoM = parseFloat(document.getElementById('av-cfg-alto')?.value || 60);
        const escala = parseFloat(document.getElementById('av-cfg-escala')?.value || 8);
        const fondoColor = document.getElementById('av-cfg-fondo')?.value || '#111827';

        const fd = new FormData();
        fd.append('planoId', planoId);
        fd.append('anchoM', anchoM);
        fd.append('altoM', altoM);
        fd.append('escalaPxM', escala);
        fd.append('fondoColor', fondoColor);

        try {
            const r = await fetch('/Consulta/ActualizarConfigPlano', { method: 'POST', body: fd });
            const d = await r.json();
            if (!d.ok) throw new Error(d.msg);

            // Aplicar color de fondo al canvas inmediatamente
            const container = document.getElementById('av-konva-container');
            if (container) container.style.background = fondoColor;

            // Actualizar _plano local
            if (_plano) {
                _plano.fondo_color = fondoColor;
                _plano.ancho_m = anchoM;
                _plano.alto_m = altoM;
                _plano.escala_px_m = escala;
            }

            _toast('Configuración aplicada', 'success');
            document.getElementById('av-config-panel')?.classList.remove('open');

            // Rerenderizar con nueva escala
            _renderizarPlano();
            zoomFit();

        } catch (err) {
            _toast('Error: ' + err.message, 'error');
        }
    }

    // ── Sidebar ──────────────────────────────────────────────────
    function _actualizarSidebar() {
        const container = document.getElementById('av-sidebar-body');
        if (!container || !_inventario) return;

        const sucursales = _inventario.sucursales || [];
        const sucursal   = sucursales.find(s => s.id === _sucursalId) || sucursales[0];
        if (!sucursal) return;

        let html = '';
        (sucursal.almacenes || []).forEach(alm => {
            const allRacks = [
                ...((alm.pasillos || []).flatMap(p => p.racks || [])),
                ...(alm.racks_sin_pasillo || [])
            ];
            const posicionados = allRacks.filter(r => _rackShapes.has(r.id)).length;

            html += `
<div class="av-tree-section">
  <div class="av-tree-node-header">
    <span class="av-tree-icon"><i class="fas fa-warehouse"></i></span>
    <span class="av-tree-label">${alm.descripcion}</span>
    <span class="av-tree-badge">${allRacks.length}</span>
    <i class="fas fa-chevron-down av-tree-chevron"></i>
  </div>
  <div class="av-tree-children open">
    ${allRacks.map(r => `
      <div class="av-rack-item ${_rackShapes.has(r.id) ? 'positioned' : ''}"
           data-rack-id="${r.id}">
        <div class="av-rack-dot"></div>
        <span>R${r.num_rack || r.id} — ${r.nombre || ''}</span>
        <small style="margin-left:auto;color:var(--av-text-dim);font-size:8px;">${r.tipo || ''}</small>
      </div>`).join('')}
  </div>
</div>`;
        });

        container.innerHTML = html;
    }

    function _onSidebarRackClick(rackId) {
        // En 3D el encuadre lo hace la cámara
        if (_vista === '3d' && window.AV3D) {
            window.AV3D.focusRack(rackId);
            _seleccionar('rack_' + rackId);
            _abrirInspectorRack(rackId);
            return;
        }

        const shape = _rackShapes.get(rackId);
        if (shape) {
            // Centrar en el rack
            const g = shape.group;
            const scale = _stage.scaleX();
            _stage.position({
                x: _stage.width()  / 2 - g.x() * scale,
                y: _stage.height() / 2 - g.y() * scale,
            });
            _stage.batchDraw();
            _seleccionar('rack_' + rackId);
            _abrirInspectorRack(rackId);
        } else {
            _abrirInspectorRack(rackId);
        }
    }

    // ── Utilidades ───────────────────────────────────────────────
    function _indexarPor(arr, key) {
        const map = {};
        (arr || []).forEach(item => { map[item[key]] = item; });
        return map;
    }

    function _mostrarLoading(show) {
        document.getElementById('av-loading')?.classList.toggle('show', show);
    }

    let _toastTimer = null;
    function _toast(msg, tipo = 'info') {
        const el = document.getElementById('av-toast');
        if (!el) return;
        el.textContent = msg;
        el.className = 'show ' + tipo;
        clearTimeout(_toastTimer);
        _toastTimer = setTimeout(() => { el.className = ''; }, 3000);
    }



    // ── Buscador de productos / ubicaciones ─────────────────────────
    let _searchIndex = [];      // índice plano construido desde _inventario
    const _indiceCodigos = new Map();   // código exacto → ubicaciones (lector)
    let _searchResultsActuales = [];
    let _searchActiveIdx = -1;
    let _spotlightAnimFrame = null;
    let _radarShapes = [];

    // Construye un índice plano: producto -> ruta completa hasta el rack
    function _construirIndiceBusqueda() {
        _searchIndex = [];
        _indiceCodigos.clear();
        if (!_inventario) return;

        const sucursales = _inventario.sucursales || [];
        const sucursal = sucursales.find(s => s.id === _sucursalId) || sucursales[0];
        if (!sucursal) return;

        (sucursal.almacenes || []).forEach(alm => {
            const allRacks = [
                ...((alm.pasillos || []).flatMap(p => p.racks || [])),
                ...(alm.racks_sin_pasillo || [])
            ];

            allRacks.forEach(rack => {
                // Entrada por el propio rack (búsqueda por ubicación/nombre de rack)
                _searchIndex.push({
                    tipo: 'rack',
                    rackId: rack.id,
                    almacenId: alm.id,
                    texto: `${rack.nombre || ''} R${rack.num_rack || ''} ${rack.tipo || ''}`.trim(),
                    almacenNombre: alm.descripcion,
                    rackLabel: `R${rack.num_rack || rack.id}${rack.nombre ? ' — ' + rack.nombre : ''}`,
                });

                (rack.columnas || []).forEach(col => {
                    (col.niveles || []).forEach(nivel => {
                        // Entrada por ubicación (ulocation) del nivel
                        if (nivel.ulocation) {
                            _searchIndex.push({
                                tipo: 'ubicacion',
                                rackId: rack.id,
                                almacenId: alm.id,
                                nivelId: nivel.id,
                                texto: nivel.ulocation,
                                almacenNombre: alm.descripcion,
                                rackLabel: `R${rack.num_rack || rack.id}${rack.nombre ? ' — ' + rack.nombre : ''}`,
                                nivelLabel: `Col ${col.num_col || ''} · Nivel ${nivel.num_nivel || ''}`,
                            });
                        }

                        (nivel.tarimas || []).forEach(tarima => {
                            (tarima.productos || []).forEach(p => {
                                _searchIndex.push({
                                    tipo: 'producto',
                                    rackId: rack.id,
                                    almacenId: alm.id,
                                    nivelId: nivel.id,
                                    tarimaId: tarima.id,
                                    texto: `${p.cve_prod || ''} ${p.descr_prod || ''} ${p.cbr || ''}`.trim(),
                                    prodId: p.id,
                                    cveProd: p.cve_prod,
                                    descrProd: p.descr_prod,
                                    cbr: p.cbr,
                                    codProv: p.cod_prov,
                                    cantidad: p.cantidad,
                                    udm: p.udm || p.unidad,
                                    almacenNombre: alm.descripcion,
                                    rackLabel: `R${rack.num_rack || rack.id}${rack.nombre ? ' — ' + rack.nombre : ''}`,
                                    nivelLabel: `Col ${col.num_col || ''} · Nivel ${nivel.num_nivel || ''}${nivel.ulocation ? ' (' + nivel.ulocation + ')' : ''}`,
                                    tarimaCodigo: tarima.codigo,
                                });
                            });
                        });
                    });
                });
            });
        });

        _construirIndiceCodigos();
    }

    /**
     * Índice de coincidencia EXACTA para el lector de código de barras.
     * Se registran código de barras, clave de producto y código de proveedor,
     * y una misma clave puede estar en varias ubicaciones: se guardan todas.
     */
    function _construirIndiceCodigos() {
        _searchIndex.forEach(item => {
            if (item.tipo !== 'producto') return;
            [item.cbr, item.cveProd, item.codProv].forEach(cod => {
                _variantesCodigo(cod).forEach(k => {
                    if (!_indiceCodigos.has(k)) _indiceCodigos.set(k, []);
                    const lista = _indiceCodigos.get(k);
                    if (!lista.includes(item)) lista.push(item);
                });
            });
        });
    }

    /**
     * Variantes bajo las que se indexa/busca un código. Los lectores suelen
     * devolver el EAN completo con ceros a la izquierda mientras que en el
     * catálogo puede estar sin ellos (o al revés), así que se registran ambas.
     */
    function _variantesCodigo(cod) {
        const base = (cod ?? '').toString().trim().toUpperCase();
        if (!base) return [];
        const v = [base];
        if (/^\d+$/.test(base)) {
            const sinCeros = base.replace(/^0+/, '');
            if (sinCeros && sinCeros !== base) v.push(sinCeros);
        }
        return v;
    }

    /** Busca un código exacto; devuelve todas las ubicaciones que lo tienen. */
    function _buscarPorCodigo(codigo) {
        for (const k of _variantesCodigo(codigo)) {
            const hit = _indiceCodigos.get(k);
            if (hit?.length) return hit;
        }
        return [];
    }

    // Normaliza texto (sin acentos, minúsculas) para comparar
    function _normTexto(s) {
        return (s || '').toString().toLowerCase()
            .normalize('NFD').replace(/[\u0300-\u036f]/g, '');
    }

    function _buscar(query) {
        const q = _normTexto(query).trim();
        if (q.length < 2) return [];

        const terms = q.split(/\s+/).filter(Boolean);

        const matches = _searchIndex
            .map(item => {
                const haystack = _normTexto(item.texto);
                const ok = terms.every(t => haystack.includes(t));
                if (!ok) return null;
                // Score simple: coincidencia exacta al inicio puntúa más alto
                let score = 0;
                if (haystack.startsWith(q)) score += 10;
                score += (item.texto.length < 40) ? 2 : 0;
                if (item.tipo === 'producto') score += 3;
                return { item, score };
            })
            .filter(Boolean)
            .sort((a, b) => b.score - a.score)
            .slice(0, 30)
            .map(m => m.item);

        return matches;
    }

    function _highlightTexto(texto, query) {
        if (!texto) return '';
        const q = _normTexto(query).trim();
        if (!q) return _escapeHtml(texto);
        const terms = q.split(/\s+/).filter(Boolean);
        let html = _escapeHtml(texto);
        terms.forEach(t => {
            if (!t) return;
            const re = new RegExp('(' + t.replace(/[.*+?^${}()|[\]\\]/g, '\\$&') + ')', 'gi');
            html = html.replace(re, '<mark class="av-search-hl">$1</mark>');
        });
        return html;
    }

    function _escapeHtml(s) {
        return (s || '').toString()
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
    }

    function _iconoParaTipo(tipo) {
        if (tipo === 'producto') return 'fa-box';
        if (tipo === 'ubicacion') return 'fa-map-pin';
        return 'fa-server';
    }

    function _renderResultadosBusqueda(query) {
        const cont = document.getElementById('av-search-results');
        if (!cont) return;

        const matches = _buscar(query);
        _searchResultsActuales = matches;
        _searchActiveIdx = -1;

        if (!query || query.trim().length < 2) {
            cont.classList.remove('open');
            cont.innerHTML = '';
            return;
        }

        if (matches.length === 0) {
            cont.innerHTML = `<div class="av-search-empty">
            <i class="fas fa-ghost"></i>
            Sin resultados para "${_escapeHtml(query)}"
        </div>`;
            cont.classList.add('open');
            return;
        }

        // Barra de acciones con botón PDF
        let html = `<div class="av-search-pdf-bar">
        <span style="font-size:10px;color:var(--av-text-muted);">
            ${matches.length} resultado${matches.length !== 1 ? 's' : ''}
        </span>
        <button class="av-search-pdf-btn" id="av-search-export-pdf" title="Exportar resultados a PDF">
            <i class="fas fa-file-pdf"></i> Exportar PDF
        </button>
    </div>`;

        matches.forEach((item, idx) => {
            const icon = _iconoParaTipo(item.tipo);
            let titulo, sub;

            if (item.tipo === 'producto') {
                titulo = _highlightTexto(item.cveProd || item.descrProd || 'Producto', query);
                sub = `${_escapeHtml(item.descrProd || '')} · ${_fmtCantidad(item.cantidad)} ${_escapeHtml(item.udm || '')}`;
            } else if (item.tipo === 'ubicacion') {
                titulo = _highlightTexto(item.texto, query);
                sub = `Ubicación · ${_escapeHtml(item.nivelLabel)}`;
            } else {
                titulo = _highlightTexto(item.rackLabel, query);
                sub = `Rack en ${_escapeHtml(item.almacenNombre || '')}`;
            }

            // Construir la ruta de ubicación completa
            const ubicacionPath = _buildUbicacionPath(item);

            html += `
        <div class="av-search-item" data-idx="${idx}">
            <div class="av-search-item-icon"><i class="fas ${icon}"></i></div>
            <div class="av-search-item-body">
                <strong>${titulo}</strong>
                <span>${sub}</span>
                ${ubicacionPath ? `<span class="av-search-item-ubicacion">
                    <i class="fas fa-map-marker-alt" style="font-size:8px;margin-right:3px;color:var(--av-accent);"></i>${_escapeHtml(ubicacionPath)}
                </span>` : ''}
            </div>
            <div class="av-search-item-path">
                ${_escapeHtml(item.almacenNombre || '')}<br>${_escapeHtml(item.rackLabel || '')}
            </div>
        </div>`;
        });

        cont.innerHTML = html;
        cont.classList.add('open');

        document.getElementById('av-search-export-pdf')?.addEventListener('click', (e) => {
            e.stopPropagation();
            const currentQuery = document.getElementById('av-search-input')?.value || '';
            _exportarBusquedaPDF(currentQuery, _searchResultsActuales);
        });
    }

    // Construye la ruta de ubicación legible para un item del índice
    function _buildUbicacionPath(item) {
        const parts = [];
        if (item.almacenNombre) parts.push(item.almacenNombre);
        if (item.rackLabel) parts.push(item.rackLabel);
        if (item.nivelLabel) parts.push(item.nivelLabel);
        if (item.tarimaCodigo) parts.push('Tarima: ' + item.tarimaCodigo);
        return parts.join(' › ');
    }

    function _fmtCantidad(n) {
        if (n === null || n === undefined) return '—';
        return parseFloat(n).toLocaleString('es-MX', { maximumFractionDigits: 2 });
    }

    // ── Selección de un resultado: pan + zoom + spotlight + radar ──
    async function _seleccionarResultadoBusqueda(item) {
        if (!item) return;

        // Cerrar dropdown y colapsar input
        document.getElementById('av-search-results')?.classList.remove('open');
        const wrap = document.getElementById('av-search-wrap');
        wrap?.classList.remove('expanded');

        await _irAResultado(item);
    }

    /**
     * Navega hasta la ubicación de un resultado (búsqueda o lectura de código).
     * @param {Object} item  entrada del índice de búsqueda
     * @param {{abrirTarima?:boolean}} [opts]  abrir el detalle de la tarima
     *        en vez del inspector del rack (lo natural tras escanear).
     */
    async function _irAResultado(item, opts = {}) {
        if (!item) return;

        // ── Vista 3D: la cámara recorre el almacén hasta la ubicación ──
        if (_vista === '3d' && window.AV3D) {
            const localizado = await window.AV3D.focusUbicacion({
                rackId: item.rackId,
                nivelId: item.nivelId,
                tarimaId: item.tarimaId,
            });

            if (!localizado) {
                _toast('El rack no está posicionado en el plano visible. Mostrando datos del rack.', 'info');
                await _abrirInspectorRack(item.rackId);
                return;
            }

            _mostrarBannerBusqueda(item);
            _seleccionar('rack_' + item.rackId);

            const ctx = opts.abrirTarima && item.tarimaId
                ? _localizarTarima(item.rackId, item.tarimaId)
                : null;

            if (ctx) {
                RackInspector.abrirTarima({ ...ctx, destacarProductoId: item.prodId });
            } else {
                await _abrirInspectorRack(item.rackId, {
                    delayMs: 500,
                    destacarTarimaId: item.tarimaId,
                });
                if (item.nivelId) {
                    setTimeout(() => _resaltarNivelEnInspector(item.nivelId), 250);
                }
            }
            return;
        }

        // Si el rack no está aún en el plano actual (otra sucursal/almacén), no podemos
        // posicionarlo visualmente — avisamos y solo abrimos el inspector.
        const shape = _rackShapes.get(item.rackId);

        if (!shape) {
            _toast('El rack no está posicionado en el plano visible. Mostrando datos del rack.', 'info');
            _abrirInspectorRack(item.rackId);
            return;
        }

        // 1. Cambiar a modo ver (si estaba en editar) para evitar confusiones
        if (_modo === 'editar') setModo('ver');

        // 2. Pan + zoom centrado en el rack
        await _panZoomARack(shape);

        // 3. Spotlight + radar pulse llamativo
        _mostrarSpotlight(shape, item);

        // 4. Seleccionar visualmente y abrir el detalle
        _seleccionar('rack_' + item.rackId);

        const ctx2d = opts.abrirTarima && item.tarimaId
            ? _localizarTarima(item.rackId, item.tarimaId)
            : null;

        if (ctx2d) {
            RackInspector.abrirTarima({ ...ctx2d, destacarProductoId: item.prodId });
            return;
        }

        await _abrirInspectorRack(item.rackId, {
            delayMs: 1500,
            destacarTarimaId: item.tarimaId,
        });

        // 5. Si el resultado apunta a un nivel específico, resaltarlo en el inspector
        if (item.nivelId) {
            setTimeout(() => _resaltarNivelEnInspector(item.nivelId), 250);
        }
    }

    /**
     * Localiza en el árbol de inventario el contexto completo de una tarima
     * (tarima, nivel, columna, rack y almacén) a partir de sus ids.
     */
    function _localizarTarima(rackId, tarimaId) {
        const sucs = _inventario?.sucursales || [];
        const suc = sucs.find(s => s.id === _sucursalId) || sucs[0];

        for (const alm of (suc?.almacenes || [])) {
            const racks = [
                ...((alm.pasillos || []).flatMap(p => p.racks || [])),
                ...(alm.racks_sin_pasillo || []),
            ];
            const rack = racks.find(r => r.id === rackId);
            if (!rack) continue;

            for (const columna of (rack.columnas || [])) {
                for (const nivel of (columna.niveles || [])) {
                    const tarima = (nivel.tarimas || []).find(t => t.id === tarimaId);
                    if (tarima) return { tarima, nivel, columna, rack, almacen: alm };
                }
            }
        }
        return null;
    }

    function _panZoomARack(shape) {
        return new Promise(resolve => {
            const targetScale = Math.min(_scaleMax, 1.6);
            const g = shape.group;
            const cx = g.x() + shape.pos.ancho / 2;
            const cy = g.y() + shape.pos.alto / 2;

            const startScale = _stage.scaleX();
            const startPos = _stage.position();
            const endScale = targetScale;
            const endPos = {
                x: _stage.width() / 2 - cx * endScale,
                y: _stage.height() / 2 - cy * endScale,
            };

            const dur = 500;
            const t0 = performance.now();

            function step(now) {
                const t = Math.min(1, (now - t0) / dur);
                const ease = 1 - Math.pow(1 - t, 3); // easeOutCubic
                const scale = startScale + (endScale - startScale) * ease;
                const x = startPos.x + (endPos.x - startPos.x) * ease;
                const y = startPos.y + (endPos.y - startPos.y) * ease;
                _stage.scale({ x: scale, y: scale });
                _stage.position({ x, y });
                _stage.batchDraw();
                _actualizarZoomLabel(scale);
                _actualizarMinimap();
                if (t < 1) {
                    requestAnimationFrame(step);
                } else {
                    resolve();
                }
            }
            requestAnimationFrame(step);
        });
    }

    function _mostrarSpotlight(shape, item) {
        _limpiarSpotlight();
        const accent = _paleta().accent;
        const overlay = document.getElementById('av-spotlight-overlay');

        if (overlay) overlay.classList.add('show');

        _mostrarBannerBusqueda(item);

        // Radar pulse: anillos concéntricos en Konva sobre el rack, en _layerUI
        // (no listening, así que no estorba interacción)
        const g = shape.group;
        const cx = g.x() + shape.pos.ancho / 2;
        const cy = g.y() + shape.pos.alto / 2;
        const baseR = Math.max(shape.pos.ancho, shape.pos.alto) / 2;

        _radarShapes = [];
        for (let i = 0; i < 3; i++) {
            const ring = new Konva.Circle({
                x: cx, y: cy,
                radius: baseR,
                stroke: accent,
                strokeWidth: 2,
                opacity: 0,
                listening: false,
            });
            _layerUI.add(ring);
            _radarShapes.push(ring);
        }

        // Resaltar borde del rack mismo (glow pulsante)
        const rect = g.findOne('Rect');
        const origStroke = rect ? rect.stroke() : null;
        const origStrokeW = rect ? rect.strokeWidth() : null;
        if (rect) {
            rect.stroke(accent);
            rect.strokeWidth(2.5);
            rect.shadowColor(accent);
            rect.shadowBlur(20);
            rect.shadowOpacity(0.9);
        }

        let start = null;
        const cycleMs = 1400;

        function animar(ts) {
            if (!start) start = ts;
            const elapsed = (ts - start) % cycleMs;

            _radarShapes.forEach((ring, i) => {
                const offset = i * (cycleMs / 3);
                const t = ((elapsed + offset) % cycleMs) / cycleMs;
                const scale = 0.4 + t * 2.2;
                const opacity = t < 0.1 ? t * 9 : Math.max(0, 0.9 * (1 - t));
                ring.radius(baseR * scale);
                ring.opacity(opacity);
            });

            _layerUI.batchDraw();
            _spotlightAnimFrame = requestAnimationFrame(animar);
        }
        _spotlightAnimFrame = requestAnimationFrame(animar);

        // Guardar referencia para restaurar el rack al limpiar
        overlay._restoreRect = () => {
            if (rect) {
                rect.stroke(origStroke);
                rect.strokeWidth(origStrokeW);
                rect.shadowBlur(0);
                rect.shadowOpacity(0);
                _layerRacks.batchDraw();
            }
        };

        // Cerrar al hacer click en el overlay oscuro
        overlay.onclick = _limpiarSpotlight;
        document.getElementById('av-search-banner-close').onclick = _limpiarSpotlight;
    }

    /** Banner superior que describe el resultado localizado (2D y 3D). */
    function _mostrarBannerBusqueda(item) {
        const banner = document.getElementById('av-search-banner');
        const bannerText = document.getElementById('av-search-banner-text');

        if (bannerText) {
            if (item.tipo === 'producto') {
                bannerText.innerHTML = `Encontrado: <strong>${_escapeHtml(item.cveProd || item.descrProd)}</strong> en ${_escapeHtml(item.rackLabel)} · ${_escapeHtml(item.almacenNombre)}`;
            } else if (item.tipo === 'ubicacion') {
                bannerText.innerHTML = `Ubicación <strong>${_escapeHtml(item.texto)}</strong> en ${_escapeHtml(item.rackLabel)}`;
            } else {
                bannerText.innerHTML = `<strong>${_escapeHtml(item.rackLabel)}</strong> · ${_escapeHtml(item.almacenNombre)}`;
            }
        }
        if (banner) banner.classList.add('show');

        const cerrar = document.getElementById('av-search-banner-close');
        if (cerrar) cerrar.onclick = _limpiarSpotlight;
    }

    function _limpiarSpotlight() {
        const overlay = document.getElementById('av-spotlight-overlay');
        const banner = document.getElementById('av-search-banner');

        if (overlay) {
            overlay.classList.remove('show');
            if (typeof overlay._restoreRect === 'function') overlay._restoreRect();
        }
        if (banner) banner.classList.remove('show');

        if (_spotlightAnimFrame) {
            cancelAnimationFrame(_spotlightAnimFrame);
            _spotlightAnimFrame = null;
        }
        _radarShapes.forEach(r => r.destroy());
        _radarShapes = [];
        _layerUI.batchDraw();
    }

    function _resaltarNivelEnInspector(nivelId) {
        const svg = document.getElementById('av-rack-iso-svg');
        if (!svg) return;
        const target = svg.querySelector(`.rack-nivel[data-nivel-id="${nivelId}"]`);
        if (!target) return;

        // Simular click para mostrar su detalle de tarimas/productos
        target.dispatchEvent(new Event('click'));

        // Resaltado visual breve sobre la celda del rack isométrico
        const rect = target.querySelector('rect');
        if (rect) {
            const orig = rect.getAttribute('stroke');
            rect.setAttribute('stroke', '#00d4ff');
            rect.setAttribute('stroke-width', '2.5');
            rect.style.filter = 'drop-shadow(0 0 4px #00d4ff)';
            setTimeout(() => {
                rect.setAttribute('stroke', orig);
                rect.setAttribute('stroke-width', '0.8');
                rect.style.filter = '';
            }, 2200);
        }

        target.scrollIntoView?.({ behavior: 'smooth', block: 'center' });
    }

    // ── Bind de eventos del buscador (llamar desde _bindEvents) ────
    function _bindBuscador() {
        const wrap = document.getElementById('av-search-wrap');
        const toggle = document.getElementById('av-search-toggle');
        const input = document.getElementById('av-search-input');
        const clearBtn = document.getElementById('av-search-clear');
        const results = document.getElementById('av-search-results');

        if (!wrap || !toggle || !input) return;

        toggle.addEventListener('click', () => {
            wrap.classList.toggle('expanded');
            if (wrap.classList.contains('expanded')) {
                input.focus();
            } else {
                results.classList.remove('open');
            }
        });

        let debounceTimer = null;
        input.addEventListener('input', () => {
            wrap.classList.toggle('has-text', input.value.trim().length > 0);
            clearTimeout(debounceTimer);
            debounceTimer = setTimeout(() => {
                _renderResultadosBusqueda(input.value);
            }, 120);
        });

        input.addEventListener('keydown', (e) => {
            const items = results.querySelectorAll('.av-search-item');
            if (e.key === 'ArrowDown') {
                e.preventDefault();
                _searchActiveIdx = Math.min(_searchActiveIdx + 1, items.length - 1);
                _marcarActivo(items);
            } else if (e.key === 'ArrowUp') {
                e.preventDefault();
                _searchActiveIdx = Math.max(_searchActiveIdx - 1, 0);
                _marcarActivo(items);
            } else if (e.key === 'Enter') {
                e.preventDefault();
                const idx = _searchActiveIdx >= 0 ? _searchActiveIdx : 0;
                if (_searchResultsActuales[idx]) {
                    _seleccionarResultadoBusqueda(_searchResultsActuales[idx]);
                }
            } else if (e.key === 'Escape') {
                wrap.classList.remove('expanded');
                results.classList.remove('open');
            }
        });

        clearBtn?.addEventListener('click', () => {
            input.value = '';
            wrap.classList.remove('has-text');
            results.classList.remove('open');
            input.focus();
        });

        results.addEventListener('click', (e) => {
            const item = e.target.closest('.av-search-item[data-idx]');
            if (!item) return;
            const idx = parseInt(item.dataset.idx);
            if (_searchResultsActuales[idx]) {
                _seleccionarResultadoBusqueda(_searchResultsActuales[idx]);
            }
        });

        // Cerrar dropdown al hacer click fuera
        document.addEventListener('click', (e) => {
            if (!wrap.contains(e.target)) {
                results.classList.remove('open');
            }
        });
    }

    function _marcarActivo(items) {
        items.forEach((el, i) => el.classList.toggle('active', i === _searchActiveIdx));
        items[_searchActiveIdx]?.scrollIntoView({ block: 'nearest' });
    }

    // ════════════════════════════════════════════════════════════════
    // MÓDULO: Historial de Producto — paginación lazy (fetch de 10 en 10)
    // ════════════════════════════════════════════════════════════════
    const HistorialProducto = (() => {

        const PAGE_SIZE = 10;

        let _productoId = null;
        let _productoLabel = '';
        let _currentPage = 0;
        let _totalPages = 0;
        let _totalRows = 0;
        let _cargando = false;

        // ── Abre el modal y carga la primera página ──────────────────
        function abrir(productoId, productoLabel) {
            _productoId = productoId;
            _productoLabel = productoLabel;
            _currentPage = 0;
            _totalPages = 0;
            _totalRows = 0;

            const overlay = document.getElementById('av-historial-modal-overlay');
            const title = document.getElementById('av-historial-modal-title');
            const sub = document.getElementById('av-historial-modal-subtitle');
            const wrap = document.getElementById('av-historial-table-wrap');

            title.textContent = 'Historial — ' + productoLabel;
            sub.textContent = 'Cargando…';
            wrap.innerHTML = `<div class="av-hm-empty">
            <div class="av-spinner-ring" style="margin:0 auto 10px;"></div>
            <span>Consultando movimientos…</span>
        </div>`;

            _limpiarPaginacion();
            overlay.classList.add('open');
            _cargarPagina(0);
        }

        // ── Carga una página específica (fetch) ──────────────────────
        async function _cargarPagina(page) {
            if (_cargando) return;
            _cargando = true;
            document.getElementById('av-historial-row-loader')?.classList.add('show');

            try {
                const r = await fetch(
                    `/Consulta/GetHistorialProducto?productoId=${_productoId}&page=${page}&pageSize=${PAGE_SIZE}`
                );
                const d = await r.json();
                if (!d.ok) throw new Error(d.msg);

                _totalRows = d.total;
                _totalPages = d.totalPages;
                _currentPage = d.page;

                _renderTabla(d.data || []);
                _renderFooter();
            } catch (err) {
                document.getElementById('av-historial-table-wrap').innerHTML = `
                <div class="av-hm-empty">
                    <i class="fas fa-exclamation-triangle" style="color:var(--av-danger)"></i>
                    <span style="color:var(--av-danger)">${err.message}</span>
                </div>`;
            } finally {
                _cargando = false;
                document.getElementById('av-historial-row-loader')?.classList.remove('show');
            }
        }

        // ── Render de la tabla ───────────────────────────────────────
        function _renderTabla(rows) {
            const wrap = document.getElementById('av-historial-table-wrap');

            if (rows.length === 0) {
                wrap.innerHTML = `
                <div class="av-hm-empty">
                    <i class="fas fa-inbox"></i>
                    <span>Sin movimientos registrados para este producto</span>
                </div>`;
                return;
            }

            let html = `
        <table class="av-historial-table">
            <thead>
                <tr>
                    <th>#</th>
                    <th>Fecha</th>
                    <th>Tipo</th>
                    <th>Cantidad</th>
                    <th>UDM</th>
                    <th>Folio</th>
                    <th>T. Origen</th>
                    <th>T. Destino</th>
                    <th>Almacén origen</th>
                    <th>Almacén destino</th>
                    <th>Sucursal origen</th>
                    <th>Motivo</th>
                    <th>Usuario</th>
                </tr>
            </thead>
            <tbody>`;

            const offset = _currentPage * PAGE_SIZE;
            rows.forEach((row, idx) => {
                const tipo = (row.tipo_movimiento || '').toLowerCase();
                const tipoClass = tipo.includes('entrada') ? 'entrada'
                    : tipo.includes('salida') ? 'salida'
                        : tipo.includes('traslado') ? 'traslado'
                            : 'ajuste';

                const tipoIcon = tipo.includes('entrada') ? 'fa-arrow-down'
                    : tipo.includes('salida') ? 'fa-arrow-up'
                        : tipo.includes('traslado') ? 'fa-exchange-alt'
                            : 'fa-sliders-h';

                const cantClass = tipoClass === 'entrada' ? 'av-hm-cant-pos' : 'av-hm-cant-neg';
                const cantPfx = tipoClass === 'entrada' ? '+' : tipoClass === 'salida' ? '−' : '';
                const fecha = dateFormatter(row.fecha);

                html += `<tr>
                <td style="color:var(--av-text-dim);font-size:9px;">${offset + idx + 1}</td>
                <td style="font-size:10px;font-family:var(--av-font-mono);white-space:nowrap;">${fecha}</td>
                <td>
                    <span class="av-hm-tipo-badge ${tipoClass}">
                        <i class="fas ${tipoIcon}"></i>
                        ${row.tipo_movimiento || '—'}
                    </span>
                </td>
                <td class="${cantClass}">${cantPfx}${_fmtNum(row.cantidad)}</td>
                <td style="color:var(--av-text-muted)">${row.cve_udm || '—'}</td>
                <td style="font-size:10px;font-family:var(--av-font-mono);color:var(--av-accent2)">${row.folio_documento || '—'}</td>
                <td style="color:var(--av-text-muted)">${row.tarima_origen || '—'}</td>
                <td style="color:var(--av-text-muted)">${row.tarima_destino || '—'}</td>
                <td>${row.almacen_origen || '—'}</td>
                <td>${row.almacen_destino || '—'}</td>
                <td>${row.sucursal_origen || '—'}</td>
                <td style="max-width:140px;overflow:hidden;text-overflow:ellipsis;" title="${_escHtml(row.motivo || '')}">${row.motivo || '—'}</td>
                <td>${row.usuario || '—'}</td>
            </tr>`;

                if (row.comentario_adicional) {
                    html += `<tr>
                    <td colspan="13" style="padding:4px 12px 8px;font-size:9px;
                        color:var(--av-warn);background:rgba(245,158,11,0.04);
                        border-bottom:1px solid rgba(245,158,11,0.1);">
                        <i class="fas fa-info-circle" style="margin-right:4px;"></i>
                        ${_escHtml(row.comentario_adicional)}
                    </td>
                </tr>`;
                }
            });

            html += `</tbody></table>`;
            wrap.innerHTML = html;
        }

        // ── Footer: info + botones de página ────────────────────────
        function _renderFooter() {
            const info = document.getElementById('av-historial-info');
            const pag = document.getElementById('av-historial-pag-btns');
            const sub = document.getElementById('av-historial-modal-subtitle');

            const from = _currentPage * PAGE_SIZE + 1;
            const to = Math.min((_currentPage + 1) * PAGE_SIZE, _totalRows);

            sub.textContent = `${_totalRows.toLocaleString('es-MX')} movimientos encontrados`;
            info.innerHTML = `Mostrando <strong>${from}–${to}</strong> de <strong>${_totalRows.toLocaleString('es-MX')}</strong>
        <button class="av-hm-pdf-btn" id="av-hm-btn-pdf" title="Exportar historial a PDF">
            <i class="fas fa-file-pdf"></i> Exportar PDF
        </button>`;

            // Generar botones de paginación (ventana deslizante ±2 páginas)
            let html = '';

            // Anterior
            html += `<button class="av-hm-pag-btn" id="av-hm-prev"
                    ${_currentPage === 0 ? 'disabled' : ''}>
                    <i class="fas fa-chevron-left" style="font-size:9px;"></i>
                 </button>`;

            // Páginas visibles
            const windowSize = 5;
            let startP = Math.max(0, _currentPage - Math.floor(windowSize / 2));
            let endP = Math.min(_totalPages - 1, startP + windowSize - 1);
            if (endP - startP < windowSize - 1) startP = Math.max(0, endP - windowSize + 1);

            if (startP > 0) {
                html += `<button class="av-hm-pag-btn" data-page="0">1</button>`;
                if (startP > 1) html += `<span class="av-hm-pag-sep">…</span>`;
            }

            for (let p = startP; p <= endP; p++) {
                html += `<button class="av-hm-pag-btn ${p === _currentPage ? 'active' : ''}"
                         data-page="${p}">${p + 1}</button>`;
            }

            if (endP < _totalPages - 1) {
                if (endP < _totalPages - 2) html += `<span class="av-hm-pag-sep">…</span>`;
                html += `<button class="av-hm-pag-btn" data-page="${_totalPages - 1}">${_totalPages}</button>`;
            }

            // Siguiente
            html += `<button class="av-hm-pag-btn" id="av-hm-next"
                    ${_currentPage >= _totalPages - 1 ? 'disabled' : ''}>
                    <i class="fas fa-chevron-right" style="font-size:9px;"></i>
                 </button>`;

            pag.innerHTML = html;

            // Bind de botones
            pag.querySelectorAll('.av-hm-pag-btn[data-page]').forEach(btn => {
                btn.addEventListener('click', () => _cargarPagina(parseInt(btn.dataset.page)));
            });
            document.getElementById('av-hm-prev')?.addEventListener('click', () => {
                if (_currentPage > 0) _cargarPagina(_currentPage - 1);
            });
            document.getElementById('av-hm-next')?.addEventListener('click', () => {
                if (_currentPage < _totalPages - 1) _cargarPagina(_currentPage + 1);
            });
            document.getElementById('av-hm-btn-pdf')?.addEventListener('click', () => {
                _abrirDialogoPDF();
            });
        }

        function _limpiarPaginacion() {
            const pag = document.getElementById('av-historial-pag-btns');
            const info = document.getElementById('av-historial-info');
            if (pag) pag.innerHTML = '';
            if (info) info.textContent = '—';
        }

        function cerrar() {
            document.getElementById('av-historial-modal-overlay')?.classList.remove('open');
        }

        // ── Bind de cierre (una sola vez) ────────────────────────────
        function _bindEvents() {
            document.getElementById('av-historial-modal-close')?.addEventListener('click', cerrar);
            document.getElementById('av-historial-modal-overlay')?.addEventListener('click', (e) => {
                if (e.target.id === 'av-historial-modal-overlay') cerrar();
            });
            document.addEventListener('keydown', (e) => {
                if (e.key === 'Escape' &&
                    document.getElementById('av-historial-modal-overlay')?.classList.contains('open')) {
                    cerrar();
                }
            });
        }

        // ── Helpers ──────────────────────────────────────────────────
        function _fmtFecha(dt) {
            if (!dt) return '—';
            try {
                const d = new Date(dt);
                return d.toLocaleDateString('es-MX') + ' ' +
                    d.toLocaleTimeString('es-MX', { hour: '2-digit', minute: '2-digit' });
            } catch { return dt; }
        }

        function _fmtNum(n) {
            if (n === null || n === undefined) return '—';
            return parseFloat(n).toLocaleString('es-MX', { maximumFractionDigits: 4 });
        }

        function _escHtml(s) {
            return (s || '').toString()
                .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
        }

        document.addEventListener('DOMContentLoaded', _bindEvents);


        function _abrirDialogoPDF() {
            // Crear un pequeño diálogo inline en el footer
            const info = document.getElementById('av-historial-info');
            if (!info) return;

            info.innerHTML = `
        <div class="av-hm-pdf-dialog">
            <span style="font-size:10px;color:var(--av-text-muted);">¿Cuántos movimientos exportar?</span>
            <input type="number" id="av-hm-pdf-count" class="av-hm-pdf-input"
                   placeholder="Dejar vacío = todos"
                   min="1" max="${_totalRows}" />
            <button class="av-hm-pdf-btn confirm" id="av-hm-pdf-confirm">
                <i class="fas fa-download"></i> Generar
            </button>
            <button class="av-hm-pdf-btn cancel" id="av-hm-pdf-cancel">Cancelar</button>
        </div>`;

            document.getElementById('av-hm-pdf-confirm')?.addEventListener('click', async () => {
                const inputVal = document.getElementById('av-hm-pdf-count')?.value;
                const cantidad = inputVal && parseInt(inputVal) > 0 ? parseInt(inputVal) : _totalRows;
                await _exportarHistorialPDF(cantidad);
                _renderFooter(); // restaurar footer
            });

            document.getElementById('av-hm-pdf-cancel')?.addEventListener('click', () => {
                _renderFooter();
            });
        }

        async function _exportarHistorialPDF(cantidad) {
            const { jsPDF } = window.jspdf;
            const doc = new jsPDF({ orientation: 'landscape', unit: 'mm', format: 'letter' });

            const pageW = doc.internal.pageSize.getWidth();
            const pageH = doc.internal.pageSize.getHeight();
            const margin = 14;

            // Fetch de los datos (puede ser varias páginas)
            let allRows = [];
            const batchSize = Math.min(cantidad, 200); // Traer hasta 200 por fetch
            let fetched = 0;
            let page = 0;

            // Mostrar toast de carga
            AlmacenVirtual._toast('Preparando datos para PDF…', 'info');

            while (fetched < cantidad) {
                const toFetch = Math.min(batchSize, cantidad - fetched);
                try {
                    const r = await fetch(
                        `/Consulta/GetHistorialProducto?productoId=${_productoId}&page=${page}&pageSize=${toFetch}`
                    );
                    const d = await r.json();
                    if (!d.ok) throw new Error(d.msg);
                    allRows = allRows.concat(d.data || []);
                    fetched += (d.data || []).length;
                    if (fetched >= d.total || (d.data || []).length === 0) break;
                    page++;
                } catch (err) {
                    AlmacenVirtual._toast('Error al obtener datos: ' + err.message, 'error');
                    return;
                }
            }

            // ── Header ────────────────────────────────────────────────────
            doc.setFillColor(7, 22, 40);
            doc.rect(0, 0, pageW, 22, 'F');

            doc.setFontSize(13);
            doc.setTextColor(245, 158, 11); // amber (color historial)
            doc.setFont('helvetica', 'bold');
            doc.text('HISTORIAL DE MOVIMIENTOS — ALMACÉN VIRTUAL', margin, 14);

            doc.setFontSize(8);
            doc.setTextColor(180, 200, 220);
            doc.setFont('helvetica', 'normal');
            const fecha = new Date().toLocaleString('es-MX', { dateStyle: 'long', timeStyle: 'short' });
            doc.text(`Generado: ${fecha}`, pageW - margin, 14, { align: 'right' });

            let cursorY = 30;
            doc.setFontSize(9);
            doc.setTextColor(100, 116, 139);
            doc.text(`Producto: ${_productoLabel}   ·   ${allRows.length} movimiento${allRows.length !== 1 ? 's' : ''}`, margin, cursorY);
            cursorY += 6;

            // ── Tabla ─────────────────────────────────────────────────────
            const head = [['#', 'Fecha', 'Tipo', 'Cantidad', 'UDM', 'Folio', 'T. Origen', 'T. Destino', 'Almacén Origen', 'Almacén Destino', 'Usuario', 'Motivo']];

            const body = allRows.map((row, i) => {
                const tipo = (row.tipo_movimiento || '').toLowerCase();
                const pfx = tipo.includes('entrada') ? '+' : tipo.includes('salida') ? '−' : '';
                const fechaFmt = row.fecha ? (() => {
                    try {
                        const d = new Date(row.fecha);
                        return d.toLocaleDateString('es-MX') + ' ' + d.toLocaleTimeString('es-MX', { hour: '2-digit', minute: '2-digit' });
                    } catch { return row.fecha; }
                })() : '—';

                return [
                    i + 1,
                    fechaFmt,
                    row.tipo_movimiento || '—',
                    pfx + (row.cantidad != null ? parseFloat(row.cantidad).toLocaleString('es-MX', { maximumFractionDigits: 4 }) : '—'),
                    row.cve_udm || '—',
                    row.folio_documento || '—',
                    row.tarima_origen || '—',
                    row.tarima_destino || '—',
                    row.almacen_origen || '—',
                    row.almacen_destino || '—',
                    row.usuario || '—',
                    row.motivo || '—',
                ];
            });

            doc.autoTable({
                startY: cursorY + 2,
                head,
                body,
                margin: { left: margin, right: margin },
                styles: { fontSize: 6.5, cellPadding: 2.2, textColor: [30, 41, 59], lineColor: [200, 214, 229], lineWidth: 0.15 },
                headStyles: { fillColor: [180, 110, 0], textColor: 255, fontStyle: 'bold', fontSize: 7 },
                alternateRowStyles: { fillColor: [252, 248, 240] },
                columnStyles: {
                    0: { cellWidth: 8, halign: 'right' },
                    1: { cellWidth: 28, font: 'courier' },
                    2: { cellWidth: 22 },
                    3: { cellWidth: 18, halign: 'right', fontStyle: 'bold' },
                    4: { cellWidth: 12 },
                    5: { cellWidth: 26, font: 'courier' },
                    6: { cellWidth: 18 },
                    7: { cellWidth: 18 },
                    8: { cellWidth: 'auto' },
                    9: { cellWidth: 'auto' },
                    10: { cellWidth: 20 },
                    11: { cellWidth: 28 },
                },
                didDrawCell: (data) => {
                    if (data.section === 'body' && data.column.index === 2) {
                        const tipo = (data.cell.raw || '').toLowerCase();
                        let color;
                        if (tipo.includes('entrada')) color = [16, 185, 129];
                        else if (tipo.includes('salida')) color = [239, 68, 68];
                        else if (tipo.includes('traslado')) color = [0, 150, 200];
                        else color = [245, 158, 11];
                        doc.setTextColor(...color);
                        doc.setFontSize(6);
                        doc.setFont('helvetica', 'bold');
                        doc.text(data.cell.raw,
                            data.cell.x + data.cell.padding('left'),
                            data.cell.y + data.cell.height / 2 + 1.5
                        );
                        doc.setFont('helvetica', 'normal');
                        doc.setTextColor(30, 41, 59);
                    }
                    if (data.section === 'body' && data.column.index === 3) {
                        const val = data.cell.raw || '';
                        const color = val.startsWith('+') ? [16, 185, 129] : val.startsWith('−') ? [239, 68, 68] : [30, 41, 59];
                        doc.setTextColor(...color);
                        doc.setFont('helvetica', 'bold');
                        doc.setFontSize(6.5);
                        doc.text(val,
                            data.cell.x + data.cell.width - data.cell.padding('right'),
                            data.cell.y + data.cell.height / 2 + 1.5,
                            { align: 'right' }
                        );
                        doc.setFont('helvetica', 'normal');
                        doc.setTextColor(30, 41, 59);
                    }
                },
                didDrawPage: (data) => {
                    const pg = doc.internal.getCurrentPageInfo().pageNumber;
                    const total = doc.internal.getNumberOfPages();
                    doc.setFontSize(7);
                    doc.setTextColor(100, 116, 139);
                    doc.text(`Página ${pg} de ${total}`, pageW / 2, pageH - 6, { align: 'center' });
                    doc.text('Sellos y Retenes — Sistema BCS ERP', margin, pageH - 6);
                },
            });

            const label = _productoLabel.replace(/[^a-zA-Z0-9_\-]/g, '_').substring(0, 30);
            doc.save(`historial_${label}_${Date.now()}.pdf`);
            AlmacenVirtual._toast('PDF generado correctamente', 'success');
        }




        return { abrir, cerrar };





    })();
    window.HistorialProducto = HistorialProducto;

    function _exportarBusquedaPDF(query, items) {
        const { jsPDF } = window.jspdf;
        const doc = new jsPDF({ orientation: 'landscape', unit: 'mm', format: 'letter' });

        const azul = [0, 102, 204];
        const gris = [100, 116, 139];
        const negro = [30, 41, 59];
        const verdeCian = [0, 180, 216];
        const fondoHeader = [7, 22, 40];

        const pageW = doc.internal.pageSize.getWidth();
        const pageH = doc.internal.pageSize.getHeight();
        const margin = 14;

        // ── Header ────────────────────────────────────────────────────
        doc.setFillColor(...fondoHeader);
        doc.rect(0, 0, pageW, 22, 'F');

        doc.setFontSize(14);
        doc.setTextColor(0, 212, 255);
        doc.setFont('helvetica', 'bold');
        doc.text('REPORTE DE BÚSQUEDA — ALMACÉN VIRTUAL', margin, 14);

        doc.setFontSize(8);
        doc.setTextColor(180, 200, 220);
        doc.setFont('helvetica', 'normal');
        const fecha = new Date().toLocaleString('es-MX', { dateStyle: 'long', timeStyle: 'short' });
        doc.text(`Generado: ${fecha}`, pageW - margin, 14, { align: 'right' });

        // ── Subtítulo ─────────────────────────────────────────────────
        let cursorY = 30;
        doc.setFontSize(9);
        doc.setTextColor(...gris);
        doc.setFont('helvetica', 'normal');
        doc.text(`Búsqueda: "${query}"   ·   ${items.length} resultado${items.length !== 1 ? 's' : ''} encontrado${items.length !== 1 ? 's' : ''}`, margin, cursorY);
        cursorY += 6;

        // ── Tabla ─────────────────────────────────────────────────────
        const head = [['Tipo', 'Clave / ID', 'Descripción', 'Cantidad', 'UDM', 'Ubicación Completa']];
        const body = items.map(item => {
            let tipo, clave, descr, cant, udm;
            const ubicacion = _buildUbicacionPath(item);

            if (item.tipo === 'producto') {
                tipo = 'Producto';
                clave = item.cveProd || '—';
                descr = item.descrProd || '—';
                cant = item.cantidad != null ? parseFloat(item.cantidad).toLocaleString('es-MX', { maximumFractionDigits: 2 }) : '—';
                udm = item.udm || '—';
            } else if (item.tipo === 'ubicacion') {
                tipo = 'Ubicación';
                clave = item.texto || '—';
                descr = item.nivelLabel || '—';
                cant = '—';
                udm = '—';
            } else {
                tipo = 'Rack';
                clave = item.rackLabel || '—';
                descr = item.almacenNombre || '—';
                cant = '—';
                udm = '—';
            }

            return [tipo, clave, descr, cant, udm, ubicacion];
        });

        doc.autoTable({
            startY: cursorY + 2,
            head,
            body,
            margin: { left: margin, right: margin },
            styles: {
                fontSize: 7.5,
                cellPadding: 3,
                textColor: negro,
                lineColor: [200, 214, 229],
                lineWidth: 0.2,
            },
            headStyles: {
                fillColor: azul,
                textColor: 255,
                fontStyle: 'bold',
                fontSize: 8,
            },
            alternateRowStyles: {
                fillColor: [245, 248, 252],
            },
            columnStyles: {
                0: { cellWidth: 18, fontStyle: 'bold' },
                1: { cellWidth: 30, font: 'courier' },
                2: { cellWidth: 55 },
                3: { cellWidth: 20, halign: 'right' },
                4: { cellWidth: 16 },
                5: { cellWidth: 'auto' },
            },
            didDrawCell: (data) => {
                // Colorear la columna Tipo
                if (data.section === 'body' && data.column.index === 0) {
                    const tipo = data.cell.raw;
                    let color;
                    if (tipo === 'Producto') color = [16, 185, 129];
                    else if (tipo === 'Ubicación') color = [0, 150, 200];
                    else color = [99, 102, 241];
                    doc.setTextColor(...color);
                    doc.setFontSize(7);
                    doc.setFont('helvetica', 'bold');
                    doc.text(tipo,
                        data.cell.x + data.cell.padding('left'),
                        data.cell.y + data.cell.height / 2 + 2
                    );
                    doc.setTextColor(...negro);
                    doc.setFont('helvetica', 'normal');
                }
            },
            // Pie de página en cada hoja
            didDrawPage: (data) => {
                const pg = doc.internal.getCurrentPageInfo().pageNumber;
                const total = doc.internal.getNumberOfPages();
                doc.setFontSize(7);
                doc.setTextColor(...gris);
                doc.text(`Página ${pg} de ${total}`, pageW / 2, pageH - 6, { align: 'center' });
                doc.text('Sellos y Retenes — Sistema BCS ERP', margin, pageH - 6);
            },
        });

        const fileName = `busqueda_${query.replace(/\s+/g, '_').replace(/[^a-zA-Z0-9_]/g, '')}_${Date.now()}.pdf`;
        doc.save(fileName);
        _toast('PDF generado correctamente', 'success');



    }



    // ═════════════════════════════════════════════════════════════
    //  LECTOR DE CÓDIGO DE BARRAS
    //  Los lectores USB se comportan como un teclado: escriben el código
    //  muy rápido y terminan con Enter. Se distingue de una persona
    //  tecleando por la cadencia, no por el foco, para que funcione sin
    //  tener que pinchar antes en ninguna caja de texto.
    // ═════════════════════════════════════════════════════════════
    const SCAN_KEY = 'av.scanner';
    const SCAN_GAP_MS = 45;    // separación máxima entre teclas del lector
    const SCAN_MIN_LEN = 3;

    let _scanActivo = true;
    let _scanBuffer = '';
    let _scanUltima = 0;

    function _bindLector() {
        try {
            _scanActivo = localStorage.getItem(SCAN_KEY) !== '0';
        } catch { _scanActivo = true; }
        _reflejarScan();

        document.getElementById('av-scan-btn')?.addEventListener('click', () => {
            _scanActivo = !_scanActivo;
            try { localStorage.setItem(SCAN_KEY, _scanActivo ? '1' : '0'); } catch { }
            _reflejarScan();
            _toast(_scanActivo
                ? 'Lector de código de barras activado'
                : 'Lector de código de barras desactivado', 'info');
        });

        document.addEventListener('keydown', _onTeclaLector, true);
    }

    function _reflejarScan() {
        const btn = document.getElementById('av-scan-btn');
        btn?.classList.toggle('active', _scanActivo);
        if (btn) {
            btn.title = _scanActivo
                ? 'Lector de código de barras activo — escanea en cualquier momento'
                : 'Lector de código de barras desactivado';
        }
    }

    function _onTeclaLector(e) {
        if (!_scanActivo) return;

        // Si se está escribiendo en un campo, solo se atiende al buscador
        const enCampo = /^(INPUT|TEXTAREA|SELECT)$/.test(e.target?.tagName || '');
        const enBuscador = e.target?.id === 'av-search-input';
        if (enCampo && !enBuscador) return;

        const ahora = performance.now();

        if (e.key === 'Enter') {
            const codigo = _scanBuffer;
            const rafaga = ahora - _scanUltima < SCAN_GAP_MS * 4;
            _scanBuffer = '';

            if (codigo.length >= SCAN_MIN_LEN && rafaga) {
                e.preventDefault();
                e.stopPropagation();
                _procesarCodigo(codigo, true);
            } else if (enBuscador) {
                // Enter "humano" en el buscador: se intenta como código exacto
                const texto = (e.target.value || '').trim();
                if (texto && _buscarPorCodigo(texto).length) {
                    e.preventDefault();
                    _procesarCodigo(texto, false);
                }
            }
            return;
        }

        if (e.key.length !== 1) return;                    // teclas de control
        if (ahora - _scanUltima > SCAN_GAP_MS) _scanBuffer = '';
        _scanBuffer += e.key;
        _scanUltima = ahora;
    }

    /** Resuelve un código leído y navega hasta su ubicación. */
    async function _procesarCodigo(codigo, esLectura) {
        const hits = _buscarPorCodigo(codigo);

        if (!hits.length) {
            _mostrarScanOverlay(codigo, 'error', 'Código no encontrado en esta sucursal');
            _toast(`Sin resultados para el código ${codigo}`, 'error');
            return;
        }

        const item = hits[0];
        const extra = hits.length > 1 ? ` · ${hits.length} ubicaciones` : '';
        _mostrarScanOverlay(codigo, 'ok',
            `${item.cveProd || ''} — ${item.descrProd || ''}${extra}`);

        if (esLectura) {
            const input = document.getElementById('av-search-input');
            if (input) input.value = codigo;
        }

        await _irAResultado(item, { abrirTarima: true });
    }

    function _mostrarScanOverlay(codigo, estado, detalle) {
        const el = document.getElementById('av-scan-overlay');
        if (!el) return;

        el.className = 'show ' + estado;
        el.innerHTML = `
            <i class="fas ${estado === 'ok' ? 'fa-barcode' : 'fa-triangle-exclamation'}"></i>
            <div class="av-scan-txt">
                <strong>${_escapeHtml(codigo)}</strong>
                <span>${_escapeHtml(detalle || '')}</span>
            </div>`;

        clearTimeout(_scanOverlayTimer);
        _scanOverlayTimer = setTimeout(() => el.classList.remove('show'), 2600);
    }

    let _scanOverlayTimer = null;

    // ═════════════════════════════════════════════════════════════
    //  PUENTE CON LA VISTA 3D (almacen-virtual-3d.js / Three.js)
    // ═════════════════════════════════════════════════════════════

    // Identifica los datos con los que se construyó la escena 3D, para no
    // reconstruirla (ni resetear la cámara) al alternar entre 2D y 3D.
    let _token3D = null;

    /** Pasa los datos ya cargados al motor 3D y refresca sus estadísticas. */
    function _sincronizar3D(forzar = false) {
        if (!window.AV3D || !_plano) return;

        window.AV3D.onRackClick = (rackId) => {
            _seleccionar('rack_' + rackId);
            _abrirInspectorRack(rackId);
        };

        // Clic en una tarima concreta → su detalle, sin pasar por el rack.
        // Los datos ya están en el árbol de inventario: no hace falta pedirlos.
        window.AV3D.onTarimaClick = (ctx) => {
            RackInspector.abrirTarima({
                tarima: ctx.tarima,
                nivel: ctx.nivel,
                columna: ctx.columna,
                rack: ctx.rack,
                almacen: ctx.almacen,
            });
        };

        const token = `${_sucursalId}|${_plano.id_plano || _plano.idPlano || ''}`;
        if (!forzar && token === _token3D) return;
        _token3D = token;

        window.AV3D.build({
            plano: _plano,
            inventario: _inventario,
            sucursalId: _sucursalId,
        });
        _actualizarStats3D();
    }

    /** Resumen numérico que se muestra bajo la leyenda de ocupación. */
    function _actualizarStats3D() {
        const el = document.getElementById('av-3d-stats');
        if (!el) return;

        const sucs = _inventario?.sucursales || [];
        const suc = sucs.find(s => s.id === _sucursalId) || sucs[0];
        const almacenes = suc?.almacenes || [];

        let racks = 0, tarimas = 0, productos = 0;
        almacenes.forEach(alm => {
            const lista = [];
            (alm.pasillos || []).forEach(p => (p.racks || []).forEach(r => lista.push(r)));
            (alm.racks_sin_pasillo || []).forEach(r => lista.push(r));
            racks += lista.length;
            lista.forEach(r => (r.columnas || []).forEach(c => (c.niveles || []).forEach(n => {
                const ts = n.tarimas || [];
                tarimas += ts.length;
                ts.forEach(t => { productos += (t.productos || []).length; });
            })));
        });

        el.innerHTML = `
            <b>${almacenes.length}</b> almacenes ·
            <b>${racks}</b> racks<br>
            <b>${tarimas}</b> tarimas ·
            <b>${productos}</b> partidas`;
    }

    /** Cambia entre el plano 2D de Konva y la escena 3D. */
    function setVista(vista) {
        const es3D = vista === '3d';

        // Sin motor 3D disponible no se puede cambiar
        if (es3D && !window.AV3D) {
            _mostrarFallback3D(true);
            return setVista('2d');
        }

        _vista = es3D ? '3d' : '2d';

        document.getElementById('av-vista-3d')?.classList.toggle('active', es3D);
        document.getElementById('av-vista-2d')?.classList.toggle('active', !es3D);

        document.getElementById('av-3d-container')?.classList.toggle('av-hidden', !es3D);
        document.getElementById('av-konva-container')?.classList.toggle('av-hidden', es3D);

        // Controles exclusivos de cada vista
        document.querySelectorAll('.av-solo-2d')
            .forEach(el => el.classList.toggle('av-off', es3D));
        document.querySelectorAll('.av-solo-3d')
            .forEach(el => el.classList.toggle('av-off', !es3D));

        ['av-3d-legend', 'av-3d-hint']
            .forEach(id => document.getElementById(id)?.classList.toggle('av-off', !es3D));

        if (!es3D) {
            document.getElementById('av-3d-config')?.classList.remove('open');
            document.getElementById('av-3d-tooltip')?.classList.remove('show');
            window.AV3D?.unmount();
            // El stage estuvo oculto: hay que remedirlo antes de encuadrar
            _onResize();
            requestAnimationFrame(() => requestAnimationFrame(zoomFit));
        } else {
            document.getElementById('av-config-panel')?.classList.remove('open');
            // Al entrar en 3D siempre se vuelve a modo "ver"
            if (_modo === 'editar') setModo('ver');

            if (window.AV3D.mount() === false) {
                _mostrarFallback3D(true);
                _toast('No se pudo iniciar WebGL; se muestra el plano 2D.', 'error');
                return setVista('2d');
            }

            if (_plano) _sincronizar3D();
            _mostrarHint3D();
        }
    }

    function _mostrarFallback3D(mostrar) {
        document.getElementById('av-3d-fallback')?.classList.toggle('show', !!mostrar);
        const btn = document.getElementById('av-vista-3d');
        if (btn && mostrar) {
            btn.disabled = true;
            btn.title = 'Vista 3D no disponible en este equipo';
            btn.style.opacity = '0.45';
            btn.style.cursor = 'not-allowed';
        }
    }

    let _hintTimer = null;
    function _mostrarHint3D() {
        const hint = document.getElementById('av-3d-hint');
        if (!hint) return;
        hint.classList.remove('fade');
        clearTimeout(_hintTimer);
        _hintTimer = setTimeout(() => hint.classList.add('fade'), 6000);
    }

    /** Cablea toda la UI propia de la vista 3D. */
    function _bind3D() {
        // Alternancia de vista
        document.getElementById('av-vista-3d')?.addEventListener('click', () => setVista('3d'));
        document.getElementById('av-vista-2d')?.addEventListener('click', () => setVista('2d'));

        // Encuadres de cámara (grupo segmentado del toolbar)
        document.querySelectorAll('.av-cam-btn').forEach(btn => {
            btn.addEventListener('click', () => {
                document.querySelectorAll('.av-cam-btn')
                    .forEach(b => b.classList.toggle('active', b === btn));
                window.AV3D?.setPreset(btn.dataset.preset);
            });
        });

        // Panel de configuración 3D
        const cfgPanel = document.getElementById('av-3d-config');
        document.getElementById('av-3d-config-btn')?.addEventListener('click', (e) => {
            e.stopPropagation();
            cfgPanel?.classList.toggle('open');
        });
        document.addEventListener('click', (e) => {
            if (!cfgPanel?.classList.contains('open')) return;
            if (!cfgPanel.contains(e.target) &&
                !e.target.closest('#av-3d-config-btn')) {
                cfgPanel.classList.remove('open');
            }
        });

        // Etiquetas on/off desde el toolbar
        document.getElementById('av-3d-labels-btn')?.addEventListener('click', (e) => {
            const on = !e.currentTarget.classList.contains('active');
            e.currentTarget.classList.toggle('active', on);
            window.AV3D?.setConfig({ verEtiquetas: on });
        });

        // Controles del panel
        const bindRange = (inputId, labelId, clave, sufijo) => {
            const input = document.getElementById(inputId);
            const label = document.getElementById(labelId);
            if (!input) return;
            const pinta = () => { if (label) label.textContent = `${input.value} ${sufijo}`; };
            input.addEventListener('input', pinta);
            input.addEventListener('change', () => {
                window.AV3D?.setConfig({ [clave]: parseFloat(input.value) });
            });
            pinta();
        };
        bindRange('av-3d-nivelh', 'av-3d-nivelh-val', 'nivelH', 'm');
        bindRange('av-3d-muroh', 'av-3d-muroh-val', 'muroH', 'm');

        const bindCheck = (id, clave) => {
            const el = document.getElementById(id);
            el?.addEventListener('change', () =>
                window.AV3D?.setConfig({ [clave]: el.checked }));
        };
        bindCheck('av-3d-chk-muros', 'verMuros');
        bindCheck('av-3d-chk-zonas', 'verZonas');
        bindCheck('av-3d-chk-tarimas', 'verTarimas');
        bindCheck('av-3d-chk-sombras', 'verSombras');
        bindCheck('av-3d-chk-uloc', 'verUbicaciones');

        // Estado inicial: si el módulo ES ya se ejecutó, arranca en 3D;
        // si no, espera su evento y, si nunca llega, se queda en 2D.
        if (window.AV3D) {
            _reflejarConfig3D();
            setVista('3d');
        } else {
            setVista('2d');
            document.addEventListener('av3d:ready', () => {
                _mostrarFallback3D(false);
                _reflejarConfig3D();
                setVista('3d');
            }, { once: true });

            setTimeout(() => {
                if (!window.AV3D) {
                    _mostrarFallback3D(true);
                    _toast('Vista 3D no disponible; se muestra el plano 2D.', 'info');
                }
            }, 8000);
        }
    }

    // ── El monito: arrastrarlo al plano para bajar al piso ───────
    /**
     * Se arrastra con eventos de puntero y captura, no con HTML5 drag&drop:
     * ese último no da coordenadas fiables durante el arrastre en todos los
     * navegadores y aquí hacen falta en cada frame para mover la previa 3D.
     */
    function _bindMonito() {
        const monito = document.getElementById('av-monito');
        const pista  = document.getElementById('av-monito-pista');
        if (!monito) return;

        let arrastrando = false;

        const pintarPista = (destino, x, y) => {
            if (!pista) return;
            if (!destino) {
                pista.classList.remove('show');
                return;
            }
            const donde = [destino.zona, destino.rack ? 'junto a ' + destino.rack : null]
                .filter(Boolean).join(' · ');
            pista.textContent = donde || 'Bajar aquí';
            pista.style.left = x + 'px';
            pista.style.top  = y + 'px';
            pista.classList.add('show');
        };

        const empezar = (e) => {
            if (_vista !== '3d' || !window.AV3D?.listo) return;
            arrastrando = true;
            monito.classList.add('arrastrando');
            document.body.classList.add('av-arrastrando-monito');
            try { monito.setPointerCapture(e.pointerId); } catch { /* da igual */ }
            e.preventDefault();
        };

        const mover = (e) => {
            if (!arrastrando) return;
            const destino = window.AV3D.preverOperario(e.clientX, e.clientY);
            monito.classList.toggle('fuera', !destino);
            pintarPista(destino, e.clientX + 18, e.clientY + 18);
        };

        const terminar = async (e) => {
            if (!arrastrando) return;
            arrastrando = false;
            monito.classList.remove('arrastrando', 'fuera');
            document.body.classList.remove('av-arrastrando-monito');
            pintarPista(null);
            try { monito.releasePointerCapture(e.pointerId); } catch { /* da igual */ }

            const destino = await window.AV3D.soltarOperario(e.clientX, e.clientY);
            if (!destino) {
                _toast('Suelta el monito dentro de la nave para bajar ahí.', 'info');
                return;
            }

            document.querySelectorAll('.av-cam-btn')
                .forEach(b => b.classList.toggle('active', b.dataset.preset === 'piso'));

            const donde = [destino.zona, destino.rack ? 'junto a ' + destino.rack : null]
                .filter(Boolean).join(' · ');
            _toast(donde ? 'Estás en ' + donde : 'Estás en el piso del almacén', 'success');
        };

        monito.addEventListener('pointerdown', empezar);
        monito.addEventListener('pointermove', mover);
        monito.addEventListener('pointerup', terminar);
        monito.addEventListener('pointercancel', () => {
            arrastrando = false;
            monito.classList.remove('arrastrando', 'fuera');
            document.body.classList.remove('av-arrastrando-monito');
            pintarPista(null);
            window.AV3D?.ocultarOperario();
        });

        // Con teclado no hay arrastre posible: se cae en el encuadre de piso.
        monito.addEventListener('keydown', (e) => {
            if (e.key !== 'Enter' && e.key !== ' ') return;
            e.preventDefault();
            window.AV3D?.setPreset('piso');
            document.querySelectorAll('.av-cam-btn')
                .forEach(b => b.classList.toggle('active', b.dataset.preset === 'piso'));
        });
    }

    /** Vuelca la config persistida del motor 3D en los controles del panel. */
    function _reflejarConfig3D() {
        const cfg = window.AV3D?.getConfig?.();
        if (!cfg) return;

        const set = (id, valor, esCheck) => {
            const el = document.getElementById(id);
            if (!el) return;
            if (esCheck) el.checked = !!valor;
            else el.value = valor;
        };
        set('av-3d-nivelh', cfg.nivelH);
        set('av-3d-muroh', cfg.muroH);
        set('av-3d-chk-muros', cfg.verMuros, true);
        set('av-3d-chk-zonas', cfg.verZonas, true);
        set('av-3d-chk-tarimas', cfg.verTarimas, true);
        set('av-3d-chk-sombras', cfg.verSombras, true);
        set('av-3d-chk-uloc', cfg.verUbicaciones !== false, true);

        const lblN = document.getElementById('av-3d-nivelh-val');
        if (lblN) lblN.textContent = `${cfg.nivelH} m`;
        const lblM = document.getElementById('av-3d-muroh-val');
        if (lblM) lblM.textContent = `${cfg.muroH} m`;

        document.getElementById('av-3d-labels-btn')
            ?.classList.toggle('active', !!cfg.verEtiquetas);
    }

    // ── API pública ──────────────────────────────────────────────
    return {
        init,
        cargarPlano,
        setModo,
        setVista,
        zoomFit,
        guardarPosiciones,
        abrirInspectorRack: _abrirInspectorRack,
        _onSidebarRackClick,
        _toast,
    };

})();

// Autoarranque
document.addEventListener('DOMContentLoaded', () => {
    AlmacenVirtual.init();
    // Si hay una sucursal preseleccionada, cargarla
    const sel = document.getElementById('av-sucursal-sel');
    if (sel && sel.value) {
        AlmacenVirtual.cargarPlano(parseInt(sel.value));
    }
});

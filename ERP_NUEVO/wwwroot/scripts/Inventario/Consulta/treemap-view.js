// ============================================================
// treemap-view.js — Vista Ocupación (ECharts treemap jerárquico)
// ============================================================

let _emptyNodeCounter = 0;

// Mapa inventoryKey/tarimaId -> id de nodo del treemap, para que
// el buscador (search-panel.js) pueda saltar directo a un nodo
// sin tener que recorrer manualmente los 6 niveles.
window._treemapNodeIndex = {};

// Mapa id de nodo -> id de su padre (null si es de primer nivel),
// usado por "Subir un nivel". Se reconstruye cada vez que se
// reconstruye el árbol.
let _parentOfNode = {};

// Id del nodo que está actualmente como raíz de la vista (null = tope,
// mostrando todas las sucursales). Se actualiza al hacer clic para
// entrar, al usar los botones de navegación, o al buscar.
let _currentRootId = null;

// ─────────────────────────────────────────────────────────
// Paleta — se resuelve desde las variables CSS del tema activo
// (incluye modo oscuro) en vez de usar colores fijos.
// ─────────────────────────────────────────────────────────

function _themeVar(name, fallback) {
    const val = getComputedStyle(document.documentElement).getPropertyValue(name).trim();
    return val || fallback;
}

function _getPalette() {
    return {
        textDark: _themeVar('--text-dark', '#0f172a'),
        textLight: _themeVar('--text-light', '#64748b'),
        primary: _themeVar('--primary-blue', '#1e3a5f'),
        accent: _themeVar('--accent-blue', '#3b82f6'),
        success: _themeVar('--success-color', '#059669'),
        warning: _themeVar('--warning-color', '#d97706'),
        danger: _themeVar('--danger-color', '#dc2626'),
        border: _themeVar('--border-light', '#e2e8f0'),
        bgGray: _themeVar('--bg-gray', '#f8fafc')
    };
}

function initializeTreemap() {
    const element = document.getElementById('treemapChart');
    if (!treemapChart) treemapChart = echarts.init(element);

    const palette = _getPalette();
    const treeData = buildCompleteTreemapData(palette);

    const option = {
        title: {
            text: 'Jerarquía del Almacén',
            subtext: 'Clic en un bloque para entrar · clic en la migas de pan ↑ para subir',
            left: 'center', top: 10,
            textStyle: { color: palette.textDark, fontSize: 16, fontWeight: 'bold' },
            subtextStyle: { color: palette.textLight, fontSize: 10 }
        },
        tooltip: {
            formatter: _treemapTooltipFormatter(),
            backgroundColor: palette.bgGray,
            borderColor: palette.accent, borderWidth: 2,
            textStyle: { color: palette.textDark },
            confine: true,
            triggerOn: 'mousemove|click',
            enterable: false,
            hideDelay: 100,
            transitionDuration: 0.2
        },
        series: [{
            id: 'almacenTreemap',
            name: 'Almacén',
            type: 'treemap',
            // Clave para que sea fácil de leer: solo se dibujan 2 niveles
            // a la vez (el actual + sus hijos directos). El resto se agrega
            // dentro hasta que el usuario hace clic para entrar. Sin esto,
            // ECharts intenta dibujar las 7 capas de golpe (sucursal >
            // almacén > pasillo > rack > columna > nivel > tarima >
            // producto), lo que se ve como un mosaico ilegible.
            leafDepth: 2,
            visibleMin: 5,
            nodeClick: 'zoomToNode',
            breadcrumb: {
                show: true, height: 30, bottom: 10,
                formatter: params => params.name.replace(/\s*\[[^\]]+\]/g, ''),
                itemStyle: {
                    color: palette.primary, borderColor: '#fff', borderWidth: 2,
                    shadowColor: 'rgba(0,0,0,0.2)', shadowBlur: 5,
                    textStyle: { color: '#fff', fontWeight: 'bold' }
                }
            },
            label: {
                show: true,
                formatter(params) {
                    const name = params.name.replace(/\s*\[[^\]]+\]/g, '');
                    if (params.data.productData) return name;
                    if (params.data.isEmpty) return `${name}\n📭`;
                    return name;
                },
                fontSize: 9, fontWeight: 'bold', color: '#fff'
            },
            upperLabel: {
                show: true, height: 25, color: '#fff', fontSize: 11, fontWeight: 'bold',
                formatter: params => params.name.replace(/\s*\[[^\]]+\]/g, '')
            },
            itemStyle: { borderColor: '#fff', borderWidth: 2, gapWidth: 2 },
            levels: [
                {},
                { itemStyle: { borderWidth: 6, gapWidth: 6, borderColor: '#333' }, upperLabel: { show: true, height: 40, fontSize: 14, fontWeight: 'bold' } },
                { itemStyle: { borderWidth: 5, gapWidth: 5, borderColor: '#555' }, upperLabel: { show: true, height: 35, fontSize: 13 } },
                { itemStyle: { borderWidth: 4, gapWidth: 4, borderColor: '#777' }, upperLabel: { show: true, height: 30, fontSize: 12 } },
                { itemStyle: { borderWidth: 3, gapWidth: 3, borderColor: '#999' }, upperLabel: { show: true, height: 25, fontSize: 11 } },
                { colorSaturation: [0.35, 0.5], itemStyle: { borderWidth: 1, gapWidth: 1, borderColorSaturation: 0.6 } }
            ],
            data: treeData
        }]
    };

    treemapChart.setOption(option, true);
    _currentRootId = null;
    _updateNavButtons();
    _wireNavButtons();

    treemapChart.off('click');
    treemapChart.on('click', function (params) {
        if (!params.data) return;
        console.log('[TREEMAP CLICK]', {
            name: params.name,
            inventoryKey: params.data.inventoryKey,
            data: params.data
        });

        // nodeClick:'zoomToNode' hace el zoom internamente; aquí solo
        // llevamos la cuenta de en qué nodo quedamos para que el botón
        // "Subir un nivel" sepa a dónde volver.
        if (params.data.children && params.data.children.length && params.data.id) {
            _currentRootId = params.data.id;
            _updateNavButtons();
        }
    });

    // Respaldo: si el usuario navega con el breadcrumb propio de ECharts
    // (en vez de nuestros botones), este evento re-sincroniza cuál es
    // la raíz actual. Si la versión de ECharts no lo emite, no rompe nada.
    treemapChart.off('treemaprootchange');
    treemapChart.on('treemaprootchange', function (params) {
        const nodeId = params && params.nodeData ? params.nodeData.id : undefined;
        _currentRootId = nodeId || null;
        _updateNavButtons();
    });
}

// Si el usuario cambia entre modo claro/oscuro (TemaPresets), reconstruimos
// el treemap para que tome los nuevos colores — ECharts no escucha
// variables CSS por sí solo.
if (!window._treemapThemeObserver) {
    window._treemapThemeObserver = new MutationObserver(muts => {
        const themeChanged = muts.some(m => m.attributeName === 'data-theme');
        if (themeChanged && treemapChart && warehouseData) {
            initializeTreemap();
        }
    });
    window._treemapThemeObserver.observe(document.documentElement, { attributes: true });
}

// ─────────────────────────────────────────────────────────
// Construcción del árbol
// ─────────────────────────────────────────────────────────

function buildCompleteTreemapData(palette) {
    _emptyNodeCounter = 0;
    window._treemapNodeIndex = {};
    _parentOfNode = {};
    const structure = {};

    warehouseData.sucursales
        .filter(s => s.almacenes && s.almacenes.length > 0)
        .forEach(sucursal => {
            const id = `suc-${sucursal.id}`;
            const key = `🏢 ${sucursal.cve} - ${sucursal.descripcion} [ID${sucursal.id}]`;
            structure[key] = { id, name: key, children: [], value: 0 };

            sucursal.almacenes.forEach(almacen => {
                const node = _buildAlmacenNode(almacen, sucursal, palette);
                if (node) { structure[key].children.push(node); structure[key].value += node.value; }
            });

            if (structure[key].value === 0) structure[key].value = 10;
        });

    const treeData = Object.values(structure);
    _indexParents(treeData, null);
    return treeData;
}

// Recorre el árbol ya construido y registra, para cada nodo con id,
// cuál es el id de su padre. Los nodos de primer nivel (sucursales)
// quedan con padre `null`, que representamos como "el tope del mapa".
function _indexParents(nodes, parentId) {
    nodes.forEach(node => {
        if (node.id) _parentOfNode[node.id] = parentId;
        if (node.children && node.children.length) {
            _indexParents(node.children, node.id || parentId);
        }
    });
}

function _buildAlmacenNode(almacen, sucursal, palette) {
    const id = `alm-${almacen.id}`;
    const key = `🏭 ${almacen.cve} - ${almacen.descripcion || 'Sin descripción'} [S${sucursal.id}]`;
    const node = { id, name: key, children: [], value: 0 };
    let has = false;

    (almacen.pasillos || []).forEach(p => {
        const c = _buildPasilloNode(p, almacen, sucursal, palette);
        if (c) { node.children.push(c); node.value += c.value; has = true; }
    });
    (almacen.racks_sin_pasillo || []).forEach(r => {
        const c = _buildRackNode(r, almacen, sucursal, null, palette);
        if (c) { node.children.push(c); node.value += c.value; has = true; }
    });

    if (!has) {
        node.children.push({ name: `📭 Sin racks [e${++_emptyNodeCounter}]`, value: 10, itemStyle: { color: palette.border }, isEmpty: true });
        node.value = 10;
    } else if (node.value === 0) { node.value = 10; }
    return node;
}

function _buildPasilloNode(pasillo, almacen, sucursal, palette) {
    const id = `pas-${almacen.id}-${pasillo.id || pasillo.num_pasillo}`;
    const node = { id, name: `🚶 ${pasillo.cve || 'Pasillo ' + pasillo.num_pasillo} [A${almacen.id}]`, children: [], value: 0 };

    if (!pasillo.racks || pasillo.racks.length === 0) {
        node.children.push({ name: `📭 Sin racks [e${++_emptyNodeCounter}]`, value: 10, itemStyle: { color: palette.border }, isEmpty: true });
        node.value = 10;
        return node;
    }

    pasillo.racks.forEach(r => {
        const c = _buildRackNode(r, almacen, sucursal, pasillo, palette);
        if (c) { node.children.push(c); node.value += c.value; }
    });
    if (node.value === 0) node.value = 10;
    return node;
}

function _buildRackNode(rack, almacen, sucursal, pasillo, palette) {
    const suffix = pasillo ? `P${pasillo.id || pasillo.num_pasillo}` : 'SP';
    const id = `rack-${almacen.id}-${suffix}-${rack.nombre}`;
    const node = { id, name: `📐 ${rack.nombre} [A${almacen.id}-${suffix}]`, children: [], value: 0 };

    if (!rack.columnas || rack.columnas.length === 0) {
        node.children.push({ name: `📭 Sin columnas [e${++_emptyNodeCounter}]`, value: 10, itemStyle: { color: palette.border }, isEmpty: true });
        node.value = 10;
        return node;
    }

    rack.columnas.forEach(col => {
        const c = _buildColumnaNode(col, rack, almacen, sucursal, pasillo, palette);
        if (c) { node.children.push(c); node.value += c.value; }
    });
    if (node.value === 0) node.value = 10;
    return node;
}

function _buildColumnaNode(columna, rack, almacen, sucursal, pasillo, palette) {
    const suffix = pasillo ? `P${pasillo.id || pasillo.num_pasillo}` : 'SP';
    const id = `col-${almacen.id}-${rack.nombre}-${suffix}-${columna.nombre}`;
    const node = { id, name: `📏 ${columna.nombre} [A${almacen.id}-${rack.nombre}-${suffix}]`, children: [], value: 0 };

    if (!columna.niveles || columna.niveles.length === 0) {
        node.children.push({ name: `📭 Sin niveles [e${++_emptyNodeCounter}]`, value: 10, itemStyle: { color: palette.border }, isEmpty: true });
        node.value = 10;
    } else {
        columna.niveles.forEach(n => {
            const c = _buildNivelNode(n, columna, rack, almacen, sucursal, pasillo, palette);
            if (c) { node.children.push(c); node.value += c.value; }
        });
        if (node.value === 0) node.value = 10;
    }
    return node;
}

function _buildNivelNode(nivel, columna, rack, almacen, sucursal, pasillo, palette) {
    const ulocation = nivel.ulocation;
    const inventoryKey = ulocation ? `${almacen.id}::${ulocation}` : null;
    const safeKey = inventoryKey ? inventoryKey.replace(/::/g, '-') : null;
    const id = safeKey ? `niv-${safeKey}` : `niv-${almacen.id}-${rack.nombre}-${columna.nombre}-${nivel.nombre}`;
    if (inventoryKey) window._treemapNodeIndex[inventoryKey] = id;
    const uniqueName = ulocation
        ? `📍 ${nivel.nombre} | ${ulocation} [A${almacen.id}]`
        : `📍 ${nivel.nombre} [A${almacen.id}-${rack.nombre}-${columna.nombre}]`;

    const node = { id, name: uniqueName, children: [], value: 0, ulocation, inventoryKey };
    if (inventoryKey) window._treemapNodeIndex[inventoryKey] = id;

    if (!nivel.tarimas || nivel.tarimas.length === 0) {
        node.children.push({ name: `📭 Sin tarimas [e${++_emptyNodeCounter}]`, value: 10, itemStyle: { color: palette.textLight }, isEmpty: true });
        node.value = 10;
    } else {
        nivel.tarimas.forEach(t => {
            const c = _buildTarimaNode(t, nivel, columna, rack, almacen, sucursal, pasillo, palette);
            if (c) { node.children.push(c); node.value += c.value; }
        });
        if (node.value === 0) node.value = 10;
    }
    return node;
}

function _buildTarimaNode(tarima, nivel, columna, rack, almacen, sucursal, pasillo, palette) {
    const inventoryKey = nivel.ulocation ? `${almacen.id}::${nivel.ulocation}` : null;
    const id = `tar-${tarima.id}`;
    const node = {
        id,
        name: `🎫 ${tarima.codigo} [ID${tarima.id}]`,
        children: [], value: 0,
        tarimaId: tarima.id, tarimaUuid: tarima.uuid,
        ulocation: nivel.ulocation, inventoryKey
    };
    window._treemapNodeIndex[`tarima:${tarima.id}`] = id;

    if (!tarima.productos || tarima.productos.length === 0) {
        node.value = 10; node.itemStyle = { color: palette.textLight }; node.isEmpty = true;
        return node;
    }

    let totalStock = 0;
    let totalStockReal = 0;

    tarima.productos.forEach((prod, idx) => {
        const cantidad = parseFloat(prod.cantidad || 0);
        totalStockReal += cantidad;

        // Normalizar valores extremos usando logaritmo para que un solo
        // producto con stock muy alto no domine visualmente el treemap.
        const valorNormalizado = cantidad > 0 ? Math.log10(cantidad + 1) * 100 : 5;
        totalStock += valorNormalizado;

        node.children.push({
            id: `prod-${tarima.id}-${idx}`,
            name: `📦 ${prod.cve_prod} [T${tarima.id}]`,
            value: valorNormalizado,
            realValue: cantidad,
            productData: {
                cve: prod.cve_prod, descripcion: prod.descr_prod,
                cantidad, unidad: prod.unidad,
                ulocation: nivel.ulocation, tarima: tarima.codigo,
                nivel: nivel.nombre, columna: columna.nombre, rack: rack.nombre,
                almacen: almacen.descripcion || almacen.cve,
                sucursal: sucursal.descripcion || sucursal.cve,
                pasillo: pasillo ? pasillo.cve : 'Sin pasillo'
            },
            itemStyle: { color: _getProductColor(cantidad, palette) }
        });
    });

    node.value = totalStock > 0 ? totalStock : 10;
    node.realValue = totalStockReal;
    node.itemStyle = { color: _getColorByStock(totalStockReal, nivel.capacidad || 1000, palette) };
    return node;
}

// ─────────────────────────────────────────────────────────
// Colores por ocupación — 4 niveles, los mismos que la leyenda
// y los badges (.badge-high/medium/low/empty) para que el color
// signifique siempre lo mismo en toda la página.
// ─────────────────────────────────────────────────────────

function _getProductColor(cantidad, palette) {
    if (cantidad <= 0) return palette.textLight;
    if (cantidad < 50) return palette.danger;
    if (cantidad < 200) return palette.warning;
    return palette.success;
}

function _getColorByStock(stock, capacity, palette) {
    if (stock <= 0) return palette.textLight;
    const pct = (stock / capacity) * 100;
    if (pct >= 70) return palette.success;
    if (pct >= 30) return palette.warning;
    return palette.danger;
}

function _occupancyTier(stock, capacity) {
    if (!stock || stock <= 0) return 'empty';
    const pct = (stock / capacity) * 100;
    if (pct >= 70) return 'high';
    if (pct >= 30) return 'medium';
    return 'low';
}

function _treemapTooltipFormatter() {
    return function (info) {
        const cleanName = info.name.replace(/\s*\[[^\]]+\]/g, '');

        if (info.data.productData) {
            const p = info.data.productData;
            return `<div style="padding:8px;max-width:280px;">
              <b style="font-size:14px;">📦 ${p.cve}</b><br/>
              <b>Descripción:</b> ${p.descripcion}<br/>
              <b>Cantidad:</b> ${p.cantidad.toLocaleString()} ${p.unidad || 'uds'}<br/>
              <b>Ubicación:</b> ${p.ulocation}<br/>
              <b>Almacén:</b> ${p.almacen}<br/>
              <b>Sucursal:</b> ${p.sucursal}</div>`;
        }

        if (info.data.tarimaId) {
            const stockReal = info.data.realValue || info.value;
            return `<div style="padding:8px;">
              <b style="font-size:14px;">🎫 ${cleanName}</b><br/>
              <b>Stock Total:</b> ${Math.round(stockReal).toLocaleString()} uds<br/>
              <b>Productos:</b> ${info.data.children ? info.data.children.length : 0}<br/>
              <b>UUID:</b> ${info.data.tarimaUuid || 'N/A'}</div>`;
        }

        if (info.data.inventoryKey) {
            const data = inventoryData[info.data.inventoryKey];
            const cap = data ? data.capacity : 1000;
            const stockReal = data ? data.stock : info.value;
            return `<div style="padding:8px;max-width:280px;">
              <b style="font-size:14px;">📍 ${info.data.ulocation}</b><br/>
              <b>Stock:</b> ${Math.round(stockReal).toLocaleString()} / ${cap.toLocaleString()} uds<br/>
              <b>Ocupación:</b> ${((stockReal / cap) * 100).toFixed(1)}%<br/>
              <b>Tarimas:</b> ${info.data.children ? info.data.children.length : 0}</div>`;
        }

        return `<div style="padding:8px;">
          <b style="font-size:14px;">${cleanName}</b><br/>
          <b>Total Stock:</b> ${Math.round(info.value).toLocaleString()} unidades<br/>
          <b>Sub-elementos:</b> ${info.data.children ? info.data.children.length : 0}</div>`;
    };
}

// ─────────────────────────────────────────────────────────
// API usada por search-panel.js para saltar a un nodo, y por los
// botones de navegación del toolbar ("Subir un nivel" / "Inicio")
// ─────────────────────────────────────────────────────────

// ─────────────────────────────────────────────────────────
// API usada por search-panel.js para saltar a un nodo
// ─────────────────────────────────────────────────────────

function focusTreemapLocation(inventoryKey) {
    const targetId = window._treemapNodeIndex[inventoryKey];
    if (!targetId || !treemapChart) return false;

    // Construir la cadena completa de ancestros: [suc, alm, pas, rack, col, niv]
    const ancestorChain = _buildAncestorChain(targetId);
    if (!ancestorChain.length) return false;

    console.log('[FOCUS] Cadena de navegación:', ancestorChain);

    // Resetear al tope primero
    initializeTreemap();

    // Navegar nivel por nivel con delay entre cada salto
    // ECharts necesita procesar cada zoom antes de aceptar el siguiente
    _navigateChainStep(ancestorChain, 0, 250);
    return true;
}

/**
 * Construye el array de IDs desde la raíz hasta el nodo destino (inclusive).
 * Usa _parentOfNode que ya está poblado por _indexParents().
 */
function _buildAncestorChain(nodeId) {
    const chain = [];
    let current = nodeId;

    // Subir hasta la raíz (padre === null significa sucursal de primer nivel)
    while (current != null) {
        chain.unshift(current);
        current = _parentOfNode[current]; // null cuando llegamos al tope
    }

    // chain[0] = sucursal, chain[N] = nodo destino
    // Con leafDepth:2 queremos navegar hasta el PADRE del destino,
    // así el destino queda visible como hijo directo.
    // Si solo hay 1 elemento (el nodo ya es de primer nivel), lo dejamos.
    if (chain.length > 1) chain.pop(); // quitar el destino, nos quedamos en su padre

    return chain;
}

/**
 * Ejecuta los zooms uno por uno, esperando `stepDelay` ms entre cada uno.
 * ECharts procesa cada dispatchAction de forma síncrona pero el re-render
 * es asíncrono, por eso hay que esperar antes del siguiente salto.
 */
function _navigateChainStep(chain, index, stepDelay) {
    if (index >= chain.length) {
        // Llegamos al destino — actualizar estado de botones
        _currentRootId = chain[chain.length - 1];
        _updateNavButtons();
        console.log('[FOCUS] Navegación completa en nodo:', _currentRootId);
        return;
    }

    const nodeId = chain[index];

    requestAnimationFrame(() => {
        treemapChart.dispatchAction({
            type: 'treemapZoomToNode',
            targetNodeId: nodeId,
            seriesId: 'almacenTreemap'
        });

        setTimeout(() => {
            _navigateChainStep(chain, index + 1, stepDelay);
        }, stepDelay);
    });
}

function treemapGoUpOneLevel() {
    if (!treemapChart || _currentRootId == null) return;
    const parentId = _parentOfNode[_currentRootId];
    if (parentId) {
        treemapChart.dispatchAction({ type: 'treemapZoomToNode', targetNodeId: parentId, seriesId: 'almacenTreemap' });
        _currentRootId = parentId;
        _updateNavButtons();
    } else {
        // El padre es el tope del mapa (todas las sucursales), que no es
        // un nodo con id propio — para ese caso hacemos un reset completo.
        resetTreemapView();
    }
}

function resetTreemapView() {
    if (!treemapChart || !warehouseData) return;
    initializeTreemap();
}

function _updateNavButtons() {
    const upBtn = document.getElementById('treemapGoUp');
    const homeBtn = document.getElementById('treemapGoHome');
    if (upBtn) upBtn.disabled = _currentRootId == null;
    if (homeBtn) homeBtn.disabled = _currentRootId == null;
}

function _wireNavButtons() {
    if (window._treemapNavWired) return;
    window._treemapNavWired = true;

    const upBtn = document.getElementById('treemapGoUp');
    const homeBtn = document.getElementById('treemapGoHome');
    if (upBtn) upBtn.addEventListener('click', treemapGoUpOneLevel);
    if (homeBtn) homeBtn.addEventListener('click', resetTreemapView);
}
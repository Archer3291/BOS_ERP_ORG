// ============================================================
// data.js — Carga y procesamiento de datos del almacén
// ============================================================

async function loadWarehouseData() {
    try {
        setLoadingStatus('Conectando con el servidor...');

        const response = await fetch('/Consulta/GetAllData', {
            method: 'POST',
            headers: { 'Content-Type': 'application/x-www-form-urlencoded' }
        });

        if (!response.ok) throw new Error(`HTTP ${response.status}`);

        setLoadingStatus('Procesando datos...');
        const result = await response.json();
        const jsonStr = result[0]['jsonb_pretty'];
        warehouseData = JSON.parse(jsonStr);

        setLoadingStatus('Construyendo inventario...');
        buildInventoryIndex();

        setLoadingStatus('Inicializando vista...');
        document.getElementById('loadingOverlay').style.display = 'none';
        document.getElementById('mainContainer').style.display = 'block';

        renderStats();
        renderView();

        if (typeof setupSearch === 'function') setupSearch();

        console.log('✅ Vista Rack inicializada');
        console.log('📦 Ubicaciones:', Object.keys(inventoryData).length);
    } catch (err) {
        console.error('Error cargando datos:', err);
        document.getElementById('loadingStatus').textContent =
            '❌ Error al cargar: ' + err.message;
    }
}

function setLoadingStatus(msg) {
    const el = document.getElementById('loadingStatus');
    if (el) el.textContent = msg;
}

// ─────────────────────────────────────────────────────────
// Construcción del índice plano inventoryData
// ─────────────────────────────────────────────────────────

function buildInventoryIndex() {
    inventoryData = {};

    warehouseData.sucursales.forEach(suc => {
        (suc.almacenes || []).forEach(alm => {
            (alm.pasillos || []).forEach(pas => {
                (pas.racks || []).forEach(rack => processRackIntoIndex(rack, alm, suc, pas));
            });
            (alm.racks_sin_pasillo || []).forEach(rack => processRackIntoIndex(rack, alm, suc, null));
        });
    });
}

function processRackIntoIndex(rack, alm, suc, pasillo) {
    (rack.columnas || []).forEach(col => {
        (col.niveles || []).forEach(niv => {
            if (!niv.ulocation) return;

            let stock = 0;
            const productos = [];

            (niv.tarimas || []).forEach(t => {
                (t.productos || []).forEach(p => {
                    const qty = parseFloat(p.cantidad || 0);
                    stock += qty;
                    productos.push({
                        cve: p.cve_prod,
                        descripcion: p.descr_prod,
                        cantidad: qty,
                        unidad: p.unidad,
                        linea: p.lin_prod,
                        grupo: p.gpo,
                        tipo: p.tp,
                        cbr: p.cbr,
                        provPpal: p.cve_prov_ppal,
                        provSec: p.cve_prov_sec,
                        udm: p.udm,
                        tarimaId: t.id_tarima,
                        tarimaCod: t.codigo,
                        tarimaFecha: t.fecha
                    });
                });
            });

            const cap = parseFloat(rack.capacidad || 1000);
            const key = `${alm.id}::${niv.ulocation}`;

            inventoryData[key] = {
                ulocation: niv.ulocation,
                key,
                stock,
                capacity: cap,
                productos,
                almacen: alm.descripcion || alm.cve,
                almacenId: alm.id,
                almacenCve: alm.cve,
                almacenTipo: alm.tipo,
                sucursal: suc.descripcion || suc.cve,
                sucursalId: suc.id,
                sucursalCve: suc.cve,
                rack: rack.nombre,
                rackTipo: rack.tipo,
                columna: col.nombre,
                nivel: niv.nombre,
                pasillo: pasillo ? pasillo.cve : 'Sin pasillo',
                lastUpdate: new Date().toISOString().slice(0, 16).replace('T', ' ')
            };
        });
    });
}

// ─────────────────────────────────────────────────────────
// Helpers: tier y porcentaje de ocupación
// ─────────────────────────────────────────────────────────

function occupancyTier(stock, capacity) {
    if (!stock || stock <= 0) return 'empty';
    const pct = (stock / capacity) * 100;
    if (pct >= 70) return 'high';
    if (pct >= 30) return 'medium';
    return 'low';
}

function occupancyPct(stock, capacity) {
    if (!capacity) return 0;
    return Math.min(100, Math.round((stock / capacity) * 100));
}

// ─────────────────────────────────────────────────────────
// Estadísticas del encabezado
// ─────────────────────────────────────────────────────────

function renderStats() {
    const locs = Object.values(inventoryData);
    const sucCount = new Set(locs.map(l => l.sucursalId)).size;
    const almCount = new Set(locs.map(l => l.almacenId)).size;
    const skuSet = new Set();
    let capTotal = 0;
    let stkTotal = 0;

    locs.forEach(l => {
        l.productos.forEach(p => skuSet.add(p.cve));
        capTotal += l.capacity;
        stkTotal += l.stock;
    });

    const ocp = capTotal > 0 ? ((stkTotal / capTotal) * 100).toFixed(1) : '0.0';

    setStatVal('totalSucursales', sucCount);
    setStatVal('totalAlmacenes', almCount);
    setStatVal('totalUbicaciones', locs.length);
    setStatVal('totalProductos', skuSet.size);
    setStatVal('totalOcupacionPromedio', ocp + '%');
}

function setStatVal(id, val) {
    const el = document.getElementById(id);
    if (el) el.textContent = val;
}
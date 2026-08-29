// ============================================================
// map-view.js — Vista Plano (ECharts geo map por almacén)
// ============================================================

// ─────────────────────────────────────────────────────────
// Selector de sucursal
// ─────────────────────────────────────────────────────────

function initializeSucursalSelector() {
    const select = document.getElementById('sucursalSelect');
    select.innerHTML = '<option value="">Seleccione una sucursal...</option>';

    warehouseData.sucursales
        .filter(s => s.almacenes && s.almacenes.length > 0)
        .forEach(sucursal => {
            const option       = document.createElement('option');
            option.value       = sucursal.id;
            option.textContent = `${sucursal.cve} - ${sucursal.descripcion}`;
            select.appendChild(option);
        });
}

function changeSucursal() {
    const select     = document.getElementById('sucursalSelect');
    const sucursalId = parseInt(select.value);

    if (!sucursalId) {
        currentSucursal  = null;
        currentWarehouse = null;
        updateWarehouseGrid([]);
        return;
    }

    currentSucursal = warehouseData.sucursales.find(s => s.id === sucursalId);
    updateWarehouseGrid(currentSucursal.almacenes || []);
}

function updateWarehouseGrid(almacenes, autoSelectId = null) {
    const grid = document.getElementById('warehouseGrid');

    if (almacenes.length === 0) {
        grid.innerHTML = '<div class="empty-state"><p>No hay almacenes disponibles</p></div>';
        if (mainChart) mainChart.clear();
        return;
    }

    grid.innerHTML = '';
    almacenes.forEach(almacen => {
        const card     = document.createElement('div');
        card.className = 'warehouse-card';
        card.onclick   = () => selectWarehouse(almacen.id);
        card.innerHTML = `
            <div class="warehouse-name">${almacen.cve}</div>
            <div class="warehouse-type">${almacen.tipo || 'General'}</div>
        `;
        grid.appendChild(card);
    });

    const targetId = autoSelectId || almacenes[0].id;
    selectWarehouse(targetId);
}

function selectWarehouse(almacenId) {
    currentWarehouse = currentSucursal.almacenes.find(a => a.id === almacenId);

    const cards      = document.querySelectorAll('.warehouse-card');
    cards.forEach(card => card.classList.remove('active'));

    const almacenIndex = currentSucursal.almacenes.findIndex(a => a.id === almacenId);
    if (almacenIndex >= 0 && cards[almacenIndex]) {
        cards[almacenIndex].classList.add('active');
    }

    updateChartForWarehouse();
}

// ─────────────────────────────────────────────────────────
// Renderizado del mapa geo
// ─────────────────────────────────────────────────────────

function updateChartForWarehouse() {
    if (!currentWarehouse) return;

    const element = document.getElementById('chart');
    if (!mainChart) {
        mainChart = echarts.init(element);
    }

    const warehouseLocations = Object.keys(inventoryData).filter(
        loc => inventoryData[loc].almacenId === currentWarehouse.id
    );

    const SPACING = {
        betweenRacks:    20,
        betweenColumnas:  6,
        betweenNiveles:   4,
        rackWidth:        5,
        rackHeight:       3
    };

    let xOffset    = 0;
    let yOffset    = 0;
    let maxRowHeight = 0;
    const maxPerRow  = 10;
    const features   = [];

    warehouseLocations.forEach((loc, idx) => {
        const data = inventoryData[loc];

        if (idx > 0 && idx % maxPerRow === 0) {
            xOffset      = 0;
            yOffset     += maxRowHeight + SPACING.betweenRacks;
            maxRowHeight  = 0;
        }

        const x = xOffset;
        const y = yOffset;

        features.push({
            type: 'Feature',
            properties: { name: loc },
            geometry: {
                type: 'Polygon',
                coordinates: [[
                    [x, y],
                    [x + SPACING.rackWidth, y],
                    [x + SPACING.rackWidth, y + SPACING.rackHeight],
                    [x, y + SPACING.rackHeight],
                    [x, y]
                ]]
            }
        });

        data.coords   = [x + SPACING.rackWidth / 2, y + SPACING.rackHeight / 2];
        xOffset      += SPACING.rackWidth + SPACING.betweenColumnas;
        maxRowHeight  = Math.max(maxRowHeight, SPACING.rackHeight);
    });

    const geoJSON = { type: 'FeatureCollection', features };
    echarts.registerMap('warehouse', geoJSON);

    const seriesData = features.map(f => {
        const name = f.properties.name;
        const d    = inventoryData[name];
        return { name, value: d.stock, ...d };
    });

    const series = _buildMapSeries(seriesData);

    const option = {
        tooltip: {
            formatter: _mapTooltipFormatter(),
            backgroundColor: 'rgba(255,255,255,0.95)',
            borderColor: '#667eea',
            borderWidth: 2,
            textStyle: { color: '#2c3e50' }
        },
        geo: {
            map: 'warehouse',
            roam: true,
            scaleLimit: { min: 0.5, max: 10 },
            label: {
                show: true,
                color: '#2c3e50',
                fontSize: 10,
                fontWeight: 'bold',
                formatter(params) {
                    const d = inventoryData[params.name];
                    return d ? (d.ulocation || params.name) : params.name;
                }
            },
            itemStyle: {
                borderColor: '#34495e',
                borderWidth: 2,
                shadowColor: 'rgba(0,0,0,0.2)',
                shadowBlur: 5
            },
            emphasis: {
                label: {
                    show: true,
                    color: '#fff',
                    fontSize: 12,
                    backgroundColor: 'rgba(0,0,0,0.7)',
                    padding: 5,
                    borderRadius: 4
                },
                itemStyle: {
                    areaColor: '#667eea',
                    borderColor: '#5568d3',
                    borderWidth: 3,
                    shadowColor: 'rgba(102,126,234,0.6)',
                    shadowBlur: 15
                }
            }
        },
        series,
        visualMap: {
            min: 0, max: 500,
            calculable: true,
            orient: 'vertical',
            left: 20, bottom: 20,
            inRange: { color: ['#95a5a6', '#e67e22', '#f39c12', '#2ecc71'] },
            text: ['Alto', 'Vacío'],
            textStyle: { color: '#2c3e50', fontWeight: 'bold' }
        }
    };

    mainChart.setOption(option, true);
    mainChart.off('click');
    mainChart.on('click', function (params) {
        if (params.componentSubType === 'map') selectLocation(params.name);
    });
}

// ─────────────────────────────────────────────────────────
// Helpers privados
// ─────────────────────────────────────────────────────────

function _buildMapSeries(seriesData) {
    const series = [{
        type: 'map',
        map: 'warehouse',
        geoIndex: 0,
        data: seriesData,
        selectedMode: 'single'
    }];

    if (currentRoute.length === 0) return series;

    const fullRoute   = [START_POINT, ...currentRoute];
    const routeCoords = fullRoute.map(r => r.coords);

    series.push({
        type: 'lines',
        coordinateSystem: 'geo',
        zlevel: 2,
        effect: { show: true, period: 3, trailLength: 0.5, color: '#667eea', symbolSize: 10 },
        lineStyle: { color: '#667eea', width: 4, curveness: 0.2, opacity: 0.8 },
        data: routeCoords.slice(0, -1).map((coord, idx) => ({
            coords: [coord, routeCoords[idx + 1]]
        }))
    });

    series.push({
        type: 'scatter',
        coordinateSystem: 'geo',
        zlevel: 3,
        symbolSize: 25,
        label: {
            show: true,
            formatter: params => params.dataIndex + 1,
            position: 'inside',
            color: '#fff',
            fontSize: 12,
            fontWeight: 'bold'
        },
        itemStyle: { color: '#667eea', borderColor: '#fff', borderWidth: 3 },
        data: currentRoute.map(stop => ({ name: stop.name, value: stop.coords }))
    });

    series.push({
        type: 'scatter',
        coordinateSystem: 'geo',
        zlevel: 4,
        symbolSize: 35,
        symbol: 'pin',
        itemStyle: { color: '#2ecc71', borderColor: '#fff', borderWidth: 2 },
        label: { show: true, formatter: '🏁', position: 'inside', fontSize: 16 },
        data: [{ name: 'Entrada', value: START_POINT.coords }]
    });

    return series;
}

function _mapTooltipFormatter() {
    return function (info) {
        if (info.name && inventoryData[info.name]) {
            const d          = inventoryData[info.name];
            const stockReal  = d.stock || 0;
            const capacity   = d.capacity || 1000;
            const percentage = ((stockReal / capacity) * 100).toFixed(1);
            return `
                <div style="padding:8px;max-width:280px;">
                  <b style="font-size:14px;color:#667eea;">📍 ${d.ulocation}</b><br/>
                  <b>Almacén:</b> ${d.almacen}<br/>
                  <b>Sucursal:</b> ${d.sucursal}<br/>
                  <b>Stock:</b> ${Math.round(stockReal)} / ${capacity} uds<br/>
                  <b>Ocupación:</b> ${percentage}%<br/>
                  <b>Productos:</b> ${d.productCount || 0}
                </div>`;
        }
        const totalItems = info.data && info.data.children ? info.data.children.length : 0;
        return `
            <div style="padding:8px;">
              <b style="font-size:14px;">${info.name}</b><br/>
              <b>Stock Total:</b> ${Math.round(info.value)} uds<br/>
              <b>Sub-elementos:</b> ${totalItems}
            </div>`;
    };
}

// ─────────────────────────────────────────────────────────
// Sincronización de contexto (compartido con search.js)
// ─────────────────────────────────────────────────────────

function autoSelectLocationContext(locationKey) {
    const data = inventoryData[locationKey];
    if (!data) return false;

    const sucursalSelect = document.getElementById('sucursalSelect');
    if (sucursalSelect.value != data.sucursalId) {
        sucursalSelect.value = data.sucursalId;
        currentSucursal = warehouseData.sucursales.find(s => s.id === data.sucursalId);
        if (!currentSucursal) return false;

        updateWarehouseGrid(currentSucursal.almacenes || [], data.almacenId);

        setTimeout(() => {
            if (mainChart) {
                mainChart.dispatchAction({ type: 'highlight', name: data.ulocation || locationKey });
            }
        }, 500);
    } else {
        if (!currentWarehouse || currentWarehouse.id !== data.almacenId) {
            selectWarehouse(data.almacenId);
        }
    }

    return true;
}

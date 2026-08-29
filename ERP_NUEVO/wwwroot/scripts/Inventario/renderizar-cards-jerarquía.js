function showSucursalesView() {
    if (!inventoryData || !inventoryData.sucursales) return;

    const content = document.getElementById('contentDisplay');
    const title = document.getElementById('contentTitle');
    const itemCount = document.getElementById('itemCount');

    title.textContent = 'Sucursales Disponibles';
    itemCount.textContent = `${inventoryData.sucursales.length} sucursales`;

    content.innerHTML = `
                            <div class="visual-grid">
                                ${inventoryData.sucursales.map(sucursal => `
                                    <div class="item-card" onclick="selectSucursalCard('${sucursal.id}')">
                                        <div class="item-header">
                                            <div class="item-icon" style="background: #f56565;">
                                                <i class="fas fa-building"></i>
                                            </div>
                                            <div>
                                                <div class="item-title">${sucursal.descripcion}</div>
                                                <div class="item-subtitle">${sucursal.cve} - ${sucursal.tipo}</div>
                                            </div>
                                        </div>
                                        <div class="item-details">
                                            <div class="item-detail">
                                                <span class="detail-label">Almacenes</span>
                                                <span class="detail-value">${sucursal.almacenes?.length || 0}</span>
                                            </div>
                                            <div class="item-detail">
                                                <span class="detail-label">ID</span>
                                                <span class="detail-value">${sucursal.id}</span>
                                            </div>
                                        </div>
                                    </div>
                                `).join('')}
                            </div>
                        `;
}

function showAlmacenesView() {
    if (!currentSelection.sucursal || !currentSelection.sucursal.almacenes) return;

    const content = document.getElementById('contentDisplay');
    const title = document.getElementById('contentTitle');
    const itemCount = document.getElementById('itemCount');

    title.textContent = `Almacenes - ${currentSelection.sucursal.descripcion}`;
    itemCount.textContent = `${currentSelection.sucursal.almacenes.length} almacenes`;

    content.innerHTML = `
                            <div class="visual-grid">
                                ${currentSelection.sucursal.almacenes.map(almacen => `
                                    <div class="item-card" onclick="selectAlmacenCard('${almacen.id}')">
                                        <div class="item-header">
                                            <div class="item-icon" style="background: #ed8936;">
                                                <i class="fas fa-warehouse"></i>
                                            </div>
                                            <div>
                                                <div class="item-title">${almacen.descripcion}</div>
                                                <div class="item-subtitle">${almacen.cve} - ${almacen.tipo}</div>
                                            </div>
                                        </div>
                                        <div class="item-details">
                                            <div class="item-detail">
                                                <span class="detail-label">Pasillos</span>
                                                <span class="detail-value">${almacen.pasillos?.length || 0}</span>
                                            </div>
                                            <div class="item-detail">
                                                <span class="detail-label">Racks Directos</span>
                                                <span class="detail-value">${almacen.racks_sin_pasillo?.length || 0}</span>
                                            </div>
                                            <div class="item-detail">
                                                <span class="detail-label">ID</span>
                                                <span class="detail-value">${almacen.id}</span>
                                            </div>
                                        </div>
                                    </div>
                                `).join('')}
                            </div>
                        `;
}

function showPasillosView() {
    if (!currentSelection.almacen) return;

    const content = document.getElementById('contentDisplay');
    const title = document.getElementById('contentTitle');
    const itemCount = document.getElementById('itemCount');

    const pasillos = currentSelection.almacen.pasillos || [];
    const racksSinPasillo = currentSelection.almacen.racks_sin_pasillo || [];
    const totalItems = pasillos.length + racksSinPasillo.length;

    title.textContent = `Estructura - ${currentSelection.almacen.descripcion}`;
    itemCount.textContent = `${pasillos.length} pasillos, ${racksSinPasillo.length} racks directos`;

    let cards = [];

    // Add pasillos
    cards = cards.concat(pasillos.map(pasillo => `
                            <div class="item-card" onclick="selectPasilloCard('${pasillo.id}')">
                                <div class="item-header">
                                    <div class="item-icon" style="background: #38a169;">
                                        <i class="fas fa-road"></i>
                                    </div>
                                    <div>
                                        <div class="item-title">Pasillo ${pasillo.num_pasillo}</div>
                                        <div class="item-subtitle">${pasillo.cve}</div>
                                    </div>
                                </div>
                                <div class="item-details">
                                    <div class="item-detail">
                                        <span class="detail-label">Racks</span>
                                        <span class="detail-value">${pasillo.racks?.length || 0}</span>
                                    </div>
                                    <div class="item-detail">
                                        <span class="detail-label">ID</span>
                                        <span class="detail-value">${pasillo.id}</span>
                                    </div>
                                </div>
                            </div>
                        `));

    // Add direct racks
    cards = cards.concat(racksSinPasillo.map(rack => `
                            <div class="item-card" onclick="selectDirectRackCard('${rack.id}')">
                                <div class="item-header">
                                    <div class="item-icon" style="background: #4299e1;">
                                        <i class="fas fa-layer-group"></i>
                                    </div>
                                    <div>
                                        <div class="item-title">Rack Directo</div>
                                        <div class="item-subtitle">${rack.nombre} - #${rack.num_rack}</div>
                                    </div>
                                </div>
                                <div class="item-details">
                                    <div class="item-detail">
                                        <span class="detail-label">Tipo</span>
                                        <span class="detail-value">${rack.tipo}</span>
                                    </div>
                                    <div class="item-detail">
                                        <span class="detail-label">Lado</span>
                                        <span class="detail-value">${rack.lado}</span>
                                    </div>
                                    <div class="item-detail">
                                        <span class="detail-label">Columnas</span>
                                        <span class="detail-value">${rack.columnas?.length || 0}</span>
                                    </div>
                                </div>
                            </div>
                        `));

    content.innerHTML = `<div class="visual-grid">${cards.join('')}</div>`;
}

function showRacksView() {
    if (!currentSelection.pasillo || !currentSelection.pasillo.racks) return;

    const content = document.getElementById('contentDisplay');
    const title = document.getElementById('contentTitle');
    const itemCount = document.getElementById('itemCount');

    title.textContent = `Racks - Pasillo ${currentSelection.pasillo.num_pasillo}`;
    itemCount.textContent = `${currentSelection.pasillo.racks.length} racks`;

    content.innerHTML = `
                            <div class="visual-grid">
                                ${currentSelection.pasillo.racks.map(rack => `
                                    <div class="item-card" onclick="selectRackCard('${rack.id}')">
                                        <div class="item-header">
                                            <div class="item-icon" style="background: #4299e1;">
                                                <i class="fas fa-layer-group"></i>
                                            </div>
                                            <div>
                                                <div class="item-title">${rack.nombre}</div>
                                                <div class="item-subtitle">#${rack.num_rack} - ${rack.tipo}</div>
                                            </div>
                                        </div>
                                        <div class="item-details">
                                            <div class="item-detail">
                                                <span class="detail-label">Lado</span>
                                                <span class="detail-value">${rack.lado}</span>
                                            </div>
                                            <div class="item-detail">
                                                <span class="detail-label">Columnas</span>
                                                <span class="detail-value">${rack.columnas?.length || 0}</span>
                                            </div>
                                            <div class="item-detail">
                                                <span class="detail-label">ID</span>
                                                <span class="detail-value">${rack.id}</span>
                                            </div>
                                        </div>
                                    </div>
                                `).join('')}
                            </div>
                        `;
}

function showRackDetailsView(rack) {
    const content = document.getElementById('contentDisplay');
    const title = document.getElementById('contentTitle');
    const itemCount = document.getElementById('itemCount');

    title.textContent = `Detalle del Rack - ${rack.nombre}`;

    let totalTarimas = 0;
    let totalProductos = 0;

    if (rack.columnas) {
        rack.columnas.forEach(columna => {
            if (columna.niveles) {
                columna.niveles.forEach(nivel => {
                    if (nivel.tarimas) {
                        totalTarimas += nivel.tarimas.length;
                        nivel.tarimas.forEach(tarima => {
                            totalProductos += tarima.productos?.length || 0;
                        });
                    }
                });
            }
        });
    }

    itemCount.textContent = `${rack.columnas?.length || 0} columnas, ${totalTarimas} tarimas, ${totalProductos} productos`;

    let cards = [];

    if (rack.columnas && rack.columnas.length > 0) {
        cards = rack.columnas.map(columna => {
            let columnaTarimas = 0;
            let columnaProductos = 0;

            if (columna.niveles) {
                columna.niveles.forEach(nivel => {
                    columnaTarimas += nivel.tarimas?.length || 0;
                    if (nivel.tarimas) {
                        nivel.tarimas.forEach(tarima => {
                            columnaProductos += tarima.productos?.length || 0;
                        });
                    }
                });
            }

            return `
                                    <div class="item-card" onclick="showColumnaDetail('${columna.id}')">
                                        <div class="item-header">
                                            <div class="item-icon" style="background: #805ad5;">
                                                <i class="fas fa-columns"></i>
                                            </div>
                                            <div>
                                                <div class="item-title">Columna ${columna.nombre}</div>
                                                <div class="item-subtitle">#${columna.num_col}</div>
                                            </div>
                                        </div>
                                        <div class="item-details">
                                            <div class="item-detail">
                                                <span class="detail-label">Niveles</span>
                                                <span class="detail-value">${columna.niveles?.length || 0}</span>
                                            </div>
                                            <div class="item-detail">
                                                <span class="detail-label">Tarimas</span>
                                                <span class="detail-value">${columnaTarimas}</span>
                                            </div>
                                            <div class="item-detail">
                                                <span class="detail-label">Productos</span>
                                                <span class="detail-value">${columnaProductos}</span>
                                            </div>
                                        </div>
                                    </div>
                                `;
        });
    } else {
        cards = [`
                                <div class="empty-state">
                                    <div class="empty-icon">
                                        <i class="fas fa-inbox"></i>
                                    </div>
                                    <div class="empty-title">Sin columnas</div>
                                    <div class="empty-message">Este rack no tiene columnas configuradas</div>
                                </div>
                            `];
    }

    content.innerHTML = `<div class="visual-grid">${cards.join('')}</div>`;
}

// showColumnaDetail con diseño de inventario
function showColumnaDetail(columnaId) {
    const rack = currentSelection.rack;
    if (!rack || !rack.columnas) return;

    const columna = rack.columnas.find(c => c.id == columnaId);
    if (!columna) return;

    // Inicializar Konva
    initKonva();

    // Actualizar título e itemCount igual que antes
    const title = document.getElementById('contentTitle');
    const itemCount = document.getElementById('itemCount');
    title.textContent = `Inventario Columna - ${columna.nombre}`;

    let totalTarimas = 0;
    let totalProductos = 0;

    if (columna.niveles) {
        columna.niveles.forEach(nivel => {
            totalTarimas += nivel.tarimas?.length || 0;
            if (nivel.tarimas) {
                nivel.tarimas.forEach(tarima => {
                    totalProductos += tarima.productos?.length || 0;
                });
            }
        });
    }

    itemCount.textContent = `${columna.niveles?.length || 0} niveles • ${totalTarimas} tarimas • ${totalProductos} SKUs`;

    // Dibujar con Konva - Estilo inventario
    konvaLayer.destroyChildren();

    // Fondo tipo almacén
    const bgPattern = new Konva.Rect({
        x: 0,
        y: 0,
        width: stage.width(),
        height: stage.height(),
        fill: '#f8f9fa'
    });
    konvaLayer.add(bgPattern);

    // Líneas de cuadrícula sutil para simular piso de almacén
    for (let i = 0; i < stage.width(); i += 40) {
        const gridLine = new Konva.Line({
            points: [i, 0, i, stage.height()],
            stroke: 'rgba(0, 0, 0, 0.02)',
            strokeWidth: 1
        });
        konvaLayer.add(gridLine);
    }

    // Header tipo etiqueta de inventario
    const headerBg = new Konva.Rect({
        x: 0,
        y: 0,
        width: stage.width(),
        height: 80,
        fill: '#2c3e50',
        stroke: '#34495e',
        strokeWidth: 2
    });
    konvaLayer.add(headerBg);

    // Líneas decorativas tipo código de barras en header
    for (let i = 0; i < 8; i++) {
        const barLine = new Konva.Line({
            points: [20 + i * 4, 10, 20 + i * 4, 25],
            stroke: '#ecf0f1',
            strokeWidth: i % 2 === 0 ? 2 : 1
        });
        konvaLayer.add(barLine);
    }

    // ID de columna tipo código
    const codigoColumna = new Konva.Text({
        x: 70,
        y: 15,
        text: `COL-${columna.id.toString().padStart(4, '0')}`,
        fontSize: 16,
        fontStyle: 'bold',
        fill: '#ecf0f1',
        fontFamily: 'monospace'
    });
    konvaLayer.add(codigoColumna);

    // Título principal
    const titleText = new Konva.Text({
        x: 70,
        y: 35,
        text: `COLUMNA ${columna.nombre}`,
        fontSize: 20,
        fontStyle: 'bold',
        fill: '#ffffff',
        fontFamily: 'system-ui, -apple-system, sans-serif'
    });
    konvaLayer.add(titleText);

    const subtitleText = new Konva.Text({
        x: 70,
        y: 55,
        text: `RACK: ${rack.nombre}`,
        fontSize: 12,
        fill: '#bdc3c7',
        fontFamily: 'system-ui, -apple-system, sans-serif'
    });
    konvaLayer.add(subtitleText);

    // Fecha y hora actual (simulando timestamp de inventario)
    const now = new Date();
    const timestamp = new Konva.Text({
        x: stage.width() - 200,
        y: 15,
        text: `INVENTARIO: ${now.toLocaleDateString()}`,
        fontSize: 11,
        fill: '#bdc3c7',
        fontFamily: 'monospace'
    });
    konvaLayer.add(timestamp);

    const timeText = new Konva.Text({
        x: stage.width() - 200,
        y: 30,
        text: `HORA: ${now.toLocaleTimeString()}`,
        fontSize: 11,
        fill: '#bdc3c7',
        fontFamily: 'monospace'
    });
    konvaLayer.add(timeText);

    // Panel de métricas tipo dashboard de almacén
    const metricsY = 100;
    const metricWidth = 160;
    const metricHeight = 90;
    const metricSpacing = 180;

    const metrics = [
        { label: 'NIVELES', value: columna.niveles?.length || 0, unit: 'SHELVES', color: '#3498db', icon: '▢' },
        { label: 'TARIMAS', value: totalTarimas, unit: 'PALLETS', color: '#e67e22', icon: '⚏' },
        { label: 'PRODUCTOS', value: totalProductos, unit: 'SKUs', color: '#27ae60', icon: '■' }
    ];

    metrics.forEach((metric, idx) => {
        const x = 40 + idx * metricSpacing;

        // Panel principal estilo industrial
        const panel = new Konva.Rect({
            x: x,
            y: metricsY,
            width: metricWidth,
            height: metricHeight,
            fill: '#ffffff',
            stroke: '#bdc3c7',
            strokeWidth: 2,
            cornerRadius: 4
        });
        konvaLayer.add(panel);

        // Header del panel
        const panelHeader = new Konva.Rect({
            x: x,
            y: metricsY,
            width: metricWidth,
            height: 25,
            fill: metric.color,
            cornerRadius: [4, 4, 0, 0]
        });
        konvaLayer.add(panelHeader);

        // Código tipo etiqueta
        const metricCode = new Konva.Text({
            x: x + 10,
            y: metricsY + 6,
            text: `M${idx + 1}`,
            fontSize: 10,
            fontStyle: 'bold',
            fill: 'white',
            fontFamily: 'monospace'
        });
        konvaLayer.add(metricCode);

        // Label del panel
        const metricLabel = new Konva.Text({
            x: x + 30,
            y: metricsY + 6,
            text: metric.label,
            fontSize: 11,
            fontStyle: 'bold',
            fill: 'white',
            fontFamily: 'system-ui, -apple-system, sans-serif'
        });
        konvaLayer.add(metricLabel);

        // Valor principal
        const metricValue = new Konva.Text({
            x: x + 20,
            y: metricsY + 35,
            text: metric.value.toString(),
            fontSize: 32,
            fontStyle: 'bold',
            fill: '#2c3e50',
            fontFamily: 'monospace'
        });
        konvaLayer.add(metricValue);

        // Unidad
        const metricUnit = new Konva.Text({
            x: x + 10,
            y: metricsY + 70,
            text: metric.unit,
            fontSize: 10,
            fill: '#7f8c8d',
            fontFamily: 'system-ui, -apple-system, sans-serif'
        });
        konvaLayer.add(metricUnit);

        // Código de barras simulado
        for (let i = 0; i < 12; i++) {
            const barHeight = Math.random() > 0.5 ? 8 : 12;
            const barCode = new Konva.Line({
                points: [x + 110 + i * 3, metricsY + 78 - barHeight, x + 110 + i * 3, metricsY + 78],
                stroke: '#2c3e50',
                strokeWidth: Math.random() > 0.7 ? 2 : 1
            });
            konvaLayer.add(barCode);
        }
    });

    // Lista de niveles estilo inventario
    const listStartY = 220;
    const rowHeight = 60;

    // Header de lista
    const listHeader = new Konva.Rect({
        x: 40,
        y: listStartY,
        width: stage.width() - 80,
        height: 30,
        fill: '#34495e',
        stroke: '#2c3e50',
        strokeWidth: 1
    });
    konvaLayer.add(listHeader);

    const headerLabels = [
        { text: 'NIVEL', x: 60 },
        { text: 'ESTADO', x: 200 },
        { text: 'TARIMAS', x: 300 },
        { text: 'CAPACIDAD', x: 450 },
        { text: 'ACCIÓN', x: 580 }
    ];

    headerLabels.forEach(label => {
        const headerText = new Konva.Text({
            x: label.x,
            y: listStartY + 8,
            text: label.text,
            fontSize: 12,
            fontStyle: 'bold',
            fill: '#ecf0f1',
            fontFamily: 'system-ui, -apple-system, sans-serif'
        });
        konvaLayer.add(headerText);
    });

    if (columna.niveles) {
        columna.niveles.forEach((nivel, idx) => {
            const rowY = listStartY + 30 + idx * (rowHeight + 5);

            // Fila alternada
            const rowBg = new Konva.Rect({
                x: 40,
                y: rowY,
                width: stage.width() - 80,
                height: rowHeight,
                fill: idx % 2 === 0 ? '#ffffff' : '#f8f9fa',
                stroke: '#e9ecef',
                strokeWidth: 1
            });
            konvaLayer.add(rowBg);

            // Código de nivel tipo etiqueta
            const nivelCode = new Konva.Text({
                x: 60,
                y: rowY + 10,
                text: `Nivel-${nivel.num_nivel.toString().padStart(3, '0')}`,
                fontSize: 12,
                fontStyle: 'bold',
                fill: '#2c3e50',
                fontFamily: 'monospace'
            });
            konvaLayer.add(nivelCode);

            // Nombre del nivel
            const nivelName = new Konva.Text({
                x: 60,
                y: rowY + 25,
                text: nivel.nombre,
                fontSize: 14,
                fontStyle: 'bold',
                fill: '#2c3e50',
                fontFamily: 'system-ui, -apple-system, sans-serif'
            });
            konvaLayer.add(nivelName);

            // Estado con indicador tipo semáforo
            const tarimasCount = nivel.tarimas?.length || 0;
            const statusColor = tarimasCount > 0 ? '#27ae60' : '#e74c3c';
            const statusText = tarimasCount > 0 ? 'OCUPADO' : 'VACÍO';

            const statusIndicator = new Konva.Circle({
                x: 210,
                y: rowY + 20,
                radius: 8,
                fill: statusColor
            });
            konvaLayer.add(statusIndicator);

            const statusLabel = new Konva.Text({
                x: 225,
                y: rowY + 15,
                text: nivel.capacidad,
                fontSize: 11,
                fontStyle: 'bold',
                fill: statusColor,
                fontFamily: 'system-ui, -apple-system, sans-serif'
            });
            konvaLayer.add(statusLabel);

            // Contador de tarimas con estilo industrial
            const countBg = new Konva.Rect({
                x: 300,
                y: rowY + 15,
                width: 40,
                height: 20,
                fill: '#ecf0f1',
                stroke: '#bdc3c7',
                strokeWidth: 1,
                cornerRadius: 2
            });
            konvaLayer.add(countBg);

            const countText = new Konva.Text({
                x: 315,
                y: rowY + 19,
                text: tarimasCount.toString().padStart(2, '0'),
                fontSize: 12,
                fontStyle: 'bold',
                fill: '#2c3e50',
                fontFamily: 'monospace'
            });
            konvaLayer.add(countText);

            // Mostrar ulocation directamente en la columna de capacidad
            const capacityText = new Konva.Text({
                x: 450,
                y: rowY + 16,           // Ajusta verticalmente según tu diseño
                text: nivel.ulocation || '', // Mostrar el string de ulocation
                fontSize: 12,
                fontStyle: 'bold',
                fill: '#2c3e50',
                fontFamily: 'monospace'
            });
            konvaLayer.add(capacityText);

            // Botón de acción estilo industrial
            const actionBtn = new Konva.Rect({
                x: 580,
                y: rowY + 15,
                width: 60,
                height: 25,
                fill: '#3498db',
                stroke: '#2980b9',
                strokeWidth: 1,
                cornerRadius: 3
            });
            konvaLayer.add(actionBtn);

            const actionText = new Konva.Text({
                x: 595,
                y: rowY + 21,
                text: 'VER',
                fontSize: 11,
                fontStyle: 'bold',
                fill: 'white',
                fontFamily: 'system-ui, -apple-system, sans-serif'
            });
            konvaLayer.add(actionText);

            // Grupo clickeable
            const nivelGroup = new Konva.Group();
            nivelGroup.add(rowBg, nivelCode, nivelName, statusIndicator, statusLabel,
                countBg, countText, capacityText,
                actionBtn, actionText);

            nivelGroup.on('click tap', () => {
                showNivelDetail(nivel.id);
            });

            nivelGroup.on('mouseenter', () => {
                stage.container().style.cursor = 'pointer';
                rowBg.fill('#e3f2fd');
                actionBtn.fill('#2980b9');
                konvaLayer.draw();
            });

            nivelGroup.on('mouseleave', () => {
                stage.container().style.cursor = 'default';
                rowBg.fill(idx % 2 === 0 ? '#ffffff' : '#f8f9fa');
                actionBtn.fill('#3498db');
                konvaLayer.draw();
            });

            konvaLayer.add(nivelGroup);
        });
    }

    konvaLayer.draw();
    adjustStageHeight();
}

// showNivelDetail con estilo inventario
function showNivelDetail(nivelId) {
    const rack = currentSelection.rack;
    if (!rack || !rack.columnas) return;

    let nivel = null;
    let columna = null;

    // Encontrar el nivel y su columna padre
    for (let c of rack.columnas) {
        if (c.niveles) {
            const foundNivel = c.niveles.find(n => n.id == nivelId);
            if (foundNivel) {
                nivel = foundNivel;
                columna = c;
                break;
            }
        }
    }

    if (!nivel || !columna) return;

    // Inicializar Konva
    initKonva();

    // Actualizar título e itemCount
    const title = document.getElementById('contentTitle');
    const itemCount = document.getElementById('itemCount');
    title.textContent = `Inventario Nivel ${nivel.nombre} - Columna ${columna.nombre}`;

    const totalTarimas = nivel.tarimas?.length || 0;
    let totalProductos = 0;

    if (nivel.tarimas) {
        nivel.tarimas.forEach(tarima => {
            totalProductos += tarima.productos?.length || 0;
        });
    }

    itemCount.textContent = `${totalTarimas} pallets • ${totalProductos} SKUs en inventario`;

    // Dibujar con Konva - Estilo inventario
    konvaLayer.destroyChildren();

    // Fondo tipo almacén
    const bgPattern = new Konva.Rect({
        x: 0,
        y: 0,
        width: stage.width(),
        height: stage.height(),
        fill: '#f8f9fa'
    });
    konvaLayer.add(bgPattern);

    // Header tipo etiqueta de inventario
    const headerBg = new Konva.Rect({
        x: 0,
        y: 0,
        width: stage.width(),
        height: 90,
        fill: '#2c3e50',
        stroke: '#34495e',
        strokeWidth: 2
    });
    konvaLayer.add(headerBg);

    // Botón volver estilo industrial
    const backGroup = new Konva.Group({ x: 20, y: 20 });

    const backBtn = new Konva.Rect({
        x: 0,
        y: 0,
        width: 80,
        height: 30,
        fill: '#e74c3c',
        stroke: '#c0392b',
        strokeWidth: 1,
        cornerRadius: 3
    });
    backGroup.add(backBtn);

    const backText = new Konva.Text({
        x: 10,
        y: 8,
        text: '← VOLVER',
        fontSize: 12,
        fontStyle: 'bold',
        fill: 'white',
        fontFamily: 'system-ui, -apple-system, sans-serif'
    });
    backGroup.add(backText);

    backGroup.on('click tap', () => {
        showColumnaDetail(columna.id);
    });

    backGroup.on('mouseenter', () => {
        stage.container().style.cursor = 'pointer';
        backBtn.fill('#c0392b');
        konvaLayer.draw();
    });

    backGroup.on('mouseleave', () => {
        stage.container().style.cursor = 'default';
        backBtn.fill('#e74c3c');
        konvaLayer.draw();
    });

    konvaLayer.add(backGroup);

    // Información del nivel estilo etiqueta
    const levelInfo = new Konva.Group({ x: 120, y: 15 });

    // Código de nivel
    const levelCode = new Konva.Text({
        x: 0,
        y: 0,
        text: `NIVEL: NIV-${nivel.id.toString().padStart(3, '0')}`,
        fontSize: 16,
        fontStyle: 'bold',
        fill: '#ecf0f1',
        fontFamily: 'monospace'
    });
    levelInfo.add(levelCode);

    const levelName = new Konva.Text({
        x: 0,
        y: 20,
        text: `NOMBRE: ${nivel.nombre}`,
        fontSize: 14,
        fill: '#bdc3c7',
        fontFamily: 'system-ui, -apple-system, sans-serif'
    });
    levelInfo.add(levelName);

    const columnRef = new Konva.Text({
        x: 0,
        y: 40,
        text: `COLUMNA: ${columna.nombre}`,
        fontSize: 12,
        fill: '#95a5a6',
        fontFamily: 'system-ui, -apple-system, sans-serif'
    });
    levelInfo.add(columnRef);

    konvaLayer.add(levelInfo);

    // Status panel
    const statusPanel = new Konva.Group({ x: stage.width() - 200, y: 15 });

    const statusBg = new Konva.Rect({
        x: 0,
        y: 0,
        width: 180,
        height: 60,
        fill: 'rgba(52, 73, 94, 0.8)',
        stroke: '#34495e',
        strokeWidth: 1,
        cornerRadius: 4
    });
    statusPanel.add(statusBg);

    const statusTitle = new Konva.Text({
        x: 10,
        y: 8,
        text: 'ESTADO ACTUAL',
        fontSize: 11,
        fontStyle: 'bold',
        fill: '#ecf0f1',
        fontFamily: 'system-ui, -apple-system, sans-serif'
    });
    statusPanel.add(statusTitle);

    const occupancy = totalTarimas > 0 ? 'OCUPADO' : 'VACÍO';
    const occupancyColor = totalTarimas > 0 ? '#27ae60' : '#e74c3c';

    const statusValue = new Konva.Text({
        x: 10,
        y: 25,
        text: occupancy,
        fontSize: 16,
        fontStyle: 'bold',
        fill: occupancyColor,
        fontFamily: 'system-ui, -apple-system, sans-serif'
    });
    statusPanel.add(statusValue);

    const dateStamp = new Konva.Text({
        x: 10,
        y: 45,
        text: `UPD: ${new Date().toLocaleString()}`,
        fontSize: 9,
        fill: '#95a5a6',
        fontFamily: 'monospace'
    });
    statusPanel.add(dateStamp);

    konvaLayer.add(statusPanel);

    // Grid de tarimas estilo almacén
    if (nivel.tarimas && nivel.tarimas.length > 0) {
        const tarimaWidth = 220;
        const tarimaHeight = 140;
        const spacing = 25;
        const startX = 30;
        const startY = 110;
        const maxCols = Math.floor((stage.width() - 60) / (tarimaWidth + spacing));

        nivel.tarimas.forEach((tarima, idx) => {
            const col = idx % maxCols;
            const row = Math.floor(idx / maxCols);
            const x = startX + col * (tarimaWidth + spacing);
            const y = startY + row * (tarimaHeight + spacing);

            const tarimaGroup = new Konva.Group({ x, y });

            // Card estilo pallet
            const palletCard = new Konva.Rect({
                x: 0,
                y: 0,
                width: tarimaWidth,
                height: tarimaHeight,
                fill: '#ffffff',
                stroke: '#bdc3c7',
                strokeWidth: 2,
                cornerRadius: 4
            });
            tarimaGroup.add(palletCard);

            // Header estilo etiqueta de pallet
            const palletHeader = new Konva.Rect({
                x: 0,
                y: 0,
                width: tarimaWidth,
                height: 35,
                fill: '#34495e',
                cornerRadius: [4, 4, 0, 0]
            });
            tarimaGroup.add(palletHeader);

            // ID de tarima
            const palletId = new Konva.Text({
                x: 10,
                y: 8,
                text: nivel.ulocation,
                fontSize: 12,
                fontStyle: 'bold',
                fill: '#ecf0f1',
                fontFamily: 'monospace'
            });
            tarimaGroup.add(palletId);

            // Estado de tarima
            const productCount = tarima.productos?.length || 0;
            const status = productCount > 0 ? 'CARGADO' : 'VACÍO';
            const statusColor = productCount > 0 ? '#27ae60' : '#e74c3c';

            const statusDot = new Konva.Circle({
                x: tarimaWidth - 25,
                y: 17,
                radius: 6,
                fill: statusColor
            });
            tarimaGroup.add(statusDot);

            // Información de la tarima
            const palletInfo = [
                { label: 'CÓDIGO:', value: tarima.codigo, y: 45 },
                { label: 'FECHA:', value: dateFormatter(tarima.fecha), y: 65 },
                { label: 'UUID:', value: tarima.uuid.substring(0, 8) + '...', y: 85 },
                { label: 'SKUs:', value: productCount.toString(), y: 105 }
            ];

            palletInfo.forEach(info => {
                const label = new Konva.Text({
                    x: 10,
                    y: info.y,
                    text: info.label,
                    fontSize: 10,
                    fontStyle: 'bold',
                    fill: '#7f8c8d',
                    fontFamily: 'system-ui, -apple-system, sans-serif'
                });
                tarimaGroup.add(label);

                const value = new Konva.Text({
                    x: 60,
                    y: info.y,
                    text: info.value,
                    fontsize: 10,
                    fill: '#2c3e50',
                    fontFamily: info.label === 'UUID:' ? 'monospace' : 'system-ui, -apple-system, sans-serif'
                });
                tarimaGroup.add(value);
            });

            // Badge de cantidad
            if (productCount > 0) {
                const qtyBadge = new Konva.Rect({
                    x: tarimaWidth - 50,
                    y: 115,
                    width: 40,
                    height: 20,
                    fill: '#3498db',
                    stroke: '#2980b9',
                    strokeWidth: 1,
                    cornerRadius: 3
                });
                tarimaGroup.add(qtyBadge);

                const qtyText = new Konva.Text({
                    x: tarimaWidth - 40,
                    y: 120,
                    text: productCount.toString(),
                    fontSize: 11,
                    fontStyle: 'bold',
                    fill: 'white',
                    fontFamily: 'monospace'
                });
                tarimaGroup.add(qtyText);
            }

            // Click handler
            tarimaGroup.on('click tap', () => {
                showTarimaDetail(tarima.id);
            });

            // Hover effects
            tarimaGroup.on('mouseenter', () => {
                stage.container().style.cursor = 'pointer';
                palletCard.stroke('#3498db');
                palletCard.strokeWidth(3);
                konvaLayer.draw();
            });

            tarimaGroup.on('mouseleave', () => {
                stage.container().style.cursor = 'default';
                palletCard.stroke('#bdc3c7');
                palletCard.strokeWidth(2);
                konvaLayer.draw();
            });

            konvaLayer.add(tarimaGroup);
        });
    } else {
        // Estado vacío tipo almacén
        const emptyArea = new Konva.Group({
            x: stage.width() / 2,
            y: stage.height() / 2 - 50
        });

        const emptyCard = new Konva.Rect({
            x: -150,
            y: -75,
            width: 300,
            height: 150,
            fill: '#ffffff',
            stroke: '#e74c3c',
            strokeWidth: 2,
            strokeDashArray: [10, 5],
            cornerRadius: 4
        });
        emptyArea.add(emptyCard);

        const warningIcon = new Konva.Text({
            x: -15,
            y: -40,
            text: '⚠️',
            fontSize: 32
        });
        emptyArea.add(warningIcon);

        const emptyTitle = new Konva.Text({
            x: -80,
            y: 0,
            text: 'NIVEL VACÍO',
            fontSize: 18,
            fontStyle: 'bold',
            fill: '#e74c3c',
            fontFamily: 'system-ui, -apple-system, sans-serif'
        });
        emptyArea.add(emptyTitle);

        const emptyDesc = new Konva.Text({
            x: -120,
            y: 25,
            text: 'No hay tarimas registradas en este nivel',
            fontSize: 14,
            fill: '#7f8c8d',
            fontFamily: 'system-ui, -apple-system, sans-serif'
        });
        emptyArea.add(emptyDesc);

        const emptyCode = new Konva.Text({
            x: -60,
            y: 50,
            text: 'STATUS: AVAILABLE FOR STOCKING',
            fontSize: 11,
            fill: '#95a5a6',
            fontFamily: 'monospace'
        });
        emptyArea.add(emptyCode);

        konvaLayer.add(emptyArea);
    }

    konvaLayer.draw();
    adjustStageHeight();
}

// showTarimaDetail con estilo inventario completo
function showTarimaDetail(tarimaId) {
    const rack = currentSelection.rack;
    if (!rack || !rack.columnas) return;

    let tarima = null;
    let nivel = null;
    let columna = null;

    // Encontrar la tarima y sus padres
    for (let c of rack.columnas) {
        if (c.niveles) {
            for (let n of c.niveles) {
                if (n.tarimas) {
                    const foundTarima = n.tarimas.find(t => t.id == tarimaId);
                    if (foundTarima) {
                        tarima = foundTarima;
                        nivel = n;
                        columna = c;
                        break;
                    }
                }
            }
            if (tarima) break;
        }
    }

    if (!tarima || !nivel || !columna) return;

    // Inicializar Konva
    initKonva();

    // Actualizar título e itemCount
    const title = document.getElementById('contentTitle');
    const itemCount = document.getElementById('itemCount');
    title.textContent = `Detalle de Pallet ${tarima.codigo} - Inventario`;
    itemCount.textContent = `${tarima.productos?.length || 0} SKUs en inventario`;

    // Dibujar con Konva - Estilo inventario profesional
    konvaLayer.destroyChildren();

    // Fondo tipo documento de inventario
    const bgDoc = new Konva.Rect({
        id: 'bgDoc',
        x: 0,
        y: 0,
        width: stage.width(),
        height: stage.height(),
        fill: '#f8f9fa'
    });
    konvaLayer.add(bgDoc);

    // Header tipo reporte de inventario
    const headerBg = new Konva.Rect({
        x: 0,
        y: 0,
        width: stage.width(),
        height: 120,
        fill: '#2c3e50',
        stroke: '#34495e',
        strokeWidth: 2
    });
    konvaLayer.add(headerBg);

    const logoImg = new Image();
    logoImg.src = `/Content/img/${empresa}/logo-light.png`; // <- Pon la ruta de tu imagen

    logoImg.onload = function () {
        const logoArea = new Konva.Image({
            x: 20,
            y: 20,
            width: 80,
            height: 80,
            image: logoImg,
            cornerRadius: 4
        });

        konvaLayer.add(logoArea);
        konvaLayer.draw();
    };

    // Información del pallet
    const palletTitle = new Konva.Text({
        x: 120,
        y: 25,
        text: 'REPORTE DE PALLET',
        fontSize: 24,
        fontStyle: 'bold',
        fill: '#ecf0f1',
        fontFamily: 'system-ui, -apple-system, sans-serif'
    });
    konvaLayer.add(palletTitle);

    const palletCode = new Konva.Text({
        x: 120,
        y: 55,
        text: `ID: PLT-${tarima.id.toString().padStart(4, '0')} | CÓDIGO: ${tarima.codigo}`,
        fontSize: 16,
        fontStyle: 'bold',
        fill: '#f39c12',
        fontFamily: 'monospace'
    });
    konvaLayer.add(palletCode);

    const locationInfo = new Konva.Text({
        x: 120,
        y: 80,
        text: `UBICACIÓN: ${nivel.ulocation}`,
        fontSize: 14,
        fill: '#bdc3c7',
        fontFamily: 'system-ui, -apple-system, sans-serif'
    });
    konvaLayer.add(locationInfo);

    // Timestamp del reporte
    const now = new Date();
    const reportTime = new Konva.Text({
        x: stage.width() - 250,
        y: 25,
        text: `FECHA REPORTE: ${now.toLocaleDateString()}`,
        fontSize: 12,
        fill: '#ecf0f1',
        fontFamily: 'monospace'
    });
    konvaLayer.add(reportTime);

    const reportHour = new Konva.Text({
        x: stage.width() - 250,
        y: 45,
        text: `HORA: ${now.toLocaleTimeString()}`,
        fontSize: 12,
        fill: '#ecf0f1',
        fontFamily: 'monospace'
    });
    konvaLayer.add(reportHour);

    // Botón volver estilo documento
    const backGroup = new Konva.Group({ x: stage.width() - 120, y: 75 });

    const backBtn = new Konva.Rect({
        x: 0,
        y: 0,
        width: 100,
        height: 25,
        fill: '#e74c3c',
        stroke: '#c0392b',
        strokeWidth: 1,
        cornerRadius: 3
    });
    backGroup.add(backBtn);

    const backText = new Konva.Text({
        x: 15,
        y: 6,
        text: '← VOLVER NIVEL',
        fontSize: 11,
        fontStyle: 'bold',
        fill: 'white',
        fontFamily: 'system-ui, -apple-system, sans-serif'
    });
    backGroup.add(backText);

    backGroup.on('click', () => {
        showNivelDetail(nivel.id);
    });

    backGroup.on('mouseenter', () => {
        stage.container().style.cursor = 'pointer';
        backBtn.fill('#c0392b');
        konvaLayer.draw();
    });

    backGroup.on('mouseleave', () => {
        stage.container().style.cursor = 'default';
        backBtn.fill('#e74c3c');
        konvaLayer.draw();
    });

    konvaLayer.add(backGroup);

    // Panel de información del pallet
    const infoPanelY = 140;
    const infoPanel = new Konva.Rect({
        x: 30,
        y: infoPanelY,
        width: stage.width() - 60,
        height: 80, // reducido porque ya no hay UUID
        fill: '#ffffff',
        stroke: '#bdc3c7',
        strokeWidth: 2,
        cornerRadius: 4
    });
    konvaLayer.add(infoPanel);

    // Header del panel
    const infoPanelHeader = new Konva.Rect({
        x: 30,
        y: infoPanelY,
        width: stage.width() - 60,
        height: 30,
        fill: '#34495e',
        cornerRadius: [4, 4, 0, 0]
    });
    konvaLayer.add(infoPanelHeader);

    const panelTitle = new Konva.Text({
        x: 50,
        y: infoPanelY + 8,
        text: 'INFORMACIÓN DEL PALLET',
        fontSize: 14,
        fontStyle: 'bold',
        fill: '#ecf0f1',
        fontFamily: 'system-ui, -apple-system, sans-serif'
    });
    konvaLayer.add(panelTitle);

    // Datos del pallet en formato tabla (UUID eliminado)
    const palletData = [
        { label: 'FECHA INGRESO:', value: dateFormatter(tarima.fecha), pos: { x: 50, y: infoPanelY + 45 } },
        { label: 'TOTAL SKUs:', value: `${tarima.productos?.length || 0} productos`, pos: { x: 400, y: infoPanelY + 45 } },
        { label: 'ESTADO:', value: (tarima.productos?.length || 0) > 0 ? 'OCUPADO' : 'VACÍO', pos: { x: 400, y: infoPanelY + 65 } }
    ];

    palletData.forEach((data) => {
        const labelText = new Konva.Text({
            x: data.pos.x,
            y: data.pos.y,
            text: data.label,
            fontSize: 12,
            fontStyle: 'bold',
            fill: '#7f8c8d',
            fontFamily: 'system-ui, -apple-system, sans-serif'
        });
        konvaLayer.add(labelText);

        const valueColor = data.label === 'ESTADO:' ?
            ((tarima.productos?.length || 0) > 0 ? '#27ae60' : '#e74c3c') : '#2c3e50';

        const valueText = new Konva.Text({
            x: data.pos.x + 120,
            y: data.pos.y,
            text: data.value,
            fontSize: 12,
            fill: valueColor,
            fontFamily: 'system-ui, -apple-system, sans-serif'
        });
        konvaLayer.add(valueText);
    });

    // Código de barras del pallet (UUID eliminado, se puede usar código de tarima si existe)
    const barcodeGroup = new Konva.Group({ x: stage.width() - 150, y: infoPanelY + 40 });

    const barcodeLabel = new Konva.Text({
        x: 0,
        y: 0,
        text: 'CÓDIGO DE BARRAS:',
        fontSize: 10,
        fill: '#7f8c8d',
        fontFamily: 'system-ui, -apple-system, sans-serif'
    });
    barcodeGroup.add(barcodeLabel);

    // Generar código de barras simulado
    for (let i = 0; i < 20; i++) {
        const barHeight = [12, 8, 15, 6, 12, 10, 8, 15, 6, 12, 8, 15, 6, 12, 10, 8, 15, 6, 12, 8][i];
        const barLine = new Konva.Line({
            points: [i * 3, 15, i * 3, 15 + barHeight],
            stroke: '#2c3e50',
            strokeWidth: i % 3 === 0 ? 2 : 1
        });
        barcodeGroup.add(barLine);
    };

    // Opcional: mostrar código de tarima en lugar de UUID
    if (tarima.codigo) {
        const barcodeNumber = new Konva.Text({
            x: 0,
            y: 40,
            text: tarima.codigo,
            fontSize: 8,
            fill: '#7f8c8d',
            fontFamily: 'monospace'
        });
        barcodeGroup.add(barcodeNumber);
    }

    konvaLayer.add(barcodeGroup);

    // Lista de productos estilo inventario
    if (tarima.productos && tarima.productos.length > 0) {
        // Header de la tabla de inventario
        const tableHeaderY = 260;
        const tableHeader = new Konva.Rect({
            x: 30,
            y: tableHeaderY,
            width: stage.width() - 60,
            height: 40,
            fill: '#34495e',
            stroke: '#2c3e50',
            strokeWidth: 1
        });
        konvaLayer.add(tableHeader);

        const inventoryTitle = new Konva.Text({
            x: 50,
            y: tableHeaderY + 8,
            text: '📋 LISTA DE INVENTARIO',
            fontSize: 16,
            fontStyle: 'bold',
            fill: '#ecf0f1',
            fontFamily: 'system-ui, -apple-system, sans-serif'
        });
        konvaLayer.add(inventoryTitle);

        const itemCount = new Konva.Text({
            x: stage.width() - 200,
            y: tableHeaderY + 12,
            text: `TOTAL ITEMS: ${tarima.productos.length}`,
            fontSize: 12,
            fill: '#f39c12',
            fontStyle: 'bold',
            fontFamily: 'monospace'
        });
        konvaLayer.add(itemCount);

        // Headers de columnas estilo inventario
        const columnHeaderY = tableHeaderY + 50;
        const columnHeader = new Konva.Rect({
            x: 30,
            y: columnHeaderY,
            width: stage.width() - 60,
            height: 30,
            fill: '#ecf0f1',
            stroke: '#bdc3c7',
            strokeWidth: 1
        });
        konvaLayer.add(columnHeader);

        const headers = [
            { text: 'SKU', x: 50, width: 120 },
            { text: 'DESCRIPCIÓN', x: 180, width: 250 },
            { text: 'CANTIDAD', x: 450, width: 80 },
            { text: 'UNIDAD', x: 550, width: 80 },
            { text: 'ESTADO', x: 650, width: 60 }
        ];

        headers.forEach(header => {
            const headerText = new Konva.Text({
                x: header.x,
                y: columnHeaderY + 8,
                text: header.text,
                fontSize: 12,
                fontStyle: 'bold',
                fill: '#2c3e50',
                fontFamily: 'system-ui, -apple-system, sans-serif'
            });
            konvaLayer.add(headerText);
        });

        // Filas de productos
        tarima.productos.forEach((producto, idx) => {
            const rowY = columnHeaderY + 35 + (idx * 30);

            // Fila alternada
            const rowBg = new Konva.Rect({
                x: 30,
                y: rowY,
                width: stage.width() - 60,
                height: 28,
                fill: idx % 2 === 0 ? '#ffffff' : '#f8f9fa',
                stroke: '#e9ecef',
                strokeWidth: 1
            });
            konvaLayer.add(rowBg);

            // Indicador de producto con código de color
            const productIndicator = new Konva.Rect({
                x: 35,
                y: rowY + 8,
                width: 8,
                height: 12,
                fill: `hsl(${(idx * 137.5) % 360}, 65%, 55%)`,
                cornerRadius: 1
            });
            konvaLayer.add(productIndicator);

            // Datos del producto
            const skuText = new Konva.Text({
                x: 50,
                y: rowY + 8,
                text: producto.cve_prod,
                fontSize: 11,
                fontStyle: 'bold',
                fill: '#2c3e50',
                fontFamily: 'monospace'
            });
            konvaLayer.add(skuText);

            const descripText = new Konva.Text({
                x: 180,
                y: rowY + 8,
                text: producto.descr_prod.length > 35 ?
                    producto.descr_prod.substring(0, 35) + '...' :
                    producto.descr_prod,
                fontSize: 11,
                fill: '#34495e',
                fontFamily: 'system-ui, -apple-system, sans-serif'
            });
            konvaLayer.add(descripText);

            // Cantidad con formato inventario
            const qtyBg = new Konva.Rect({
                x: 450,
                y: rowY + 6,
                width: 60,
                height: 16,
                fill: '#e8f5e8',
                stroke: '#27ae60',
                strokeWidth: 1,
                cornerRadius: 3
            });
            konvaLayer.add(qtyBg);

            const qtyText = new Konva.Text({
                x: 470,
                y: rowY + 9,
                text: producto.cantidad.toString(),
                fontSize: 11,
                fontStyle: 'bold',
                fill: '#27ae60',
                fontFamily: 'monospace'
            });
            konvaLayer.add(qtyText);

            const unidadText = new Konva.Text({
                x: 550,
                y: rowY + 8,
                text: producto.unidad,
                fontSize: 11,
                fill: '#7f8c8d',
                fontFamily: 'system-ui, -apple-system, sans-serif'
            });
            konvaLayer.add(unidadText);

            // Estado del producto
            const statusBadge = new Konva.Circle({
                x: 670,
                y: rowY + 14,
                radius: 6,
                fill: '#27ae60'
            });
            konvaLayer.add(statusBadge);

            const statusText = new Konva.Text({
                x: 685,
                y: rowY + 9,
                text: 'OK',
                fontSize: 9,
                fontStyle: 'bold',
                fill: '#27ae60',
                fontFamily: 'system-ui, -apple-system, sans-serif'
            });
            konvaLayer.add(statusText);
        });

        // Footer de inventario
        const footerY = columnHeaderY + 45 + (tarima.productos.length * 30);
        const footer = new Konva.Rect({
            x: 30,
            y: footerY,
            width: stage.width() - 60,
            height: 35,
            fill: '#ecf0f1',
            stroke: '#bdc3c7',
            strokeWidth: 1
        });
        konvaLayer.add(footer);

        const footerText = new Konva.Text({
            x: 50,
            y: footerY + 12,
            text: `TOTAL DE PRODUCTOS REGISTRADOS: ${tarima.productos.length}`,
            fontSize: 12,
            fontStyle: 'bold',
            fill: '#2c3e50',
            fontFamily: 'system-ui, -apple-system, sans-serif'
        });
        konvaLayer.add(footerText);

        const auditText = new Konva.Text({
            x: stage.width() - 200,
            y: footerY + 12,
            text: 'AUDITADO ✓',
            fontSize: 11,
            fontStyle: 'bold',
            fill: '#27ae60',
            fontFamily: 'system-ui, -apple-system, sans-serif'
        });
        konvaLayer.add(auditText);

    } else {
        // Estado vacío estilo reporte
        const emptyReport = new Konva.Group({
            x: stage.width() / 2,
            y: 350
        });

        const emptyBox = new Konva.Rect({
            x: -200,
            y: -75,
            width: 400,
            height: 150,
            fill: '#ffffff',
            stroke: '#e74c3c',
            strokeWidth: 3,
            strokeDashArray: [15, 5],
            cornerRadius: 4
        });
        emptyReport.add(emptyBox);

        const warningIcon = new Konva.Text({
            x: -20,
            y: -40,
            text: '📋',
            fontSize: 40
        });
        emptyReport.add(warningIcon);

        const emptyTitle = new Konva.Text({
            x: -120,
            y: 0,
            text: 'PALLET SIN INVENTARIO',
            fontSize: 18,
            fontStyle: 'bold',
            fill: '#e74c3c',
            fontFamily: 'system-ui, -apple-system, sans-serif'
        });
        emptyReport.add(emptyTitle);

        const emptyDesc = new Konva.Text({
            x: -140,
            y: 25,
            text: 'Este pallet no tiene productos registrados',
            fontSize: 14,
            fill: '#7f8c8d',
            fontFamily: 'system-ui, -apple-system, sans-serif'
        });
        emptyReport.add(emptyDesc);

        const actionSuggestion = new Konva.Text({
            x: -100,
            y: 50,
            text: 'STATUS: DISPONIBLE PARA CARGA',
            fontSize: 12,
            fill: '#f39c12',
            fontStyle: 'bold',
            fontFamily: 'monospace'
        });
        emptyReport.add(actionSuggestion);

        konvaLayer.add(emptyReport);
    }

    konvaLayer.draw();
    adjustStageHeight();
}
// Función para exportar a CSV basado en la selección actual
function exportToCSV() {
    if (!inventoryData) {
        alert('No hay datos para exportar');
        return;
    }

    const csvData = [];
    const headers = [
        'Sucursal ID', 'Sucursal', 'Almacen ID', 'Almacen', 'Pasillo ID', 'Pasillo',
        'Rack ID', 'Rack', 'Columna ID', 'Columna', 'Nivel ID', 'Nivel', 'ULocation',
        'Tarima ID', 'Tarima Codigo', 'Tarima Fecha', 'Producto CVE', 'Producto Descripcion',
        'Cantidad', 'Unidad'
    ];

    csvData.push(headers);

    // Determinar el alcance de exportación basado en la selección actual
    let filename = 'inventario_completo';

    if (currentSelection.rack) {
        // Exportar solo el rack actual
        const sucursal = currentSelection.sucursal;
        const almacen = currentSelection.almacen;
        const pasillo = currentSelection.pasillo;
        const rack = currentSelection.rack;

        processRackForCSV(csvData, sucursal, almacen, pasillo, rack);
        filename = `rack_${rack.nombre}_${new Date().toISOString().split('T')[0]}`;

    } else if (currentSelection.pasillo) {
        // Exportar solo el pasillo actual
        const sucursal = currentSelection.sucursal;
        const almacen = currentSelection.almacen;
        const pasillo = currentSelection.pasillo;

        if (pasillo.racks) {
            pasillo.racks.forEach(rack => {
                processRackForCSV(csvData, sucursal, almacen, pasillo, rack);
            });
        }
        filename = `pasillo_${pasillo.num_pasillo}_${new Date().toISOString().split('T')[0]}`;

    } else if (currentSelection.almacen) {
        // Exportar solo el almacén actual
        const sucursal = currentSelection.sucursal;
        const almacen = currentSelection.almacen;

        // Procesar pasillos
        if (almacen.pasillos) {
            almacen.pasillos.forEach(pasillo => {
                if (pasillo.racks) {
                    pasillo.racks.forEach(rack => {
                        processRackForCSV(csvData, sucursal, almacen, pasillo, rack);
                    });
                }
            });
        }

        // Procesar racks sin pasillo
        if (almacen.racks_sin_pasillo) {
            almacen.racks_sin_pasillo.forEach(rack => {
                processRackForCSV(csvData, sucursal, almacen, null, rack);
            });
        }
        filename = `almacen_${almacen.descripcion.replace(/[^a-zA-Z0-9]/g, '_')}_${new Date().toISOString().split('T')[0]}`;

    } else if (currentSelection.sucursal) {
        // Exportar solo la sucursal actual
        const sucursal = currentSelection.sucursal;

        if (sucursal.almacenes) {
            sucursal.almacenes.forEach(almacen => {
                // Procesar pasillos
                if (almacen.pasillos) {
                    almacen.pasillos.forEach(pasillo => {
                        if (pasillo.racks) {
                            pasillo.racks.forEach(rack => {
                                processRackForCSV(csvData, sucursal, almacen, pasillo, rack);
                            });
                        }
                    });
                }

                // Procesar racks sin pasillo
                if (almacen.racks_sin_pasillo) {
                    almacen.racks_sin_pasillo.forEach(rack => {
                        processRackForCSV(csvData, sucursal, almacen, null, rack);
                    });
                }
            });
        }
        filename = `sucursal_${sucursal.descripcion.replace(/[^a-zA-Z0-9]/g, '_')}_${new Date().toISOString().split('T')[0]}`;

    } else {
        // Exportar todo el inventario
        inventoryData.sucursales.forEach(sucursal => {
            if (sucursal.almacenes) {
                sucursal.almacenes.forEach(almacen => {
                    // Procesar pasillos
                    if (almacen.pasillos) {
                        almacen.pasillos.forEach(pasillo => {
                            if (pasillo.racks) {
                                pasillo.racks.forEach(rack => {
                                    processRackForCSV(csvData, sucursal, almacen, pasillo, rack);
                                });
                            }
                        });
                    }

                    // Procesar racks sin pasillo
                    if (almacen.racks_sin_pasillo) {
                        almacen.racks_sin_pasillo.forEach(rack => {
                            processRackForCSV(csvData, sucursal, almacen, null, rack);
                        });
                    }
                });
            }
        });
        filename = `inventario_completo_${new Date().toISOString().split('T')[0]}`;
    }

    // Convertir a CSV
    const csvContent = csvData.map(row =>
        row.map(field => `"${String(field).replace(/"/g, '""')}"`).join(',')
    ).join('\n');

    // Descargar archivo
    const blob = new Blob(['\ufeff' + csvContent], { type: 'text/csv;charset=utf-8;' });
    const link = document.createElement('a');
    const url = URL.createObjectURL(blob);
    link.setAttribute('href', url);
    link.setAttribute('download', `${filename}.csv`);
    link.style.visibility = 'hidden';
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
}

function processRackForCSV(csvData, sucursal, almacen, pasillo, rack) {
    if (rack.columnas) {
        rack.columnas.forEach(columna => {
            if (columna.niveles) {
                columna.niveles.forEach(nivel => {
                    if (nivel.tarimas && nivel.tarimas.length > 0) {
                        nivel.tarimas.forEach(tarima => {
                            if (tarima.productos && tarima.productos.length > 0) {
                                tarima.productos.forEach(producto => {
                                    csvData.push([
                                        sucursal.id, sucursal.descripcion,
                                        almacen.id, almacen.descripcion,
                                        pasillo ? pasillo.id : '', pasillo ? `Pasillo ${pasillo.num_pasillo}` : 'Sin Pasillo',
                                        rack.id, rack.nombre,
                                        columna.id, columna.nombre,
                                        nivel.id, nivel.nombre, nivel.ulocation || '',
                                        tarima.id, tarima.codigo, dateFormatter(tarima.fecha),
                                        producto.cve_prod, producto.descr_prod,
                                        producto.cantidad, producto.unidad
                                    ]);
                                });
                            } else {
                                // Tarima sin productos
                                csvData.push([
                                    sucursal.id, sucursal.descripcion,
                                    almacen.id, almacen.descripcion,
                                    pasillo ? pasillo.id : '', pasillo ? `Pasillo ${pasillo.num_pasillo}` : 'Sin Pasillo',
                                    rack.id, rack.nombre,
                                    columna.id, columna.nombre,
                                    nivel.id, nivel.nombre, nivel.ulocation || '',
                                    tarima.id, tarima.codigo, dateFormatter(tarima.fecha),
                                    '', '', '', ''
                                ]);
                            }
                        });
                    } else {
                        // Nivel sin tarimas
                        csvData.push([
                            sucursal.id, sucursal.descripcion,
                            almacen.id, almacen.descripcion,
                            pasillo ? pasillo.id : '', pasillo ? `Pasillo ${pasillo.num_pasillo}` : 'Sin Pasillo',
                            rack.id, rack.nombre,
                            columna.id, columna.nombre,
                            nivel.id, nivel.nombre, nivel.ulocation || '',
                            '', '', '', '', '', '', ''
                        ]);
                    }
                });
            }
        });
    }
}

// Función para exportar a PDF basado en la selección actual
function exportToPDF() {
    if (!inventoryData) {
        alert('No hay datos para exportar');
        return;
    }

    // Crear ventana de impresión con estilos
    const printWindow = window.open('', '', 'height=600,width=800');

    const htmlContent = generatePDFContent();

    printWindow.document.write(htmlContent);
    printWindow.document.close();
    printWindow.focus();

    // Esperar a que se cargue y luego imprimir
    setTimeout(() => {
        printWindow.print();
        printWindow.close();
    }, 250);
}

function generatePDFContent() {
    const now = new Date();

    // Determinar el título y alcance basado en la selección actual
    let reportTitle = 'REPORTE DE INVENTARIO COMPLETO';
    let reportSubtitle = 'Sistema de Gestión de Almacenes';
    let scopeData = null;

    if (currentSelection.rack) {
        reportTitle = `REPORTE DE RACK - ${currentSelection.rack.nombre}`;
        reportSubtitle = `${currentSelection.sucursal.descripcion} → ${currentSelection.almacen.descripcion}`;
        if (currentSelection.pasillo) {
            reportSubtitle += ` → Pasillo ${currentSelection.pasillo.num_pasillo}`;
        }
        scopeData = { type: 'rack', data: currentSelection.rack };

    } else if (currentSelection.pasillo) {
        reportTitle = `REPORTE DE PASILLO ${currentSelection.pasillo.num_pasillo}`;
        reportSubtitle = `${currentSelection.sucursal.descripcion} → ${currentSelection.almacen.descripcion}`;
        scopeData = { type: 'pasillo', data: currentSelection.pasillo };

    } else if (currentSelection.almacen) {
        reportTitle = `REPORTE DE ALMACÉN - ${currentSelection.almacen.descripcion}`;
        reportSubtitle = `${currentSelection.sucursal.descripcion}`;
        scopeData = { type: 'almacen', data: currentSelection.almacen };

    } else if (currentSelection.sucursal) {
        reportTitle = `REPORTE DE SUCURSAL - ${currentSelection.sucursal.descripcion}`;
        reportSubtitle = `Sucursal: ${currentSelection.sucursal.cve}`;
        scopeData = { type: 'sucursal', data: currentSelection.sucursal };
    }

    let html = `
    <!DOCTYPE html>
    <html>
    <head>
        <meta charset="utf-8">
        <title>${reportTitle}</title>
        <style>
            @@page { margin: 20mm; size: A4; }
            body {
                font-family: Arial, sans-serif;
                font-size: 11px;
                line-height: 1.4;
                color: #333;
            }
            .header {
                text-align: center;
                border-bottom: 2px solid #2c3e50;
                padding-bottom: 20px;
                margin-bottom: 20px;
            }
            .header h1 {
                color: #2c3e50;
                margin: 0;
                font-size: 22px;
            }
            .header .subtitle {
                color: #7f8c8d;
                margin: 5px 0;
                font-size: 14px;
            }
            .stats {
                display: grid;
                grid-template-columns: repeat(4, 1fr);
                gap: 10px;
                margin-bottom: 20px;
            }
            .stat-box {
                background: #ecf0f1;
                padding: 15px;
                text-align: center;
                border-radius: 5px;
                border: 1px solid #bdc3c7;
            }
            .stat-number {
                font-size: 18px;
                font-weight: bold;
                color: #2c3e50;
            }
            .stat-label {
                color: #7f8c8d;
                font-size: 10px;
                margin-top: 5px;
            }
            .section {
                margin-bottom: 20px;
                break-inside: avoid;
            }
            .section-title {
                background: #34495e;
                color: white;
                padding: 8px 12px;
                font-weight: bold;
                font-size: 12px;
                margin-bottom: 10px;
            }
            .item-grid {
                display: grid;
                grid-template-columns: repeat(2, 1fr);
                gap: 10px;
            }
            .item {
                border: 1px solid #bdc3c7;
                padding: 10px;
                background: #ffffff;
            }
            .item-title {
                font-weight: bold;
                color: #2c3e50;
                margin-bottom: 5px;
            }
            .item-detail {
                color: #7f8c8d;
                font-size: 10px;
                margin: 2px 0;
            }
            .footer {
                margin-top: 30px;
                padding-top: 20px;
                border-top: 1px solid #bdc3c7;
                text-align: center;
                color: #7f8c8d;
                font-size: 10px;
            }
            table {
                width: 100%;
                border-collapse: collapse;
                margin-top: 10px;
            }
            th, td {
                border: 1px solid #bdc3c7;
                padding: 5px;
                text-align: left;
                font-size: 9px;
            }
            th {
                background: #ecf0f1;
                font-weight: bold;
            }
            .detail-section {
                background: #f8f9fa;
                padding: 15px;
                border-radius: 5px;
                margin: 10px 0;
            }
            .detail-title {
                font-weight: bold;
                color: #2c3e50;
                margin-bottom: 10px;
                font-size: 12px;
            }
        </style>
    </head>
    <body>
        <div class="header">
            <h1>${reportTitle}</h1>
            <div class="subtitle">${reportSubtitle}</div>
            <div class="subtitle">Generado: ${now.toLocaleString()}</div>
        </div>
    `;

    // Generar estadísticas basadas en el alcance
    const stats = generateScopeStats(scopeData);
    html += generateStatsHTML(stats);

    // Generar contenido específico basado en la selección
    if (scopeData) {
        html += generateScopeContent(scopeData);
    } else {
        // Contenido completo (todas las sucursales)
        inventoryData.sucursales.forEach(sucursal => {
            html += `
                <div class="section">
                    <div class="section-title">${sucursal.descripcion} (${sucursal.cve})</div>
                    <div class="item-grid">
            `;

            if (sucursal.almacenes) {
                sucursal.almacenes.forEach(almacen => {
                    const almacenStats = calculateAlmacenStats(almacen);
                    html += `
                        <div class="item">
                            <div class="item-title">${almacen.descripcion}</div>
                            <div class="item-detail">Código: ${almacen.cve}</div>
                            <div class="item-detail">Tipo: ${almacen.tipo}</div>
                            <div class="item-detail">Pasillos: ${almacenStats.pasillos}</div>
                            <div class="item-detail">Racks: ${almacenStats.racks}</div>
                            <div class="item-detail">Tarimas: ${almacenStats.tarimas}</div>
                        </div>
                    `;
                });
            }

            html += '</div></div>';
        });
    }

    html += `
        <div class="footer">
            <p>Reporte generado automáticamente por el Sistema de Inventario</p>
            <p>Fecha de generación: ${now.toLocaleString()}</p>
        </div>
    </body>
    </html>
    `;

    return html;
}

// Función para generar estadísticas basadas en el alcance
function generateScopeStats(scopeData) {
    if (!scopeData) {
        return calculateStats(); // Estadísticas completas
    }

    let stats = { sucursales: 0, almacenes: 0, pasillos: 0, racks: 0, columnas: 0, niveles: 0, tarimas: 0, productos: 0 };

    switch (scopeData.type) {
        case 'rack':
            const rack = scopeData.data;
            stats.racks = 1;
            if (rack.columnas) {
                stats.columnas = rack.columnas.length;
                rack.columnas.forEach(columna => {
                    if (columna.niveles) {
                        stats.niveles += columna.niveles.length;
                        columna.niveles.forEach(nivel => {
                            if (nivel.tarimas) {
                                stats.tarimas += nivel.tarimas.length;
                                nivel.tarimas.forEach(tarima => {
                                    if (tarima.productos) {
                                        stats.productos += tarima.productos.length;
                                    }
                                });
                            }
                        });
                    }
                });
            }
            break;

        case 'pasillo':
            const pasillo = scopeData.data;
            stats.pasillos = 1;
            if (pasillo.racks) {
                stats.racks = pasillo.racks.length;
                pasillo.racks.forEach(rack => {
                    if (rack.columnas) {
                        stats.columnas += rack.columnas.length;
                        rack.columnas.forEach(columna => {
                            if (columna.niveles) {
                                stats.niveles += columna.niveles.length;
                                columna.niveles.forEach(nivel => {
                                    if (nivel.tarimas) {
                                        stats.tarimas += nivel.tarimas.length;
                                        nivel.tarimas.forEach(tarima => {
                                            if (tarima.productos) {
                                                stats.productos += tarima.productos.length;
                                            }
                                        });
                                    }
                                });
                            }
                        });
                    }
                });
            }
            break;

        case 'almacen':
            const almacen = scopeData.data;
            stats.almacenes = 1;
            const almacenStats = calculateAlmacenStatsDetailed(almacen);
            Object.assign(stats, almacenStats);
            break;

        case 'sucursal':
            const sucursal = scopeData.data;
            stats.sucursales = 1;
            if (sucursal.almacenes) {
                stats.almacenes = sucursal.almacenes.length;
                sucursal.almacenes.forEach(almacen => {
                    const almacenStats = calculateAlmacenStatsDetailed(almacen);
                    stats.pasillos += almacenStats.pasillos;
                    stats.racks += almacenStats.racks;
                    stats.columnas += almacenStats.columnas;
                    stats.niveles += almacenStats.niveles;
                    stats.tarimas += almacenStats.tarimas;
                    stats.productos += almacenStats.productos;
                });
            }
            break;
    }

    return stats;
}

// Función para generar HTML de estadísticas
function generateStatsHTML(stats) {
    return `
        <div class="stats">
            <div class="stat-box">
                <div class="stat-number">${stats.racks || 0}</div>
                <div class="stat-label">RACKS</div>
            </div>
            <div class="stat-box">
                <div class="stat-number">${stats.columnas || 0}</div>
                <div class="stat-label">COLUMNAS</div>
            </div>
            <div class="stat-box">
                <div class="stat-number">${stats.tarimas || 0}</div>
                <div class="stat-label">TARIMAS</div>
            </div>
            <div class="stat-box">
                <div class="stat-number">${stats.productos || 0}</div>
                <div class="stat-label">PRODUCTOS</div>
            </div>
        </div>
    `;
}

// Función para generar contenido específico del alcance
function generateScopeContent(scopeData) {
    let html = '';

    switch (scopeData.type) {
        case 'rack':
            html += generateRackDetailHTML(scopeData.data);
            break;
        case 'pasillo':
            html += generatePasilloDetailHTML(scopeData.data);
            break;
        case 'almacen':
            html += generateAlmacenDetailHTML(scopeData.data);
            break;
        case 'sucursal':
            html += generateSucursalDetailHTML(scopeData.data);
            break;
    }

    return html;
}

// Función para generar detalle de rack
function generateRackDetailHTML(rack) {
    let html = `
        <div class="detail-section">
            <div class="detail-title">Información del Rack</div>
            <div class="item-grid">
                <div class="item">
                    <div class="item-title">Detalles Generales</div>
                    <div class="item-detail">ID: ${rack.id}</div>
                    <div class="item-detail">Nombre: ${rack.nombre}</div>
                    <div class="item-detail">Número: #${rack.num_rack}</div>
                    <div class="item-detail">Tipo: ${rack.tipo}</div>
                    <div class="item-detail">Lado: ${rack.lado}</div>
                </div>
                <div class="item">
                    <div class="item-title">Estructura</div>
                    <div class="item-detail">Columnas: ${rack.columnas?.length || 0}</div>
                    <div class="item-detail">Total Niveles: ${rack.columnas?.reduce((sum, col) => sum + (col.niveles?.length || 0), 0) || 0}</div>
                </div>
            </div>
        </div>
    `;

    if (rack.columnas && rack.columnas.length > 0) {
        html += `
            <div class="section">
                <div class="section-title">Detalle de Columnas</div>
                <table>
                    <thead>
                        <tr>
                            <th>Columna</th>
                            <th>Niveles</th>
                            <th>Tarimas</th>
                            <th>Productos</th>
                            <th>Estado</th>
                        </tr>
                    </thead>
                    <tbody>
        `;

        rack.columnas.forEach(columna => {
            const niveles = columna.niveles?.length || 0;
            let tarimas = 0;
            let productos = 0;

            if (columna.niveles) {
                columna.niveles.forEach(nivel => {
                    tarimas += nivel.tarimas?.length || 0;
                    if (nivel.tarimas) {
                        nivel.tarimas.forEach(tarima => {
                            productos += tarima.productos?.length || 0;
                        });
                    }
                });
            }

            const estado = tarimas > 0 ? 'OCUPADO' : 'VACÍO';

            html += `
                <tr>
                    <td>${columna.nombre} (#${columna.num_col})</td>
                    <td>${niveles}</td>
                    <td>${tarimas}</td>
                    <td>${productos}</td>
                    <td>${estado}</td>
                </tr>
            `;
        });

        html += `
                    </tbody>
                </table>
            </div>
        `;
    }

    return html;
}

// Función para calcular estadísticas detalladas de almacén
function calculateAlmacenStatsDetailed(almacen) {
    let stats = { pasillos: 0, racks: 0, columnas: 0, niveles: 0, tarimas: 0, productos: 0 };

    if (almacen.pasillos) {
        stats.pasillos = almacen.pasillos.length;
        almacen.pasillos.forEach(pasillo => {
            if (pasillo.racks) {
                stats.racks += pasillo.racks.length;
                pasillo.racks.forEach(rack => {
                    if (rack.columnas) {
                        stats.columnas += rack.columnas.length;
                        rack.columnas.forEach(columna => {
                            if (columna.niveles) {
                                stats.niveles += columna.niveles.length;
                                columna.niveles.forEach(nivel => {
                                    if (nivel.tarimas) {
                                        stats.tarimas += nivel.tarimas.length;
                                        nivel.tarimas.forEach(tarima => {
                                            if (tarima.productos) {
                                                stats.productos += tarima.productos.length;
                                            }
                                        });
                                    }
                                });
                            }
                        });
                    }
                });
            }
        });
    }

    if (almacen.racks_sin_pasillo) {
        stats.racks += almacen.racks_sin_pasillo.length;
        almacen.racks_sin_pasillo.forEach(rack => {
            if (rack.columnas) {
                stats.columnas += rack.columnas.length;
                rack.columnas.forEach(columna => {
                    if (columna.niveles) {
                        stats.niveles += columna.niveles.length;
                        columna.niveles.forEach(nivel => {
                            if (nivel.tarimas) {
                                stats.tarimas += nivel.tarimas.length;
                                nivel.tarimas.forEach(tarima => {
                                    if (tarima.productos) {
                                        stats.productos += tarima.productos.length;
                                    }
                                });
                            }
                        });
                    }
                });
            }
        });
    }

    return stats;
}


// Funciones auxiliares para generar detalles de otros niveles
function generatePasilloDetailHTML(pasillo) {
    return `
        <div class="detail-section">
            <div class="detail-title">Detalle del Pasillo ${pasillo.num_pasillo}</div>
            <p>Código: ${pasillo.cve}</p>
            <p>Total de Racks: ${pasillo.racks?.length || 0}</p>
        </div>
    `;
}

function generateAlmacenDetailHTML(almacen) {
    return `
        <div class="detail-section">
            <div class="detail-title">Detalle del Almacén ${almacen.descripcion}</div>
            <p>Código: ${almacen.cve}</p>
            <p>Tipo: ${almacen.tipo}</p>
            <p>Pasillos: ${almacen.pasillos?.length || 0}</p>
            <p>Racks Directos: ${almacen.racks_sin_pasillo?.length || 0}</p>
        </div>
    `;
}

// Funciones auxiliares para generar detalles de otros niveles
function generatePasilloDetailHTML(pasillo) {
    return `
        <div class="detail-section">
            <div class="detail-title">Detalle del Pasillo ${pasillo.num_pasillo}</div>
            <p>Código: ${pasillo.cve}</p>
            <p>Total de Racks: ${pasillo.racks?.length || 0}</p>
        </div>
    `;
}

function generateAlmacenDetailHTML(almacen) {
    return `
        <div class="detail-section">
            <div class="detail-title">Detalle del Almacén ${almacen.descripcion}</div>
            <p>Código: ${almacen.cve}</p>
            <p>Tipo: ${almacen.tipo}</p>
            <p>Pasillos: ${almacen.pasillos?.length || 0}</p>
            <p>Racks Directos: ${almacen.racks_sin_pasillo?.length || 0}</p>
        </div>
    `;
}

function generateSucursalDetailHTML(sucursal) {
    return `
        <div class="detail-section">
            <div class="detail-title">Detalle de la Sucursal ${sucursal.descripcion}</div>
            <p>Código: ${sucursal.cve}</p>
            <p>Tipo: ${sucursal.tipo}</p>
            <p>Total de Almacenes: ${sucursal.almacenes?.length || 0}</p>
        </div>
    `;
}

function generateReporteHTML(inventoryData) {
    const now = new Date();

    let html = `
    <!DOCTYPE html>
    <html>
    <head>
        <meta charset="UTF-8">
        <title>Reporte de Inventario</title>
        <style>
            .header {
                text-align: center;
                border-bottom: 2px solid #2c3e50;
                padding-bottom: 20px;
                margin-bottom: 20px;
            }
            .header h1 { 
                color: #2c3e50; 
                margin: 0;
                font-size: 24px;
            }
            .header .subtitle {
                color: #7f8c8d;
                margin: 5px 0;
            }
            .stats {
                display: grid;
                grid-template-columns: repeat(4, 1fr);
                gap: 10px;
                margin-bottom: 20px;
            }
            .stat-box {
                background: #ecf0f1;
                padding: 15px;
                text-align: center;
                border-radius: 5px;
                border: 1px solid #bdc3c7;
            }
            .stat-number {
                font-size: 18px;
                font-weight: bold;
                color: #2c3e50;
            }
            .stat-label {
                color: #7f8c8d;
                font-size: 10px;
                margin-top: 5px;
            }
            .section {
                margin-bottom: 20px;
                break-inside: avoid;
            }
            .section-title {
                background: #34495e;
                color: white;
                padding: 8px 12px;
                font-weight: bold;
                font-size: 12px;
                margin-bottom: 10px;
            }
            .item-grid {
                display: grid;
                grid-template-columns: repeat(2, 1fr);
                gap: 10px;
            }
            .item {
                border: 1px solid #bdc3c7;
                padding: 10px;
                background: #ffffff;
            }
            .item-title {
                font-weight: bold;
                color: #2c3e50;
                margin-bottom: 5px;
            }
            .item-detail {
                color: #7f8c8d;
                font-size: 10px;
                margin: 2px 0;
            }
            .footer {
                margin-top: 30px;
                padding-top: 20px;
                border-top: 1px solid #bdc3c7;
                text-align: center;
                color: #7f8c8d;
                font-size: 10px;
            }
            table {
                width: 100%;
                border-collapse: collapse;
                margin-top: 10px;
            }
            th, td {
                border: 1px solid #bdc3c7;
                padding: 5px;
                text-align: left;
                font-size: 9px;
            }
            th {
                background: #ecf0f1;
                font-weight: bold;
            }
        </style>
    </head>
    <body>
        <div class="header">
            <h1>REPORTE DE INVENTARIO</h1>
            <div class="subtitle">Sistema de Gestión de Almacenes</div>
            <div class="subtitle">Generado: ${now.toLocaleString()}</div>
        </div>
    `;

    // Estadísticas generales
    const stats = calculateStats();
    html += `
        <div class="stats">
            <div class="stat-box">
                <div class="stat-number">${stats.sucursales}</div>
                <div class="stat-label">SUCURSALES</div>
            </div>
            <div class="stat-box">
                <div class="stat-number">${stats.almacenes}</div>
                <div class="stat-label">ALMACENES</div>
            </div>
            <div class="stat-box">
                <div class="stat-number">${stats.racks}</div>
                <div class="stat-label">RACKS</div>
            </div>
            <div class="stat-box">
                <div class="stat-number">${stats.tarimas}</div>
                <div class="stat-label">TARIMAS</div>
            </div>
        </div>
    `;

    // Detalles por sucursal
    inventoryData.sucursales.forEach(sucursal => {
        html += `
            <div class="section">
                <div class="section-title">${sucursal.descripcion} (${sucursal.cve})</div>
                <div class="item-grid">
        `;

        if (sucursal.almacenes) {
            sucursal.almacenes.forEach(almacen => {
                const almacenStats = calculateAlmacenStats(almacen);
                html += `
                    <div class="item">
                        <div class="item-title">${almacen.descripcion}</div>
                        <div class="item-detail">Código: ${almacen.cve}</div>
                        <div class="item-detail">Tipo: ${almacen.tipo}</div>
                        <div class="item-detail">Pasillos: ${almacenStats.pasillos}</div>
                        <div class="item-detail">Racks: ${almacenStats.racks}</div>
                        <div class="item-detail">Tarimas: ${almacenStats.tarimas}</div>
                    </div>
                `;
            });
        }

        html += `</div></div>`;
    });

    html += `
        <div class="footer">
            <p>Reporte generado automáticamente por el Sistema de Inventario</p>
            <p>Fecha de generación: ${now.toLocaleString()}</p>
        </div>
    </body>
    </html>
    `;

    return html;
}


// Función para exportar como imagen basado en la selección actual
function exportToImage() {
    if (!stage || !konvaLayer) {
        alert('No hay vista activa para exportar como imagen');
        return;
    }

    // Determinar nombre del archivo basado en la selección
    let filename = 'inventario_vista';

    if (currentSelection.rack) {
        filename = `rack_${currentSelection.rack.nombre}_vista`;
    } else if (currentSelection.pasillo) {
        filename = `pasillo_${currentSelection.pasillo.num_pasillo}_vista`;
    } else if (currentSelection.almacen) {
        filename = `almacen_${currentSelection.almacen.descripcion.replace(/[^a-zA-Z0-9]/g, '_')}_vista`;
    } else if (currentSelection.sucursal) {
        filename = `sucursal_${currentSelection.sucursal.descripcion.replace(/[^a-zA-Z0-9]/g, '_')}_vista`;
    }

    // Crear un stage temporal con fondo blanco
    const tempStage = stage.clone();
    const tempLayer = tempStage.getLayers()[0];

    // Agregar fondo blanco
    const whiteBg = new Konva.Rect({
        x: 0,
        y: 0,
        width: tempStage.width(),
        height: tempStage.height(),
        fill: 'white'
    });

    tempLayer.add(whiteBg);
    whiteBg.moveToBottom();

    // Crear imagen
    const dataURL = tempStage.toDataURL({
        mimeType: 'image/png',
        quality: 1.0,
        pixelRatio: 2 // Para mejor calidad
    });

    // Descargar imagen
    const link = document.createElement('a');
    link.download = `${filename}_${new Date().toISOString().split('T')[0]}.png`;
    link.href = dataURL;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);

    // Limpiar
    tempStage.destroy();
}

// Función auxiliar para calcular estadísticas
function calculateStats() {
    let stats = {
        sucursales: inventoryData.sucursales.length,
        almacenes: 0,
        racks: 0,
        tarimas: 0
    };

    inventoryData.sucursales.forEach(sucursal => {
        if (sucursal.almacenes) {
            stats.almacenes += sucursal.almacenes.length;
            sucursal.almacenes.forEach(almacen => {
                const almacenStats = calculateAlmacenStats(almacen);
                stats.racks += almacenStats.racks;
                stats.tarimas += almacenStats.tarimas;
            });
        }
    });

    return stats;
}

function calculateAlmacenStats(almacen) {
    let stats = { pasillos: 0, racks: 0, tarimas: 0 };

    if (almacen.pasillos) {
        stats.pasillos = almacen.pasillos.length;
        almacen.pasillos.forEach(pasillo => {
            if (pasillo.racks) {
                stats.racks += pasillo.racks.length;
                pasillo.racks.forEach(rack => {
                    stats.tarimas += countTarifasInRack(rack);
                });
            }
        });
    }

    if (almacen.racks_sin_pasillo) {
        stats.racks += almacen.racks_sin_pasillo.length;
        almacen.racks_sin_pasillo.forEach(rack => {
            stats.tarimas += countTarifasInRack(rack);
        });
    }

    return stats;
}

// Función para mostrar el menú de exportación con información contextual
function showExportMenu() {
    // Determinar el contexto actual para mostrar información relevante
    let contextInfo = getExportContext();

    // Crear overlay
    const overlay = document.createElement('div');
    overlay.style.cssText = `
        position: fixed;
        top: 0;
        left: 0;
        width: 100%;
        height: 100%;
        background: rgba(0, 0, 0, 0.5);
        display: flex;
        justify-content: center;
        align-items: center;
        z-index: 10000;
    `;

    // Crear modal
    const modal = document.createElement('div');
    modal.style.cssText = `
        background: white;
        border-radius: 12px;
        padding: 30px;
        box-shadow: 0 20px 40px rgba(0, 0, 0, 0.1);
        max-width: 500px;
        width: 90%;
        max-height: 80vh;
        overflow-y: auto;
    `;

    modal.innerHTML = `
        <h3 style="margin: 0 0 10px 0; color: #2c3e50; text-align: center;">
            <i class="fas fa-download" style="margin-right: 10px;"></i>
            Exportar Inventario
        </h3>
        
        <div style="background: #f8f9fa; padding: 15px; border-radius: 8px; margin-bottom: 20px; border-left: 4px solid #3498db;">
            <h4 style="margin: 0 0 10px 0; color: #2c3e50; font-size: 14px;">
                <i class="fas fa-info-circle" style="margin-right: 5px;"></i>
                Alcance del Reporte
            </h4>
            <p style="margin: 0; font-size: 13px; color: #7f8c8d;">
                <strong>${contextInfo.title}</strong><br>
                ${contextInfo.description}
            </p>
            <div style="display: grid; grid-template-columns: repeat(2, 1fr); gap: 10px; margin-top: 10px;">
                ${contextInfo.stats.map(stat => `
                    <div style="background: white; padding: 8px; border-radius: 4px; text-align: center;">
                        <div style="font-weight: bold; color: #2c3e50; font-size: 16px;">${stat.value}</div>
                        <div style="font-size: 10px; color: #7f8c8d;">${stat.label}</div>
                    </div>
                `).join('')}
            </div>
        </div>

        <div style="display: grid; gap: 15px;">
            <button onclick="exportToCSV(); closeExportMenu();" style="
                padding: 15px;
                border: 2px solid #27ae60;
                background: #27ae60;
                color: white;
                border-radius: 8px;
                font-size: 16px;
                cursor: pointer;
                transition: all 0.2s;
            ">
                <i class="fas fa-file-csv" style="margin-right: 10px;"></i>
                Exportar como CSV
                <div style="font-size: 12px; margin-top: 5px; opacity: 0.9;">
                    Datos tabulares para análisis en Excel
                </div>
            </button>
            <button onclick="exportToPDF(); closeExportMenu();" style="
                padding: 15px;
                border: 2px solid #e74c3c;
                background: #e74c3c;
                color: white;
                border-radius: 8px;
                font-size: 16px;
                cursor: pointer;
                transition: all 0.2s;
            ">
                <i class="fas fa-file-pdf" style="margin-right: 10px;"></i>
                Exportar como PDF
                <div style="font-size: 12px; margin-top: 5px; opacity: 0.9;">
                    Reporte profesional para impresión
                </div>
            </button>
            <button onclick="exportToImage(); closeExportMenu();" style="
                padding: 15px;
                border: 2px solid #3498db;
                background: ${!stage || !konvaLayer ? '#bdc3c7' : '#3498db'};
                color: white;
                border-radius: 8px;
                font-size: 16px;
                cursor: ${!stage || !konvaLayer ? 'not-allowed' : 'pointer'};
                transition: all 0.2s;
            " ${!stage || !konvaLayer ? 'disabled' : ''}>
                <i class="fas fa-image" style="margin-right: 10px;"></i>
                Exportar como Imagen
                <div style="font-size: 12px; margin-top: 5px; opacity: 0.9;">
                    ${!stage || !konvaLayer ? 'Vista activa requerida' : 'Captura de la vista actual'}
                </div>
            </button>
            <button onclick="closeExportMenu();" style="
                padding: 10px;
                border: 2px solid #95a5a6;
                background: transparent;
                color: #95a5a6;
                border-radius: 8px;
                font-size: 14px;
                cursor: pointer;
                margin-top: 10px;
            ">
                Cancelar
            </button>
        </div>
    `;

    overlay.appendChild(modal);
    document.body.appendChild(overlay);
    window.exportOverlay = overlay;

    // Cerrar al hacer clic fuera del modal
    overlay.addEventListener('click', (e) => {
        if (e.target === overlay) {
            closeExportMenu();
        }
    });

    // Agregar efectos hover
    modal.querySelectorAll('button').forEach((btn, index) => {
        if (index < 3 && !btn.disabled) { // Solo los botones de exportación habilitados
            btn.addEventListener('mouseenter', () => {
                btn.style.transform = 'translateY(-2px)';
                btn.style.boxShadow = '0 5px 15px rgba(0, 0, 0, 0.2)';
            });
            btn.addEventListener('mouseleave', () => {
                btn.style.transform = 'translateY(0)';
                btn.style.boxShadow = 'none';
            });
        }
    });
}

// Función para obtener el contexto de exportación actual
function getExportContext() {
    let context = {
        title: 'Inventario Completo',
        description: 'Se exportarán todas las sucursales, almacenes y sus contenidos',
        stats: []
    };

    if (currentSelection.rack) {
        const rack = currentSelection.rack;
        let columnas = rack.columnas?.length || 0;
        let niveles = 0;
        let tarimas = 0;
        let productos = 0;

        if (rack.columnas) {
            rack.columnas.forEach(columna => {
                if (columna.niveles) {
                    niveles += columna.niveles.length;
                    columna.niveles.forEach(nivel => {
                        if (nivel.tarimas) {
                            tarimas += nivel.tarimas.length;
                            nivel.tarimas.forEach(tarima => {
                                productos += tarima.productos?.length || 0;
                            });
                        }
                    });
                }
            });
        }

        context = {
            title: `Rack: ${rack.nombre}`,
            description: `Datos específicos del rack seleccionado en ${currentSelection.almacen.descripcion}`,
            stats: [
                { label: 'COLUMNAS', value: columnas },
                { label: 'NIVELES', value: niveles },
                { label: 'TARIMAS', value: tarimas },
                { label: 'PRODUCTOS', value: productos }
            ]
        };

    } else if (currentSelection.pasillo) {
        const pasillo = currentSelection.pasillo;
        let racks = pasillo.racks?.length || 0;
        let tarimas = 0;
        let productos = 0;

        if (pasillo.racks) {
            pasillo.racks.forEach(rack => {
                tarimas += countTarifasInRack(rack);
                if (rack.columnas) {
                    rack.columnas.forEach(columna => {
                        if (columna.niveles) {
                            columna.niveles.forEach(nivel => {
                                if (nivel.tarimas) {
                                    nivel.tarimas.forEach(tarima => {
                                        productos += tarima.productos?.length || 0;
                                    });
                                }
                            });
                        }
                    });
                }
            });
        }

        context = {
            title: `Pasillo ${pasillo.num_pasillo}`,
            description: `Datos del pasillo en ${currentSelection.almacen.descripcion}`,
            stats: [
                { label: 'RACKS', value: racks },
                { label: 'TARIMAS', value: tarimas },
                { label: 'PRODUCTOS', value: productos },
                { label: 'CÓDIGO', value: pasillo.cve }
            ]
        };

    } else if (currentSelection.almacen) {
        const almacen = currentSelection.almacen;
        const stats = calculateAlmacenStatsDetailed(almacen);

        context = {
            title: `Almacén: ${almacen.descripcion}`,
            description: `Datos completos del almacén en ${currentSelection.sucursal.descripcion}`,
            stats: [
                { label: 'PASILLOS', value: stats.pasillos },
                { label: 'RACKS', value: stats.racks },
                { label: 'TARIMAS', value: stats.tarimas },
                { label: 'PRODUCTOS', value: stats.productos }
            ]
        };

    } else if (currentSelection.sucursal) {
        const sucursal = currentSelection.sucursal;
        let almacenes = sucursal.almacenes?.length || 0;
        let racks = 0;
        let tarimas = 0;
        let productos = 0;

        if (sucursal.almacenes) {
            sucursal.almacenes.forEach(almacen => {
                const stats = calculateAlmacenStatsDetailed(almacen);
                racks += stats.racks;
                tarimas += stats.tarimas;
                productos += stats.productos;
            });
        }

        context = {
            title: `Sucursal: ${sucursal.descripcion}`,
            description: `Datos completos de la sucursal ${sucursal.cve}`,
            stats: [
                { label: 'ALMACENES', value: almacenes },
                { label: 'RACKS', value: racks },
                { label: 'TARIMAS', value: tarimas },
                { label: 'PRODUCTOS', value: productos }
            ]
        };

    } else {
        // Inventario completo
        const stats = calculateStats();
        context = {
            title: 'Inventario Completo',
            description: 'Se exportarán todas las sucursales, almacenes y sus contenidos',
            stats: [
                { label: 'SUCURSALES', value: stats.sucursales },
                { label: 'ALMACENES', value: stats.almacenes },
                { label: 'RACKS', value: stats.racks },
                { label: 'TARIMAS', value: stats.tarimas }
            ]
        };
    }

    return context;
}

function closeExportMenu() {
    if (window.exportOverlay) {
        document.body.removeChild(window.exportOverlay);
        window.exportOverlay = null;
    }
}

// Función auxiliar para formatear fechas
function dateFormatter(dateString) {
    if (!dateString) return 'N/A';
    try {
        const date = new Date(dateString);
        return date.toLocaleDateString();
    } catch (e) {
        return dateString;
    }
}

// Reemplazar la función exportData existente
function exportData() {
    showExportMenu();
}

// Agregar soporte para ESC para cerrar el modal
document.addEventListener('keydown', function (event) {
    if (event.key === 'Escape' && window.exportOverlay) {
        closeExportMenu();
    }
});


// Add export button
function addExportButton() {
    const headerActions = document.querySelector('.header-actions');
    const exportBtn = document.createElement('button');
    exportBtn.className = 'btn btn-outline';
    exportBtn.innerHTML = '<i class="fas fa-download"></i> Exportar';
    exportBtn.onclick = exportData;

    headerActions.appendChild(exportBtn);
}

// Add export button on load
document.addEventListener('DOMContentLoaded', function () {
    addExportButton();
});
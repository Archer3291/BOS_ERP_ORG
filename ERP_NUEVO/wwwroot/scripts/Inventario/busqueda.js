// Search functionality
function setupSearch() {
    const searchInput = document.createElement('input');
    searchInput.type = 'text';
    searchInput.placeholder = 'Buscar en inventario...';
    searchInput.className = 'search-input';
    searchInput.style.cssText = `
                            width: 300px;
                            padding: 10px 16px;
                            border: 2px solid #e2e8f0;
                            border-radius: var(--border-radius);
                            font-size: 14px;
                            transition: var(--transition);
                        `;

    searchInput.addEventListener('focus', function () {
        this.style.borderColor = 'var(--primary)';
        this.style.boxShadow = '0 0 0 3px rgba(102, 126, 234, 0.1)';
    });

    searchInput.addEventListener('blur', function () {
        this.style.borderColor = '#e2e8f0';
        this.style.boxShadow = 'none';
    });

    searchInput.addEventListener('input', debounce(handleSearch, 300));

    const headerActions = document.querySelector('.header-actions');
    headerActions.insertBefore(searchInput, headerActions.firstChild);
}

function handleSearch(event) {
    const searchTerm = event.target.value.toLowerCase().trim();

    if (!searchTerm) {
        // Si no hay término de búsqueda, volver a la vista actual
        if (currentSelection.rack) {
            showRackDetailsView(currentSelection.rack);
        } else if (currentSelection.pasillo) {
            showRacksView();
        } else if (currentSelection.almacen) {
            showPasillosView();
        } else if (currentSelection.sucursal) {
            showAlmacenesView();
        } else {
            showSucursalesView();
        }
        return;
    }

    // Realizar búsqueda
    const results = searchInventory(searchTerm);
    showSearchResults(searchTerm, results);
}

function debounce(func, wait) {
    let timeout;
    return function executedFunction(...args) {
        const later = () => {
            clearTimeout(timeout);
            func(...args);
        };
        clearTimeout(timeout);
        timeout = setTimeout(later, wait);
    };
}

// Initialize search on load
document.addEventListener('DOMContentLoaded', function () {
    setupSearch();
});



// Keyboard shortcuts
document.addEventListener('keydown', function (event) {
    // ESC key - close dropdowns
    if (event.key === 'Escape') {
        document.querySelectorAll('.select-display.active').forEach(display => {
            display.classList.remove('active');
            display.parentNode.querySelector('.select-dropdown').classList.remove('show');
        });
    }

    // F5 or Ctrl+R - reload data
    if (event.key === 'F5' || (event.ctrlKey && event.key === 'r')) {
        event.preventDefault();
        loadData();
    }

    // Ctrl+Home - reset navigation
    if (event.ctrlKey && event.key === 'Home') {
        event.preventDefault();
        resetNavigation();
    }
});

function searchInventory(searchTerm) {
    const results = {
        productos: [],
        ubicaciones: []
    };

    if (!inventoryData || !inventoryData.sucursales) {
        return results;
    }

    inventoryData.sucursales.forEach(sucursal => {
        if (!sucursal.almacenes) return;

        sucursal.almacenes.forEach(almacen => {
            // Buscar en pasillos
            if (almacen.pasillos) {
                almacen.pasillos.forEach(pasillo => {
                    if (pasillo.racks) {
                        pasillo.racks.forEach(rack => {
                            searchInRack(rack, sucursal, almacen, pasillo, null, searchTerm, results);
                        });
                    }
                });
            }

            // Buscar en racks directos
            if (almacen.racks_sin_pasillo) {
                almacen.racks_sin_pasillo.forEach(rack => {
                    searchInRack(rack, sucursal, almacen, null, null, searchTerm, results);
                });
            }
        });
    });

    return results;
}

function searchInRack(rack, sucursal, almacen, pasillo, nivel, searchTerm, results) {
    if (!rack.columnas) return;

    rack.columnas.forEach(columna => {
        if (!columna.niveles) return;

        columna.niveles.forEach(nivel => {
            // Buscar por ulocation
            if (nivel.ulocation && nivel.ulocation.toLowerCase().includes(searchTerm)) {
                results.ubicaciones.push({
                    type: 'ubicacion',
                    ulocation: nivel.ulocation,
                    sucursal: sucursal.descripcion,
                    almacen: almacen.descripcion,
                    pasillo: pasillo ? `Pasillo ${pasillo.num_pasillo}` : 'Rack Directo',
                    rack: rack.nombre,
                    columna: columna.nombre,
                    nivel: nivel.nombre,
                    tarimasCount: nivel.tarimas?.length || 0,
                    path: {
                        sucursal: sucursal,
                        almacen: almacen,
                        pasillo: pasillo,
                        rack: rack,
                        columna: columna,
                        nivel: nivel
                    }
                });
            }

            // Buscar en tarimas y productos
            if (nivel.tarimas) {
                nivel.tarimas.forEach(tarima => {
                    if (tarima.productos) {
                        tarima.productos.forEach(producto => {
                            // Buscar por código de producto
                            const matchesCodigo = producto.cve_prod &&
                                producto.cve_prod.toLowerCase().includes(searchTerm);

                            // Buscar por descripción
                            const matchesDescripcion = producto.descr_prod &&
                                producto.descr_prod.toLowerCase().includes(searchTerm);

                            if (matchesCodigo || matchesDescripcion) {
                                results.productos.push({
                                    type: 'producto',
                                    codigo: producto.cve_prod,
                                    descripcion: producto.descr_prod,
                                    cantidad: producto.cantidad,
                                    unidad: producto.unidad,
                                    ulocation: nivel.ulocation,
                                    sucursal: sucursal.descripcion,
                                    almacen: almacen.descripcion,
                                    pasillo: pasillo ? `Pasillo ${pasillo.num_pasillo}` : 'Rack Directo',
                                    rack: rack.nombre,
                                    columna: columna.nombre,
                                    nivel: nivel.nombre,
                                    tarimaId: tarima.id,
                                    tarimaCode: tarima.codigo,
                                    matchType: matchesCodigo ? 'codigo' : 'descripcion',
                                    path: {
                                        sucursal: sucursal,
                                        almacen: almacen,
                                        pasillo: pasillo,
                                        rack: rack,
                                        columna: columna,
                                        nivel: nivel,
                                        tarima: tarima
                                    }
                                });
                            }
                        });
                    }
                });
            }
        });
    });
}

function showSearchResults(searchTerm, results) {
    const content = document.getElementById('contentDisplay');
    const title = document.getElementById('contentTitle');
    const itemCount = document.getElementById('itemCount');

    const totalResults = results.productos.length + results.ubicaciones.length;

    title.textContent = `Resultados de Búsqueda: "${searchTerm}"`;
    itemCount.textContent = `${totalResults} resultados encontrados (${results.productos.length} productos, ${results.ubicaciones.length} ubicaciones)`;

    if (totalResults === 0) {
        content.innerHTML = `
            <div class="empty-state">
                <div class="empty-icon">
                    <i class="fas fa-search"></i>
                </div>
                <div class="empty-title">Sin resultados</div>
                <div class="empty-message">
                    No se encontraron productos o ubicaciones que coincidan con "<strong>${searchTerm}</strong>"
                </div>
                <div style="margin-top: 20px; font-size: 14px; color: #718096;">
                    <p>Búsqueda realizada en:</p>
                    <ul style="text-align: left; display: inline-block;">
                        <li>Códigos de producto (SKU)</li>
                        <li>Descripciones de producto</li>
                        <li>Ubicaciones (ulocation)</li>
                    </ul>
                </div>
            </div>
        `;
        return;
    }

    let resultsHtml = '<div class="search-results">';

    // Mostrar resultados de productos
    if (results.productos.length > 0) {
        resultsHtml += `
            <div class="results-section">
                <h3 class="results-section-title">
                    <i class="fas fa-box" style="color: #4299e1;"></i>
                    Productos Encontrados (${results.productos.length})
                </h3>
                <div class="visual-grid">
        `;

        results.productos.forEach(producto => {
            const matchIcon = producto.matchType === 'codigo' ? 'fa-barcode' : 'fa-tag';
            const matchColor = producto.matchType === 'codigo' ? '#e53e3e' : '#38a169';

            resultsHtml += `
                <div class="item-card search-result-card" onclick="navigateToProduct('${producto.path.sucursal.id}', '${producto.path.almacen.id}', ${producto.path.pasillo ? `'${producto.path.pasillo.id}'` : 'null'}, '${producto.path.rack.id}', '${producto.path.columna.id}', '${producto.path.nivel.id}', '${producto.path.tarima.id}')">
                    <div class="item-header">
                        <div class="item-icon" style="background: ${matchColor};">
                            <i class="fas ${matchIcon}"></i>
                        </div>
                        <div style="flex: 1;">
                            <div class="item-title">${producto.codigo}</div>
                            <div class="item-subtitle">${producto.descripcion.length > 40 ? producto.descripcion.substring(0, 40) + '...' : producto.descripcion}</div>
                        </div>
                        <div class="search-match-badge">
                            ${producto.matchType === 'codigo' ? 'SKU' : 'DESC'}
                        </div>
                    </div>
                    <div class="item-details">
                        <div class="item-detail">
                            <span class="detail-label">Cantidad</span>
                            <span class="detail-value">${producto.cantidad} ${producto.unidad}</span>
                        </div>
                        <div class="item-detail">
                            <span class="detail-label">Ubicación</span>
                            <span class="detail-value">${producto.ulocation}</span>
                        </div>
                        <div class="item-detail">
                            <span class="detail-label">Almacén</span>
                            <span class="detail-value">${producto.almacen}</span>
                        </div>
                    </div>
                    <div class="location-path">
                        <i class="fas fa-map-marker-alt"></i>
                        ${producto.sucursal} → ${producto.almacen} → ${producto.pasillo} → ${producto.rack} → ${producto.columna} → ${producto.nivel}
                    </div>
                </div>
            `;
        });

        resultsHtml += '</div></div>';
    }

    // Mostrar resultados de ubicaciones
    if (results.ubicaciones.length > 0) {
        resultsHtml += `
            <div class="results-section">
                <h3 class="results-section-title">
                    <i class="fas fa-map-marker-alt" style="color: #ed8936;"></i>
                    Ubicaciones Encontradas (${results.ubicaciones.length})
                </h3>
                <div class="visual-grid">
        `;

        results.ubicaciones.forEach(ubicacion => {
            resultsHtml += `
                <div class="item-card search-result-card" onclick="navigateToUbicacion('${ubicacion.path.sucursal.id}', '${ubicacion.path.almacen.id}', ${ubicacion.path.pasillo ? `'${ubicacion.path.pasillo.id}'` : 'null'}, '${ubicacion.path.rack.id}', '${ubicacion.path.columna.id}', '${ubicacion.path.nivel.id}')">
                    <div class="item-header">
                        <div class="item-icon" style="background: #ed8936;">
                            <i class="fas fa-map-marker-alt"></i>
                        </div>
                        <div>
                            <div class="item-title">${ubicacion.ulocation}</div>
                            <div class="item-subtitle">${ubicacion.nivel}</div>
                        </div>
                        <div class="search-match-badge location-badge">
                            LOC
                        </div>
                    </div>
                    <div class="item-details">
                        <div class="item-detail">
                            <span class="detail-label">Tarimas</span>
                            <span class="detail-value">${ubicacion.tarimasCount}</span>
                        </div>
                        <div class="item-detail">
                            <span class="detail-label">Estado</span>
                            <span class="detail-value" style="color: ${ubicacion.tarimasCount > 0 ? '#38a169' : '#e53e3e'}">${ubicacion.tarimasCount > 0 ? 'Ocupado' : 'Vacío'}</span>
                        </div>
                        <div class="item-detail">
                            <span class="detail-label">Rack</span>
                            <span class="detail-value">${ubicacion.rack}</span>
                        </div>
                    </div>
                    <div class="location-path">
                        <i class="fas fa-map-marker-alt"></i>
                        ${ubicacion.sucursal} → ${ubicacion.almacen} → ${ubicacion.pasillo} → ${ubicacion.rack} → ${ubicacion.columna} → ${ubicacion.nivel}
                    </div>
                </div>
            `;
        });

        resultsHtml += '</div></div>';
    }

    resultsHtml += '</div>';
    content.innerHTML = resultsHtml;
}
// Funciones de navegación desde resultados de búsqueda
function navigateToProduct(sucursalId, almacenId, pasilloId, rackId, columnaId, nivelId, tarimaId) {
    // Navegar hasta la tarima específica
    const sucursal = inventoryData.sucursales.find(s => s.id == sucursalId);
    const almacen = sucursal.almacenes.find(a => a.id == almacenId);

    currentSelection.sucursal = sucursal;
    currentSelection.almacen = almacen;

    if (pasilloId && pasilloId !== 'null') {
        const pasillo = almacen.pasillos.find(p => p.id == pasilloId);
        const rack = pasillo.racks.find(r => r.id == rackId);
        currentSelection.pasillo = pasillo;
        currentSelection.rack = rack;
    } else {
        const rack = almacen.racks_sin_pasillo.find(r => r.id == rackId);
        currentSelection.pasillo = null;
        currentSelection.rack = rack;
    }

    updateSelectsFromCurrentSelection();
    updateBreadcrumb();
    showTarimaDetail(tarimaId);
}

function navigateToUbicacion(sucursalId, almacenId, pasilloId, rackId, columnaId, nivelId) {
    // Navegar hasta el nivel específico
    const sucursal = inventoryData.sucursales.find(s => s.id == sucursalId);
    const almacen = sucursal.almacenes.find(a => a.id == almacenId);

    currentSelection.sucursal = sucursal;
    currentSelection.almacen = almacen;

    if (pasilloId && pasilloId !== 'null') {
        const pasillo = almacen.pasillos.find(p => p.id == pasilloId);
        const rack = pasillo.racks.find(r => r.id == rackId);
        currentSelection.pasillo = pasillo;
        currentSelection.rack = rack;
    } else {
        const rack = almacen.racks_sin_pasillo.find(r => r.id == rackId);
        currentSelection.pasillo = null;
        currentSelection.rack = rack;
    }

    updateSelectsFromCurrentSelection();
    updateBreadcrumb();
    showNivelDetail(nivelId);
}

function updateSelectsFromCurrentSelection() {
    // Actualizar los selects para reflejar la navegación
    if (currentSelection.sucursal) {
        updateSelectDisplay('sucursalSelect', currentSelection.sucursal.descripcion);
        populateAlmacenes();

        if (currentSelection.almacen) {
            updateSelectDisplay('almacenSelect', currentSelection.almacen.descripcion);
            populatePasillos();

            if (currentSelection.pasillo) {
                updateSelectDisplay('pasilloSelect', `Pasillo ${currentSelection.pasillo.num_pasillo}`);
                populateRacks();

                if (currentSelection.rack) {
                    updateSelectDisplay('rackSelect', currentSelection.rack.nombre);
                }
            } else if (currentSelection.rack) {
                updateSelectDisplay('pasilloSelect', `Rack Directo: ${currentSelection.rack.nombre}`);
                disableSelect('rackSelect');
            }
        }
    }
}

// CSS adicional para los resultados de búsqueda (agregar al final del CSS existente)
const searchStyles = `
<style>
.search-results {
    padding: 20px 0;
}

.results-section {
    margin-bottom: 40px;
}

.results-section-title {
    font-size: 20px;
    font-weight: 600;
    color: #2d3748;
    margin-bottom: 20px;
    padding-bottom: 10px;
    border-bottom: 2px solid #e2e8f0;
    display: flex;
    align-items: center;
    gap: 10px;
}

.search-result-card {
    position: relative;
    transition: all 0.3s ease;
}

.search-result-card:hover {
    transform: translateY(-2px);
    box-shadow: 0 8px 25px rgba(0, 0, 0, 0.1);
}

.search-match-badge {
    position: absolute;
    top: 10px;
    right: 10px;
    background: #4299e1;
    color: white;
    padding: 4px 8px;
    border-radius: 12px;
    font-size: 10px;
    font-weight: bold;
    text-transform: uppercase;
}

.location-badge {
    background: #ed8936 !important;
}

.location-path {
    margin-top: 12px;
    padding: 8px 12px;
    background: #f7fafc;
    border-radius: 6px;
    font-size: 12px;
    color: #718096;
    border-left: 3px solid #4299e1;
}

.location-path i {
    color: #4299e1;
    margin-right: 6px;
}

.search-input:focus {
    outline: none;
    border-color: #4299e1;
    box-shadow: 0 0 0 3px rgba(66, 153, 225, 0.1);
}
</style>
`;

// Agregar los estilos al documento
if (!document.getElementById('searchStyles')) {
    const styleElement = document.createElement('div');
    styleElement.id = 'searchStyles';
    styleElement.innerHTML = searchStyles;
    document.head.appendChild(styleElement);
}

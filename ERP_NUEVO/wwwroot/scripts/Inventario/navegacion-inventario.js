
let inventoryData = null;
let currentSelection = {
    sucursal: null,
    almacen: null,
    pasillo: null,
    rack: null
};
// Initialize app
document.addEventListener('DOMContentLoaded', function () {
    loadData();
    setupEventListeners();
});

function setupEventListeners() {
    // Setup custom selects
    setupCustomSelect('sucursalSelect', handleSucursalChange);
    setupCustomSelect('almacenSelect', handleAlmacenChange);
    setupCustomSelect('pasilloSelect', handlePasilloChange);
    setupCustomSelect('rackSelect', handleRackChange);
}

function setupCustomSelect(selectId, changeHandler) {
    const select = document.getElementById(selectId);
    const display = select.querySelector('.select-display');
    const dropdown = select.querySelector('.select-dropdown');

    display.addEventListener('click', function () {
        if (display.classList.contains('disabled')) return;

        // Close other dropdowns
        document.querySelectorAll('.select-display.active').forEach(el => {
            if (el !== display) {
                el.classList.remove('active');
                el.parentNode.querySelector('.select-dropdown').classList.remove('show');
            }
        });

        display.classList.toggle('active');
        dropdown.classList.toggle('show');
    });

    // Close dropdown when clicking outside
    document.addEventListener('click', function (e) {
        if (!select.contains(e.target)) {
            display.classList.remove('active');
            dropdown.classList.remove('show');
        }
    });
}

async function loadData() {
    showLoading();
    try {
        const response = await fetch('/Consulta/GetAllData', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/x-www-form-urlencoded',
            }
        });

        if (!response.ok) {
            throw new Error('Error al cargar los datos');
        }

        const result = await response.json();
        console.log((result[0].jsonb_pretty).l)
        inventoryData = JSON.parse(result[0].jsonb_pretty);

        populateSucursales();
        updateStats();
        showSucursalesView();

    } catch (error) {
        console.error('Error:', error);
        showError('Error al cargar los datos del inventario');
    }
}

function showLoading() {
    const content = document.getElementById('contentDisplay');
    content.innerHTML = `
    <div class="loading-state">
        <div class="loading-spinner"></div>
        <p>Cargando inventario...</p>
    </div>
    `;
}

function showError(message) {
    const content = document.getElementById('contentDisplay');
    content.innerHTML = `
    <div class="empty-state">
        <div class="empty-icon">
            <i class="fas fa-exclamation-triangle"></i>
        </div>
        <div class="empty-title">Error</div>
        <div class="empty-message">${message}</div>
    </div>
    `;
}

function populateSucursales() {
    const select = document.getElementById('sucursalSelect');
    const dropdown = select.querySelector('.select-dropdown');
    const display = select.querySelector('.select-display');

    if (!inventoryData || !inventoryData.sucursales) return;

    dropdown.innerHTML = inventoryData.sucursales.map(sucursal => `
    <div class="select-option" data-value="${sucursal.id}">
        <i class="fas fa-building" style="color: #f56565;"></i>
        <div>
            <div style="font-weight: 600;">${sucursal.descripcion}</div>
            <div style="font-size: 0.8rem; color: #718096;">${sucursal.cve}</div>
        </div>
    </div>
    `).join('');

    // Enable select
    display.classList.remove('disabled');

    // Add click handlers
    dropdown.querySelectorAll('.select-option').forEach(option => {
        option.addEventListener('click', function () {
            const sucursalId = this.dataset.value;
            handleSucursalChange(sucursalId);
        });
    });
}

function handleSucursalChange(sucursalId) {
    const sucursal = inventoryData.sucursales.find(s => s.id == sucursalId);
    if (!sucursal) return;

    currentSelection.sucursal = sucursal;
    currentSelection.almacen = null;
    currentSelection.pasillo = null;
    currentSelection.rack = null;

    updateSelectDisplay('sucursalSelect', sucursal.descripcion);
    resetSelect('almacenSelect');
    resetSelect('pasilloSelect');
    resetSelect('rackSelect');

    populateAlmacenes();
    updateBreadcrumb();
    showAlmacenesView();
}

function populateAlmacenes() {
    const select = document.getElementById('almacenSelect');
    const dropdown = select.querySelector('.select-dropdown');
    const display = select.querySelector('.select-display');

    if (!currentSelection.sucursal || !currentSelection.sucursal.almacenes) return;

    dropdown.innerHTML = currentSelection.sucursal.almacenes.map(almacen => `
    <div class="select-option" data-value="${almacen.id}">
        <i class="fas fa-warehouse" style="color: #ed8936;"></i>
        <div>
            <div style="font-weight: 600;">${almacen.descripcion}</div>
            <div style="font-size: 0.8rem; color: #718096;">${almacen.cve} - ${almacen.tipo}</div>
        </div>
    </div>
    `).join('');

    display.classList.remove('disabled');

    dropdown.querySelectorAll('.select-option').forEach(option => {
        option.addEventListener('click', function () {
            const almacenId = this.dataset.value;
            handleAlmacenChange(almacenId);
        });
    });
}

function handleAlmacenChange(almacenId) {
    const almacen = currentSelection.sucursal.almacenes.find(a => a.id == almacenId);
    if (!almacen) return;

    currentSelection.almacen = almacen;
    currentSelection.pasillo = null;
    currentSelection.rack = null;

    updateSelectDisplay('almacenSelect', almacen.descripcion);
    resetSelect('pasilloSelect');
    resetSelect('rackSelect');

    populatePasillos();
    updateBreadcrumb();
    showPasillosView();
}

function populatePasillos() {
    const select = document.getElementById('pasilloSelect');
    const dropdown = select.querySelector('.select-dropdown');
    const display = select.querySelector('.select-display');

    if (!currentSelection.almacen) return;

    let options = [];

    // Add pasillos
    if (currentSelection.almacen.pasillos) {
        options = currentSelection.almacen.pasillos.map(pasillo => `
                                <div class="select-option" data-value="${pasillo.id}" data-type="pasillo">
                                    <i class="fas fa-road" style="color: #38a169;"></i>
                                    <div>
                                        <div style="font-weight: 600;">Pasillo ${pasillo.num_pasillo}</div>
                                        <div style="font-size: 0.8rem; color: #718096;">${pasillo.cve}</div>
                                    </div>
                                </div>
                            `);
    }

    // Add direct racks
    if (currentSelection.almacen.racks_sin_pasillo) {
        const directRacks = currentSelection.almacen.racks_sin_pasillo.map(rack => `
    <div class="select-option" data-value="${rack.id}" data-type="rack">
        <i class="fas fa-layer-group" style="color: #4299e1;"></i>
        <div>
            <div style="font-weight: 600;">Rack Directo: ${rack.nombre}</div>
            <div style="font-size: 0.8rem; color: #718096;">#${rack.num_rack} - ${rack.tipo}</div>
        </div>
    </div>
    `);
        options = options.concat(directRacks);
    }

    dropdown.innerHTML = options.join('');
    display.classList.remove('disabled');

    dropdown.querySelectorAll('.select-option').forEach(option => {
        option.addEventListener('click', function () {
            const id = this.dataset.value;
            const type = this.dataset.type;
            if (type === 'pasillo') {
                handlePasilloChange(id);
            } else {
                handleDirectRackChange(id);
            }
        });
    });
}

function handlePasilloChange(pasilloId) {
    const pasillo = currentSelection.almacen.pasillos.find(p => p.id == pasilloId);
    if (!pasillo) return;

    currentSelection.pasillo = pasillo;
    currentSelection.rack = null;

    updateSelectDisplay('pasilloSelect', `Pasillo ${pasillo.num_pasillo}`);
    resetSelect('rackSelect');

    populateRacks();
    updateBreadcrumb();
    showRacksView();
}

function handleDirectRackChange(rackId) {
    const rack = currentSelection.almacen.racks_sin_pasillo.find(r => r.id == rackId);
    if (!rack) return;

    currentSelection.rack = rack;
    currentSelection.pasillo = null; // Clear pasillo since this is a direct rack

    updateSelectDisplay('pasilloSelect', `Rack Directo: ${rack.nombre}`);
    disableSelect('rackSelect');

    updateBreadcrumb();
    showRackDetailsView(rack);
}

function populateRacks() {
    const select = document.getElementById('rackSelect');
    const dropdown = select.querySelector('.select-dropdown');
    const display = select.querySelector('.select-display');

    if (!currentSelection.pasillo || !currentSelection.pasillo.racks) return;

    dropdown.innerHTML = currentSelection.pasillo.racks.map(rack => `
    <div class="select-option" data-value="${rack.id}">
        <i class="fas fa-layer-group" style="color: #4299e1;"></i>
        <div>
            <div style="font-weight: 600;">${rack.nombre}</div>
            <div style="font-size: 0.8rem; color: #718096;">#${rack.num_rack} - ${rack.tipo} - ${rack.lado}</div>
        </div>
    </div>
    `).join('');

    display.classList.remove('disabled');

    dropdown.querySelectorAll('.select-option').forEach(option => {
        option.addEventListener('click', function () {
            const rackId = this.dataset.value;
            handleRackChange(rackId);
        });
    });
}

function handleRackChange(rackId) {
    const rack = currentSelection.pasillo.racks.find(r => r.id == rackId);
    if (!rack) return;

    currentSelection.rack = rack;

    updateSelectDisplay('rackSelect', rack.nombre);
    updateBreadcrumb();
    showRackDetailsView(rack);
}

function updateSelectDisplay(selectId, text) {
    const select = document.getElementById(selectId);
    const textElement = select.querySelector('.select-text');
    const display = select.querySelector('.select-display');
    const dropdown = select.querySelector('.select-dropdown');

    textElement.textContent = text;
    //textElement.classList.remove('placeholder');
    display.classList.remove('active');
    dropdown.classList.remove('show');
}

function resetSelect(selectId) {
    const select = document.getElementById(selectId);
    const textElement = select.querySelector('.select-text');
    const display = select.querySelector('.select-display');
    const dropdown = select.querySelector('.select-dropdown');

    const placeholders = {
        'almacenSelect': 'Seleccionar almacén...',
        'pasilloSelect': 'Seleccionar pasillo...',
        'rackSelect': 'Seleccionar rack...'
    };

    textElement.textContent = placeholders[selectId] || 'Seleccionar...';
    //textElement.classList.add('placeholder');
    display.classList.add('disabled');
    display.classList.remove('active');
    dropdown.classList.remove('show');
    dropdown.innerHTML = '';
}

function disableSelect(selectId) {
    const select = document.getElementById(selectId);
    const display = select.querySelector('.select-display');
    display.classList.add('disabled');
}

function updateBreadcrumb() {
    const breadcrumb = document.getElementById('breadcrumb');
    let items = [`
    <div class="breadcrumb-item ${!currentSelection.sucursal ? 'active' : ''}" onclick="resetNavigation()">
        <i class="fas fa-home"></i>
        <span>Inicio</span>
    </div>
    `];

    if (currentSelection.sucursal) {
        items.push(`<span class="breadcrumb-separator">›</span>`);
        items.push(`
    <div class="breadcrumb-item ${!currentSelection.almacen ? 'active' : ''}" onclick="goToSucursal()">
        <i class="fas fa-building"></i>
        <span>${currentSelection.sucursal.descripcion}</span>
    </div>
    `);
    }

    if (currentSelection.almacen) {
        items.push(`<span class="breadcrumb-separator">›</span>`);
        items.push(`
    <div class="breadcrumb-item ${!currentSelection.pasillo && !currentSelection.rack ? 'active' : ''}" onclick="goToAlmacen()">
        <i class="fas fa-warehouse"></i>
        <span>${currentSelection.almacen.descripcion}</span>
    </div>
    `);
    }

    if (currentSelection.pasillo) {
        items.push(`<span class="breadcrumb-separator">›</span>`);
        items.push(`
    <div class="breadcrumb-item ${!currentSelection.rack ? 'active' : ''}" onclick="goToPasillo()">
        <i class="fas fa-road"></i>
        <span>Pasillo ${currentSelection.pasillo.num_pasillo}</span>
    </div>
    `);
    }

    if (currentSelection.rack) {
        items.push(`<span class="breadcrumb-separator">›</span>`);
        items.push(`
    <div class="breadcrumb-item active">
        <i class="fas fa-layer-group"></i>
        <span>${currentSelection.rack.nombre}</span>
    </div>
    `);
    }

    breadcrumb.innerHTML = items.join('');
}

// Card selection functions
function selectSucursalCard(sucursalId) {
    const select = document.getElementById('sucursalSelect');
    const dropdown = select.querySelector('.select-dropdown');
    const option = dropdown.querySelector(`[data-value="${sucursalId}"]`);
    if (option) {
        option.click();
    }
}

function selectAlmacenCard(almacenId) {
    const select = document.getElementById('almacenSelect');
    const dropdown = select.querySelector('.select-dropdown');
    const option = dropdown.querySelector(`[data-value="${almacenId}"]`);
    if (option) {
        option.click();
    }
}

function selectPasilloCard(pasilloId) {
    const select = document.getElementById('pasilloSelect');
    const dropdown = select.querySelector('.select-dropdown');
    const option = dropdown.querySelector(`[data-value="${pasilloId}"][data-type="pasillo"]`);
    if (option) {
        option.click();
    }
}

function selectDirectRackCard(rackId) {
    const select = document.getElementById('pasilloSelect');
    const dropdown = select.querySelector('.select-dropdown');
    const option = dropdown.querySelector(`[data-value="${rackId}"][data-type="rack"]`);
    if (option) {
        option.click();
    }
}

function selectRackCard(rackId) {
    const select = document.getElementById('rackSelect');
    const dropdown = select.querySelector('.select-dropdown');
    const option = dropdown.querySelector(`[data-value="${rackId}"]`);
    if (option) {
        option.click();
    }
}

// Navigation functions
function resetNavigation() {
    currentSelection = {
        sucursal: null,
        almacen: null,
        pasillo: null,
        rack: null
    };

    // Reset all selects
    resetSelect('almacenSelect');
    resetSelect('pasilloSelect');
    resetSelect('rackSelect');

    // Reset sucursal select
    const sucursalSelect = document.getElementById('sucursalSelect');
    const sucursalText = sucursalSelect.querySelector('.select-text');
    sucursalText.textContent = 'Seleccionar sucursal...';
    //sucursalText.classList.add('placeholder');

    updateBreadcrumb();
    showSucursalesView();
}

function goToSucursal() {
    if (!currentSelection.sucursal) return;

    currentSelection.almacen = null;
    currentSelection.pasillo = null;
    currentSelection.rack = null;

    resetSelect('almacenSelect');
    resetSelect('pasilloSelect');
    resetSelect('rackSelect');

    populateAlmacenes();
    updateBreadcrumb();
    showAlmacenesView();
}

function goToAlmacen() {
    if (!currentSelection.almacen) return;

    currentSelection.pasillo = null;
    currentSelection.rack = null;

    resetSelect('pasilloSelect');
    resetSelect('rackSelect');

    populatePasillos();
    updateBreadcrumb();
    showPasillosView();
}

function goToPasillo() {
    if (!currentSelection.pasillo) return;

    currentSelection.rack = null;
    resetSelect('rackSelect');

    populateRacks();
    updateBreadcrumb();
    showRacksView();
}

function updateStats() {
    if (!inventoryData || !inventoryData.sucursales) return;

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
                // Count racks in pasillos
                if (almacen.pasillos) {
                    almacen.pasillos.forEach(pasillo => {
                        if (pasillo.racks) {
                            stats.racks += pasillo.racks.length;

                            pasillo.racks.forEach(rack => {
                                stats.tarimas += countTarifasInRack(rack);
                            });
                        }
                    });
                }

                // Count direct racks
                if (almacen.racks_sin_pasillo) {
                    stats.racks += almacen.racks_sin_pasillo.length;

                    almacen.racks_sin_pasillo.forEach(rack => {
                        stats.tarimas += countTarifasInRack(rack);
                    });
                }
            });
        }
    });

    // Update stat displays
    document.getElementById('sucursalesCount').textContent = stats.sucursales.toLocaleString();
    document.getElementById('almacenesCount').textContent = stats.almacenes.toLocaleString();
    document.getElementById('racksCount').textContent = stats.racks.toLocaleString();
    document.getElementById('tarimasCount').textContent = stats.tarimas.toLocaleString();
}

function countTarifasInRack(rack) {
    let count = 0;
    if (rack.columnas) {
        rack.columnas.forEach(columna => {
            if (columna.niveles) {
                columna.niveles.forEach(nivel => {
                    count += nivel.tarimas?.length || 0;
                });
            }
        });
    }
    return count;
}

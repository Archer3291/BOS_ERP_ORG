// ============================================================
// search-route.js — Búsqueda, sugerencias y gestión de rutas
// ============================================================

// ─────────────────────────────────────────────────────────
// Búsqueda
// ─────────────────────────────────────────────────────────

function switchSearchTab(mode) {
    currentSearchMode = mode;

    document.querySelectorAll('.search-tab').forEach(tab => tab.classList.remove('active'));
    document.querySelector(`[data-tab="${mode}"]`).classList.add('active');

    const input = document.getElementById('searchInput');
    input.placeholder = mode === 'location'
        ? 'Buscar ubicación (ej: R1-A1-T1)'
        : 'Buscar producto (ej: 0-355/V90)';
    input.value = '';
    hideSuggestions();
}

function setupSearch() {
    const input = document.getElementById('searchInput');

    input.addEventListener('input', function () {
        const value = this.value.trim();
        if (!value) { hideSuggestions(); return; }

        let matches = [];

        if (currentSearchMode === 'location') {
            matches = Object.keys(inventoryData)
                .filter(key => (inventoryData[key].ulocation || '').toUpperCase().includes(value.toUpperCase()))
                .slice(0, 10)
                .map(key => ({
                    type: 'location',
                    location: key,
                    displayLocation: inventoryData[key].ulocation || key,
                    data: inventoryData[key]
                }));
        } else {
            Object.keys(inventoryData).forEach(loc => {
                const data = inventoryData[loc];
                const hasMatch = data.productos.some(p =>
                    p.cve.toLowerCase().includes(value.toLowerCase()) ||
                    p.descripcion.toLowerCase().includes(value.toLowerCase())
                );
                if (hasMatch) {
                    matches.push({
                        type: 'product',
                        location: loc,
                        data,
                        matchedProducts: data.productos.filter(p =>
                            p.cve.toLowerCase().includes(value.toLowerCase()) ||
                            p.descripcion.toLowerCase().includes(value.toLowerCase())
                        )
                    });
                }
            });
            matches = matches.slice(0, 10);
        }

        // Excluir ubicaciones ya en la ruta
        matches = matches.filter(m => !currentRoute.find(r => r.name === m.location));

        if (!matches.length) { hideSuggestions(); return; }
        displaySuggestions(matches);
    });

    input.addEventListener('keydown', e => { if (e.key === 'Enter') addFromSearch(); });
}

function displaySuggestions(matches) {
    const container = document.getElementById('suggestions');

    const html = matches.map(match => {
        const status    = getStockStatus(match.data.stock, match.data.capacity);
        const badgeCls  = { high: 'badge-high', medium: 'badge-medium', low: 'badge-low', empty: 'badge-empty' }[status];
        const icon      = match.type === 'location' ? '📍' : '📦';

        let title, subtitle;
        if (match.type === 'location') {
            title    = match.displayLocation || match.data.ulocation || match.location;
            subtitle = `${match.data.almacen} (${match.data.sucursal}) • ${match.data.category}`;
        } else {
            const prod = match.matchedProducts?.[0] || { cve: 'N/A', descripcion: match.data.product || 'Sin producto' };
            title    = prod.descripcion;
            subtitle = `${prod.cve} • ${match.data.ulocation || match.location} — ${match.data.almacen}`;
        }

        return `
            <div class="suggestion-item" onclick="selectSuggestion('${match.location}')">
                <div class="suggestion-icon">${icon}</div>
                <div class="suggestion-content">
                    <div class="suggestion-title">${title}</div>
                    <div class="suggestion-subtitle">${subtitle}</div>
                </div>
                <div class="suggestion-badge ${badgeCls}">${Math.round(match.data.stock)} uds</div>
            </div>
        `;
    }).join('');

    container.innerHTML = html;
    container.classList.add('show');
}

function hideSuggestions() {
    document.getElementById('suggestions').classList.remove('show');
}

function selectSuggestion(locationName) {
    autoSelectLocationContext(locationName);
    selectLocation(locationName);
    addLocationToRoute(locationName);
    document.getElementById('searchInput').value = '';
    hideSuggestions();
}

function addFromSearch() {
    const input = document.getElementById('searchInput');
    const value = input.value.trim();
    if (!value) return;

    let locationToAdd = null;

    if (currentSearchMode === 'location') {
        const upper = value.toUpperCase();
        locationToAdd = Object.keys(inventoryData).find(key =>
            (inventoryData[key].ulocation || '').toUpperCase() === upper
        ) || null;
    } else {
        locationToAdd = Object.keys(inventoryData).find(key =>
            inventoryData[key].productos.some(p =>
                p.cve.toLowerCase().includes(value.toLowerCase()) ||
                p.descripcion.toLowerCase().includes(value.toLowerCase())
            )
        );
    }

    if (locationToAdd) {
        autoSelectLocationContext(locationToAdd);
        selectLocation(locationToAdd);
        addLocationToRoute(locationToAdd);
        input.value = '';
        hideSuggestions();
    } else {
        alert('No se encontró ninguna coincidencia.');
    }
}

function getStockStatus(stock, capacity) {
    if (stock === 0) return 'empty';
    const pct = (stock / capacity) * 100;
    if (pct >= 70) return 'high';
    if (pct >= 30) return 'medium';
    return 'low';
}

// ─────────────────────────────────────────────────────────
// Gestión de rutas
// ─────────────────────────────────────────────────────────

function addLocationToRoute(locationKey) {
    if (!inventoryData[locationKey]) { alert('Ubicación no encontrada'); return; }
    if (currentRoute.find(r => r.name === locationKey)) { alert('Esta ubicación ya está en la ruta'); return; }

    const data = inventoryData[locationKey];
    currentRoute.push({
        name: locationKey,
        displayName: data.ulocation || locationKey,
        coords: data.coords || [0, 0],
        product: data.product,
        productCount: data.productCount
    });

    updateRouteDisplay();
}

function removeLocationFromRoute(index) {
    currentRoute.splice(index, 1);
    updateRouteDisplay();
}

function clearRoute() {
    if (!currentRoute.length) return;
    if (confirm('¿Estás seguro de que quieres limpiar toda la ruta?')) {
        currentRoute = [];
        updateRouteDisplay();
    }
}

function optimizeRoute() {
    if (currentRoute.length < 2) { alert('Necesitas al menos 2 ubicaciones para optimizar la ruta'); return; }

    const optimized = [];
    const remaining = [...currentRoute];
    let current     = START_POINT.coords;

    while (remaining.length > 0) {
        let nearestIdx = 0;
        let nearestDist = calculateDistance(current, remaining[0].coords);

        for (let i = 1; i < remaining.length; i++) {
            const d = calculateDistance(current, remaining[i].coords);
            if (d < nearestDist) { nearestDist = d; nearestIdx = i; }
        }

        const nearest = remaining.splice(nearestIdx, 1)[0];
        optimized.push(nearest);
        current = nearest.coords;
    }

    currentRoute = optimized;
    updateRouteDisplay();
    alert('✅ Ruta optimizada');
}

function updateRouteDisplay() {
    const container = document.getElementById('routeStops');

    if (!currentRoute.length) {
        container.innerHTML = `
            <div class="empty-state" style="padding:20px;">
                <p style="font-size:13px;">Busca por ubicación o producto para crear tu ruta</p>
            </div>`;
    } else {
        container.innerHTML = currentRoute.map((stop, idx) => {
            const prev     = idx === 0 ? START_POINT.coords : currentRoute[idx - 1].coords;
            const distance = calculateDistance(prev, stop.coords);
            return `
                <div class="route-stop">
                    <div class="route-number">${idx + 1}</div>
                    <div class="route-stop-info">
                        <div class="route-stop-name">${stop.displayName || stop.name}</div>
                        <div class="route-stop-product">${stop.productCount || 0} productos</div>
                    </div>
                    <button class="remove-btn" onclick="removeLocationFromRoute(${idx})">×</button>
                </div>`;
        }).join('');
    }

    const totalDistance = calculateTotalDistance([START_POINT, ...currentRoute]);
    document.getElementById('totalStops').textContent    = currentRoute.length;
    document.getElementById('totalDistance').textContent = Math.round(totalDistance * 2) + ' m';
    document.getElementById('estimatedTime').textContent = Math.ceil((totalDistance * 2) / 50) + ' min';

    if (currentWarehouse) updateChartForWarehouse();
}

// ─────────────────────────────────────────────────────────
// Distancias
// ─────────────────────────────────────────────────────────

function calculateDistance(c1, c2) {
    const dx = c2[0] - c1[0];
    const dy = c2[1] - c1[1];
    return Math.sqrt(dx * dx + dy * dy);
}

function calculateTotalDistance(route) {
    if (route.length < 2) return 0;
    let total = 0;
    for (let i = 0; i < route.length - 1; i++) {
        total += calculateDistance(route[i].coords, route[i + 1].coords);
    }
    return total;
}

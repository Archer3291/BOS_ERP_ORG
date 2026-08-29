// ============================================================
// details.js — Panel de información y cambio de vistas
// ============================================================

// ─────────────────────────────────────────────────────────
// Cambio de vistas (Plano / Ocupación)
// ─────────────────────────────────────────────────────────

function switchView(view) {
    currentView = view;

    document.querySelectorAll('.view-tab').forEach(tab => tab.classList.remove('active'));
    event.target.classList.add('active');

    document.querySelectorAll('.chart-view').forEach(v => v.classList.remove('active'));

    if (view === 'map') {
        document.getElementById('mapView').classList.add('active');
        if (mainChart) mainChart.resize();
    } else {
        document.getElementById('treemapView').classList.add('active');
        if (treemapChart) treemapChart.resize();
    }
}

// ─────────────────────────────────────────────────────────
// Selección y panel de detalles
// ─────────────────────────────────────────────────────────

function selectLocation(locationKey) {
    selectedLocation = locationKey;
    const data       = inventoryData[locationKey];
    if (!data) return;

    const displayLocation = data.ulocation || locationKey;
    const percentage      = ((data.stock / data.capacity) * 100).toFixed(1);
    const inRoute         = currentRoute.find(r => r.name === locationKey);

    let productosHTML = '';
    if (data.productos && data.productos.length > 0) {
        productosHTML = `
            <div class="product-list">
                ${data.productos.map(p => `
                    <div class="product-item">
                        <span class="product-code">${p.cve}</span><br/>
                        <span style="font-size:11px;color:#7f8c8d;">${p.descripcion}</span><br/>
                        <span class="product-qty">Cantidad: ${p.cantidad}</span>
                    </div>
                `).join('')}
            </div>`;
    }

    document.getElementById('detailsContent').innerHTML = `
        <div class="info-section">
            <h3>Información General</h3>
            <div class="info-row">
                <span class="info-label">Ubicación</span>
                <span class="info-value">${displayLocation}</span>
            </div>
            <div class="info-row">
                <span class="info-label">Sucursal</span>
                <span class="info-value">${data.sucursal}</span>
            </div>
            <div class="info-row">
                <span class="info-label">Almacén</span>
                <span class="info-value">${data.almacen}</span>
            </div>
            <div class="info-row">
                <span class="info-label">Rack</span>
                <span class="info-value">${data.rack}</span>
            </div>
            <div class="info-row">
                <span class="info-label">Columna</span>
                <span class="info-value">${data.columna}</span>
            </div>
            <div class="info-row">
                <span class="info-label">Nivel</span>
                <span class="info-value">${data.nivel}</span>
            </div>
        </div>

        <div class="info-section">
            <h3>Estado del Inventario</h3>
            <div class="info-row">
                <span class="info-label">Stock Actual</span>
                <span class="info-value">${Math.round(data.stock)} unidades</span>
            </div>
            <div class="info-row">
                <span class="info-label">Capacidad Máxima</span>
                <span class="info-value">${data.capacity} unidades</span>
            </div>
            <div class="info-row">
                <span class="info-label">Ocupación</span>
                <span class="info-value">${percentage}%</span>
            </div>
            <div class="info-row">
                <span class="info-label">Productos</span>
                <span class="info-value">${data.productCount || 0}</span>
            </div>
        </div>

        ${data.productos && data.productos.length > 0 ? `
        <div class="info-section">
            <h3>Productos en esta ubicación</h3>
            ${productosHTML}
        </div>
        ` : ''}

        <div style="margin-top:20px;">
            ${!inRoute
                ? `<button class="btn btn-primary" onclick="addLocationToRoute('${locationKey}')" style="width:100%;">
                       ➕ Agregar a Ruta
                   </button>`
                : `<button class="btn btn-danger" onclick="removeLocationFromRoute(${currentRoute.indexOf(inRoute)})" style="width:100%;">
                       🗑️ Quitar de Ruta
                   </button>`
            }
        </div>
    `;
}

// ============================================================
// app-ocupacion.js — Estado global y arranque (Vista Rack)
// ============================================================

let warehouseData = null;   // JSON completo del servidor
let inventoryData = {};     // Mapa plano: inventoryKey → datos de nivel
let currentView = 'rack'; // 'rack' | 'list'
let searchQuery = '';      // Filtro de búsqueda activo
let selectedKey = null;    // inventoryKey del nivel seleccionado

// ─────────────────────────────────────────────────────────
// Resize del contenedor del gráfico si ECharts NO está en uso
// ─────────────────────────────────────────────────────────
window.addEventListener('resize', () => {
    // noop — el layout rack es CSS, no necesita resize manual
});

// ─────────────────────────────────────────────────────────
// Arranque
// ─────────────────────────────────────────────────────────
document.addEventListener('DOMContentLoaded', function () {
    loadWarehouseData();
});
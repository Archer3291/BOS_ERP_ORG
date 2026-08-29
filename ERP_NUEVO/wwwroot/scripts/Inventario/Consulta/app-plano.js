// ============================================================
// app-plano.js — Estado global e inicialización para Vista Plano
// ============================================================

// ─────────────────────────────────────────────────────────
// Estado global de la aplicación (vista plano)
// ─────────────────────────────────────────────────────────

let warehouseData = null;   // JSON completo del servidor
let inventoryData = {};     // Mapa plano: inventoryKey → datos de nivel

let mainChart = null;   // Instancia ECharts — vista plano

let currentRoute = [];     // Paradas seleccionadas
let selectedLocation = null;   // Última ubicación seleccionada
let currentSearchMode = 'location';  // 'location' | 'product'
let currentSucursal = null;   // Objeto sucursal activa
let currentWarehouse = null;   // Objeto almacén activo

const START_POINT = { name: 'Entrada', coords: [0, 0] };

// ─────────────────────────────────────────────────────────
// Eventos globales
// ─────────────────────────────────────────────────────────

window.addEventListener('resize', () => {
    if (mainChart) mainChart.resize();
});

document.addEventListener('click', function (e) {
    if (!e.target.closest('.input-group') && !e.target.closest('.suggestions')) {
        hideSuggestions();
    }
});

// ─────────────────────────────────────────────────────────
// Arranque
// ─────────────────────────────────────────────────────────

document.addEventListener('DOMContentLoaded', function () {
    loadWarehouseData();
});

console.log('🚀 Sistema de Gestión de Almacén - Vista Plano cargada');
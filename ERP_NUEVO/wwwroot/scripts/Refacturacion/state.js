// state.js — Estado compartido entre todos los archivos

var AppState = {
    currentStep: 1,
    selectedType: null,   // objeto de TIPOS_REFACTURACION
    selectedCFDI: null,   // objeto normalizado del CFDI seleccionado
    changes: {},          // cambios capturados en Step 4 (uso futuro)
    motivo: '01',         // motivo de cancelación SAT seleccionado
    adendaModo: 'solo-corregir',
    adendaSeleccionada: null,
    adendasDisponibles: [],
};
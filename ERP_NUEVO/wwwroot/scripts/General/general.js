function llenarSelect(selectId, datos, valueKey, textKey) {
    const select = document.getElementById(selectId);
    select.innerHTML = '<option value="">Selecciona una opción...</option>';
    datos.forEach(item => {
        const option = document.createElement('option');
        option.value = item[valueKey];
        option.textContent = item[textKey];
        select.appendChild(option);
    });
}

function llenarSelectTom(idSelect, datos, valueField, textField) {
    const select = document.getElementById(idSelect);
    if (!select || !Array.isArray(datos)) return;

    // Limpiar select actual
    select.innerHTML = '<option value="">Seleccione una opción</option>';

    datos.forEach(item => {
        const option = document.createElement('option');
        option.value = item[valueField];
        option.textContent = item[textField];
        select.appendChild(option);
    });
}

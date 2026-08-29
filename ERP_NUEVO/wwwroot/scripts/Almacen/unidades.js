document.getElementById('formUnidades').addEventListener('submit', function (e) {
    e.preventDefault();
    alert("Unidad enviada.");
});

document.getElementById("btnLimpiarResultadosUnidad").addEventListener("click", function () {
    document.getElementById("inputBuscarUnidad").value = "";
    document.getElementById("listaResultadosUnidades").innerHTML = "";
});

fetch('/Almacen/Unidades/Datos')
    .then(response => response.json())
    .then(data => {
        llenarSelect('claveUnidad', data.unidades, 'c1', 'c1');
    })
    .catch(error => console.error('Error al obtener datos:', error));

crearBuscador({
    modalId: 'modalBuscarUnidad',
    inputBusquedaId: 'inputBuscarUnidad',
    listaResultadosId: 'listaResultadosUnidades',
    paginacionId: 'pagination-unidades',
    urlBusqueda: '/Almacen/Unidades/Buscar',
    urlDetalle: '/Almacen/Unidades/BuscarUnidades',
    onSelect: unidad => {
        document.getElementById('claveUnidad').value = unidad.c1 || '';
        document.getElementById('nombreUnidad').value = unidad.c2 || '';
    }
});
document.getElementById('formLineas').addEventListener('submit', function (e) {
    e.preventDefault();
    alert("Formulario enviado.");
});
document.getElementById("btnLimpiarResultados").addEventListener("click", function () {
    document.getElementById("inputBuscarNombre").value = "";
    document.getElementById("listaResultados").innerHTML = "";
});

fetch('/Almacen/Lineas/Datos')
    .then(response => response.json())
    .then(data => {
        llenarSelect('claveLinea', data.lineas, 'c1', 'c1');

    })
    .catch(error => {
        console.error('Error al obtener los datos:', error);
    });

crearBuscador({
    modalId: 'modalBuscarLinea',
    inputBusquedaId: 'inputBuscarNombreLinea',
    listaResultadosId: 'listaResultadosLineas',
    paginacionId: 'pagination-lineas',
    urlBusqueda: '/Almacen/Lineas/Buscar',
    urlDetalle: '/Almacen/Lineas/BuscarLineas',
    onSelect: producto => {
        // Llenar inputs del DOM con los datos del producto seleccionado
        document.getElementById('claveLinea').value = producto.c1 || '';
        document.getElementById('nombreLinea').value = producto.c2 || '';

    }
});
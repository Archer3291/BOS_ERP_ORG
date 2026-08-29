
window.DevolucionModule = {
    init() {
        function agregarFila(datosProducto = null) {
            const tbody = document.querySelector("#tabla-materiales tbody");
            const fila = document.createElement("tr");

            const c1 = datosProducto?.cve_prod || '';
            const c2 = datosProducto?.descr_prod || '';
            const c11 = datosProducto?.udm || '';

            let html =
                `<td class="text-center"><input type="checkbox" class="seleccionar-fila" /></td>` +
                `<td></td>` +
                `<td contenteditable="true" class="input-cantidad"></td>` +
                `<td contenteditable="true" class="input-codigo">${c1}</td>` +
                `<td contenteditable="true" class="input-descripcion">${c2}</td>` +
                `<td contenteditable="true" class="input-unidad">${c11}</td>`;

            fila.innerHTML = html;

            tbody.appendChild(fila);
        }

        function eliminarFilasSeleccionadas() {
            const checkboxes = document.querySelectorAll(".seleccionar-fila:checked");
            checkboxes.forEach(chk => {
                chk.closest("tr").remove();
            });
        }

        const limpiarNumero = (str) => str.replace(/[^\d.]/g, '');

        // Función para buscar productos con paginación
        const pageSize = 50; // Número de productos por página
        function buscarProductos(nombre, page = 1) {
            fetch(`/Almacen/Producto/Buscar?nombre=${encodeURIComponent(nombre)}&page=${page}&pageSize=${pageSize}`)
                .then(res => res.json())
                .then(data => {
                    const lista = document.getElementById('listaResultados');
                    lista.innerHTML = ''; // Limpiar resultados anteriores

                    if (data.length === 0) {
                        lista.innerHTML = '<div class="p-3">No se encontraron productos.</div>';
                        return;
                    }

                    // Crear tabla
                    const tabla = document.createElement('table');
                    tabla.classList.add('table', 'table-hover', 'table-bordered');

                    // Encabezado
                    tabla.innerHTML = `
                <thead class="table-secondary">
                    <tr>
                        <th style="width: 30%;">Clave</th>
                        <th>Descripción</th>
                    </tr>
                </thead>
                <tbody></tbody>
            `;

                    const tbody = tabla.querySelector('tbody');

                    data.forEach(producto => {
                        const fila = document.createElement('tr');
                        fila.style.cursor = 'pointer';
                        fila.innerHTML = `
                    <td>${producto.id}</td>
                    <td>${producto.descripcion}</td>
                `;

                        // Acción al hacer clic sobre una fila
                        fila.addEventListener('click', () => {
                            fetch(`/Almacen/Producto/BuscarProducto?idproducto=${producto.id}`)
                                .then(res => res.json())
                                .then(detalle => {
                                    const productId = detalle[0]?.cve_prod;
                                    const table = document.querySelectorAll("#tabla-materiales tbody tr");
                                    const ifExist = Array.from(table).some(fila => fila.cells[3]?.innerText.trim() === productId);

                                    if (ifExist) {
                                        toastMixin.fire({
                                            icon: 'warning',
                                            title: 'Este producto ya ha sido agregado.'
                                        });
                                        return;
                                    }

                                    $('#modalBuscarProducto').modal('hide')
                                    agregarFila(detalle[0]);
                                });
                        });

                        tbody.appendChild(fila);
                    });

                    lista.appendChild(tabla);

                    // Actualizar la paginación
                    actualizarPaginacion(page, data.length);

                    // Volver al inicio del scroll
                    lista.scrollTop = 0;
                })
                .catch(err => {
                    console.error('Error al buscar: ', err);
                });
        }


        // Función para actualizar los controles de paginación
        function actualizarPaginacion(page, currentPageResults) {
            const pagination = document.getElementById('pagination');
            pagination.innerHTML = ''; // Limpiar paginación anterior

            // Deshabilitar "Siguiente" si no hay más registros
            const previousButton = document.createElement('button');
            previousButton.classList.add('btn', 'btn-outline-primary');
            previousButton.innerText = 'Anterior';
            previousButton.disabled = page === 1;
            previousButton.onclick = () => buscarProductos(document.getElementById('inputBuscarNombre').value, page - 1);

            // "Siguiente" solo se habilita si hay más productos
            const nextButton = document.createElement('button');
            nextButton.classList.add('btn', 'btn-outline-primary');
            nextButton.innerText = 'Siguiente';
            nextButton.disabled = currentPageResults < pageSize; // Si no hay suficientes resultados, deshabilitar "Siguiente"
            nextButton.onclick = () => buscarProductos(document.getElementById('inputBuscarNombre').value, page + 1);

            pagination.appendChild(previousButton);
            pagination.appendChild(nextButton);
        }

        // Inicializar la búsqueda cuando se abre el modal
        document.getElementById('modalBuscarProducto').addEventListener('shown.bs.modal', () => {
            buscarProductos(''); // Realiza una búsqueda sin filtro (muestra todos los productos)
        });

        // Llamada para buscar productos cuando se escribe en el input
        document.getElementById('inputBuscarNombre').addEventListener('input', () => {
            buscarProductos(document.getElementById('inputBuscarNombre').value);
        });

        // Buscar automáticamente al abrir el modal
        const modalBuscarProducto = document.getElementById('modalBuscarProducto');

        modalBuscarProducto.addEventListener('shown.bs.modal', () => {
            const nombre = inputBuscarNombre.value.trim();
            buscarProductos(nombre);
        });


        document.getElementById('btnLimpiarResultados').addEventListener('click', () => {
            //document.getElementById('listaResultados').innerHTML = '';
            buscarProductos('');
            document.getElementById('inputBuscarNombre').value = '';
        });

        document.getElementById('btnLimpiarResultados').addEventListener('click', () => {
            //document.getElementById('listaResultados').innerHTML = '';
            buscarProductos('');
            document.getElementById('inputBuscarNombre').value = '';
            document.getElementById('inputBuscarNombre').focus();
        });

        // Validar datos de la tabla
        function validarDatos() {
            const filasMateriales = [];

            let errorFila = false;

            document.querySelectorAll("#tabla-materiales tbody tr").forEach((fila) => {
                const cantidad = limpiarNumero(fila.cells[2].innerText.trim());
                const codigo = fila.cells[3].innerText.trim();
                const descripcion = fila.cells[4].innerText.trim();
                const unidad = fila.cells[5].innerText.trim();

                let filaValida = cantidad !== "" && cantidad !== "0" && codigo !== "" && descripcion !== "" && unidad !== "";

                if (filaValida) {
                    const fila = {
                        cantidad,
                        codigo,
                        descripcion,
                        unidad,
                    };

                    filasMateriales.push(fila);
                } else {
                    errorFila = true;
                }
            });

            if (filasMateriales.length === 0 || errorFila) {
                toastMixin.fire({
                    icon: 'error',
                    title: 'Hay filas incompletas o vacías. Revisa que todos los campos obligatorios estén llenos.'
                });
                return false;
            }

            return true;
        }
    },

    destroy() { }
}
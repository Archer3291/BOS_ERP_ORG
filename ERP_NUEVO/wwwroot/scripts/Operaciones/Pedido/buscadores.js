crearBuscador({
    modalId: 'modalBuscarProducto',
    inputBusquedaId: 'inputBuscarNombreProducto',
    listaResultadosId: 'listaResultados',
    paginacionId: 'pagination',
    urlBusqueda: '/Operaciones/buscar',
    urlDetalle: '/Operaciones/BuscarProducto',
    onSelect: producto => {
        const rowData = [
            producto.c1,
            '<input type="number" class="form-control cantidad" value="1" min="1">',
            "N/A",
            "N/A",
            producto.c11,
            `<input type="number" class="form-control precio" value="${parseFloat(producto.precio).toFixed(2)}" min="0" readonly>`,
            '<input type="number" class="form-control descuento" value="0" min="0" max="100">',
            `<span class="importe text-end">${parseFloat(producto.c23).toFixed(2)}</span>`,
            producto.c2,
            "N/A",
            "N/A",
            "N/A",
            "N/A",

        ];
        // Desactivar temporalmente la paginación
        table.settings()[0].oFeatures.bPaginate = false;
        table.draw(false);

        // Contar filas antes de agregar
        const rowIndex = table.rows().count();
        table.row.add(rowData).draw(false); // Agregar fila

        // Recuperar nodo DOM de la última fila agregada
        const rowNode = table.row(rowIndex).node();

        if (rowNode) {
            rowNode.dataset.iva = producto.c18 ?? "";
            rowNode.dataset.objeto = "02";
            rowNode.dataset.id = "1";
            rowNode.dataset.clavesat = producto.clavesat ?? "";
            rowNode.dataset.udmsat = producto.udmsat ?? "";
            rowNode.dataset.preciouni = producto.precio ?? "";
        } else {
            // Eliminar la fila recién agregada
            table.row(rowIndex).remove().draw(false);
            toastr.error("No se pudo agregar la fila: ocurrió un error al renderizar el contenido.");
        }

        // Volver a activar la paginación
        table.settings()[0].oFeatures.bPaginate = true;
        table.draw(false);



        // Función para ajustar el ancho dinámicamente
        function ajustarAnchoInput(input) {
            const valor = input.value || '0'; // usar '0' si está vacío
            const tmpSpan = document.createElement('span');
            tmpSpan.style.visibility = 'hidden';
            tmpSpan.style.position = 'absolute';
            tmpSpan.style.whiteSpace = 'pre';
            tmpSpan.style.font = getComputedStyle(input).font;
            tmpSpan.textContent = valor;
            document.body.appendChild(tmpSpan);

            const nuevoAncho = tmpSpan.offsetWidth + 40; // +padding
            document.body.removeChild(tmpSpan);

            // Aplica límites
            const anchoFinal = Math.max(70, Math.min(nuevoAncho, 115)); // mínimo 70, máximo 200

            input.style.width = anchoFinal + 'px';
        }


        // Aplicar solo a los inputs .cantidad y .precio
        $(rowNode).find('.cantidad, .precio').each(function () {
            ajustarAnchoInput(this); // ajustar al crear
            this.addEventListener('input', () => ajustarAnchoInput(this)); // ajustar en tiempo real
        });


        $(rowNode).find('.cantidad, .descuento, .precio').on('input', function () {
            actualizarTotal();
        });

        $(rowNode).find('.precio').on('dblclick', function () {
            const input = this;
            const csrfToken = $('input[name="__RequestVerificationToken"]').val();

            fetch('/Operaciones/ValidarEdicionPrecio', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': csrfToken
                },
                body: '{}'
            })
                .then(res => res.json())
                .then(data => {
                    if (data.permitido) {
                        input.removeAttribute('readonly');
                        input.focus();
                    } else {
                        const currentTheme = localStorage.getItem('theme') || 'light';
                        Swal.fire({
                            title: 'Permiso requerido',
                            html: `<div class="form-floating mb-2">
                                        <input type="text" id="swal-user" class="form-control" placeholder="Usuario">
                                        <label for="swal-user">Usuario</label>
                                       </div>
                                       <div class="form-floating">
                                        <input type="password" id="swal-pass" class="form-control" placeholder="Contraseña">
                                        <label for="swal-pass">Contraseña</label>
                                       </div>`,
                            confirmButtonText: 'Validar',
                            focusConfirm: false,
                            theme: currentTheme,
                            preConfirm: () => {
                                const usuario = document.getElementById('swal-user').value;
                                const contrasena = document.getElementById('swal-pass').value;
                                if (!usuario || !contrasena) {
                                    Swal.showValidationMessage('Ambos campos son requeridos');
                                }
                                return { usuario, contrasena };
                            }
                        }).then(result => {
                            if (result.isConfirmed) {
                                const formData = new FormData();
                                formData.append('__RequestVerificationToken', csrfToken);
                                formData.append('usuario', result.value.usuario);
                                formData.append('contrasena', result.value.contrasena);

                                fetch('/Operaciones/ValidarAccesoFijo', {
                                    method: 'POST',
                                    body: formData
                                })
                                    .then(res => res.json())
                                    .then(data => {
                                        if (data.permitido) {
                                            input.removeAttribute('readonly');
                                            input.focus();
                                        } else {
                                            Swal.fire('Acceso denegado', 'Usuario o contraseña incorrectos.', 'error');
                                        }
                                    });
                            }
                        });
                    }
                });

            actualizarTotal();
        });

        actualizarTotal();
    }

});

crearBuscador({
    modalId: 'modalBuscarCliente',
    inputBusquedaId: 'inputBuscarNombreCliente',
    listaResultadosId: 'listaResultadosCliente',
    paginacionId: 'pagination-cliente',
    urlBusqueda: '/Operaciones/buscarC',
    urlDetalle: '/Operaciones/BuscarCliente',
    onSelect: cliente => {
        // Llenar inputs del DOM con los datos del producto seleccionado
        document.getElementById('cliente').value = cliente.c2 || '';
        document.getElementById('rfc').value = cliente.c10 || '';
        document.getElementById('vendedor').value = cliente.c12 || '';
        document.getElementById('info-proveedor').value = cliente.c3 + "\n" + cliente.c4 + ", " + cliente.c5 + "\ncp: " + cliente.c27 + ", " + cliente.c6 || '';
        //document.getElementById('producto-descripcion').value = producto.descripcion || '';
        //document.getElementById('producto-precio').value = parseFloat(producto.preciolista || 0).toFixed(2);
        limpiarTabla()     
    }
});

function limpiarTabla() {
    if (table) {
        table.clear().draw(); // Borra todas las filas y actualiza la vista
        actualizarTotal();    // Recalcula el total si es necesario
    }
}

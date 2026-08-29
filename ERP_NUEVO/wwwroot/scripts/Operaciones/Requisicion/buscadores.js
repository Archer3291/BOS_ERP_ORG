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
            producto.c6 || '',
            '<input type="number" class="form-control cantidad" value="1" min="1">',
            producto.c11,
            `<input type="number" class="form-control precio" value="${parseFloat(producto.c23).toFixed(2)}" min="0" readonly>`,
            '<input type="number" class="form-control descuento" value="0" min="0" max="100">',
            `<span class="importe text-end">${parseFloat(producto.c23).toFixed(2)}</span>`,
            producto.c2,
            producto.c6 || ''
        ];
        const rowNode = table.row.add(rowData).draw(false).node();

        // Guardamos el IVA directamente en el <tr>
        rowNode.dataset.iva = producto.c18;

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
    modalId: 'modalBuscarProveedor',
    inputBusquedaId: 'inputBuscarNombreProveedor',
    listaResultadosId: 'listaResultadosProveedor',
    paginacionId: 'pagination-proveedor',
    urlBusqueda: '/Operaciones/buscarP',
    urlDetalle: '/Operaciones/BuscarProveedor',
    onSelect: producto => {
        // Llenar inputs del DOM con los datos del producto seleccionado
        document.getElementById('proveedor').value = producto.c2 || '';
        document.getElementById('rfc').value = producto.c10 || '';
        document.getElementById('info-proveedor').value = producto.c3 + "\n" + producto.c4 + ", " + producto.c5 + "\nCP: " + producto.c27 + ", " + producto.c6 || '';
        //document.getElementById('producto-descripcion').value = producto.descripcion || '';
        //document.getElementById('producto-precio').value = parseFloat(producto.preciolista || 0).toFixed(2);
    }
});
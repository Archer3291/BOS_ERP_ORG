$(document).ready(function () {
    // Configuración de DataTables
    var table = $('#agentesTable').DataTable({
        ajax: {
            url: '/AgenteCompra/GetAgentes',
            type: 'GET',
            dataType: 'json',
            dataSrc: 'data',
            error: function (xhr, error, thrown) {
                console.error('Error al cargar datos: ', error, thrown);
                Swal.fire({
                    icon: 'error',
                    title: 'Error',
                    text: 'No se pudieron cargar los datos. Por favor, recarga la página.'
                });
            }
        },
        columns: [
            { data: 'Clave' },
            { data: 'Descripcion' },
            {
                data: null,
                render: function (data, type, row) {
                    return `
                    <div class="" role="group">
                        <button class="btn btn-sm btn-floating btn-outline-warning btn-edit" data-mdb-ripple-init data-mdb-ripple-color="warning" data-mdb-ripple-duration="1000ms" title="Editar" data-clave="${row.Clave}">
                            <i class="fa-duotone fa-solid fa-pen-to-square fa-beat"></i>
                        </button>
                        <button class="btn btn-sm btn-floating btn-outline-danger btn-delete" data-mdb-ripple-init data-mdb-ripple-color="danger" data-mdb-ripple-duration="1000ms" title="Eliminar" data-clave="${row.Clave}">
                            <i class="fa-duotone fa-solid fa-trash fa-beat"></i> 
                        </button>
                    </div>
                    `;
                },
                orderable: false,
                width: '20%'
            }
        ],
        language: {
            select: {
                rows: '%d fila(s) seleccionada(s)'
            },
            buttons: {
                copyTitle: 'Copiado al portapapeles',
                copySuccess: {
                    _: '%d filas copiadas',
                    1: '1 fila copiada'
                }
            }
        },
        responsive: true,
        select: true,
        layout: {
            topStart: {
                buttons: [
                    {
                        extend: 'copyHtml5',
                        text: '<i class="fa-duotone fa-solid fa-copy fa-beat"></i>',
                        titleAttr: 'Copiar'
                    },
                    {
                        extend: 'excelHtml5',
                        text: '<i class="fa-duotone fa-solid fa-file-excel fa-beat"></i>',
                        titleAttr: 'Exportar a Excel'
                    },
                    {
                        extend: 'pdfHtml5',
                        text: '<i class="fa-duotone fa-solid fa-file-pdf fa-beat"></i>',
                        titleAttr: 'Exportar a PDF'
                    },
                    'colvis'
                ],
                pageLength: {
                    menu: [5, 10, 25, 50]
                }
            }
        },
        columnDefs: [
            { className: 'dt-center', targets: '_all' }
        ]
    });

    // Mostrar modal para agregar
    $('#btnAdd').click(function () {
        $('#action').val('create');
        $('#agenteModalLabel').text('Registrar Nueva Zona');
        $('#agenteForm')[0].reset();
        $('#clave').prop('readonly', false).removeClass('is-valid is-invalid');
        $('#descripcion').removeClass('is-valid is-invalid');
        $('#agenteModal').modal('show');
    });

    // Editar zona
    $('#agentesTable').on('click', '.btn-edit', function () {
        var rowData = table.row($(this).closest('tr')).data();
        if (rowData) {
            $('#action').val('edit');
            $('#agenteModalLabel').text('Editar Zona');
            $('#clave').val(rowData.Clave).prop('readonly', true).removeClass('is-valid is-invalid');
            $('#descripcion').val(rowData.Descripcion).removeClass('is-valid is-invalid');
            $('#agenteModal').modal('show');
        }
    });

    // Eliminar zona
    $('#agentesTable').on('click', '.btn-delete', function () {
        var clave = $(this).data('clave');

        Swal.fire({
            title: '¿Estás seguro?',
            text: "¡Esta acción no se puede deshacer!",
            icon: 'warning',
            buttonsStyling: true,
            customClass: {
                confirmButton: "btn btn-outline-danger",
                cancelButton: "btn btn-outline-primary"
            },
            showCancelButton: true,
            confirmButtonColor: '#d33',
            cancelButtonColor: '#3085d6',
            confirmButtonText: '<i class="fa-duotone fa-solid fa-thumbs-up fa-beat"></i> ¡Sí, eliminarlo!',
            cancelButtonText: '<i class="fa-duotone fa-solid fa-xmark fa-beat"></i> Cancelar'
        }).then((result) => {
            if (result.isConfirmed) {
                $.ajax({
                    url: '/AgenteCompra/Delete',
                    type: 'POST',
                    data: { clave: clave },
                    success: function (response) {
                        if (response.success) {
                            Swal.fire(
                                '¡Registro Eliminado!',
                                response.message,
                                'success'
                            );
                            table.ajax.reload(null, false);
                        } else {
                            Swal.fire(
                                'Error',
                                response.message,
                                'error'
                            );
                        }
                    },
                    error: function (xhr, status, error) {
                        Swal.fire(
                            'Error',
                            'Ocurrió un error al eliminar: ' + error,
                            'error'
                        );
                    }
                });
            }
        });
    });

    // Enviar formulario
    $('#agenteForm').submit(function (e) {
        e.preventDefault();

        var formData = {
            Clave: $('#clave').val(),
            Descripcion: $('#descripcion').val()
        };

        var url = $('#action').val() === 'create' ? '/AgenteCompra/Create' : '/AgenteCompra/Edit';
        var method = 'POST';

        // Validación manual
        var isValid = true;
        $('#agenteForm input').each(function () {
            if (!$(this).val()) {
                $(this).addClass('is-invalid');
                isValid = false;
            } else {
                $(this).removeClass('is-invalid').addClass('is-valid');
            }
        });

        if (!isValid) {
            Swal.fire({
                icon: 'error',
                title: 'Error',
                text: 'Por favor complete todos los campos requeridos.'
            });
            return;
        }

        $.ajax({
            url: url,
            type: method,
            data: formData,
            success: function (response) {
                if (response.success) {
                    Swal.fire({
                        icon: 'success',
                        title: 'Éxito',
                        text: response.message
                    });
                    $('#agenteModal').modal('hide');
                    table.ajax.reload(null, false);
                } else {
                    Swal.fire({
                        icon: 'error',
                        title: 'Error',
                        text: response.message
                    });
                }
            },
            error: function (xhr, status, error) {
                Swal.fire({
                    icon: 'error',
                    title: 'Error',
                    text: 'Ocurrió un error al procesar la solicitud: ' + error
                });
            }
        });
    });

    // Validación en tiempo real
    $('#agenteForm input').on('input', function () {
        if ($(this).val()) {
            $(this).removeClass('is-invalid').addClass('is-valid');
        } else {
            $(this).removeClass('is-valid').addClass('is-invalid');
        }
    });

    // Configuración del validador del formulario
    $("#agenteForm").validate({
        rules: {
            Clave: {
                required: true,
                maxlength: 5
            },
            Descripcion: {
                required: true,
                maxlength: 30
            }
        },
        messages: {
            Clave: {
                required: "La clave es obligatoria",
                maxlength: "La clave no puede exceder 5 caracteres"
            },
            Descripcion: {
                required: "La descripción es obligatoria",
                maxlength: "La descripción no puede exceder 30 caracteres"
            }
        },
        errorElement: 'div',
        errorPlacement: function (error, element) {
            error.addClass('invalid-feedback');
            element.closest('.form-outline').append(error);
        },
        highlight: function (element, errorClass, validClass) {
            $(element).addClass('is-invalid').removeClass('is-valid');
        },
        unhighlight: function (element, errorClass, validClass) {
            $(element).removeClass('is-invalid').addClass('is-valid');
        }
    });

    //clave.oninput = function () {
    //    if (this.value.length > 5) {
    //        this.value = this.value.slice(0, 5);
    //    }
    //}

});
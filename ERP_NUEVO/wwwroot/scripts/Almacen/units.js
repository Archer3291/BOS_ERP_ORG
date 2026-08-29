$(document).ready(function () {
    // Configuración de DataTables
    var table = $('#unitsTable').DataTable({
        ajax: {
            url: '/Unit/GetUnits',
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
            { data: 'Description' },
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
            url: '//cdn.datatables.net/plug-ins/1.10.25/i18n/Spanish.json',
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
        $('#unitModalLabel').text('Registrar Nueva Unidad');
        $('#unitForm')[0].reset();
        $('#clave').prop('readonly', false).removeClass('is-valid is-invalid');
        $('#description').removeClass('is-valid is-invalid');
        $('#unitModal').modal('show');
    });

    // Editar unidad
    $('#unitsTable').on('click', '.btn-edit', function () {
        var rowData = table.row($(this).closest('tr')).data();
        if (rowData) {
            $('#action').val('edit');
            $('#unitModalLabel').text('Editar Unidad');
            $('#clave').val(rowData.Clave).prop('readonly', true).removeClass('is-valid is-invalid');
            $('#description').val(rowData.Description).removeClass('is-valid is-invalid');
            $('#unitModal').modal('show');
        }
    });

    // Eliminar unidad
    $('#unitsTable').on('click', '.btn-delete', function () {
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
                    url: '/Unit/Delete',
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
    $('#unitForm').submit(function (e) {
        e.preventDefault();

        var formData = {
            Clave: $('#clave').val(),
            Description: $('#description').val()
        };

        var url = $('#action').val() === 'create' ? '/Unit/Create' : '/Unit/Edit';
        var method = 'POST';

        // Validación manual
        var isValid = true;
        $('#unitForm input').each(function () {
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
                    $('#unitModal').modal('hide');
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
    $('#unitForm input').on('input', function () {
        if ($(this).val()) {
            $(this).removeClass('is-invalid').addClass('is-valid');
        } else {
            $(this).removeClass('is-valid').addClass('is-invalid');
        }
    });    

    // Configuración del validador del formulario
    $("#unitForm").validate({
        rules: {
            Clave: {
                required: true,
                maxlength: 3
            },
            Description: {
                required: true,
                maxlength: 30
            },            
        },
        messages: {
            Clave: {
                required: "La clave es obligatoria",
                maxlength: "La clave no puede exceder 3 caracteres"
            },
            Description: {
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

});
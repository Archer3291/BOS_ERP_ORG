
// Configuración de DataTables
var table = $('#conceptsTable').DataTable({
    ajax: {
        url: '/Concept/GetConcepts',
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
        { data: 'clave', title: 'Clave' },
        { data: 'descripcion', title: 'Descripcion' },
        { data: 'numerocuenta', title: 'No. cuenta' },
        {
            data: 'activo',
            title: 'Activo',
            render: function (data, type, row) {
                if (data === true || data === "true") {
                    return '<span class="badge bg-success">Sí</span>';
                } else {
                    return '<span class="badge bg-danger">No</span>';
                }
            }
        },
        {
            data: null,
            render: function (data, type, row) {
                let buttons = `
            <button class="btn btn-sm blue-500 btn-edit" 
                    title="Editar" 
                    data-numerocuenta="${data.numerocuenta}" 
                    data-descripcion="${data.descripcion}" 
                    data-clave="${data.clave}" 
                    onclick="editConcept(this)">
                <i class="fa-duotone fa-pen-to-square"></i>
            </button>
        `;

                if (data.activo === true || data.activo === "true") {
                    // Mostrar botón de desactivar
                    buttons += `
                <button class="btn btn-sm red-500 btn-delete" 
                        title="Desactivar" 
                        data-clave="${data.clave}">
                    <i class="fa-duotone fa-trash"></i> 
                </button>
            `;
                } else {
                    // Mostrar botón de reactivar
                    buttons += `
                <button class="btn btn-sm green-500 btn-reactivate" 
                        title="Reactivar" 
                        data-clave="${data.clave}">
                    <i class="fa-duotone fa-rotate-left"></i> 
                </button>
            `;
                }

                return `<div class="d-flex gap-1 justify-content-center">${buttons}</div>`;
            },
            orderable: false,
            width: '20%'
        }
    ],
    responsive: true,
    layout: {
        topStart: {
            pageLength: {
                menu: [5, 10, 25, 50]
            }
        }
    },
});

// Mostrar modal para agregar
$('#btnAdd').click(function () {
    $('#action').val('create');
    $('#conceptModalLabel').text('Registrar Nuevo Concepto');
    $('#conceptForm')[0].reset();
    $('#clave').prop('readonly', false).removeClass('is-valid is-invalid');
    $('#descripcion').removeClass('is-valid is-invalid');
    $('#numerocuenta').removeClass('is-valid is-invalid');
    $('#conceptModal').modal('show');
});

function editConcept(e) {
    const clave = $(e).data('clave');
    const descripcion = $(e).data('descripcion');
    const numerocuenta = $(e).data('numerocuenta');

    $('#action').val('edit');
    $('#conceptModalLabel').text('Editar Concepto');
    $('#clave').val(clave).prop('readonly', true).removeClass('is-valid is-invalid');
    $('#descripcion').val(descripcion).removeClass('is-valid is-invalid');
    $('#numerocuenta').val(numerocuenta).removeClass('is-valid is-invalid');
    $('#conceptModal').modal('show');
}

// Eliminar concepto
$('#conceptsTable').on('click', '.btn-delete', function () {
    var clave = $(this).data('clave');

    _Swal.fire({
        title: '¿Estás seguro?',
        text: "¡Esta acción no se puede deshacer!",
        icon: 'warning',    
        confirmButtonColor: '#d33',
        cancelButtonColor: '#3085d6',
        confirmButtonText: '<i class="fa-duotone fa-solid fa-thumbs-up fa-beat"></i> ¡Sí, eliminarlo!',
        cancelButtonText: '<i class="fa-duotone fa-solid fa-xmark fa-beat"></i> Cancelar'
    }).then((result) => {
        if (result.isConfirmed) {
            $.ajax({
                url: '/Concept/Delete',
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

$('#conceptsTable').on('click', '.btn-reactivate', function () {
    var clave = $(this).data('clave');

    Swal.fire({
        title: '¿Reactivar este concepto?',
        text: "El concepto volverá a estar activo.",
        icon: 'question',
        buttonsStyling: true,
        showCancelButton: true,
        confirmButtonColor: '#28a745',
        cancelButtonColor: '#6c757d',
        confirmButtonText: '<i class="fa-duotone fa-thumbs-up fa-beat"></i> ¡Sí, reactivarlo!',
        cancelButtonText: '<i class="fa-duotone fa-xmark fa-beat"></i> Cancelar'
    }).then((result) => {
        if (result.isConfirmed) {
            $.ajax({
                url: '/Concept/Reactivate',
                type: 'POST',
                data: { clave: clave },
                success: function (response) {
                    if (response.success) {
                        Swal.fire(
                            '¡Registro Reactivado!',
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
                        'Ocurrió un error al reactivar: ' + error,
                        'error'
                    );
                }
            });
        }
    });
});


// Enviar formulario
$('#conceptForm').submit(function (e) {
    e.preventDefault();

    var formData = {
        Clave: $('#clave').val(),
        Descripcion: $('#descripcion').val(),
        NumeroCuenta: $('#numerocuenta').val()
    };

    var url = $('#action').val() === 'create' ? '/Concept/Create' : '/Concept/Edit';
    var method = 'POST';

    // Validación manual
    var isValid = true;
    $('#conceptForm input').each(function () {
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
                $('#conceptModal').modal('hide');
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
$('#conceptForm input').on('input', function () {
    if ($(this).val()) {
        $(this).removeClass('is-invalid').addClass('is-valid');
    } else {
        $(this).removeClass('is-valid').addClass('is-invalid');
    }
});

// Configuración del validador del formulario
$("#conceptForm").validate({
    rules: {
        Clave: {
            required: true,
            maxlength: 10
        },
        Descripcion: {
            required: true,
            maxlength: 30
        },
        NumeroCuenta: {
            required: true,
            maxlength: 20
        }
    },
    messages: {
        Clave: {
            required: "La clave es obligatoria",
            maxlength: "La clave no puede exceder 10 caracteres"
        },
        Descripcion: {
            required: "La descripción es obligatoria",
            maxlength: "La descripción no puede exceder 30 caracteres"
        },
        NumeroCuenta: {
            required: "El número de cuenta es obligatorio",
            maxlength: "El número de cuenta no puede exceder 20 caracteres"
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
// Filtro por columna personalizada
// Aplicar filtro inicial si hay valor por defecto en el select
const valorInicial = $('#filtroActivo').val(); // Esto será "Sí"
if (valorInicial) {
    table.column(3).search(valorInicial).draw(); // Ajusta el índice si "Activo" no es la columna 4
}

// Manejar cambios posteriores del filtro
$('#filtroActivo').on('change', function () {
    const valor = this.value;
    table.column(3).search(valor).draw();
});

//clave.oninput = function () {
//    if (this.value.length > 10) {
//        this.value = this.value.slice(0, 10);
//    }
//}

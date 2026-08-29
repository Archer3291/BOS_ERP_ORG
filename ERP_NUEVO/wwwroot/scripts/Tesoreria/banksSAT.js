// Configuración de DataTables
var table = $('#banksTable').DataTable({
    ajax: {
        url: '/BankSAT/GetBanks',
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
        { data: 'nombre', title: 'Nombre' },
        { data: 'razonsocial', title: 'Razon social' },
        {
            data: null,
            render: function (data, type, row) {
                return `
                    <button class="btn btn-sm green-500 btn-edit" title="Editar" data-clave="${data.clave}" onclick="editBank(this)">
                        <i class="fa-duotone fa-solid fa-pen-to-square"></i>
                    </button>
                    <button class="btn btn-sm red-500 btn-delete" title="Eliminar" data-clave="${data.clave}">
                        <i class="fa-duotone fa-solid fa-trash"></i> 
                    </button>
                    `;
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
    $('#bankModalLabel').text('Registrar Nuevo Banco SAT');
    $('#bankForm')[0].reset();
    $('#clave').prop('readonly', false).removeClass('is-valid is-invalid');
    $('#nombre').removeClass('is-valid is-invalid');
    $('#razonsocial').removeClass('is-valid is-invalid');
    $('#bankModal').modal('show');
});

// Editar banco SAT
function editBank(e) {
    const id = $(e).data('clave')

    GetData({
        path: '/BankSAT/getBankDetails',
        data: {
            clave: id
        }
    }).then((_res) => {
        $('#action').val('edit');
        $('#bankModalLabel').text('Editar Banco SAT');
        $('#clave').val(_res[0].clave).prop('readonly', true).removeClass('is-valid is-invalid');
        $('#nombre').val(_res[0].nombre).removeClass('is-valid is-invalid');
        $('#razonsocial').val(_res[0].razonsocial).removeClass('is-valid is-invalid');
        $('#bankModal').modal('show');
    })
}

// Eliminar banco SAT
$('#banksTable').on('click', '.btn-delete', function () {
    var clave = $(this).data('clave');

    _Swal.fire({
        title: '¿Estás seguro?',
        text: "¡Esta acción no se puede deshacer!",
        icon: 'warning',
        confirmButtonColor: '#d33',
        cancelButtonColor: '#3085d6',
        confirmButtonText: '<i class="fa-duotone fa-solid fa-thumbs-up"></i> ¡Sí, eliminarlo!',
        cancelButtonText: '<i class="fa-duotone fa-solid fa-xmark"></i> Cancelar'
    }).then((result) => {
        if (result.isConfirmed) {
            $.ajax({
                url: '/BankSAT/Delete',
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
$('#bankForm').submit(function (e) {
    e.preventDefault();

    var formData = {
        Clave: $('#clave').val(),
        Nombre: $('#nombre').val(),
        RazonSocial: $('#razonsocial').val()
    };

    var url = $('#action').val() === 'create' ? '/BankSAT/Create' : '/BankSAT/Edit';
    var method = 'POST';

    // Validación manual
    var isValid = true;
    $('#bankForm input').each(function () {
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
                $('#bankModal').modal('hide');
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
$('#bankForm input').on('input', function () {
    if ($(this).val()) {
        $(this).removeClass('is-invalid').addClass('is-valid');
    } else {
        $(this).removeClass('is-valid').addClass('is-invalid');
    }
});

// Configuración del validador del formulario
$("#bankForm").validate({
    rules: {
        Clave: {
            required: true,
            maxlength: 3
        },
        Nombre: {
            required: true,
            maxlength: 50
        },
        RazonSocial: {
            required: true,
            maxlength: 50
        }
    },
    messages: {
        Clave: {
            required: "La clave es obligatoria",
            maxlength: "La clave no puede exceder 3 caracteres"
        },
        Nombre: {
            required: "El nombre es obligatorio",
            maxlength: "El nombre no puede exceder 50 caracteres"
        },
        RazonSocial: {
            required: "La Razón Social es obligatoria",
            maxlength: "La Razón Social no puede exceder 50 caracteres"
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
//    if (this.value.length > 3) {
//        this.value = this.value.slice(0, 3);
//    }
//}
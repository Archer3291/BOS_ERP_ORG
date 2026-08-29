// Configuración de DataTables
var table = $('#banksTable').DataTable({
    ajax: {
        url: '/Bank/GetBanks',
        type: 'GET',
        dataType: 'json',
        dataSrc: 'data',
        error: function (xhr, error, thrown) {
            Swal.fire({
                icon: 'error',
                title: 'Error',
                text: 'No se pudieron cargar los datos. Por favor, recarga la página.'
            });
        }
    },
    columns: [
        { data: 'clave' },
        { data: 'nombre' },
        { data: 'cuentabancaria' },
        {
            data: 'moneda',
            render: function (data, type, row) {
                // Obtener la descripción de la moneda si está disponible
                if (row.MonedaDescripcion) {
                    return row.MonedaDescripcion;
                }
                return data || 'No asignada';
            }
        },
        { data: 'numerocuenta' },
        { data: 'saldo' },
        { data: 'rfc' },
        {
            data: null,
            render: function (data, type, row) {
                return `
                    <div class="" role="group">
                        <button class="btn btn-sm green-500 btn-edit" title="Editar" data-clave="${data.clave}" onclick="editBank(this)">
                            <i class="fa-duotone fa-solid fa-pen-to-square"></i>
                        </button>
                        <button class="btn btn-sm red-500 btn-delete" title="Eliminar" data-clave="${data.clave}">
                            <i class="fa-duotone fa-solid fa-trash"></i> 
                        </button>
                    </div>
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
    $('#bankModalLabel').text('Registrar Nuevo Banco');
    $('#bankForm')[0].reset();
    $('#clave').prop('readonly', false).removeClass('is-valid is-invalid');
    $('#nombre').removeClass('is-valid is-invalid');
    $('#cuentaBancaria').removeClass('is-valid is-invalid');
    $('#moneda').removeClass('is-valid is-invalid');
    $('#numeroCuenta').removeClass('is-valid is-invalid');
    $('#saldo').removeClass('is-valid is-invalid');
    $('#rfc').removeClass('is-valid is-invalid');
    $('#bankModal').modal('show');
});

function editBank(e) {
    const id = $(e).data('clave')
    GetData({
        path: '/Bank/GetBankDetails',
        data: {
            clave: id
        }
    }).then((_res) => {
        $('#action').val('edit');
        $('#bankModalLabel').text('Editar Banco');
        $('#clave').val(_res[0].clave).prop('readonly', true).removeClass('is-valid is-invalid');
        $('#nombre').val(_res[0].nombre).removeClass('is-valid is-invalid');
        $('#cuentaBancaria').val(_res[0].cuentabancaria).removeClass('is-valid is-invalid');
        $('#moneda').val(_res[0].moneda);
        $('#numeroCuenta').val(_res[0].numerocuenta).removeClass('is-valid is-invalid');
        $('#saldo').val(_res[0].saldo).removeClass('is-valid is-invalid');
        $('#rfc').val(_res[0].rfc).removeClass('is-valid is-invalid');
        $('#bankModal').modal('show');
    })
}

// Eliminar banco
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
                url: '/Bank/Delete',
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

    var url = $('#action').val() === 'create' ? '/Bank/Create' : '/Bank/Edit';

    postFormData('bankForm', url).then(response => {
        if (!response) return;

        if (response.success) {
            toastMixin.fire({
                icon: 'success',
                title: response.message
            });
            $('#bankModal').modal('hide');
            table.ajax.reload(null, false);
        } else {
            toastMixin.fire({
                icon: 'error',
                title: response.message
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
            maxlength: 7
        },
        Nombre: {
            required: true,
            maxlength: 30
        },
        CuentaBancaria: {
            required: true,
            maxlength: 30
        },
        Moneda: {
            required: true
        },
        NumeroCuenta: {
            required: true,
            maxlength: 20
        },
        RFC: {
            required: true,
            maxlength: 13
        },
        Saldo: {
            required: true,
            maxlength: 1
        },
    },
    messages: {
        Clave: {
            required: "La clave es obligatoria",
            maxlength: "La clave no puede exceder 7 caracteres"
        },
        Nombre: {
            required: "El nombre es obligatorio",
            maxlength: "El nombre no puede exceder 30 caracteres"
        },
        CuentaBancaria: {
            required: "La cuenta bancaria es obligatoria",
            maxlength: "La cuenta bancaria no puede exceder 30 caracteres"
        },
        Moneda: {
            required: "Seleccione una opción"
        },
        NumeroCuenta: {
            required: "El número de cuenta es obligatorio",
            maxlength: "El número de cuenta no puede exceder 20 caracteres"
        },
        RFC: {
            required: "El RFC es obligatorio",
            maxlength: "El RFC no puede exceder 13 caracteres"
        },
        Saldo: {
            required: "El saldo inicial es obligatorio",
            maxlength: "Solo se puede ingresar un dígito"
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

//cuentaBancaria.oninput = function () {
//    if (this.value.length > 30) {
//        this.value = this.value.slice(0, 30);
//    }
//}
//saldo.oninput = function () {
//    if (this.value.length > 1) {
//        this.value = this.value.slice(0, 1);
//    }
//}
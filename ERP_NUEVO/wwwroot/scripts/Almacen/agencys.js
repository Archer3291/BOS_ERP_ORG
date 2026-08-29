// Configuración de DataTables
var table = $('#agencysTable').DataTable({
    ajax: {
        url: '/Agencies/GetAgencies',
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
        { data: 'nombre' },
        { data: 'calle' },
        { data: 'colonia' },
        { data: 'poblacion' },
        { data: 'telefono' },
        { data: 'telefono2' },
        { data: 'fax' },
        {
            data: null,
            render: function (data, type, row) {
                return `
                    <button class="btn btn-sm blue-500 btn-edit" title="Editar" data-clave="${data.claveaduana}" onclick="editAduana(this)">
                        <i class="fa-duotone fa-solid fa-pen-to-square"></i>
                    </button>
                    <button class="btn btn-sm red-500 btn-delete" title="Eliminar" data-clave="${data.claveaduana}">
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
    $('#agencyModalLabel').text('Registrar Nueva Agencia');
    $('#agencyForm')[0].reset();
    $('#Clave').prop('readonly', false).removeClass('is-valid is-invalid');
    $('#Descripcion').removeClass('is-valid is-invalid');
    $('#Calle').removeClass('is-valid is-invalid');
    $('#Colonia').removeClass('is-valid is-invalid');
    $('#Poblacion').removeClass('is-valid is-invalid');
    $('#Telefono').removeClass('is-valid is-invalid');
    $('#Telefono2').removeClass('is-valid is-invalid');
    $('#Fax').removeClass('is-valid is-invalid');
    $('#agencyModal').modal('show');
});

// Editar agencia
function editAduana(e) {
    const claveAduana = $(e).data('clave');

    GetData({
        path: '/Agencies/GetAgencyById',
        data: { claveAduana: claveAduana },
    }).then((response) => {
        if (response.success) {
            $('#action').val('edit');
            $('#agencyModalLabel').text('Editar Agencia');
            $('#Clave').val(response.data.claveaduana).prop('readonly', true).removeClass('is-valid is-invalid');
            $('#Nombre').val(response.data.nombre).removeClass('is-valid is-invalid');
            $('#Calle').val(response.data.calle).removeClass('is-valid is-invalid');
            $('#Colonia').val(response.data.colonia).removeClass('is-valid is-invalid');
            $('#Poblacion').val(response.data.poblacion).removeClass('is-valid is-invalid');
            $('#Telefono').val(response.data.telefono).removeClass('is-valid is-invalid');
            $('#Telefono2').val(response.data.telefono2).removeClass('is-valid is-invalid');
            $('#Fax').val(response.data.fax).removeClass('is-valid is-invalid');
            $('#agencyModal').modal('show');
        } else {
            Swal.fire({
                icon: 'error',
                title: response.message
            });
        }
    })
}

// Eliminar agencia
$('#agencysTable').on('click', '.btn-delete', function () {
    var claveAduana = $(this).data('clave');

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
        confirmButtonText: '<i class="fa-duotone fa-solid fa-thumbs-up"></i> ¡Sí, eliminarlo!',
        cancelButtonText: '<i class="fa-duotone fa-solid fa-xmark"></i> Cancelar'
    }).then((result) => {
        if (result.isConfirmed) {
            GetData({
                path: '/Agencies/Delete',
                data: { claveAduana: claveAduana },
            }).then((response) => {
                if (response.success) {
                    toastMixin.fire({
                        title: response.message,
                        icon: 'success'
                    });
                    table.ajax.reload(null, false);
                } else {
                    Swal.fire({
                        title: response.message,
                        icon: 'error'
                    });
                }
            })
        }
    });
});

// Enviar formulario
function sendForm() {
    var url = $('#action').val() === 'create' ? '/Agencies/Create' : '/Agencies/Edit';

    // Validación manual
    var isValid = true;
    $('#agencyForm input').each(function () {
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

    postFormData('agencyForm', url).then((res) => {
        if (res.success) {
            toastMixin.fire({
                icon: 'success',
                title: res.message,
                //timer: 1500,
                //showConfirmButton: false
            });
            $('#agencyModal').modal('hide');
            table.ajax.reload(null, false);
        } else {
            toastMixin.fire({
                icon: 'error',
                title: res.message
            });
        }
    })
}

// Validación en tiempo real
$('#agencyForm input').on('input', function () {
    if ($(this).val()) {
        $(this).removeClass('is-invalid').addClass('is-valid');
    } else {
        $(this).removeClass('is-valid').addClass('is-invalid');
    }
});

// Configuración del validador del formulario
$("#agencyForm").validate({
    rules: {
        Clave: {
            required: true,
            maxlength: 7
        },
        Nombre: {
            required: true,
            maxlength: 30
        },
        Calle: {
            required: true,
            maxlength: 40
        },
        Colonia: {
            required: true,
            maxlength: 40
        },
        Poblacion: {
            required: true,
            maxlength: 14
        },
        Telefono: {
            required: true,
            maxlength: 14
        }
    },
    messages: {
        Clave: {
            required: "La clave es obligatoria",
            maxlength: "La clave no puede exceder 7 caracteres"
        },
        Nombre: {
            required: "La descripción es obligatoria",
            maxlength: "La descripción no puede exceder 30 caracteres"
        },
        Calle: {
            required: "La calle es obligatoria",
            maxlength: "La calle no puede exceder 40 caracteres"
        },
        Colonia: {
            required: "La colonia es obligatoria",
            maxlength: "La colonia no puede exceder 40 caracteres"
        },
        Poblacion: {
            required: "La población es obligatoria",
            maxlength: "La población no puede exceder 40 caracteres"
        },
        Telefono: {
            required: "El teléfono es obligatorio",
            maxlength: "El teléfono no puede exceder 14 caracteres"
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
//    if (this.value.length > 7) {
//        this.value = this.value.slice(0, 7);
//    }
//}
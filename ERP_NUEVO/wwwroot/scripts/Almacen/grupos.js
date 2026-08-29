// Configuración de DataTables
var table = $('#groupsTable').DataTable({
    ajax: {
        url: '/Grupos/GetGroups',
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
        { data: 'clave' },
        { data: 'descripcion' },
        {
            data: 'activo',
            render: function (data) {
                return data === 1 ? '<span class="badge badge-success">Activo</span>' : '<span class="badge badge-danger">Inactivo</span>';
            }
        },
        {
            data: null,
            render: function (data, type, row) {
                return `
                        <button class="btn btn-sm green-500 btn-edit" title="Editar" data-clave="${data.clave}" onclick="editGroup(this)">
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
function createGroup() {
    $('#action').val('create');
    $('#groupModalLabel').text('Registrar Nuevo Grupo');
    $('#groupForm')[0].reset();
    $('#Clave').prop('readonly', false).removeClass('is-valid is-invalid');
    $('#Description').removeClass('is-valid is-invalid');
    $('#groupModal').modal('show');
}

// Editar grupo
function editGroup(e) {
    const clave = $(e).data('clave');

    GetData({
        path: '/Grupos/GroupDetails',
        data: { clave: clave },
    }).then((_res) => {
        $('#action').val('edit');
        $('#groupModalLabel').text('Editar Grupo');
        $('#Clave').val(_res.clave).prop('readonly', true).removeClass('is-valid is-invalid');
        $('#Description').val(_res.descripcion).removeClass('is-valid is-invalid');
        $('#Activo').val(_res.activo);
        $('#groupModal').modal('show');
    })
}

// Eliminar grupo
$('#groupsTable').on('click', '.btn-delete', function () {
    var clave = $(this).data('clave');

    _Swal.fire({
        title: '¿Estás seguro?',
        text: "¡Esta acción no se puede deshacer!",
        icon: 'warning',
        confirmButtonText: '<i class="fa-duotone fa-solid fa-thumbs-up"></i> ¡Sí, eliminarlo!',
        cancelButtonText: '<i class="fa-duotone fa-solid fa-xmark"></i> Cancelar'
    }).then((result) => {
        if (result.isConfirmed) {
            GetData({
                path: '/Grupos/Delete',
                data: { clave: clave }
            }).then((_res) => {
                if (_res.success) {
                    toastMixin.fire({
                        icon: 'success',
                        title: _res.message,
                    });
                    table.ajax.reload(null, false);
                } else {
                    toastMixin.fire({
                        icon: 'error',
                        title: _res.message,
                    });
                }
            })
        }
    });
});

// Enviar formulario
function sendForm() {
    let url = $('#action').val() === 'create' ? '/Grupos/Create' : '/Grupos/Update';

    // Validación manual
    var isValid = true;
    $('#groupForm input').each(function () {
        if (!$(this).val()) {
            $(this).addClass('is-invalid');
            isValid = false;
        } else {
            $(this).removeClass('is-invalid').addClass('is-valid');
        }
    });

    if (!isValid) {
        toastMixin.fire({
            icon: 'error',
            title: 'Por favor complete todos los campos requeridos.'
        });
        return;
    }

    postFormData('groupForm', url).then((res) => {
        if (res.success) {
            toastMixin.fire({
                icon: 'success',
                title: res.message,
                //timer: 1500,
                //showConfirmButton: false
            });
            $('#groupModal').modal('hide');
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
$('#groupForm input').on('input', function () {
    if ($(this).val()) {
        $(this).removeClass('is-invalid').addClass('is-valid');
    } else {
        $(this).removeClass('is-valid').addClass('is-invalid');
    }
});

// Configuración del validador del formulario
$("#groupForm").validate({
    rules: {
        Clave: {
            required: true,
            maxlength: 3
        },
        Description: {
            required: true,
            maxlength: 30
        }
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
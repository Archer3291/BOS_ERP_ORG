document.getElementById('formTiposProducto').addEventListener('submit', function (e) {
    e.preventDefault();
    // lógica de guardado
    alert("Tipo de producto enviado.");
});

document.getElementById("btnLimpiarResultadosTipo").addEventListener("click", function () {
    document.getElementById("inputBuscarTipoProducto").value = "";
    document.getElementById("listaResultadosTiposProducto").innerHTML = "";
});

// Llenar el select con los tipos de producto
fetch('/Almacen/Tipos/Datos')
    .then(response => response.json())
    .then(data => {
        llenarSelect('claveTipoProducto', data.tipos, 'c1', 'c1');
    })
    .catch(error => console.error('Error al obtener datos:', error));

crearBuscador({
    modalId: 'modalBuscarTipoProducto',
    inputBusquedaId: 'inputBuscarTipoProducto',
    listaResultadosId: 'listaResultadosTiposProducto',
    paginacionId: 'pagination-tipos-producto',
    urlBusqueda: '/Almacen/Tipos/Buscar',
    urlDetalle: '/Almacen/Tipos/BuscarTipos',
    onSelect: tipo => {
        document.getElementById('claveTipoProducto').value = tipo.c1 || '';
        document.getElementById('nombreTipoProducto').value = tipo.c2 || '';
        document.getElementById('Status').value = tipo.c4;
        document.getElementById('editTypeButton').setAttribute('data-clave', tipo.c1 || '');
    }
});


// Fragmento para la logica del CRUD de tipos de producto

function createType() {
    $('#action').val('create');
    $('#typeModalLabel').text('Registrar Nuevo Tipo');
    $('#typeForm')[0].reset();
    $('#Clave').prop('readonly', false).removeClass('is-valid is-invalid');
    $('#Description').removeClass('is-valid is-invalid');
    $('#modalAgregarTipo').modal('show');
}

// Editar grupo
function editType(e) {
    const id = e.getAttribute('data-clave');

    GetData({
        path: '/Almacen/Tipos/Detalles',
        data: { id: id },
    }).then((_res) => {
        if (_res.success) {
            $('#action').val('edit');
            $('#typeModalLabel').text(`Editar Tipo "${_res.data.descripcion}"`);
            $('#Clave').val(_res.data.clave).prop('readonly', true).removeClass('is-valid is-invalid');
            $('#Description').val(_res.data.descripcion).removeClass('is-valid is-invalid');
            $('#modalAgregarTipo #Status').val(_res.data.estado);
            $('#modalAgregarTipo').modal('show');
        } else {
            toastMixin.fire({
                icon: 'error',
                title: _res.message
            })
        }
    })
}

// Eliminar grupo
//$('#groupsTable').on('click', '.btn-delete', function () {
//    var clave = $(this).data('clave');

//    Swal.fire({
//        title: '¿Estás seguro?',
//        text: "¡Esta acción no se puede deshacer!",
//        icon: 'warning',
//        buttonsStyling: true,
//        customClass: {
//            confirmButton: "btn btn-outline-danger",
//            cancelButton: "btn btn-outline-primary"
//        },
//        showCancelButton: true,
//        confirmButtonColor: '#d33',
//        cancelButtonColor: '#3085d6',
//        confirmButtonText: '<i class="fa-duotone fa-solid fa-thumbs-up fa-beat"></i> ¡Sí, eliminarlo!',
//        cancelButtonText: '<i class="fa-duotone fa-solid fa-xmark fa-beat"></i> Cancelar'
//    }).then((result) => {
//        if (result.isConfirmed) {
//            GetData({
//                path: '/Almacen/Tipos/Eliminar',
//                data: { clave: clave }
//            }).then((_res) => {
//                if (_res.success) {
//                    toastMixin.fire({
//                        icon: 'success',
//                        title: _res.message,
//                    });
//                    table.ajax.reload(null, false);
//                } else {
//                    toastMixin.fire({
//                        icon: 'error',
//                        title: _res.message,
//                    });
//                }
//            })
//        }
//    });
//});

// Enviar formulario
function sendForm() {
    let url = $('#action').val() === 'create' ? '/Almacen/Tipos/Crear' : '/Almacen/Tipos/Editar';

    // Validación manual
    var isValid = true;
    $('#typeForm input').each(function () {
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

    postFormData('typeForm', url).then((res) => {
        if (res.success) {
            toastMixinReload({
                icon: 'success',
                title: res.message,
                //timer: 1500,
                //showConfirmButton: false
            });
            $('#modalAgregarTipo').modal('hide');
        } else {
            toastMixin.fire({
                icon: 'error',
                title: res.message
            });
        }
    })
}

// Validación en tiempo real
$('#typeForm input').on('input', function () {
    if ($(this).val()) {
        $(this).removeClass('is-invalid').addClass('is-valid');
    } else {
        $(this).removeClass('is-valid').addClass('is-invalid');
    }
});

// Configuración del validador del formulario
$("#typeForm").validate({
    rules: {
        Clave: {
            required: true,
            maxlength: 2
        },
        Description: {
            required: true,
            maxlength: 30
        },
        Activo: {
            required: true
        }
    },
    messages: {
        Clave: {
            required: "La clave es obligatoria",
            maxlength: "La clave no puede exceder 2 caracteres"
        },
        Description: {
            required: "La descripción es obligatoria",
            maxlength: "La descripción no puede exceder 30 caracteres"
        },
        Activo: {
            required: "El estado es obligatorio"
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
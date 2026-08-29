// Configuración de DataTables
var table = $('#typeclientsTable').DataTable({
    ajax: {
        url: '/TypeClient/GetTypeClients',
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
            data: null,
            render: function (data, type, row) {
                return `
            <div class="d-flex gap-1" role="group">
                <button class="btn btn-sm btn-floating btn-warning btn-edit" 
                    data-mdb-ripple-init 
                    data-mdb-ripple-color="light" 
                    data-mdb-ripple-duration="1000ms" 
                    title="Editar" 
                    data-clave="${data.clave}" 
                    onclick="editTypeClient(this)">
                    <i class="fa-duotone fa-solid fa-pen-to-square"></i>
                </button>
                <button class="btn btn-sm btn-floating btn-danger btn-delete" 
                    data-mdb-ripple-init 
                    data-mdb-ripple-color="light" 
                    data-mdb-ripple-duration="1000ms" 
                    title="Eliminar" 
                    data-clave="${data.clave}">
                    <i class="fa-duotone fa-solid fa-trash"></i> 
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
function addTypeClient() {
    $('#action').val('create');
    $('#typeclientModalLabel').text('Registrar Nuevo Tipo de Cliente');
    $('#typeclientForm')[0].reset();
    $('#clave').prop('readonly', false).removeClass('is-valid is-invalid');
    $('#descripcion').removeClass('is-valid is-invalid');
    $('#typeclientModal').modal('show');
}

// Editar tipo de cliente
function editTypeClient(e) {
    const clave = $(e).data('clave');

    GetData({
        path: '/TypeClient/GetTypeClientDetails',
        data: { clave: clave },
    }).then((_res) => {
        $('#action').val('edit');
        $('#typeclientModalLabel').text('Editar Tipo de Cliente');
        $('#clave').val(_res.clave).prop('readonly', true).removeClass('is-valid is-invalid');
        $('#descripcion').val(_res.descripcion).removeClass('is-valid is-invalid');
        $('#typeclientModal').modal('show');
    })
}

// Eliminar tipo de cliente
$('#typeclientsTable').on('click', '.btn-delete', function () {
    var clave = $(this).data('clave');

    Swal.fire({
        title: '¿Estás seguro?',
        text: "¡Esta acción no se puede deshacer!",
        icon: 'warning',
        buttonsStyling: true,
        showCancelButton: true,
        confirmButtonColor: '#d33',
        cancelButtonColor: '#3085d6',
        confirmButtonText: '<i class="fa-duotone fa-solid fa-thumbs-up fa-beat"></i> ¡Sí, eliminarlo!',
        cancelButtonText: '<i class="fa-duotone fa-solid fa-xmark fa-beat"></i> Cancelar'
    }).then((result) => {
        if (result.isConfirmed) {
            GetData({
                path: '/TypeClient/Delete',
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
    let url = $('#action').val() === 'create' ? '/TypeClient/Create' : '/TypeClient/Edit';

    postFormData('typeclientForm', url).then((res) => {
        if (res.success) {
            toastMixin.fire({
                icon: 'success',
                title: res.message
            });
            $('#typeclientModal').modal('hide');
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
$('#typeclientForm input').on('input', function () {
    if ($(this).val()) {
        $(this).removeClass('is-invalid').addClass('is-valid');
    } else {
        $(this).removeClass('is-valid').addClass('is-invalid');
    }
});

// Configuración del validador del formulario
$("#typeclientForm").validate({
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
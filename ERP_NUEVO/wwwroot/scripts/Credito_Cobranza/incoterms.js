// Inicialización de DataTable
var table = $('#incotermsTable').DataTable({
    ajax: {
        url: '/Incoterm/GetIncoterms',
        dataSrc: 'data'
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
                    data-id="${data.clave}" 
                    title="Editar" 
                    onclick="editIncoterm(this)">
                    <i class="fa-duotone fa-solid fa-pen-to-square"></i>
                </button>
                <button class="btn btn-sm btn-floating btn-danger btn-delete" 
                    data-mdb-ripple-init 
                    data-mdb-ripple-color="light" 
                    data-mdb-ripple-duration="1000ms" 
                    data-id="${data.clave}" 
                    title="Eliminar">
                    <i class="fa-duotone fa-solid fa-trash"></i> 
                </button>
            </div>
        `;
            },
            orderable: false,
            width: '100px'
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

// Validación personalizada para la clave (c1)
$.validator.addMethod("claveFormat", function (value, element) {
    return this.optional(element) || /^[A-Za-z0-9]{1,5}$/.test(value);
}, "La clave debe contener hasta 5 caracteres alfanuméricos");

// Configuración del validador del formulario
$("#incotermForm").validate({
    rules: {
        Clave: {
            required: true,
            claveFormat: true,
            maxlength: 5
        },
        Descripcion: {
            required: true,
            maxlength: 100
        }
    },
    messages: {
        Clave: {
            required: "La clave es obligatoria",
            maxlength: "La clave no puede exceder 5 caracteres"
        },
        Descripcion: {
            required: "La descripción es obligatoria",
            maxlength: "La descripción no puede exceder 100 caracteres"
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

// Mostrar modal para agregar nuevo incoterm
function createIncoterm() {
    $('#action').val('add');
    $('#incotermModalLabel').text('Registrar Nuevo Incoterm');
    $('#incotermForm')[0].reset();
    $('#Clave').prop('readonly', false);
    $('.form-control').removeClass('is-valid is-invalid');
    $('.invalid-feedback').remove();
    $('#incotermModal').modal('show');
}

// Editar incoterm
function editIncoterm(e) {
    const id = $(e).data('id');

    GetData({
        path: '/Incoterm/GetIncotermDetails',
        data: { id: id },
    }).then((data) => {
        if (data.success) {
            $('#action').val('edit');
            $('#incotermModalLabel').text('Editar Incoterm');
            $('#Clave').val(data.incoterm.clave).prop('readonly', true);
            $('#Descripcion').val(data.incoterm.descripcion);
            $('.form-control').removeClass('is-valid is-invalid');
            $('.invalid-feedback').remove();
            $('#incotermModal').modal('show');
        }
        else {
            toastMixin.fire({
                icon: 'error',
                title: data.message
            });
        }
    })
}

// Eliminar incoterm
$('#incotermsTable').on('click', '.btn-delete', function () {
    var id = $(this).data('id');
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
                path: '/Incoterm/DeleteIncoterm',
                data: { id: id },
            }).then((response) => {
                if (response.success) {
                    toastMixin.fire({
                        icon: 'success',
                        title: response.message,
                        //timer: 1500,
                        //showConfirmButton: false
                    });
                    table.ajax.reload();
                } else {
                    toastMixin.fire({
                        icon: 'error',
                        title: response.message
                    });
                }
            })
        }
    });
});

// Guardar incoterm
function sendForm() {
    url = $('#action').val() === 'add' ? '/Incoterm/CreateIncoterm' : '/Incoterm/UpdateIncoterm';

    postFormData('incotermForm', url).then((res) => {
        if (res.success) {
            toastMixin.fire({
                icon: 'success',
                title: res.message,
                //timer: 1500,
                //showConfirmButton: false
            });
            $('#incotermModal').modal('hide');
            table.ajax.reload(null, false);
        } else {
            toastMixin.fire({
                icon: 'error',
                title: res.message
            });
        }
    })
}

// Validación en tiempo real para el campo clave
$('#c1').on('input', function () {
    $(this).val($(this).val().toUpperCase());
});
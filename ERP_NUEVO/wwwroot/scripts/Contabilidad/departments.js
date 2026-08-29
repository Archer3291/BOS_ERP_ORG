$(document).ready(function () {

    // ── Inicialización de la tabla ────────────────────────────────────────────
    var table = createTable({
        selector: '#departmentsTable',
        path: '/Contabilidad/GetDepartments',
        rowKey: 'areaid',
        columns: [
            { data: 'nombre', title: 'Nombre' },
            { data: 'descripcion', title: 'Descripción' },
            { data: 'abreviatura', title: 'Abreviatura' },
        ],
        actions: [
            { title: 'Editar', icon: 'fa-pen-to-square', color: 'sky-500', onClick: function (row) { openEditModal(row); } },
        ],
        searchPlaceholder: 'Buscar departamento...',
    });

    // ── Validación personalizada ──────────────────────────────────────────────
    $.validator.addMethod('abreviaturaFormat', function (value, element) {
        return this.optional(element) || /^[A-Za-z0-9]{1,10}$/.test(value);
    }, 'La abreviatura debe contener hasta 10 caracteres alfanuméricos');

    // ── Validación del formulario ─────────────────────────────────────────────
    $('#departmentForm').validate({
        rules: {
            nombre: {
                required: true,
                maxlength: 100
            },
            descripcion: {
                required: true,
                maxlength: 255
            },
            abreviatura: {
                required: true,
                abreviaturaFormat: true,
                maxlength: 10
            }
        },
        messages: {
            nombre: {
                required: 'El nombre es obligatorio',
                maxlength: 'El nombre no puede exceder 100 caracteres'
            },
            descripcion: {
                required: 'La descripción es obligatoria',
                maxlength: 'La descripción no puede exceder 255 caracteres'
            },
            abreviatura: {
                required: 'La abreviatura es obligatoria',
                maxlength: 'La abreviatura no puede exceder 10 caracteres'
            }
        },
        errorElement: 'div',
        errorPlacement: function (error, element) {
            error.addClass('invalid-feedback');
            element.closest('.form-outline').append(error);
        },
        highlight: function (element) {
            $(element).addClass('is-invalid').removeClass('is-valid');
        },
        unhighlight: function (element) {
            $(element).removeClass('is-invalid').addClass('is-valid');
        }
    });

    // ── Abrir modal para nuevo departamento ───────────────────────────────────
    $('#btnAddDepartment').click(function () {
        $('#action').val('add');
        $('#departmentModalLabel').text('Registrar Nuevo Departamento');
        $('#departmentForm')[0].reset();
        $('.form-control').removeClass('is-valid is-invalid');
        $('.invalid-feedback').remove();
        $('#departmentModal').modal('show');
    });

    // ── Guardar (crear o actualizar) ──────────────────────────────────────────
    $('#btnSaveDepartment').click(function () {
        if (!$('#departmentForm').valid()) return;

        var department = {
            id: $('#id').val(),
            nombre: $('#nombre').val().trim(),
            descripcion: $('#descripcion').val().trim(),
            abreviatura: $('#abreviatura').val().trim()
        };

        var isAdd = $('#action').val() === 'add';
        var url = isAdd ? '/Contabilidad/CrearDepartamento' : '/Contabilidad/ActualizarDepartamento';
        var token = $('input[name="__RequestVerificationToken"]').val();

        $.ajax({
            url: url,
            type: 'POST',
            data: $.extend(department, { __RequestVerificationToken: token }),
            success: function (response) {
                if (response.success) {
                    Swal.fire({ icon: 'success', title: 'Éxito', text: response.message });
                    table.reload();
                    $('#departmentModal').modal('hide');
                } else {
                    Swal.fire({ icon: 'error', title: 'Error', text: response.message });
                }
            },
            error: function () {
                Swal.fire({ icon: 'error', title: 'Error', text: 'Ocurrió un error al procesar la solicitud.' });
            }
        });
    });

    // ── Abrir modal de edición ────────────────────────────────────────────────
    function openEditModal(row) {
        console.log(row)

        $('#action').val('edit');
        $('#departmentModalLabel').text('Editar Departamento');
        $('#id').val(row.areaid);
        $('#nombre').val(row.nombre);
        $('#descripcion').val(row.descripcion);
        $('#abreviatura').val(row.abreviatura);
        $('.form-control').removeClass('is-valid is-invalid');
        $('.invalid-feedback').remove();
        $('#departmentModal').modal('show');
    }

    // ── Eliminar departamento ─────────────────────────────────────────────────
    function deleteDepartment(id) {
        Swal.fire({
            title: '¿Estás seguro?',
            text: '¡Esta acción no se puede deshacer!',
            icon: 'warning',
            showCancelButton: true,
            confirmButtonColor: '#d33',
            cancelButtonColor: '#3085d6',
            confirmButtonText: '<i class="fa-solid fa-thumbs-up"></i> ¡Sí, eliminarlo!',
            cancelButtonText: '<i class="fa-solid fa-xmark"></i> Cancelar'
        }).then(function (result) {
            if (!result.isConfirmed) return;

            var token = $('input[name="__RequestVerificationToken"]').val();

            $.ajax({
                url: '/Contabilidad/EliminarDepartamento',
                type: 'POST',
                data: { id: id, __RequestVerificationToken: token },
                success: function (response) {
                    if (response.success) {
                        Swal.fire({ icon: 'success', title: '¡Registro Eliminado!', text: response.message });
                        table.reload();
                    } else {
                        Swal.fire({ icon: 'error', title: 'Error', text: response.message });
                    }
                },
                error: function () {
                    Swal.fire({ icon: 'error', title: 'Error', text: 'Ocurrió un error al eliminar el registro.' });
                }
            });
        });
    }

    // ── Forzar mayúsculas en abreviatura ─────────────────────────────────────
    $('#abreviatura').on('input', function () {
        $(this).val($(this).val().toUpperCase());
    });
});
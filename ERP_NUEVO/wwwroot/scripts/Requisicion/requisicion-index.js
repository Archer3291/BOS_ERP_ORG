$(document).ready(function () {
    var table = $('#requisicionesTable').DataTable({
        ajax: {
            url: '/Requisicion/GetRequisiciones',
            dataSrc: 'data'
        },
        columns: [
            { data: 'Id' },
            { data: 'Solicitante' },
            { data: 'Departamento' },
            {
                data: 'FechaSolicitud',
                render: function (data, type, row) {
                    if (type === 'display' || type === 'filter') {
                        return data ? moment(data).format('DD/MM/YYYY') : '';
                    }
                    return data;
                },
                type: 'date'
            },
            { data: 'Proyecto' },
            {
                data: 'Total',
                render: function (data) {
                    return '$' + parseFloat(data).toFixed(2);
                }
            },
            {
                data: 'Id',
                render: function (data) {
                    return `
                        <div class="btn-group" role="group">
                            <a href="/Requisicion/Edit/${data}" class="btn btn-sm btn-warning">Editar</a>
                            <a href="/Requisicion/ExportToExcel/${data}" class="btn btn-sm btn-info">Excel</a>
                            <button class="btn btn-sm btn-danger delete-btn" data-id="${data}">Eliminar</button>
                        </div>
                    `;
                },
                orderable: false,
                searchable: false
            }
        ],
        dom: 'Bfrtip',
        buttons: [
            {
                extend: 'excel',
                text: 'Exportar todo a Excel',
                className: 'btn btn-success',
                exportOptions: {
                    columns: [0, 1, 2, 3, 4, 5]
                },
                title: 'Listado de Requisiciones'
            }
        ],
    });

    // Eliminar requisición
    $(document).on('click', '.delete-btn', function () {
        var id = $(this).data('id');
        if (confirm('¿Está seguro que desea eliminar esta requisición?')) {
            $.post('/Requisicion/Delete/' + id)
                .done(function (response) {
                    if (response.success) {
                        table.ajax.reload();
                        toastr.success(response.message);
                    } else {
                        toastr.error(response.message);
                    }
                })
                .fail(function () {
                    toastr.error('Ocurrió un error al intentar eliminar.');
                });
        }
    });
});
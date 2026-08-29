$(document).ready(function () {
    // Agregar nueva fila
    $("#addRow").click(function () {
        var index = $("#detallesTable tbody tr").length;
        var newRow = `
            <tr>
                <td>
                    <input type="hidden" name="Detalles[${index}].Id" value="0" />
                    <input class="form-control" type="text" name="Detalles[${index}].Descripcion" />
                </td>
                <td><input class="form-control cantidad" type="number" name="Detalles[${index}].Cantidad" value="1" min="1" /></td>
                <td><input class="form-control" type="text" name="Detalles[${index}].UnidadMedida" value="UN" /></td>
                <td><input class="form-control costo" type="number" name="Detalles[${index}].CostoUnitario" step="0.01" min="0.01" value="0.00" /></td>
                <td><span class="subtotal">0.00</span></td>
                <td><button type="button" class="btn btn-danger btn-sm remove-row">Eliminar</button></td>
            </tr>`;
        $("#detallesTable tbody").append(newRow);
    });

    // Eliminar fila
    $(document).on("click", ".remove-row", function () {
        $(this).closest("tr").remove();
        calculateTotal();
        reindexRows();
    });

    // Calcular subtotales
    $(document).on("change", ".cantidad, .costo", function () {
        var row = $(this).closest("tr");
        var cantidad = parseFloat(row.find(".cantidad").val()) || 0;
        var costo = parseFloat(row.find(".costo").val()) || 0;
        var subtotal = cantidad * costo;
        row.find(".subtotal").text(subtotal.toFixed(2));
        calculateTotal();
    });

    // Reindexar filas
    function reindexRows() {
        $("#detallesTable tbody tr").each(function (index) {
            $(this).find("input, select").each(function () {
                var name = $(this).attr("name");
                if (name) {
                    name = name.replace(/\[\d+\]/, '[' + index + ']');
                    $(this).attr("name", name);
                }
            });
        });
    }

    // Calcular total
    function calculateTotal() {
        var total = 0;
        $(".subtotal").each(function () {
            total += parseFloat($(this).text()) || 0;
        });
        $("#total").text(total.toFixed(2));
    }

    // Calcular totales al cargar
    calculateTotal();
});
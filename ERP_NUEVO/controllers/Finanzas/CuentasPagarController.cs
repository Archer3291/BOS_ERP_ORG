using BOS_ERP.Controllers;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using static BOS_ERP.Controllers.SqlHelper;

namespace BOS_ERP.Controllers
{
    public partial class FinanzasController : Utilities
    {
        #region Obtener datos generales
        public JsonResult GetOrdenesCompra()
        {
            string query = "SELECT * FROM ordenes_compra WHERE estado = 'Pendiente'";
            var result = RunQuery(query);
            return Json(result);
        }

        [AreaAuthorize(new string[] { "Administración y Finanzas" }, "ERP_SRS")]
        public JsonResult GetDataCalendario()
        {
            var parameters = new Dictionary<string, object>();
            string query = "SELECT id, proveedor, encabezado_id, fecha_pago, monto, frecuencia, " +
                "   tasa_interes, es_contado, estado, descripcion, numero_pago, total_pagos " +
                "FROM pagos_proveedor " +
                "WHERE creado_por = @usuarioId";
            parameters.Add("usuarioId", GetUserId(User.Identity.Name));
            var pagos = RunQuery(query, parameters);

            var eventos = pagos.Select(p =>
            {
                string estado = p["estado"].ToString();
                string color = "#95a5a6"; // Gris por defecto

                if (estado == "Pendiente")
                    color = "#f39c12"; // Naranja
                else if (estado == "Vencido")
                    color = "#e74c3c"; // Rojo
                else if (estado == "Pagado")
                    color = "#2ecc71"; // Verde
                else if (estado == "Cancelado")
                    color = "#7F8C8D"; // Gris oscuro

                return new
                {
                    id = p["id"],
                    title = $"{p["proveedor"]} - Pago #{p["numero_pago"]} (${p["monto"]})",
                    start = Convert.ToDateTime(p["fecha_pago"]).ToString("yyyy-MM-ddTHH:mm:ss"),
                    allDay = false,
                    backgroundColor = color,
                    borderColor = color,
                    textColor = "white",
                    extendedProps = new
                    {
                        encabezado_id = p["encabezado_id"],
                        frecuencia = p["frecuencia"],
                        total_pagos = p["total_pagos"],
                        tasa_interes = p["tasa_interes"],
                        descripcion = p["descripcion"],
                        estado = p["estado"],
                        pago = Convert.ToDecimal(p["monto"]),
                        numero_pago = p["numero_pago"],
                    }
                };
            });

            return Json(eventos);
        }

        [HttpPost, ValidateAntiForgeryToken]
        [AreaAuthorize(new string[] { "Administración y Finanzas" }, "ERP_SRS")]
        public JsonResult ActualizarEstadoPago(IFormCollection fc)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                string query = "UPDATE pagos_proveedor SET estado = @estado, fecha_actualizado = NOW() " +
                    "WHERE encabezado_id = @encabezado_id AND numero_pago = @numero_pago";
                parameters.Add("estado", fc["estatus"].ToString());
                parameters.Add("encabezado_id", Convert.ToInt32(fc["encabezado_id"].ToString()));
                parameters.Add("numero_pago", Convert.ToInt32(fc["numero_pago"].ToString()));
                RunQuery(query, parameters);
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "Finanzas/ActualizarEstadoPago");
                return Json(new { success = false, message = $"Ocurrio un error inesperado." });
            }
        }

        #endregion
    }
}
using BOS_ERP.Models;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers.Operaciones
{
    public class PedidoCeController : FacturacionComercioExController
    {
        //public CotizacionController() : base("ERP_SRS") { }
        [Route("PedidoCe/GuardarFacturaCE")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult GuardarFacturaCE()
        {
            try
            {
                // Creamos un nuevo objeto de la factura
                var factura = new FacturaComercioEx
                {
                    Serie = "A",
                    Folio = "1205",
                    IdTipoPago = "99",
                    Moneda = Request.Form["moneda"].ToString(),
                    MdpFactura = Request.Form["metodopago"].ToString(),
                    TipoCambio = Convert.ToDouble(Request.Form["paridad"].ToString()),
                    RsoEmisor = "ESCUELA KEMPER URGATE",
                    RfcEmisor = "EKU9003173C9",
                    CpE = "42501",
                    Rege = "601",
                    IdUsoCFDI = Request.Form["usocfdi"].ToString(),
                    Tproductos = new System.Data.DataTable()
                };

                // Definimos las columnas para los productos
                factura.Tproductos.Columns.Add("articulo");
                factura.Tproductos.Columns.Add("cantidad", typeof(double));
                factura.Tproductos.Columns.Add("ClaveUnidad");
                factura.Tproductos.Columns.Add("Unidad");
                factura.Tproductos.Columns.Add("precio", typeof(double));
                factura.Tproductos.Columns.Add("descuento", typeof(double));
                factura.Tproductos.Columns.Add("importe", typeof(double));
                factura.Tproductos.Columns.Add("descripcion");
                factura.Tproductos.Columns.Add("iva");
                factura.Tproductos.Columns.Add("objeto");
                factura.Tproductos.Columns.Add("claveprod");
                factura.Tproductos.Columns.Add("preciouni");

                // Procesamos los datos del cliente
                var parameters = new Dictionary<string, object>();
                string selectCliente = "SELECT c1,c2,c3,c10, c4, c5 ,c6, c12, c27, c60  FROM sellosop.kdud WHERE c2 = @id";
                string selectDirCliente = "SELECT c19, c18, c13  FROM sellosop.kdfedir WHERE c2 = @id";
                parameters.Add("id", Request.Form["cliente"].ToString());

                var resultCliente = RunQuery(selectCliente, parameters, false, null, null, "ERP_SRS");
                var resultDirCliente = RunQuery(selectDirCliente, parameters, false, null, null, "ERP_SRS");

                if (resultCliente.Count == 0 || resultDirCliente.Count == 0)
                {
                    return Json(new { resultado = false, mensaje = "Cliente o dirección no encontrados." });
                }

                factura.RsoCliente = resultDirCliente[0]["c19"].ToString();
                factura.RfcCliente = resultCliente[0]["c10"].ToString();
                factura.CpR = resultDirCliente[0]["c13"].ToString();
                factura.Regc = resultDirCliente[0]["c18"].ToString();

                // Procesamos los artículos
                int i = 0;
                while (!string.IsNullOrWhiteSpace(Request.Form[$"articulos[{i}].articulo"]))
                {
                    var articulo = Request.Form[$"articulos[{i}].articulo"];
                    var cantidad = Convert.ToDouble(Request.Form[$"articulos[{i}].cantidad"]);
                    var claveUnidad = Request.Form[$"articulos[{i}].ClaveUnidad"];
                    var Unidad = Request.Form[$"articulos[{i}].Unidad"];
                    var precio = Convert.ToDouble(Request.Form[$"articulos[{i}].precio"]);
                    var descuento = Convert.ToDouble(Request.Form[$"articulos[{i}].descuento"]);
                    var importe = Convert.ToDouble(Request.Form[$"articulos[{i}].importe"]);
                    var descripcion = Request.Form[$"articulos[{i}].descripcion"];
                    var iva = Convert.ToDouble(Request.Form[$"articulos[{i}].iva"]);
                    var objeto = Request.Form[$"articulos[{i}].objeto"];
                    var claveprod = Request.Form[$"articulos[{i}].claveprod"];
                    var preciouni = Request.Form[$"articulos[{i}].preciouni"];

                    // Añadimos el artículo a la tabla
                    factura.Tproductos.Rows.Add(
                        articulo,
                        cantidad,
                        claveUnidad,
                        Unidad,
                        precio,
                        descuento,
                        importe,
                        descripcion,
                        Math.Round((cantidad * precio) * (iva / 100), 2),
                        objeto,
                        claveprod,
                        preciouni
                    );

                    i++;
                }

                // Aquí generamos el XML o guardamos en la base de datos
                GenerarXml(factura); // Esta puede lanzar excepción si falla

                return Json(new { resultado = true, mensaje = "Factura procesada correctamente" });
            }
            catch (Exception ex)
            {
                // Log del error si tienes sistema de logs
                return Json(new
                {
                    resultado = false,
                    mensaje = "Error al procesar la factura: " + ex.Message
                });
            }
        }


    }
}

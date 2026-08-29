using Microsoft.AspNetCore.Authorization;
using Newtonsoft.Json;
using BOS_ERP.Filters;
using BOS_ERP.Helpers;
using BOS_ERP.Infrastructure;
using BOS_ERP.Models;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers.Ventas.Ventas_Industriales
{
    [Authorize]
    public class VTCotizacionController : Utilities
    {
        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Ventas Industriales", Accion = "Creacion de cotizacion")]
        public JsonResult Guardar(IFormCollection fc)
        {
            try
            {
                TokenStore.LimpiarExpirados();

                string usuarioActual = User.Identity.Name;
                string descuentoToken = fc["descuentoToken"].ToString() ?? "";
                string precioToken = fc["precioToken"].ToString() ?? "";

                var productos = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(
                    fc["productosJSON"].ToString() ?? "[]");

                bool descuentoAutorizado = TokenStore.Validar(descuentoToken, usuarioActual, "DESCUENTO");
                bool precioAutorizado = TokenStore.Validar(precioToken, usuarioActual, "CAMBIO DE PRECIO");

                // ── Resolver cliente ──────────────────────────────────────────
                // Antes de validar precios: la lista de precios y las reglas por cliente
                // dependen de él, y tomarlo de la sesión permitía validar contra otro.
                int empresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));
                int idCliente = this.ResolverClienteId(fc["cliente"].ToString(), empresaId);

                // ── Validar cada partida contra las reglas de precio ──────────
                var reglas = this.ValidarPartidas(
                    empresaId, idCliente, productos, precioAutorizado, descuentoAutorizado);

                if (!reglas.Permitido)
                    return Json(new { success = false, message = reglas.Mensaje });

                // Invalidar tokens DESPUÉS de validar todo correctamente
                TokenStore.Invalidar(descuentoToken);
                TokenStore.Invalidar(precioToken);

                // ── Encabezado ────────────────────────────────────────────────
                var encabezado = new DocumentoEncabezado
                {
                    EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")),
                    IdArea = 26,
                    IdTpDoc = 87,
                    UsrDep = GetAreaName(User.Identity.Name),
                    Anio = DateTime.Now.Year,
                    Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")),
                    Fch = DateTime.Now,
                    TpMov = "TYBCOT",
                    ComentAut = fc["comentarios"].ToString(),
                    UsrDoc = User.Identity.Name,
                    FchCap = DateTime.Now,
                    Usr0 = GetUserId(User.Identity.Name),
                    Fch0 = DateTime.Now,
                    Imp = Convert.ToDecimal(fc["total"].ToString()),
                    Dto = Convert.ToDecimal(fc["descuento"].ToString()),
                    CliProv = fc["cliente"].ToString(),
                    Ccy = fc["moneda"].ToString(),
                    Ref = idCliente,
                    Estatus = 1,
                    Flete = Convert.ToDecimal(fc["flete"].ToString()),
                    VdrCpr = fc["vendedor"].ToString(),
                    Coment1 = fc["concepto"].ToString(),
                    Par = Convert.ToDecimal(fc["paridad"].ToString()),
                    NatDocPadreChar = fc["ordenCompra"].ToString(),
                    Mdp = fc["tipoPago"].ToString() == "credito" ? "PPD" : "PUE",
                };

                // ── Partidas ──────────────────────────────────────────────────
                var partidas = new List<PartidaDocumento>();

                foreach (var p in productos)
                {
                    decimal cantidad = p.ContainsKey("cantidad")
                        ? decimal.Parse(p["cantidad"], System.Globalization.CultureInfo.InvariantCulture) : 0m;
                    decimal precio = p.ContainsKey("precio")
                        ? decimal.Parse(p["precio"], System.Globalization.CultureInfo.InvariantCulture) : 0m;
                    decimal descuento = p.ContainsKey("descuento")
                        ? decimal.Parse(p["descuento"], System.Globalization.CultureInfo.InvariantCulture) : 0m;
                    string cveProd = p.ContainsKey("productoId") ? p["productoId"] : "";

                    int productId = Convert.ToInt32(RunScalar(
                        "SELECT id_catproductos FROM catproductos " +
                        "WHERE cve_prod = @cve_prod AND empresa_id = @empresa_id",
                        new Dictionary<string, object>
                        {
                            { "cve_prod",   cveProd },
                            { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) }
                        }));

                    partidas.Add(new PartidaDocumento
                    {
                        CveProd = cveProd,
                        DescrProd = p.ContainsKey("descripcion") ? p["descripcion"] : "",
                        CantUd = cantidad,
                        PvProd = precio,
                        Dto1 = descuento,
                        ImpPart = cantidad * precio * (1 - descuento / 100),
                        Ud = p.ContainsKey("unidad") ? p["unidad"] : "PZA",
                        IdProducto = productId,
                        TpDocAnt = p.ContainsKey("comentario") ? p["comentario"] : ""
                    });
                }

                var folio = GenerarDocumentoConPartidas(encabezado, partidas);

                ViewData["detalles"] = new AuditDetails
                {
                    DocumentoId = Convert.ToInt32(folio["IdEncabezado"]),
                    Folio = folio["folio_generado"].ToString()
                };

                return Json(new
                {
                    success = true,
                    message = "Cotización creada exitosamente.",
                    folio_generado = folio["folio_generado"].ToString()
                });
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "VTCotizacion/Guardar");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // La consulta de la regla y la validación de las partidas viven en
        // Helpers/ReglasPrecioHelper.cs, compartidas con los demás documentos.
    }
}
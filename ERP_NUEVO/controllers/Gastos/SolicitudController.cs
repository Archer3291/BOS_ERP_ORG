using BOS_ERP.Filters;
using BOS_ERP.Models;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System.Text.Json;

namespace BOS_ERP.Controllers
{
    public partial class GastosController
    {

        public IActionResult DatosSelect()
        {
            var result = new Dictionary<string, List<Dictionary<string, object>>>();
            string queryMoneda = "SELECT clave as id, nombre as nombre FROM cat_monedas ORDER BY clave DESC";        
            string proveedores = "SELECT cve_prov as id, n_prov as nombre, id_prov FROM catproveedores ORDER BY cve_prov;";
            string centros = "SELECT areaid as id, nombre FROM areas;";
            result.Add("monedas", RunQuery(queryMoneda));
            result.Add("proveedores", RunQuery(proveedores));
            result.Add("centros", RunQuery(centros));

            return Json(result);
        }

        public IActionResult DatosCentroCostos()
        {
            string query = "SELECT areaid, nombre FROM areas;";
            var result = RunQuery(query);
            return Json(new { result, success = true });
        }

        #region Acciones
        [Route("Gastos/Crear")]
        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Gastos", Accion = "Creacion de solicitud de gasto")]
        public JsonResult Crear(SolicitudGastosViewModel model)
        {
            try
            {
                // ── 1. Validaciones básicas ──────────────────────────────────────
                if (string.IsNullOrWhiteSpace(model.Concepto) || model.Monto <= 0)
                    return Json(new { success = false, message = "Faltan datos obligatorios." });

                var parameters = new Dictionary<string, object>();

                // ── 2. Resolver usuario, área y gerente (igual que en Requisicion) ──
                var area = GetAreaName(User.Identity.Name);
                if (string.IsNullOrEmpty(area))
                    return Json(new { success = false, message = "No se encontró un área asignada para este usuario." });

                string queryUser =
                    "SELECT u.usuarioid, u.nombre || ' ' || u.apellido AS nombreUsuario, " +
                    "       u.email AS emailUsuario, u.nombreusuario AS aliasUsuario, " +
                    "       u2.usuarioid AS managerId, u2.nombre || ' ' || u2.apellido AS nombreManager, " +
                    "       u2.email AS emailManager, u2.nombreusuario AS aliasManager " +
                    "FROM usuarios u " +
                    "INNER JOIN (SELECT * FROM usuarios u WHERE u.rolid = 8) u2 ON u2.areaid = u.areaid " +
                    "WHERE u.nombreusuario = @userName";
                parameters.Add("userName", User.Identity.Name);
                var userResult = RunQuery(queryUser, parameters)[0];

                if (userResult == null)
                    return Json(new { success = false, message = "No se encontró un gerente para esta área." });

                // ── 3. Resolver IdTpDoc para el tipo de movimiento "SSG" ─────────
                //      (agrega "SSG" a tu catálogo tpdoc con abreviatura = "SSG")
                parameters = new Dictionary<string, object>();
                parameters.Add("tpmov", "SSG");
                string queryTpDoc = "SELECT idtpdoc FROM tpdoc WHERE abreviaturatpdoc = @tpmov";
                var idTpDocumento = RunScalar(queryTpDoc, parameters);

                // ── 4. Resolver el id numérico del aprobador seleccionado ─────────
                //      Busca por nombre completo en la tabla de usuarios
                parameters = new Dictionary<string, object>();
                parameters.Add("nombreAprobador", model.Aprobador);
                string queryAprob =
                    "SELECT usuarioid FROM usuarios " +
                    "WHERE nombre || ' ' || apellido = @nombreAprobador " +
                    "   OR nombreusuario = @nombreAprobador " +
                    "LIMIT 1";
                var idAprobador = RunScalar(queryAprob, parameters);

                // ── 5. Armar el encabezado ────────────────────────────────────────
                var encabezado = new DocumentoEncabezado();

                encabezado.EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));
                encabezado.IdArea = 4;  // tu helper existent;
                encabezado.IdTpDoc = 82;
                encabezado.TpMov = "FSGTO";
                encabezado.Anio = DateTime.Now.Year;
                encabezado.Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));

                // Campos de negocio
                encabezado.Fch = string.IsNullOrEmpty(model.Fecha) ? DateTime.Now : DateTime.Parse(model.Fecha);
                encabezado.Ccy = model.Moneda ?? "MXN";
                encabezado.CliProv = model.Proveedor;
                encabezado.Ref = Convert.ToInt32(RunScalar("SELECT id_prov FROM catproveedores WHERE cve_prov = @cve_prov AND id_empresa = @id_empresa ", new Dictionary<string, object> { { "cve_prov", model.Proveedor }, { "id_empresa", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) } }));
                encabezado.Imp = model.Monto;
                encabezado.ComentAut = model.Justificacion;
                encabezado.Coment1 = model.Urgencia;           // urgencia en primer comentari;
                encabezado.Coment2 = model.NotasAdicionales;   // notas en segundo comentari;
                encabezado.Coment3 = model.Categoria + " / " + model.Subcategoria; // trazabilida;         

                // Flujo de aprobación
                encabezado.UsrDoc = User.Identity.Name;
                encabezado.FchCap = DateTime.Now;
                encabezado.Usr0 = GetUserId(User.Identity.Name);  // creado;
                encabezado.Fch0 = DateTime.Now;
                encabezado.Usr1 = idAprobador != null ? Convert.ToInt32(idAprobador) : Convert.ToInt32(userResult["managerid"]);
                encabezado.Usr2 = Convert.ToInt32(userResult["managerid"]); // gerente del área

                // Centro de costo
                encabezado.CentroCostos = ResolveCentroCosto(model.CentroCosto); // ver helper abajo

                // Estatus inicial: borrador/pendiente
                encabezado.Estatus = 1;
                encabezado.EnPresupuesto = false;
                encabezado.TipoPoceso = "gasto";
                encabezado.EsServicio = true;   // los gastos son servicios, no inventario físico
                encabezado.UsrDep = area;


                // ── 6. Armar la partida (una sola línea por solicitud de gasto) ───
                //      Si en el futuro quieres desglosar ítems, agrega más partidas.
                var partidas = new List<PartidaDocumento>
    {
        new PartidaDocumento
        {
            NroPart   = 1,
            CveProd   = model.Subcategoria?.Replace(" ", "_").ToUpper() ?? "GASTO",
            DescrProd = $"[{model.Categoria}] {model.Subcategoria} — {model.Concepto}",
            CantUd    = 1,
            Ud        = "SER",          // unidad "Servicio" en tu catálogo
            PvProd    = model.Monto,
            ImpPart   = model.Monto,
            Ccy       = model.Moneda ?? "MXN",
        }
    };

                // ── 7. Guardar usando tu función existente ────────────────────────
                var folio = GenerarDocumentoConPartidas(encabezado, partidas);

                // ── 8b. Guardar campos dinámicos ─────────────────────────────────────
                if (!string.IsNullOrWhiteSpace(model.CamposDinamicos))
                {
                    var campos = JsonConvert.DeserializeObject<List<CampoDinamico>>(model.CamposDinamicos);
                    if (campos != null)
                    {
                        foreach (var campo in campos)
                        {
                            var p = new Dictionary<string, object>
            {
                { "encabezadoId",  Convert.ToInt32(folio["IdEncabezado"]) },
                { "campoId",       campo.CampoId?.ToString() ?? "" },
                { "campoLabel",    campo.CampoLabel?.ToString() ?? "" },
                { "valorTexto",    campo.Valor?.ToString() ?? "" },
                { "categoria",     model.Categoria ?? "" },
                { "subcategoria",  model.Subcategoria ?? "" }
            };

                            RunUpdate(
                                "INSERT INTO solicitud_gastos_campos " +
                                "(encabezado_id, campo_id, campo_label, valor_texto, categoria, subcategoria) " +
                                "VALUES (@encabezadoId, @campoId, @campoLabel, @valorTexto, @categoria, @subcategoria)",
                                p
                            );
                        }
                    }
                }


                // ── 8. Guardar archivos adjuntos ──────────────────────────────────
                for (int i = 0; i < Request.Form.Files.Count; i++)
                {
                    IFormFile archivo = Request.Form.Files[i];
                    if (archivo == null || archivo.Length == 0) continue;

                    var ext = Path.GetExtension(archivo.FileName);
                    var uuid = Guid.NewGuid().ToString();
                    var ruta = "content/archivos_solicitud/";

                    parameters = new Dictionary<string, object>
        {
            { "nombreOriginal", archivo.FileName },
            { "ruta",           ruta },
            { "uuid",           uuid },
            { "extencion",      ext },
            { "encabezado",     Convert.ToInt32(folio["IdEncabezado"]) }
        };

                    RunUpdate(
                        "INSERT INTO archivos_solicitud (nombre_original, path, uuid, extencion, encabezado_id) " +
                        "VALUES (@nombreOriginal, @ruta, @uuid, @extencion, @encabezado)",
                        parameters
                    );

                    UploadFormFileToPath(ruta, archivo, uuid, ext);
                }

                // ── 9. Notificación al aprobador (mismo patrón que Requisicion) ───
                var urlBase = $"{Request.Scheme}://{Request.Host}";
                string urlDetalle = $"{urlBase}/Gastos/Detalle?id={folio["folio_generado"]}";

                _ = SendNotificationInterno(
                    userResult["aliasmanager"].ToString(),
                    userResult["emailmanager"].ToString(),
                    new
                    {
                        icon = "info",
                        title = "Nueva solicitud de gasto",
                        message = $"El usuario {userResult["nombreusuario"]} creó la solicitud de gasto " +
                                  $"{folio["folio_generado"]} por ${model.Monto:N2} {model.Moneda}. " +
                                  $"Requiere tu aprobación.",
                        buttons = new[]
                        {
                new { text = "Ver Detalles", style = "primary",
                      action = $"window.open('{urlDetalle}', '_blank')" },
                new { text = "Más Tarde",    style = "secondary", action = (string)null }
                        },
                        timer = 0,
                        folio = folio["folio_generado"],
                    }
                );

                // ── 10. Auditoría ─────────────────────────────────────────────────
                ViewData["detalles"] = new AuditDetails
                {
                    DocumentoId = Convert.ToInt32(folio["IdEncabezado"]),
                    Folio = folio["folio_generado"].ToString(),
                    Observaciones = model.Justificacion
                };

                return Json(new
                {
                    success = true,
                    folio = folio["folio_generado"].ToString(),
                    message = $"Solicitud de gasto creada. Folio: {folio["folio_generado"]}"
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al procesar: " + ex.Message });
            }
        }

        // ── Helper: convierte el string de centro de costo a su id numérico ──────────
        private int? ResolveCentroCosto(string nombre)
        {
            if (string.IsNullOrWhiteSpace(nombre)) return null;
            var p = new Dictionary<string, object> { { "nombre", Convert.ToInt32(nombre) } };
            var id = RunScalar("SELECT areaid FROM areas WHERE areaid = @nombre LIMIT 1", p);
            return id != null ? Convert.ToInt32(id) : (int?)null;
        }

        #endregion
    }

    public class CampoDinamico
    {
        public int? CampoId { get; set; }
        public string? CampoLabel { get; set; }
        public string? Valor { get; set; }
    }
}
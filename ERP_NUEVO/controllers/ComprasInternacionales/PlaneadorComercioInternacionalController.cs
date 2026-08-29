using ClosedXML.Excel;
using BOS_ERP.Filters;
using BOS_ERP.Models;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace BOS_ERP.Controllers.ComprasInternacionales
{
    [Authorize]
    public class PlaneadorComercioInternacionalController : Utilities
    {
        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Compras Internacionales", Accion = "Creacion del planeador de compras")]
        public IActionResult GuardarExcel(IFormFile archivoExcel)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                //Validar archivo recibido
                if (archivoExcel == null || archivoExcel.Length <= 0)
                    return Json(new { success = false, message = "No se recibió ningún archivo válido." });

                //Lista de columnas obligatorias normalizadas
                var columnasRequeridas = new List<string>
                {
                    "No. Parte SRS",
                    "Descripción producto",
                    "Unidad de Medida",
                    "Total Almacenes",
                    "Con. Mes",
                    "Cobertura",
                    "Con. Diario",
                    "Por cubrir",
                    "Lead Time",
                    "Exis. Arribo",
                    "En Fabricación (Listo para entrega)",
                    "En Fabricación",
                    "Inv. Minimo",
                    "Inv. Optimo",
                    "Invt. Maximo",
                    "Sugerido Optimo (Compra)",
                    "Peso",
                    "Peso total"
                }
                .Select(c => c.Replace("\r", " ")
                              .Replace("\n", " ")
                              .Replace("  ", " ")
                              .Trim())
                .ToList();

                //Leer Excel con ClosedXML
                using (var workbook = new XLWorkbook(archivoExcel.OpenReadStream()))
                {
                    var worksheet = workbook.Worksheets.FirstOrDefault();
                    if (worksheet == null)
                        return Json(new { success = false, message = "El archivo no contiene hojas." });

                    //Leer encabezados (primera fila normalizados)
                    var encabezados = new List<string>();
                    var row = worksheet.Row(1);

                    foreach (var cell in row.CellsUsed())
                    {
                        var valor = cell.GetString();

                        valor = valor.Replace("\r", " ")
                                     .Replace("\n", " ")
                                     .Replace("  ", " ")
                                     .Trim();

                        if (!string.IsNullOrEmpty(valor))
                            encabezados.Add(valor);
                    }

                    // ✅ Validar columnas requeridas
                    var faltantes = columnasRequeridas
                        .Except(encabezados, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (faltantes.Any())
                    {
                        return Json(new
                        {
                            success = false,
                            message = "El archivo no contiene todas las columnas necesarias. Faltan: " + string.Join(", ", faltantes)
                        });
                    }
                }

                //Si pasó validación, ahora sí crear encabezado y guardar
                var encabezado = new DocumentoEncabezado
                {
                    EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")),
                    IdArea = 3,
                    IdTpDoc = 22,
                    UsrDep = GetAreaName(User.Identity.Name),
                    Anio = DateTime.Now.Year,
                    Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")),
                    Fch = DateTime.Now,
                    TpMov = "PLCI",
                    Coment1 = Request.Form["observaciones"].ToString(),
                    UsrDoc = User.Identity.Name,
                    FchCap = DateTime.Now,
                    Usr0 = GetUserId(User.Identity.Name),
                    Fch0 = DateTime.Now,
                    CliProv = "srs",
                    Estatus = 1,
                };

                var documento = GenerarDocumentoConPartidas(encabezado, new List<PartidaDocumento>());

                //Guardar archivo en disco
                string nombreOriginal = Path.GetFileName(archivoExcel.FileName);
                string ruta = "content/archivos_planeador_ci/";
                string uuid = Guid.NewGuid().ToString();
                string extension = Path.GetExtension(nombreOriginal);
                var result = UploadFormFileToPath(ruta, archivoExcel, uuid, extension);

                //Guardar registro en BD
                string query = "INSERT INTO archivos_planeador_ci (nombre_original, path, uuid, extencion, encabezado_id) " +
                               "VALUES (@nombreOriginal, @ruta, @uuid, @extension, @encabezado_id)";
                var parametersArchivos = new Dictionary<string, object>
                {
                    { "nombreOriginal", nombreOriginal },
                    { "ruta", ruta + uuid + extension },
                    { "uuid", uuid },
                    { "extension", extension },
                    { "encabezado_id", Convert.ToInt32(documento["IdEncabezado"]) },
                };
                RunUpdate(query, parametersArchivos);

                query = "SELECT u.usuarioid, u.nombre || u.apellido AS nombreUsuario,  " +
                    "                       u.email AS emailUsuario, u.nombreusuario AS aliasUsuario " +
                    "                    FROM usuarios u  " +
                    "                    WHERE u.usuarioid = @userName";
                parameters.Add("userName", Convert.ToInt32(Request.Form["director"].ToString()));
                var userResult = RunQuery(query, parameters)[0];

                if (userResult == null || userResult.Count() == 0)
                {
                    return Json(new { success = false, message = "No se encontro un gerente para esta area." });
                }
                var urlBase = $"{Request.Scheme}://{Request.Host}";
                string urlCotizacion = $"{urlBase}/?id={documento["folio_generado"].ToString()}";

                _ = SendNotificationInterno(userResult["aliasusuario"].ToString(), userResult["emailusuario"].ToString(), new
                {
                    icon = "info",
                    title = "Nueva solicitud de cotizacion creada",
                    message = $"El usuario {User.Identity.Name} ha creado una nueva solicitud de cotizacion con el folio {documento["folio_generado"]}, la cual requiere su aprobación.",
                    buttons = new[]
                   {
                        new { text = "Ver Detalles", style = "primary", action = $"window.open('{urlCotizacion.ToString()}', '_blank')" },
                        new { text = "Más Tarde", style = "secondary", action = (string)null }
                    },
                    timer = 0,
                    folio = documento["folio_generado"].ToString(),
                });

                return Json(new { success = true, message = "Archivo cargado correctamente.\nFolio: " + documento["folio_generado"] });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al guardar en la base de datos: " + ex.Message });
            }
        }


        [HttpPost]
        public IActionResult ProcesarExcel(IFormFile archivoExcel)
        {
            var listaProductos = new List<InventarioProducto>();

            if (archivoExcel != null && archivoExcel.Length > 0)
            {
                using (var workbook = new XLWorkbook(archivoExcel.OpenReadStream()))
                {
                    var hoja = workbook.Worksheet(1); // primera hoja

                    var rows = hoja.RangeUsed().RowsUsed(); // obtiene las filas usadas
                    bool esPrimeraFila = true;

                    foreach (var row in rows)
                    {
                        // saltar encabezados
                        if (esPrimeraFila)
                        {
                            esPrimeraFila = false;
                            continue;
                        }

                        var producto = new InventarioProducto
                        {
                            NoParteSRS = row.Cell(1).GetString().Trim(),
                            DescripcionProducto = row.Cell(2).GetString().Trim(),
                            UnidadMedida = row.Cell(3).GetString().Trim(),
                            TotalAlmacenes = row.Cell(4).GetDouble(),
                            ConsumoMes = row.Cell(5).GetDouble(),
                            PorCubrir = row.Cell(6).GetDouble(),
                            EnFabricacionListo = row.Cell(7).GetDouble(),
                            EnFabricacion = row.Cell(8).GetDouble(),
                            InventarioMaximo = row.Cell(10).GetDouble(),
                            Peso = row.Cell(11).GetDouble(),
                            PesoTotal = row.Cell(12).GetDouble(),
                        };

                        listaProductos.Add(producto);
                    }
                }
            }
            return View("/Views/ComprasInternacionales/InventarioListado.cshtml", listaProductos);
        }
    }
}
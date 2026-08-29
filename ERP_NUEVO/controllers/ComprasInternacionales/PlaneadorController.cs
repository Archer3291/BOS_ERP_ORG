using ClosedXML.Excel;
using BOS_ERP.Models;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using BOS_ERP.Controllers;

namespace BOS_ERP.controllers.ComprasInternacionales
{
    [Authorize]
    public class PlaneadorController : Utilities
    {
        [HttpPost]
        public IActionResult ProcesarExcel(IFormFile archivoExcel)
        {
            var encabezadosEsperados = new List<string>
            {
                "NO.",
                "ITEM SRS",
                "DESCRIPCION SRS",
                "LINEA",
                "TIPO",
                "GRUPO",
                "UNIDAD 1",
                "UNIDAD 2",
                "FACTOR DE CONVERSION",
                "ITEM TRYGONAL",
                "PROVEEDOR",
                "MONEDA DE COMPRA",
                "IMPORTACION",
                "PRODUCTO",
                "DESCRIPCION",
                "UNIDAD DE MEDIDA",
                "DESCRIPCION",
                "UNIDAD SECUNDARIA",
                "DESCRIPCION",
                "IVA EXENTO S/N",
                "OBJETO DE IMPUESTO"
            };

            if (archivoExcel == null || archivoExcel.Length == 0)
                return Json(new { success = false, message = "No se recibió ningún archivo." });

            using (var workbook = new XLWorkbook(archivoExcel.OpenReadStream()))
            {
                var worksheet = workbook.Worksheet(1);
                var filas = worksheet.RowsUsed().ToList();

                if (filas == null || filas.Count == 0)
                    return Json(new { success = false, message = "El archivo está vacío." });

                // Normalizado de los esperados
                var esperadosNorm = encabezadosEsperados.Select(NormalizarTexto).ToList();
                int expectedCount = encabezadosEsperados.Count;

                // Buscar la fila de encabezado entre las primeras N filas (ej: 10)
                int maxSearchRows = Math.Min(10, filas.Count);
                int headerIndexInList = -1;
                int bestMatchCount = -1;
                int bestMatchIndex = -1;

                for (int r = 0; r < maxSearchRows; r++)
                {
                    var row = filas[r];
                    var leidosNorm = new List<string>();
                    for (int c = 1; c <= expectedCount; c++)
                        leidosNorm.Add(NormalizarTexto(row.Cell(c).GetString()));

                    int matches = 0;
                    for (int i = 0; i < expectedCount; i++)
                        if (leidosNorm[i] == esperadosNorm[i]) matches++;

                    if (matches == expectedCount)
                    {
                        // coincidencia exacta en la fila r
                        headerIndexInList = r;
                        break;
                    }

                    if (matches > bestMatchCount)
                    {
                        bestMatchCount = matches;
                        bestMatchIndex = r;
                    }
                }

                // Si no tenemos coincidencia exacta, aceptar la mejor si supera umbral (p.e. 85% o -3 columnas)
                if (headerIndexInList == -1)
                {
                    int threshold = Math.Max((int)Math.Ceiling(expectedCount * 0.85), expectedCount - 3);
                    if (bestMatchCount >= threshold)
                        headerIndexInList = bestMatchIndex;
                }

                if (headerIndexInList == -1)
                {
                    return Json(new
                    {
                        success = false,
                        message = "No se encontró la fila de encabezado en las primeras filas. Asegúrate del formato (encabezados en columnas 1..21)."
                    });
                }

                // Leemos encabezados (sin normalizar) desde la fila encontrada
                var filaEncabezado = filas[headerIndexInList];
                var encabezadoGeneral = new List<string>();
                var encabezadoProveedor = new List<string>();
                var encabezadoProductoSat = new List<string>();

                for (int i = 1; i <= 9; i++)
                    encabezadoGeneral.Add(filaEncabezado.Cell(i).GetString());
                for (int i = 10; i <= 13; i++)
                    encabezadoProveedor.Add(filaEncabezado.Cell(i).GetString());
                for (int i = 14; i <= 21; i++)
                    encabezadoProductoSat.Add(filaEncabezado.Cell(i).GetString());

                // Preparar colecciones de datos
                var general = new List<List<string>>();
                var proveedor = new List<List<string>>();
                var productoSat = new List<List<string>>();

                var productosNuevos = new List<FilaProcesada>();
                var productosYProveedoresExistentes = new List<FilaProcesada>();
                var productosConProveedorInexistente = new List<FilaProcesada>();
                var proveedoresNuevos = new List<List<string>>();

                // Iterar las filas de datos desde la fila siguiente a la de encabezado
                for (int f = headerIndexInList + 1; f < filas.Count; f++)
                {
                    var fila = filas[f];

                    var filaGeneral = new List<string>();
                    var filaProveedor = new List<string>();
                    var filaProductoSat = new List<string>();

                    for (int i = 1; i <= 9; i++)
                        filaGeneral.Add(fila.Cell(i).GetString());

                    for (int i = 10; i <= 13; i++)
                        filaProveedor.Add(fila.Cell(i).GetString());

                    for (int i = 14; i <= 21; i++)
                        filaProductoSat.Add(fila.Cell(i).GetString());

                    // Si la fila está totalmente vacía, omitir (seguridad)
                    bool filaVacia = filaGeneral.All(s => string.IsNullOrWhiteSpace(s))
                                    && filaProveedor.All(s => string.IsNullOrWhiteSpace(s))
                                    && filaProductoSat.All(s => string.IsNullOrWhiteSpace(s));
                    if (filaVacia) continue;

                    general.Add(filaGeneral);
                    proveedor.Add(filaProveedor);
                    productoSat.Add(filaProductoSat);

                    string cveProd = fila.Cell(2).GetString();  // columna 2 (ITEM SRS)
                    string cveProv = fila.Cell(11).GetString(); // columna 11 (PROVEEDOR)

                    var existeProd = datoProducto(cveProd); // tu función existente

                    var filaCompleta = new FilaProcesada
                    {
                        General = filaGeneral,
                        Proveedor = filaProveedor,
                        ProductoSAT = filaProductoSat
                    };

                    if (existeProd == null || existeProd.Count == 0)
                    {
                        productosNuevos.Add(filaCompleta);
                    }
                    else
                    {
                        var existeProv = datoPProveedor(cveProv); // tu función existente
                        if (existeProv == null || existeProv.Count == 0)
                        {
                            productosConProveedorInexistente.Add(filaCompleta);
                            proveedoresNuevos.Add(filaProveedor);
                        }
                        else
                        {
                            productosYProveedoresExistentes.Add(filaCompleta);
                        }
                    }
                }

                var model = new VistaExcelViewModel()
                {
                    EncabezadosGeneral = encabezadoGeneral,
                    EncabezadosProveedor = encabezadoProveedor,
                    EncabezadosProductoSat = encabezadoProductoSat,
                    General = general,
                    Proveedor = proveedor,
                    ProductoSAT = productoSat,
                    ProductosNuevos = productosNuevos,
                    ProductosConProveedorInexistente = productosConProveedorInexistente,
                    ProductosYProveedoresExistentes = productosYProveedoresExistentes,
                    ProveedoresNuevos = proveedoresNuevos
                };

                // Si la petición es AJAX, devolvemos JSON con redirect y guardamos en Session para mostrar la vista luego
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    var key = "VistaExcelModel_" + Guid.NewGuid().ToString();
                    HttpContext.Session.SetString(key, Newtonsoft.Json.JsonConvert.SerializeObject(model));
                    var redirectUrl = Url.Action("VistaExcelFromSession", "Planeador", new { key = key });
                    return Json(new { success = true, redirectUrl = redirectUrl });
                }

                // Si no es AJAX, retornamos la vista directamente
                return View("/Views/ComprasInternacionales/VistaExcel.cshtml", model);
            }
            return RedirectToAction("ComprasInternacionales/Index");
        }

        [HttpPost]
        public JsonResult GuardarProductosNuevos()
        {
            var _parameters = new List<Dictionary<string, object>>();
            var _parameters2 = new List<Dictionary<string, object>>();
            try
            {
                var productosJson = Request.Form["productos_completos"].ToString();
                if (string.IsNullOrEmpty(productosJson))
                {
                    return Json(new { success = false, message = "No se recibieron productos." });
                }

                var productosCompletos = Newtonsoft.Json.JsonConvert.DeserializeObject<List<ProductoConSatYProveedor>>(productosJson);

                foreach (var item in productosCompletos)
                {
                    var parametersProductos = new Dictionary<string, object>();
                    var parametersProductosSAT = new Dictionary<string, object>();
                    var fila = item.Producto;
                    var sat = item.SAT;
                    var proveedor = item.Proveedor;

                    // Validaciones de producto
                    var descripcion = fila[2];
                    var clave = fila[1];

                    string lineaDesc = fila[3].ToString().Trim();
                    string cveLinea = ObtenerClavePorDescripcion("catlineas", "cve_linea", lineaDesc);
                    if (cveLinea == null)
                        return Json(new { success = false, message = $"La línea '{lineaDesc}' no existe." });

                    string tipoDesc = fila[4].ToString().Trim();
                    string cveTipo = ObtenerClavePorDescripcion("cattipo_prd", "cve_tipo", tipoDesc);
                    if (cveTipo == null)
                        return Json(new { success = false, message = $"El tipo '{tipoDesc}' no existe." });

                    string grupoDesc = fila[5].ToString().Trim();
                    string cveGrupo = ObtenerClavePorDescripcion("catgrupo", "cve_grupo", grupoDesc);
                    if (cveGrupo == null)
                        return Json(new { success = false, message = $"El grupo '{grupoDesc}' no existe." });

                    string unidad1Desc = fila[6].ToString().Trim();
                    string cveUnidad1 = ObtenerClavePorDescripcion("catunidades", "cve_udm", unidad1Desc);
                    if (cveUnidad1 == null)
                        return Json(new { success = false, message = $"Unidad 1 '{unidad1Desc}' no existe." });

                    string unidad2Desc = fila[7].ToString().Trim();
                    string cveUnidad2 = ObtenerClavePorDescripcion("catunidades", "cve_udm", unidad2Desc);
                    if (cveUnidad2 == null)
                        return Json(new { success = false, message = $"Unidad 2 '{unidad2Desc}' no existe." });

                    var fConversion = fila[8].ToString().Trim();

                    var claveSat = sat.ElementAtOrDefault(0);
                    var descripcionSat = sat.ElementAtOrDefault(1);

                    parametersProductos.Add("cve_prod", clave);
                    parametersProductos.Add("descr_prod", descripcion);
                    parametersProductos.Add("lin_prod", cveLinea);
                    parametersProductos.Add("tp", cveTipo);
                    parametersProductos.Add("gpo", cveGrupo);
                    parametersProductos.Add("udm", cveUnidad1);
                    parametersProductos.Add("ud_alt", cveUnidad2);
                    parametersProductos.Add("conv_ud", Convert.ToDouble(fConversion));
                    parametersProductos.Add("stat", "A");
                    parametersProductos.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
                    // Proveedor 
                    parametersProductos.Add("cod_prov", proveedor.ElementAtOrDefault(0));
                    parametersProductos.Add("cve_prov_ppal", proveedor.ElementAtOrDefault(1));
                    parametersProductos.Add("cve_ccy_comp", proveedor.ElementAtOrDefault(2));
                    var valor = proveedor.ElementAtOrDefault(3);
                    parametersProductos.Add("imp", !string.IsNullOrEmpty(valor) ? valor.Substring(0, 1) : null);
                    // SAT
                    parametersProductosSAT.Add("prod_kepler", clave);
                    parametersProductosSAT.Add("prod_sat", sat.ElementAtOrDefault(0));
                    parametersProductosSAT.Add("ud_sat", sat.ElementAtOrDefault(2));
                    parametersProductosSAT.Add("iva_ex", sat.ElementAtOrDefault(6));
                    parametersProductosSAT.Add("ud_sec", sat.ElementAtOrDefault(4));
                    parametersProductosSAT.Add("obj_impto", sat.ElementAtOrDefault(7));

                    _parameters.Add(parametersProductos);
                    _parameters2.Add(parametersProductosSAT);

                }
                string query = "INSERT INTO catproductos " +
                    "(cve_prod, descr_prod, lin_prod, tp, gpo, cod_prov, cve_prov_ppal, cve_ccy_comp, imp, udm, ud_alt, conv_ud, stat, empresa_id) " +
                    "values " +
                    "(@cve_prod, @descr_prod, @lin_prod, @tp, @gpo, @cod_prov, @cve_prov_ppal, @cve_ccy_comp, @imp, @udm, @ud_alt, @conv_ud, @stat, @empresa_id);";

                RunUpdate(query, _parameters);

                query = "INSERT INTO catrelacion (prod_kepler, prod_sat, ud_sat, iva_ex, ud_sec, obj_impto) " +
                        "SELECT @prod_kepler, @prod_sat, @ud_sat, @iva_ex, @ud_sec, @obj_impto " +
                        "WHERE NOT EXISTS(" +
                        "    SELECT 1 FROM catrelacion " +
                        "    WHERE prod_kepler = @prod_kepler AND prod_sat = @prod_sat " +
                        "); ";
                RunUpdate(query, _parameters2);
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al procesar los productos: " + ex.Message });
            }
        }

        public string ObtenerClavePorDescripcion(string tabla, string campoClave, string descripcion)
        {
            // Validar tabla permitida para evitar SQL Injection
            var tablasPermitidas = new Dictionary<string, string>
            {
                { "catlineas", "cve_linea" },
                { "cattipo_prd", "cve_tipo" },
                { "catgrupo", "cve_grupo" },
                { "catunidades", "cve_udm" }
            };

            if (!tablasPermitidas.ContainsKey(tabla) || tablasPermitidas[tabla] != campoClave)
                throw new ArgumentException("Tabla o campo clave no permitido.");

            string query = $"SELECT {campoClave} FROM {tabla} WHERE LOWER(descripcion) = LOWER(@descripcion)";
            var parameters = new Dictionary<string, object>
            {
                { "descripcion", descripcion }
            };

            var result = RunQuery(query, parameters);
            return result.Count > 0 ? result[0][campoClave]?.ToString() : null;
        }


        // Clase auxiliar
        public class ProductoConSatYProveedor
        {
            public List<string> Producto { get; set; }
            public List<string> SAT { get; set; }
            public List<string> Proveedor { get; set; }
        }


        public List<Dictionary<string, object>> datoProducto(string cve_prod)
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("cve_prod", cve_prod);
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
            string query = "SELECT cve_prod FROM catproductos WHERE cve_prod = @cve_prod AND empresa_id = @empresa_id ";
            var result = RunQuery(query, parameters);
            return result;
        }

        public List<Dictionary<string, object>> datoPProveedor(string cve_prov)
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("cve_prov", cve_prov);
            parameters.Add("id_empresa", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));
            string query = "SELECT cve_prov FROM catproveedores WHERE cve_prov =@cve_prov AND id_empresa = @id_empresa ";
            var result = RunQuery(query, parameters);
            return result;
        }


        [HttpPost]
        public JsonResult ProcesarCsv(IFormFile archivoCsv, bool tieneEncabezados = false)
        {
            if (archivoCsv == null || archivoCsv.Length == 0)
            {
                return Json(new { success = false, message = "No se recibió ningún archivo." });
            }

            if (!archivoCsv.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                return Json(new { success = false, message = "El archivo debe ser .csv" });
            }

            try
            {
                var resultado = new List<Dictionary<string, string>>();
                string[] headers;

                using (var reader = new StreamReader(archivoCsv.OpenReadStream(), Encoding.UTF8))
                {
                    string primeraLinea = reader.ReadLine();
                    string[] posiblesHeaders = primeraLinea.Split(',');

                    // Heurística: si todas las columnas tienen texto sin números, probablemente son encabezados
                    bool esEncabezado = posiblesHeaders.All(col => !Regex.IsMatch(col, @"\d"));

                    if (esEncabezado)
                    {
                        headers = posiblesHeaders.Select(h => h.Trim('"')).ToArray();
                    }
                    else
                    {
                        headers = new[] { "ID", "Clave Producto", "Unidad Medida", "Cantidad", "Precio Unitario" };
                        // Retrocede la lectura para procesar esta primera línea como datos
                        reader.BaseStream.Seek(0, SeekOrigin.Begin);
                        reader.DiscardBufferedData();
                    }

                    while (!reader.EndOfStream)
                    {
                        var line = reader.ReadLine();
                        var valores = line.Split(',');

                        var fila = new Dictionary<string, string>();
                        for (int i = 0; i < headers.Length && i < valores.Length; i++)
                        {
                            var key = headers[i];
                            if (string.IsNullOrWhiteSpace(key)) continue;

                            fila[key] = valores[i].Trim().Trim('"');
                        }

                        resultado.Add(fila);
                    }
                }

                return Json(new { success = true, data = resultado });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al procesar el archivo: " + ex.Message });
            }
        }

        public IActionResult VistaExcelFromSession(string key)
        {
            if (string.IsNullOrEmpty(key)) return RedirectToAction("ComprasInternacionales/Index");

            var model = Newtonsoft.Json.JsonConvert.DeserializeObject<VistaExcelViewModel>(HttpContext.Session.GetString(key));
            if (model == null) return RedirectToAction("ComprasInternacionales/Index");

            HttpContext.Session.Remove(key);

            return View("/Views/ComprasInternacionales/VistaExcel.cshtml", model);
        }

        private string NormalizarTexto(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return string.Empty;

            texto = texto.Trim().ToUpperInvariant();

            var normalized = texto.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (var c in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }
    }
}
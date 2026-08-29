using BOS_ERP.Models;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace BOS_ERP.Controllers.Almacen
{
    /// <summary>
    /// Alta masiva de productos a partir de un archivo CSV.
    ///
    /// El proceso es todo o nada: se leen las filas, se insertan los
    /// productos y se genera un documento de tipo CARLAY con una partida
    /// por producto. Si algo falla, no queda nada guardado.
    /// </summary>
    public partial class ProductosController : Utilities
    {
        /// <summary>Número de columnas que debe traer cada fila del CSV.</summary>
        private const int ColumnasLayout = 24;

        [RightAuthorize("carga_layout")]
        [ValidateAntiForgeryToken, HttpPost]
        public JsonResult CargaLayOut(IFormFile file)
        {
            try
            {
                if (file == null || file.Length == 0)
                    return Json(new { icon = "error", title = "Ocurrió un error", html = "El archivo no contiene información válida o no fue cargado correctamente.", crearProducto = false });

                var utils = new Utilities(true);
                string connStr = utils._configuration.GetConnectionString("ERP_SRS");
                var documento = new Dictionary<string, object>();

                using (var conn = new NpgsqlConnection(connStr))
                {
                    conn.Open();

                    using (var tx = conn.BeginTransaction())
                    {
                        try
                        {
                            var lectura = LeerLayout(file, conn, tx);

                            // Formato inválido o unidad desconocida: se corta aquí
                            if (lectura.Error != null) return lectura.Error;

                            // Se avisa de los duplicados en vez de crearlos otra vez
                            if (lectura.Existentes.Any())
                                return Json(new
                                {
                                    icon = "error",
                                    title = "Ya existen estos productos",
                                    html = $"<ul>{string.Join("", lectura.Existentes)}</ul>"
                                });

                            documento = CrearDocumentoDeCarga(lectura.Partidas, conn, tx);

                            tx.Commit();
                        }
                        catch
                        {
                            tx.Rollback();
                            throw;
                        }
                    }
                }

                return Json(new
                {
                    icon = "success",
                    title = "Productos creados exitosamente.",
                    html = $"Se creo el documento de creacion de producto por layout con el folio: {documento["folio_generado"]}"
                });
            }
            catch (Exception ex)
            {
                return Json(new { icon = "error", title = "Ocurrio un error inesperado.", html = ex.Message });
            }
        }

        #region Lectura del CSV

        /// <summary>Resultado de recorrer el archivo.</summary>
        private class LecturaLayout
        {
            /// <summary>Si viene con valor, hay que abortar y devolverlo tal cual.</summary>
            public JsonResult Error { get; set; }

            /// <summary>Una partida por producto insertado.</summary>
            public List<PartidaDocumento> Partidas { get; } = new();

            /// <summary>Productos que ya existían, como &lt;li&gt; listos para mostrar.</summary>
            public List<string> Existentes { get; } = new();
        }

        /// <summary>
        /// Recorre el CSV insertando los productos nuevos. La primera
        /// línea es el encabezado y se descarta.
        /// </summary>
        private LecturaLayout LeerLayout(IFormFile file, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            var resultado = new LecturaLayout();

            using (var reader = new StreamReader(file.OpenReadStream()))
            {
                reader.ReadLine();

                string line;
                int numeroLinea = 1;
                int numeroPartida = 1;

                while ((line = reader.ReadLine()) != null)
                {
                    numeroLinea++;

                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    var columnas = line.Split(',');

                    if (columnas.Length < ColumnasLayout)
                    {
                        resultado.Error = Json(new
                        {
                            icon = "error",
                            title = "Formato de archivo inválido",
                            html = $"La línea {numeroLinea} no cumple con la estructura esperada del archivo CSV.",
                            crearProducto = false
                        });
                        return resultado;
                    }

                    string clave = GetString(columnas[0]);
                    string descripcion = GetString(columnas[1]);

                    if (ProductoYaRegistrado(clave, conn, tx))
                    {
                        resultado.Existentes.Add($"<li>{clave} - {descripcion}</li>");
                        continue;
                    }

                    string unidad = GetString(columnas[7]);
                    if (!UnidadRegistrada(unidad, conn, tx))
                    {
                        Response.StatusCode = 400;
                        resultado.Error = Json(new
                        {
                            icon = "error",
                            title = "Unidad de medida no válida",
                            html = $"La unidad '{unidad}' en la línea {numeroLinea} no se encuentra registrada en el sistema.",
                            crearProducto = false
                        });
                        return resultado;
                    }

                    decimal? iva = GetDecimal(columnas[10]);
                    int idProducto = InsertarProductoDeLayout(columnas, conn, tx);

                    resultado.Partidas.Add(new PartidaDocumento
                    {
                        NroPart = numeroPartida++,
                        CveProd = clave,
                        DescrProd = descripcion,
                        CantUd = 1,
                        CveVdrCpr = "srs",
                        Ref = null,
                        Ud = unidad,
                        FolDocAnt = "carga_layout",
                        FPagoId = null,
                        IdProducto = idProducto,
                        Iva = iva
                    });
                }
            }

            return resultado;
        }

        private bool ProductoYaRegistrado(string clave, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("clave", clave);
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));

            string query = "SELECT COUNT(*) qty FROM catproductos WHERE cve_prod = @clave AND empresa_id = @empresa_id";
            return Convert.ToInt32(RunScalar(query, parameters, false, conn, tx)) > 0;
        }

        private bool UnidadRegistrada(string unidad, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("unidad", unidad);

            string query = "SELECT id_udm FROM catunidades WHERE cve_udm = @unidad";
            return RunScalar(query, parameters, false, conn, tx) != null;
        }

        /// <summary>
        /// Inserta el producto de una fila del CSV y devuelve su id.
        /// El orden de las columnas es el de la plantilla de carga.
        /// </summary>
        private int InsertarProductoDeLayout(string[] columnas, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            var parameters = new Dictionary<string, object>();
            parameters["clave"] = GetString(columnas[0]);
            parameters["descripcion"] = GetString(columnas[1]);
            parameters["linea"] = GetString(columnas[2]);
            parameters["tipo"] = GetString(columnas[3]);
            parameters["grupo"] = GetString(columnas[4]);
            parameters["claveProv"] = GetString(columnas[5]);
            parameters["claveSecc"] = GetString(columnas[6]);
            parameters["unidad"] = GetString(columnas[7]);
            parameters["unidadAlt"] = GetString(columnas[8]);
            parameters["factorConversion"] = GetDecimal(columnas[9]);
            parameters["iva"] = GetDecimal(columnas[10]);
            parameters["impuestoEpecial"] = GetDecimal(columnas[11]);
            parameters["moneda"] = GetString(columnas[12]);
            parameters["importacion"] = GetString(columnas[13]);
            parameters["paisOrigen"] = GetString(columnas[14]);
            parameters["monedaCompra"] = GetString(columnas[15]);
            parameters["naturaleza"] = GetString(columnas[16]);
            parameters["estatus"] = GetString(columnas[17]);
            parameters["tipoCosto"] = GetString(columnas[18]);
            parameters["claveProducto"] = GetString(columnas[19]);
            parameters["unidadMedida"] = GetString(columnas[20]);
            parameters["unidadSecundaria"] = GetString(columnas[21]);
            parameters["ivaExento"] = GetString(columnas[22]);
            parameters["objetoImpuesto"] = GetString(columnas[23]);
            parameters["empresa"] = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));

            string query = "INSERT INTO catproductos " +
                "   (cve_prod, descr_prod, lin_prod, tp, gpo, cod_prov, cve_prov_ppal, udm, ud_alt, conv_ud, iva_prod, impto_by_s, cve_ccy_pv, sn, " +
                "       cve_pais_orig, cve_ccy_comp, fmcan, stat, tp_cto, empresa_id) " +
                "VALUES " +
                "   (@clave, @descripcion, @linea, @tipo, @grupo, @claveProv, @claveSecc, @unidad, @unidadAlt, @factorConversion, @iva, @impuestoEpecial, " +
                "       @moneda, @importacion, @paisOrigen, @monedaCompra, @naturaleza, @estatus, @tipoCosto, @empresa) RETURNING id_catproductos";

            return Convert.ToInt32(RunScalar(query, parameters, false, conn, tx));
        }

        #endregion

        /// <summary>
        /// Genera el documento CARLAY que deja constancia de la carga.
        /// </summary>
        private Dictionary<string, object> CrearDocumentoDeCarga(
            List<PartidaDocumento> partidas, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            var encabezado = new DocumentoEncabezado();
            encabezado.EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa"));
            encabezado.IdArea = 4;
            encabezado.IdTpDoc = 83;
            encabezado.Anio = DateTime.Now.Year;
            encabezado.Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal"));
            encabezado.Fch = DateTime.Now;
            encabezado.TpMov = "CARLAY";
            encabezado.UsrDoc = User.Identity.Name;
            encabezado.FchCap = DateTime.Now;
            encabezado.Fch0 = DateTime.Now;
            encabezado.EncabezadoPadre = 0;
            encabezado.Estatus = 1;
            encabezado.UsrDep = GetAreaName(User.Identity.Name);
            encabezado.TipoPoceso = "carga_layout";
            encabezado.Usr0 = GetUserId(User.Identity.Name);
            encabezado.CliProv = "srs";

            return GenerarDocumentoConPartidas(encabezado, partidas, conn, tx);
        }
    }
}

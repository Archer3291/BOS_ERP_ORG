using BOS_ERP.Controllers;
using Microsoft.AspNetCore.Mvc;

public class OperacionesController : Utilities
{
    public IActionResult Index()
    {
        return View();
    }

    public IActionResult AltaCotizacion(string tipo)
    {
        ViewBag.tipo = tipo;
        var parameters = new Dictionary<string, object>();

        // 1. Obtener el área correspondiente
        string queryAreas = "SELECT areaid, nombre, descripcion, abreviatura " +
                            "FROM Areas " +
                            "WHERE LOWER(Nombre) LIKE LOWER('Ventas ' || @tipo || '%')";
        parameters.Add("tipo", tipo);


        var result = RunQuery(queryAreas, parameters);
        if (result.Count == 0)
        {
            return NotFound("Área no encontrada.");
        }

        var areaId = result[0]["areaid"];
        ViewBag.area = result[0]["abreviatura"];
        ViewBag.idarea = areaId;

        // 2. Obtener tipo de documento
        parameters.Clear();
        parameters.Add("idarea", areaId);

        string queryDocumento = "SELECT idtpdoc, idarea, tpdoc, abreviaturatpdoc, consec, descr, fch " +
                                "FROM tpdoc " +
                                "WHERE idarea = @idarea";

        var resultArea = RunQuery(queryDocumento, parameters);
        if (resultArea.Count == 0)
        {
            return NotFound("Tipo de documento no encontrado.");
        }

        var idtpdoc = resultArea[0]["idtpdoc"];
        ViewBag.tpdoc = idtpdoc;
        ViewBag.tpdocAb = resultArea[0]["abreviaturatpdoc"];

        // 3. Obtener folio actual
        parameters.Clear();
        parameters.Add("idarea", areaId);
        parameters.Add("idtpdoc", idtpdoc);

        string queryFolio = "SELECT idfoldoc, areaid, tpdocid, anio, ultconsec " +
                            "FROM foldoc " +
                            "WHERE areaid = @idarea AND tpdocid = @idtpdoc";

        var folio = RunQuery(queryFolio, parameters);
        int consecutivo = 1;

        if (folio.Count > 0 && folio[0]["ultconsec"] != DBNull.Value)
        {
            consecutivo = Convert.ToInt32(folio[0]["ultconsec"]) + 1;
        }

        ViewBag.folio = consecutivo.ToString("D7");
        ViewBag.anio = DateTime.Now.Year;

        Console.WriteLine($"Tipo de cotización: {tipo}");
        return View();
    }


    public IActionResult SolicitudCotizacion()
    {
        return View();
    }

    public IActionResult PolizasDiscrepancia()
    {
        return View();
    }

    public IActionResult ConsultaPolizasDiscrepancia()
    {
        return View();
    }

    public IActionResult AltaPedidos()
    {
        return View();
    }

    public IActionResult AltaPedidosCE()
    {
        return View();
    }
    
    public IActionResult Categories()
    {
        return View();
    }
    public IActionResult Proveedor()
    {
        return View();
    }

    public IActionResult DatosSelect()
    {
        var result = new Dictionary<string, List<Dictionary<string, object>>>();
        string queryMoneda = "SELECT c1,c2,c3 FROM sellosop.kdmy ORDER BY c1 DESC";
        string queryVendedor = "SELECT c1,c2,c3 FROM sellosop.kduv ORDER BY c1 DESC";
        string queryFormasPago = "SELECT c1,c2 FROM sellosop.kdfe33satfopg ORDER BY c1 DESC";
        string queryMetodoPago = "SELECT c1,c2 FROM sellosop.kdfe33satmto ORDER BY c1 DESC";
        string queryUsoCFDI = "SELECT c1,c2 FROM sellosop.kdfe33satuso ORDER BY c1 DESC";

        result.Add("monedas", RunQuery(queryMoneda));
        result.Add("vendedores", RunQuery(queryVendedor));
        result.Add("formaspago", RunQuery(queryFormasPago));
        result.Add("metodopago", RunQuery(queryMetodoPago));
        result.Add("usocfdi", RunQuery(queryUsoCFDI));

        return Json(result);
    }

    public string GetColumnaPrecioPorCliente(string idCliente)
    {
        switch (idCliente)
        {
            case "01":
                return "c14";
            case "02":
                return "c15";
            case "03":
                return "c16";
            case "04":
                return "c17";
            case "05":
                return "c81";
            case "06":
                return "c82";
            case "07":
                return "c83";
            case "08":
                return "c84";
            case "09":
                return "c85";
        default:
                return "c14"; // valor por defecto
        }
    }

    public IActionResult BuscarProducto(string id, string idCliente)
    {
        try
        {
             idCliente = HttpContext.Session.GetString("idCliente");
            string columnaPrecio = GetColumnaPrecioPorCliente(idCliente);
            var parameters = new Dictionary<string, object>();
            string queryProducto = $"SELECT kdii.c1,kdii.c2,kdii.c6,c11,c23,c18,rel.c3,rel.c2  AS clavesat, rel.c3 AS udmsat, {columnaPrecio} AS precio FROM sellosop.kdii kdii " +
                "INNER JOIN sellosop.kdfe33relprd rel " +
                "ON rel.c1 = kdii.c1 " +
                $"WHERE kdii.c1 = @id";
            parameters.Add("id", id);
            var result = RunQuery(queryProducto, parameters);
            return Json(result);
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    public IActionResult BuscarProveedor(string id)
    {
        var parameters = new Dictionary<string, object>();
        string queryProducto = "SELECT c1,c2,c3,c10, c4, c5 ,c6, c27  FROM sellosop.kdxd WHERE c2 = @id";
        parameters.Add("id", id);
        var result = RunQuery(queryProducto, parameters);
        return Json(result);
    }
    public IActionResult BuscarCliente(string id)
    {
        var parameters = new Dictionary<string, object>();
        string queryProducto = "SELECT c1,c2,c3,c10, c4, c5 ,c6, c12, c27, c60  FROM sellosop.kdud WHERE c2 = @id";
        parameters.Add("id", id);

        var result = RunQuery(queryProducto, parameters);

        // Asignar c60 a la variable global
        if (result != null && result.Count > 0)
        {
            var row = result[0];
            if (row.ContainsKey("c60"))
            {
                HttpContext.Session.SetString("idCliente", row["c60"]?.ToString());
            }
        }

        // Retorna el resultado original
        return Json(result);
    }

    public IActionResult Buscar(string nombre, int page = 1, int pageSize = 50)
    {
        var parameters = new Dictionary<string, object>();
        string query = " SELECT c1 AS id, c2 As descripcion FROM sellosop.kdii " +
        "WHERE LOWER(c2) LIKE LOWER(@nombre) " +
        "OR LOWER(c1) LIKE LOWER(@nombre) " +
        "ORDER BY c1 " +
        "OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

        parameters.Add("nombre", $"%{nombre}%");
        parameters.Add("offset", (page - 1) * pageSize); // Calcula el offset
        parameters.Add("pageSize", pageSize); // Número de resultados por página

        var result = RunQuery(query, parameters);
        return Json(result);
    }
    public IActionResult BuscarP(string nombre, int page = 1, int pageSize = 50)
    {
            var parameters = new Dictionary<string, object>();
            string query = "SELECT c2 AS id, c3 As descripcion FROM sellosop.kdxd " +
                            "WHERE LOWER(c2) LIKE LOWER(@nombre) " +
                            "OR LOWER(c3) LIKE LOWER(@nombre) " +
                            "ORDER BY c1 " +
                            "OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

            parameters.Add("nombre", $"%{nombre}%");
            parameters.Add("offset", (page - 1) * pageSize); // Calcula el offset
            parameters.Add("pageSize", pageSize); // Número de resultados por página

            var result = RunQuery(query, parameters);
            return Json(result);
        
    }

    public IActionResult BuscarC(string nombre, int page = 1, int pageSize = 50)
    { 

            var parameters = new Dictionary<string, object>();
            string query = "SELECT c2 AS id, c3 As descripcion FROM sellosop.kdud " +
                            "WHERE LOWER(c2) LIKE LOWER(@nombre) " +
                            "OR LOWER(c3) LIKE LOWER(@nombre) " +
                            "ORDER BY c2 " +
                            "OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

            parameters.Add("nombre", $"%{nombre}%");
            parameters.Add("offset", (page - 1) * pageSize); // Calcula el offset
            parameters.Add("pageSize", pageSize); // Número de resultados por página

            var result = RunQuery(query, parameters);
            return Json(result);

    }

    public JsonResult ValidarEdicionPrecio()
    {
        string usuario = User.Identity.Name;

        bool permitido = VerificarPermisos(usuario);
        return Json(new { permitido });
    }

    private bool VerificarPermisos(string usuario)
    {
        string rol = GetUserRole(usuario, "ERP_SRS");
        return rol == "Admin" || rol == "Admin";
    }
    [HttpPost, ValidateAntiForgeryToken]
    public JsonResult ValidarAccesoFijo(string usuario, string contrasena)
    {
        // Usuario y contraseña fijos (idealmente usar config o variables de entorno)
        const string usuarioPermitido = "adminedicion";
        const string clavePermitida = "clave123";

        bool permitido = usuario == usuarioPermitido && contrasena == clavePermitida;

        return Json(new { permitido });
    }

    public IActionResult ConsultarPackingList()
    {
        return View();
    }

    public IActionResult DocumentosPendientes()
    {
        return View();
    }

    public IActionResult Servicios()
    {
        return View();
    }
    public IActionResult SolicitudGasto()
    {
        return View();
    }
}

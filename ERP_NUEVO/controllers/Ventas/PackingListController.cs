using Microsoft.AspNetCore.Authorization;
using Npgsql;
using BOS_ERP.Models;
using System.Configuration;
using System.Text;
using Microsoft.AspNetCore.Mvc;


namespace BOS_ERP.Controllers.Ventas
{
    [Authorize]
    public class PackingListController : Utilities
    {

        public IActionResult DatosSelect()
        {
            var result = new Dictionary<string, List<Dictionary<string, object>>>();
            string queryFacturas = "SELECT  " +
                "    em.id_encabezado as id, " +
                "    em.folio || " +
                "        CASE WHEN em.variacion > 0  " +
                "             THEN '-' || num_to_letters(em.variacion)  " +
                "             ELSE '' END as nombre " +
                "FROM encabezadomov em " +
                "WHERE em.nat = 'VINFAC' " +
                "ORDER BY em.fch DESC";


            result.Add("facturas", RunQuery(queryFacturas));


            return Json(result);
        }

        public IActionResult BuscarDocumento(int id)
        {
            try
            {
                var parameters = new Dictionary<string, object>
                {
                    { "id", id },
                    { "empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")) }
                };

                // 🔹 Consulta del encabezado
                string queryEncabezado = "" +
                                     "SELECT  " +
                                     "     f.id AS idfactura, " +
                                     "     f.serie, " +
                                     "     f.folio, " +
                                     "     CONCAT(f.serie, '-', f.folio) AS factura_ref, " +
                                     "     f.fecha, " +
                                     "     f.moneda, " +
                                     "     f.total, " +
                                     "     f.uuid, " +
                                     "     f.observaciones, " +
                                     "     f.idpedido, " +
                                     "     f.idvendedor, " +
                                     "     f.rsocliente AS nombre_cliente, " +
                                     "     f.rfccliente, " +
                                     "     f.emlcliente AS email_cliente, " +
                                     "     f.rsoemisor AS nombre_emisor, " +
                                     "     f.rfcemisor, " +
                                     "     f.idexpedicion, " +
                                     "     d.idproducto, " +
                                     "     COALESCE(d.descripcion, p.descr_prod) AS descripcion, " +
                                     "     p.descr_prod, " +
                                     "     p.cve_prod, " +
                                     "     d.cantidad, " +
                                     "     fc.frac AS fraccion_arancelaria, " +
                                     "     d.udm, " +
                                     "     d.idfactura AS id_detalle, " +
                                     "     p.peso_prod AS peso_unitario, " +
                                     "     ROUND(d.cantidad * COALESCE(p.peso_prod, 0), 2) AS peso_neto,   " +
                                     "     p.fr_ar, " +
                                     "     p.cve_pais_orig AS pais_origen, " +
                                     "     p.lin_prod AS linea_producto, " +
                                     "     p.gpo AS grupo_producto, " +
                                     "     p.lgo AS largo, " +
                                     "     p.dmt_int AS diametro_int, " +
                                     "     p.dmt_ext AS diametro_ext,     " +
                                     "     df.calle as dir, " +
                                     "     df.no_exterior, " +
                                     "     df.no_interior, " +
                                     "     df.colonia as col, " +
                                     "     df.estado as pob, " +
                                     "     df.codigo_postal as cp,  " +
                                     "     df.pais,  " +
                                     "     fc.peso " +
                                     " FROM factura f " +
                                     " INNER JOIN dfactura d ON f.id = d.idfac " +
                                     " LEFT JOIN catproductos p ON d.idproducto = p.id_catproductos AND p.empresa_id = @empresa_id " +
                                     " INNER JOIN catclientes cc ON cc.id_cliente  = f.idcliente AND cc.empresa_id = @empresa_id" +
                                     " INNER JOIN encabezadomov e  on e.id_encabezado  = f.encabezado_id   " +
                                     " left join frac_arancelarias fc on fc.cve_prod = p.cve_prod " +
                                     " left join direcciones_facturacion df on df.entidad_clave = cc.cve_cli AND df.empresa_id = @empresa_id  " +
                                     " WHERE e.id_encabezado = @id;";

                var encabezadoResult = RunQuery(queryEncabezado, parameters);
                if (encabezadoResult == null || encabezadoResult.Count == 0)
                    return Json(new { success = false, message = "Documento no encontrado." });

                var encabezado = encabezadoResult;

                var result = encabezado;

                return Json(result);
            }
            catch (Exception ex)
            {
                RegistrarErrorParaTicket(ex, "PackingList/BuscarDocumento");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]

        public IActionResult GuardarPackingList(IFormCollection form)
        {
            var utils = new Utilities(true);
            string connStr = utils._configuration.GetConnectionString("ERP_SRS");
            using (var conn = new NpgsqlConnection(connStr))
            {
                conn.Open();
                using (var transaction = conn.BeginTransaction())
                {
                    try
                    {
                        var partidas = new List<PartidaDocumento>();
                        string json = form["data"];
                        dynamic packing = Newtonsoft.Json.JsonConvert.DeserializeObject(json);

                        // 1️⃣ Obtener encabezado (factura referencia)
                        string encabezado = "SELECT id FROM factura WHERE encabezado_id = @encabezado_id";
                        int idEncabezado;

                        using (var cmdEnc = new NpgsqlCommand(encabezado, conn, transaction))
                        {
                            cmdEnc.Parameters.AddWithValue("@encabezado_id", (int)packing.facturaReferencia);
                            idEncabezado = Convert.ToInt32(cmdEnc.ExecuteScalar());
                        }

                        // 2️⃣ Insertar registro principal en packinglist
                        string queryPacking = @"
                    INSERT INTO packinglist 
                    (factura_referencia, fecha_embarque, encabezado_id, medio_transporte, 
                     puerto_salida, puerto_destino, incoterm, numero_guia, orden_compra_comprador, observaciones)
                    VALUES (@factura_referencia, @fecha_embarque, @encabezado_id, @medio_transporte, 
                            @puerto_salida, @puerto_destino, @incoterm, @numero_guia, @orden_compra_comprador, @observaciones)
                    RETURNING id;
                ";

                        int idPackingList;
                        using (var cmdPacking = new NpgsqlCommand(queryPacking, conn, transaction))
                        {
                            cmdPacking.Parameters.AddWithValue("@factura_referencia", idEncabezado);
                            cmdPacking.Parameters.AddWithValue("@fecha_embarque", (DateTime)packing.fechaEmbarque);
                            cmdPacking.Parameters.AddWithValue("@encabezado_id", (int)packing.facturaReferencia);
                            cmdPacking.Parameters.AddWithValue("@medio_transporte", (string)packing.medioTransporte);
                            cmdPacking.Parameters.AddWithValue("@puerto_salida", (string)packing.puertoSalida);
                            cmdPacking.Parameters.AddWithValue("@puerto_destino", (string)packing.puertoDestino);
                            cmdPacking.Parameters.AddWithValue("@incoterm", (string)packing.incoterm);
                            cmdPacking.Parameters.AddWithValue("@numero_guia", (string)packing.numeroContenedor);
                            cmdPacking.Parameters.AddWithValue("@orden_compra_comprador", (string)packing.ODCComprador);
                            cmdPacking.Parameters.AddWithValue("@observaciones", (string)(packing.observaciones ?? ""));
                            idPackingList = Convert.ToInt32(cmdPacking.ExecuteScalar());
                        }

                        // 3️⃣ Insertar consignatario
                        dynamic cons = packing.consignatario;
                        string queryCons = @"
                    INSERT INTO packinglist_consignatario
                    (id_packinglist, nombre, calle, nexterior, ninterior, colonia, cp, estado, pais)
                    VALUES (@id_packinglist, @nombre, @calle, @nexterior, @ninterior, @colonia, @cp, @estado, @pais);
                ";

                        using (var cmdCons = new NpgsqlCommand(queryCons, conn, transaction))
                        {
                            cmdCons.Parameters.AddWithValue("@id_packinglist", idPackingList);
                            cmdCons.Parameters.AddWithValue("@nombre", (string)cons.nombre);
                            cmdCons.Parameters.AddWithValue("@calle", (string)cons.calle);
                            cmdCons.Parameters.AddWithValue("@nexterior", (string)cons.nexterior);
                            cmdCons.Parameters.AddWithValue("@ninterior", (string)cons.ninterior);
                            cmdCons.Parameters.AddWithValue("@colonia", (string)cons.colonia);
                            cmdCons.Parameters.AddWithValue("@cp", (string)cons.cp);
                            cmdCons.Parameters.AddWithValue("@estado", (string)cons.estado);
                            cmdCons.Parameters.AddWithValue("@pais", (string)cons.pais);
                            cmdCons.ExecuteNonQuery();
                        }

                        // 4️⃣ Procesar TARIMAS, BULTOS e ITEMS
                        foreach (var tarima in packing.tarimas)
                        {
                            // Identificador de tarima (descripción)
                            string identificadorTarima = tarima.numero;

                            // Procesar cada bulto de la tarima
                            foreach (var bulto in tarima.bultos)
                            {
                                string queryBulto = @"
                            INSERT INTO packinglist_bultos
                            (id_packinglist, numero, tipo_embalaje, marca, largo, ancho, alto, 
                             volumen, peso_neto, peso_bruto, tarima)
                            VALUES (@id_packinglist, @numero, @tipo_embalaje, @marca, 
                                    @largo, @ancho, @alto, @volumen, @peso_neto, @peso_bruto, @tarima)
                            RETURNING id;
                        ";

                                int idBulto;
                                using (var cmdBulto = new NpgsqlCommand(queryBulto, conn, transaction))
                                {
                                    cmdBulto.Parameters.AddWithValue("@id_packinglist", idPackingList);
                                    cmdBulto.Parameters.AddWithValue("@numero", (int)bulto.numero);
                                    cmdBulto.Parameters.AddWithValue("@tipo_embalaje", (string)bulto.tipoEmbalaje);
                                    cmdBulto.Parameters.AddWithValue("@marca", (string)bulto.marca);
                                    cmdBulto.Parameters.AddWithValue("@largo", (decimal)bulto.dimensiones.largo);
                                    cmdBulto.Parameters.AddWithValue("@ancho", (decimal)bulto.dimensiones.ancho);
                                    cmdBulto.Parameters.AddWithValue("@alto", (decimal)bulto.dimensiones.alto);
                                    cmdBulto.Parameters.AddWithValue("@volumen", (decimal)bulto.dimensiones.volumen);
                                    cmdBulto.Parameters.AddWithValue("@peso_neto", (decimal)bulto.pesoNeto);
                                    cmdBulto.Parameters.AddWithValue("@peso_bruto", (decimal)bulto.pesoBruto);
                                    cmdBulto.Parameters.AddWithValue("@tarima", identificadorTarima); // 🎯 NUEVO

                                    idBulto = Convert.ToInt32(cmdBulto.ExecuteScalar());
                                }

                                // Insertar items del bulto
                                foreach (var item in bulto.items)
                                {
                                    string queryItem = @"
                                INSERT INTO packinglist_items
                                (id_bulto, id_concepto_factura, sku, descripcion, cantidad, unidad, fraccion)
                                VALUES (@id_bulto, @id_concepto_factura, @sku, @descripcion, @cantidad, @unidad, @fraccion);
                            ";

                                    using (var cmdItem = new NpgsqlCommand(queryItem, conn, transaction))
                                    {
                                        cmdItem.Parameters.AddWithValue("@id_bulto", idBulto);
                                        cmdItem.Parameters.AddWithValue("@id_concepto_factura", (int)item.id);
                                        cmdItem.Parameters.AddWithValue("@sku", (string)item.sku);
                                        cmdItem.Parameters.AddWithValue("@descripcion", (string)item.descripcion);
                                        cmdItem.Parameters.AddWithValue("@cantidad", (decimal)item.cantidad);
                                        cmdItem.Parameters.AddWithValue("@unidad", (string)item.unidad);
                                        cmdItem.Parameters.AddWithValue("@fraccion", item.fraccion.ToString());
                                        cmdItem.ExecuteNonQuery();
                                    }

                                    // Agregar a lista de partidas para el documento
                                    partidas.Add(new PartidaDocumento
                                    {
                                        CveProd = (string)item.sku,
                                        DescrProd = (string)item.descripcion,
                                        CantUd = (decimal)item.cantidad,
                                        Ud = (string)item.unidad
                                    });
                                }
                            }
                        }

                        // 5️⃣ Generar documento del sistema
                        var encabezadoD = new DocumentoEncabezado
                        {
                            EmpresaId = Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")),
                            IdArea = 2,
                            IdTpDoc = 59,
                            UsrDep = GetAreaName(User.Identity.Name),
                            Anio = DateTime.Now.Year,
                            Suc = Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")),
                            Fch = DateTime.Now,
                            CliProv = (string)cons.nombre,
                            TpMov = "PL",
                            ComentAut = (packing.observaciones ?? ""),
                            UsrDoc = User.Identity.Name,
                            FchCap = DateTime.Now,
                            Usr0 = GetUserId(User.Identity.Name),
                            Fch0 = DateTime.Now,
                            Estatus = 1,
                            EncabezadoPadre = (int)packing.facturaReferencia
                        };

                        var folio = GenerarDocumentoConPartidas(encabezadoD, partidas);

                        if (!string.IsNullOrEmpty(folio["folio_generado"].ToString()))
                        {
                            transaction.Commit();
                        }

                        return Json(new
                        {
                            success = true,
                            folio = folio["folio_generado"].ToString(),
                            factura = GetDocumentFolio((int)packing.facturaReferencia),
                            message = "Packing list con tarimas guardado correctamente."
                        });
                    }
                    catch (Exception ex)
                    {
                        RegistrarErrorParaTicket(ex, "PackingList/?");
                        transaction.Rollback();
                        return Json(new
                        {
                            success = false,
                            message = ex.Message
                        });
                    }
                }
            }
        }

        public IActionResult DocumentosPacking(string nombre, int page = 1, int pageSize = 50)
        {
            var parameters = new Dictionary<string, object>
            {
                { "nombre", nombre ?? "" },
                { "offset", (page - 1) * pageSize },
                { "pageSize", pageSize },
            };
            string where = " WHERE (LOWER(c.pais) LIKE LOWER('%' || @nombre || '%') " +
                "OR LOWER(c.nombre) LIKE LOWER('%' || @nombre || '%') " +
                "OR LOWER(pl.numero_guia) LIKE LOWER('%' || @nombre || '%') " +
                "OR LOWER(em.folio || " +
                "  CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END) LIKE LOWER('%' || @nombre || '%')) ";
            string having = " HAVING 1=1 ";

            string query = "SELECT  " +
                "    pl.id AS packinglist_id, " +
                "    pl.factura_referencia, " +
                "    pl.fecha_embarque, " +
                "    pl.medio_transporte, " +
                "    pl.puerto_salida, " +
                "    pl.puerto_destino, " +
                "    pl.incoterm, " +
                "    pl.numero_guia, " +
                "    pl.orden_compra_comprador, " +
                "    pl.observaciones, " +
                "    pl.creado_en, " +
                "    em.id_encabezado, " +
                "    em.folio || " +
                "        CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END AS folio, " +
                "    em.fch, " +
                "    em.cli_prov, " +
                "    em.refe, " +
                "    em.coment1, " +
                "    em.coment2, " +
                "    em.coment3, " +
                "    em.alm, " +
                "    em.nro_tp_doc, " +
                "    em.nro_gpo_doc, " +
                "    em.ccy, " +
                "    em.pl_dias, " +
                "    em.tp_mov, " +
                "    em.f_pago, " +
                "    c.id AS consignatario_id, " +
                "    c.nombre AS consignatario_nombre, " +
                "    c.calle AS consignatario_calle, " +
                "    c.nexterior AS consignatario_nexterior, " +
                "    c.ninterior AS consignatario_ninterior, " +
                "    c.colonia AS consignatario_colonia, " +
                "    c.cp AS consignatario_cp, " +
                "    c.estado AS consignatario_estado, " +
                "    c.pais AS consignatario_pais " +
                "FROM packinglist pl " +
                "INNER JOIN encabezadomov em ON em.id_encabezado = pl.encabezado_id " +
                "LEFT JOIN packinglist_consignatario c ON c.id_packinglist = pl.id " +
                $" {where} " +
                "GROUP BY  " +
                "    pl.id, pl.factura_referencia, pl.fecha_embarque, pl.medio_transporte,  " +
                "    pl.puerto_salida, pl.puerto_destino, pl.incoterm, pl.numero_guia,  " +
                "    pl.orden_compra_comprador, pl.observaciones, pl.creado_en, " +
                "    em.id_encabezado, em.gen, em.nat, em.fch, em.fol_doc, em.variacion, " +
                "    em.cli_prov, em.refe, em.coment1, em.coment2, em.coment3, " +
                "    em.alm, em.nro_tp_doc, em.nro_gpo_doc, em.ccy, em.pl_dias,  " +
                "    em.tp_mov, em.f_pago, " +
                "    c.id, c.nombre, c.calle, c.nexterior, c.ninterior,  " +
                "    c.colonia, c.cp, c.estado, c.pais " +
                $" {having} " +
                "ORDER BY pl.id DESC " +
                "OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;";



            var data = RunQuery(query, parameters);

            query = "SELECT COUNT(*) " +
                "FROM(select distinct pl.id, pl.factura_referencia FROM packinglist pl " +
                "INNER JOIN encabezadomov em ON em.id_encabezado = pl.encabezado_id " +
                "LEFT JOIN packinglist_consignatario c ON c.id_packinglist = pl.id " +
                $" {where} " +
                "GROUP BY  pl.id, pl.factura_referencia" +
            $" {having}) sub  ";
            var total = RunScalar(query, parameters);

            return Json(new { data, total });
        }

        public IActionResult DocumentoDetailPacking(int id)
        {
            string query = @"
    SELECT json_build_object(
    'packinglist_id', pl.id,
    'factura_referencia', pl.factura_referencia,
    'facturaReferenciaTexto', em.folio ||
             CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END,
    'fecha_embarque', pl.fecha_embarque,
    'fechaEmbarque', pl.fecha_embarque,
    'medio_transporte', pl.medio_transporte,
    'medioTransporte', pl.medio_transporte,
    'puerto_salida', pl.puerto_salida,
    'puertoSalida', pl.puerto_salida,
    'puerto_destino', pl.puerto_destino,
    'puertoDestino', pl.puerto_destino,
    'incoterm', pl.incoterm,
    'numero_guia', pl.numero_guia,
    'numeroContenedor', pl.numero_guia,
    'orden_compra_comprador', pl.orden_compra_comprador,
    'ODCComprador', pl.orden_compra_comprador,
    'observaciones', pl.observaciones,
    'creado_en', pl.creado_en,
    'folio', em.folio ||
             CASE WHEN em.variacion > 0 THEN '-' || num_to_letters(em.variacion) ELSE '' END,
    'fecha_documento', em.fch,
    'cliente_proveedor', em.cli_prov,
    'comentarios', json_build_array(em.coment1, em.coment2, em.coment3),
    'consignatario', json_build_object(
        'consignatario_id', c.id,
        'nombre', c.nombre,
        'calle', c.calle,
        'nexterior', c.nexterior,
        'ninterior', c.ninterior,
        'colonia', c.colonia,
        'cp', c.cp,
        'estado', c.estado,
        'pais', c.pais
    ),
    'tarimas', COALESCE(
(
    SELECT json_agg(
        json_build_object(
            'numero', t.numero_tarima,
            'descripcion', t.tarima,
            'bultos',
            (
                SELECT json_agg(
                    json_build_object(
                        'bulto_id', b.id,
                        'numero', b.numero,
                        'tipoEmbalaje', b.tipo_embalaje,
                        'marca', b.marca,
                        'largo', b.largo,
                        'ancho', b.ancho,
                        'alto', b.alto,
                        'dimensiones', json_build_object(
                            'largo', b.largo,
                            'ancho', b.ancho,
                            'alto', b.alto,
                            'volumen', b.volumen
                        ),
                        'volumen', b.volumen,
                        'peso_neto', b.peso_neto,
                        'pesoBruto', b.peso_bruto,
                        'items', COALESCE(
                            (
                                SELECT json_agg(
                                    json_build_object(
                                        'item_id', i.id,
                                        'id', i.id_concepto_factura,
                                        'sku', i.sku,
                                        'descripcion', i.descripcion,
                                        'cantidad', i.cantidad,
                                        'unidad', i.unidad,
                                        'fraccion', i.fraccion,
                                        'peso', COALESCE(
                                            (SELECT peso
                                             FROM frac_arancelarias
                                             WHERE cve_prod = i.sku
                                             LIMIT 1),
                                            0
                                        )
                                    )
                                )
                                FROM packinglist_items i
                                WHERE i.id_bulto = b.id
                            ),
                            '[]'::json
                        )
                    )
                    ORDER BY b.numero
                )
                FROM packinglist_bultos b
                WHERE b.tarima = t.tarima
                  AND b.id_packinglist = pl.id
            )
        )
        ORDER BY t.numero_tarima
    )
    FROM (
        SELECT
            b.tarima,
            ROW_NUMBER() OVER (ORDER BY MIN(b.id)) AS numero_tarima
        FROM packinglist_bultos b
        WHERE b.id_packinglist = pl.id
        GROUP BY b.tarima
    ) t
),
'[]'::json
)
) AS packinglist_json
FROM packinglist pl
INNER JOIN encabezadomov em ON em.id_encabezado = pl.encabezado_id
LEFT JOIN packinglist_consignatario c ON c.id_packinglist = pl.id
WHERE pl.id = @id
GROUP BY 
    pl.id, pl.factura_referencia, pl.fecha_embarque, pl.medio_transporte, 
    pl.puerto_salida, pl.puerto_destino, pl.incoterm, pl.numero_guia, 
    pl.orden_compra_comprador, pl.observaciones, pl.creado_en,
    em.id_encabezado, em.gen, em.nat, em.fch, em.fol_doc, em.variacion,
    em.cli_prov, em.coment1, em.coment2, em.coment3,
    c.id, c.nombre, c.calle, c.nexterior, c.ninterior, c.colonia, c.cp, c.estado, c.pais;
    ";

            var parameters = new Dictionary<string, object>();
            parameters.Add("id", id);
            var result = RunQuery(query, parameters);

            return Json(result);
        }

    }
}
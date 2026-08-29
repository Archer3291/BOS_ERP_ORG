using BOS_ERP.Controllers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers
{
    public partial class ConsultaController : Utilities
    {

        public JsonResult GetAllData(IFormCollection fc)
        {
            var parameters= new Dictionary<string, object>();
            parameters.Add("empresa_id", Convert.ToInt32(HttpContext.Session.GetInt32("Empresa")));

            string query = "SELECT jsonb_pretty( " +
                "    jsonb_build_object( " +
                "        'sucursales', COALESCE( " +
                "            ( " +
                "                SELECT jsonb_agg( " +
                "                    jsonb_build_object( " +
                "                        'id', s.id_sucursal, " +
                "                        'cve', s.cve_sucursal, " +
                "                        'descripcion', s.descripcion, " +
                "                        'tipo', s.tipo, " +
                "                        'almacenes', COALESCE( " +
                "                            ( " +
                "                                SELECT jsonb_agg( " +
                "                                    jsonb_build_object( " +
                "                                        'id', a.id_almacen, " +
                "                                        'cve', a.cve_almacen, " +
                "                                        'descripcion', a.descripcion, " +
                "                                        'tipo', a.tipo, " +
                "                                        'pasillos', COALESCE( " +
                "                                            ( " +
                "                                                SELECT jsonb_agg( " +
                "                                                    jsonb_build_object( " +
                "                                                        'id', p.id_pasillo, " +
                "                                                        'cve', p.cve_pasillo, " +
                "                                                        'num_pasillo', p.num_pasillo, " +
                "                                                        'racks', COALESCE( " +
                "                                                            ( " +
                "                                                                SELECT jsonb_agg( " +
                "                                                                    jsonb_build_object( " +
                "                                                                        'id', r.id_rack, " +
                "                                                                        'nombre', r.nombre, " +
                "                                                                        'num_rack', r.num_rack, " +
                "                                                                        'tipo', r.tipo, " +
                "                                                                        'lado', r.lado, " +
                "                                                                        'columnas', COALESCE( " +
                "                                                                            ( " +
                "                                                                                SELECT jsonb_agg(" +
                "                                                                                    jsonb_build_object( " +
                "                                                                                        'id', c.id_columna, " +
                "                                                                                       'nombre', c.nombre, " +
                "                                                                                        'num_col', c.num_col, " +
                "                                                                                        'niveles', COALESCE( " +
                "                                                                                            ( " +
                "                                                                                                SELECT jsonb_agg( " +
                "                                                                                                    jsonb_build_object( " +
                "                                                                                                        'id', n.id_nivel, " +
                "                                                                                                        'nombre', n.nombre, " +
                "                                                                                                        'ulocation', n.ulocation, " +
                "                                                                                                        'num_nivel', n.num_nivel, " +
                "                                                                                                        'capacidad', r.capacidad, " +
                "                                                                                                       'tarimas', COALESCE( " +
                "                                                                                                            ( " +
                "                                                                                                                SELECT jsonb_agg( " +
                "                                                                                                                    jsonb_build_object( " +
                "                                                                                                                        'id', t.id_tarima, " +
                "                                                                                                                        'codigo', t.codigo, " +
                "                                                                                                                        'uuid', t.\"uuid\", " +
                "                                                                                                                        'fecha', t.fecha, " +
                "                                                                                                                       'productos', COALESCE( " +
                "                                                                                                                            ( " +
                "                                                                                                                                SELECT jsonb_agg( " +
                "                                                                                                                                    jsonb_build_object( " +
                "                                                                                                                                       'id', tp.id_tarima_producto, " +
                "                                                                                                                                        'producto_id', tp.producto_id, " +
                "                                                                                                                                        'cantidad', tp.cantidad, " +
                "                                                                                                                                        'unidad', tp.unidad, " +
                "                                                                                                                                       'cve_prod', p.cve_prod, " +
                "                                                                                                                                        'descr_prod', p.descr_prod, " +
                "                                                                                                                                       'lin_prod', p.lin_prod, " +
                "                                                                                                                                        'tp', p.tp, " +
                "                                                                                                                                        'gpo', p.gpo, " +
                "                                                                                                                                       'cod_prov', p.cod_prov, " +
                "                                                                                                                                       'cbr', p.cbr, " +
                "                                                                                                                                        'cve_prov_ppal', p.cve_prov_ppal, " +
                "                                                                                                                                        'cve_prov_sec', p.cve_prov_sec, " +
                "                                                                                                                                      'cve_ub_alm', p.cve_ub_alm, " +
                "                                                                                                                                        'udm', p.udm " +
                "                                                                                                                                    ) " +
                "                                                                                                                               ) " +
                "                                                                                                                                FROM tarima_productos tp " +
                "                                                                                                                                JOIN catproductos p ON p.id_catproductos = tp.producto_id " +
                "                                                                                                                                WHERE tp.tarima_id = t.id_tarima   " +
                "                                                                                                                                AND p.empresa_id = @empresa_id" +
                // Solo partidas con existencia: una fila en 0 no es stock, es un
                // residuo del movimiento que la vació. Filtrarlo aquí y no en el
                // front mantiene de acuerdo a listas, cajas 3D y buscador.
                "                                                                                                                                AND tp.cantidad > 0 " +
                "                                                                                                                           ), '[]'::jsonb " +
                "                                                                                                                        ) " +
                "                                                                                                                   ) " +
                "                                                                                                              ) " +
                "                                                                                                                FROM cattarimas t " +
                "                                                                                                                WHERE t.nivel_id = n.id_nivel " +
                "                                                                                                            ), '[]'::jsonb " +
                "                                                                                                        ) " +
                "                                                                                                    ) " +
                "                                                                                                ) " +
                "                                                                                                FROM catniveles n " +
                "                                                                                                JOIN catcolumnas c2 ON c2.id_columna = n.columna_id " +
                "                                                                                                JOIN catracks r ON r.id_rack = c2.rack_id " +
                "                                                                                                WHERE n.columna_id = c.id_columna " +
                "                                                                                            ), '[]'::jsonb " +
                "                                                                                        ) " +
                "                                                                                    ) " +
                "                                                                                ) " +
                "                                                                                FROM catcolumnas c " +
                "                                                                                WHERE c.rack_id = r.id_rack " +
                "                                                                            ), '[]'::jsonb " +
                "                                                                        ) " +
                "                                                                   ) " +
                "                                                                ) " +
                "                                                                FROM catracks r " +
                "                                                                WHERE r.almacen_id = a.id_almacen " +
                "                                                                  AND r.pasillo_id = p.id_pasillo " +
                "                                                            ), '[]'::jsonb " +
                "                                                        ) " +
                "                                                    ) " +
                "                                                ) " +
                "                                                FROM catpasillos p " +
                "                                                WHERE p.almacen_id = a.id_almacen " +
                "                                            ), '[]'::jsonb " +
                "                                        ), " +
                "                                        'racks_sin_pasillo', COALESCE( " +
                "                                            ( " +
                "                                                SELECT jsonb_agg( " +
                "                                                    jsonb_build_object( " +
                "                                                        'id', r.id_rack, " +
                "                                                        'nombre', r.nombre, " +
                "                                                        'num_rack', r.num_rack, " +
                "                                                        'tipo', r.tipo, " +
                "                                                        'lado', r.lado, " +
                "                                                        'columnas', COALESCE( " +
                "                                                            ( " +
                "                                                                SELECT jsonb_agg( " +
                "                                                                    jsonb_build_object( " +
                "                                                                        'id', c.id_columna, " +
                "                                                                        'nombre', c.nombre, " +
                "                                                                        'num_col', c.num_col, " +
                "                                                                        'niveles', COALESCE( " +
                "                                                                            ( " +
                "                                                                                SELECT jsonb_agg( " +
                "                                                                                   jsonb_build_object( " +
                "                                                                                        'id', n.id_nivel, " +
                "                                                                                        'nombre', n.nombre, " +
                "                                                                                        'ulocation', n.ulocation, " +
                "                                                                                        'num_nivel', n.num_nivel, " +
                "                                                                                        'capacidad', r.capacidad, " +
                "                                                                                        'tarimas', COALESCE( " +
                "                                                                                            ( " +
                "                                                                                                SELECT jsonb_agg( " +
                "                                                                                                    jsonb_build_object( " +
                "                                                                                                        'id', t.id_tarima, " +
                "                                                                                                        'codigo', t.codigo, " +
                "                                                                                                        'uuid', t.\"uuid\", " +
                "                                                                                                        'fecha', t.fecha, " +
                "                                                                                                       'productos', COALESCE( " +
                "                                                                                                            ( " +
                "                                                                                                                SELECT jsonb_agg( " +
                "                                                                                                                    jsonb_build_object( " +
                "                                                                                                                        'id', tp.id_tarima_producto, " +
                "                                                                                                                        'producto_id', tp.producto_id, " +
                "                                                                                                                        'cantidad', tp.cantidad, " +
                "                                                                                                                        'unidad', tp.unidad, " +
                "                                                                                                                        'cve_prod', p.cve_prod, " +
                "                                                                                                                        'descr_prod', p.descr_prod, " +
                "                                                                                                                        'lin_prod', p.lin_prod, " +
                "                                                                                                                        'tp', p.tp, " +
                "                                                                                                                        'gpo', p.gpo, " +
                "                                                                                                                        'cod_prov', p.cod_prov, " +
                "                                                                                                                        'cbr', p.cbr, " +
                "                                                                                                                        'cve_prov_ppal', p.cve_prov_ppal, " +
                "                                                                                                                        'cve_prov_sec', p.cve_prov_sec, " +
                "                                                                                                                        'cve_ub_alm', p.cve_ub_alm, " +
                "                                                                                                                        'udm', p.udm " +
                "                                                                                                                    ) " +
                "                                                                                                                ) " +
                "                                                                                                                FROM tarima_productos tp " +
                "                                                                                                                JOIN catproductos p ON p.id_catproductos = tp.producto_id " +
                "                                                                                                                WHERE tp.tarima_id = t.id_tarima " +
                "                                                                                                                AND tp.cantidad > 0 " +
                "                                                                                                            ), '[]'::jsonb " +
                "                                                                                                        ) " +
                "                                                                                                    ) " +
                "                                                                                                ) " +
                "                                                                                                FROM cattarimas t " +
                "                                                                                               WHERE t.nivel_id = n.id_nivel " +
                "                                                                                            ), '[]'::jsonb " +
                "                                                                                       ) " +
                "                                                                                    ) " +
                "                                                                                ) " +
                "                                                                                FROM catniveles n " +
                "                                                                                JOIN catcolumnas c2 ON c2.id_columna = n.columna_id " +
                "                                                                                JOIN catracks r ON r.id_rack = c2.rack_id " +
                "                                                                                WHERE n.columna_id = c.id_columna " +
                "                                                                            ), '[]'::jsonb " +
                "                                                                       ) " +
                "                                                                    ) " +
                "                                                               ) " +
                "                                                                FROM catcolumnas c " +
                "                                                                WHERE c.rack_id = r.id_rack " +
                "                                                            ), '[]'::jsonb " +
                "                                                       ) " +
                "                                                    ) " +
                "                                                ) " +
                "                                                FROM catracks r " +
                "                                                WHERE r.almacen_id = a.id_almacen " +
                "                                                  AND r.pasillo_id IS NULL " +
                "                                            ), '[]'::jsonb " +
                "                                        ) " +
                "                                    ) " +
                "                                ) " +
                "                                FROM catalmacenes a " +
                "                                WHERE a.sucursal_id = s.id_sucursal " +
                "                            ), '[]'::jsonb " +
                "                        ) " +
                "                   ) " +
                "                ) " +
                "                FROM catsucursales s  " +
                "            ), '[]'::jsonb " +
                "        ) " +
                "    ) " +
                ");";
            var result = RunQuery(query);
            return Json(result);
        }
    }

}
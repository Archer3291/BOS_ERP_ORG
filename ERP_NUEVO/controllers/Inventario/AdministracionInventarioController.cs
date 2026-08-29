using Newtonsoft.Json;
using BOS_ERP.Filters;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Controllers.Inventario
{
    public partial class AdministracionInventarioController : Utilities
    {
        #region Obtener datos generales
        public JsonResult Sucursal()
        {
            string query = "SELECT cs.id_sucursal, cs.cve_sucursal, cs.descripcion, cs.tipo, em.nombre " +
                "FROM catsucursales cs " +
                "INNER JOIN empresas em ON em.empresaid = cs.empresa_id";
            var result = RunQuery(query);
            return Json(new { data = result });
        }
        public JsonResult SelectSucursal()
        {
            string query = "SELECT cs.id_sucursal as value, cs.cve_sucursal || ' - ' || em.nombre as label " +
                "FROM catsucursales cs " +
                "INNER JOIN empresas em ON em.empresaid = cs.empresa_id";
            var result = RunQuery(query);
            return Json(new { data = result });
        }
        public JsonResult Almacen()
        {
            // 1. Obtener almacenes
            string queryAlmacenes = @"
        SELECT id_almacen, cve_almacen, cs.cve_sucursal, cs.id_sucursal, ca.descripcion, ca.tipo
        FROM catalmacenes ca
        INNER JOIN catsucursales cs ON cs.id_sucursal = ca.sucursal_id";
            var almacenes = RunQuery(queryAlmacenes);

            // 2. Obtener pasillos
            string queryPasillos = @"
        SELECT id_pasillo, cve_pasillo, almacen_id, cc.cve_almacen
        FROM catpasillos cp
        INNER JOIN catalmacenes cc ON cc.id_almacen = cp.almacen_id";
            var pasillos = RunQuery(queryPasillos);

            // 3. Relacionar: agregar la lista de pasillos a cada almacén
            foreach (var almacen in almacenes)
            {
                var idAlmacen = Convert.ToInt32(almacen["id_almacen"]);

                // Filtrar pasillos que pertenecen al almacén
                var pasillosDeAlmacen = pasillos
                    .Where(p => Convert.ToInt32(p["almacen_id"]) == idAlmacen)
                    .ToList();

                // Insertar la lista en la clave "pasillos"
                almacen["pasillos"] = pasillosDeAlmacen;
            }

            // 4. Devolver todo en formato que tu script espera
            return Json(new { data = almacenes });
        }

        public JsonResult SelectAlmacen(int? sucursalId = null)
        {
            string query = @"SELECT id_almacen as value, cve_almacen as label 
                     FROM catalmacenes";

            var parameters = new Dictionary<string, object>();

            if (sucursalId.HasValue)
            {
                query += " WHERE sucursal_id = @sucursalId;";
                parameters.Add("@sucursalId", sucursalId.Value);
            }

            var result = RunQuery(query, parameters);
            return Json(new { data = result });
        }

        public JsonResult Pasillo(int pasilloId)
        {
            try
            {
                string query = @"
            SELECT cp.id_pasillo, cp.cve_pasillo, cp.almacen_id, cc.cve_almacen, cp.num_pasillo
            FROM catpasillos cp
            INNER JOIN catalmacenes cc ON cc.id_almacen = cp.almacen_id
            WHERE cp.id_pasillo = @pasilloId;
        ";

                var parameters = new Dictionary<string, object>
        {
            { "@pasilloId", pasilloId }
        };

                var result = RunQuery(query, parameters);

                return Json(new { data = result });
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message });
            }
        }

        public JsonResult SelectPasillos(int? almacenId = null)
        {
            string query = @"SELECT id_pasillo as value, cve_pasillo as label 
                     FROM catpasillos";

            var parameters = new Dictionary<string, object>();

            if (almacenId.HasValue)
            {
                query += " WHERE almacen_id = @almacenId;";
                parameters.Add("@almacenId", almacenId.Value);
            }

            var result = RunQuery(query, parameters);
            return Json(new { data = result });
        }
        public JsonResult Racks()
        {
            string query = @"
                SELECT 
                    cr.id_rack, cr.nombre AS rack_nombre, cr.almacen_id, ca.id_almacen, ca.cve_almacen, cr.lado, cr.pasillo_id,
                    (cp.cve_pasillo || '-' || cr.lado) AS cve_pasillo, cr.tipo, cp.id_pasillo,
                    cc.id_columna, cc.nombre AS columna_nombre, cc.num_col,
                    cn.id_nivel, cn.nombre AS nivel_nombre, cn.ulocation, cr.num_rack, cn.num_nivel, cs.id_sucursal, cs.cve_sucursal
                FROM catracks cr
                INNER JOIN catalmacenes ca ON ca.id_almacen = cr.almacen_id
                INNER JOIN catsucursales cs on cs.id_sucursal = ca.sucursal_id
                LEFT JOIN catpasillos cp ON cp.id_pasillo = cr.pasillo_id
                LEFT JOIN catcolumnas cc ON cc.rack_id = cr.id_rack
                LEFT JOIN catniveles cn ON cn.columna_id = cc.id_columna;
            ";

            var result = RunQuery(query); // esto te da un DataTable o List<Dictionary<string,object>>

            // Transformación a objeto jerárquico
            var racks = result
                .GroupBy(r => new {
                    id_rack = r["id_rack"],
                    nombre = r["rack_nombre"],
                    cve_almacen = r["cve_almacen"],
                    cve_pasillo = r["cve_pasillo"],
                    num_rack = r["num_rack"],
                    cve_sucursal = r["cve_sucursal"],
                    id_sucursal = r["id_sucursal"],
                    tipo = r["tipo"],
                    id_almacen = r["id_almacen"],
                    id_pasillo = r["id_pasillo"]
                })
                .Select(rg => new {
                    id_rack = rg.Key.id_rack,
                    nombre = rg.Key.nombre,
                    cve_almacen = rg.Key.cve_almacen,
                    cve_pasillo = rg.Key.cve_pasillo,
                    num_rack = rg.Key.num_rack,
                    cve_sucursal = rg.Key.cve_sucursal,
                    id_sucursal = rg.Key.id_sucursal,
                    tipo = rg.Key.tipo,
                    id_almacen = rg.Key.id_almacen,
                    id_pasillo = rg.Key.id_pasillo,

                    // SOLO columnas válidas
                    columnas = rg
                        .Where(c => c["id_columna"] != DBNull.Value && c["id_columna"] != null)
                        .GroupBy(cg => new {
                            id_columna = cg["id_columna"],
                            nombre = cg["columna_nombre"],
                            num_col = cg["num_col"]
                        })
                        .Select(cg => new {
                            id_columna = cg.Key.id_columna,
                            nombre = cg.Key.nombre,
                            num_col = cg.Key.num_col,

                            // SOLO niveles válidos
                            niveles = cg
                                .Where(n => n["id_nivel"] != DBNull.Value && n["id_nivel"] != null)
                                .Select(n => new {
                                    id_nivel = n["id_nivel"],
                                    nombre = n["nivel_nombre"],
                                    ulocation = n["ulocation"],
                                    num_nivel = n["num_nivel"]
                                }).ToList()
                        })
                        .ToList()
                }).ToList();


            return Json(new { data = racks });
        }

        public JsonResult SelectRacks(int? pasilloId = null, int? almacenId = null, bool sinPasillo = false)
        {
            string query = @"SELECT id_rack as value, nombre as label 
                     FROM catracks WHERE 1=1";
            var parameters = new Dictionary<string, object>();

            // Caso 1: Buscar racks sin pasillo (pasillo_id IS NULL)
            if (sinPasillo)
            {
                //query += " AND (pasillo_id IS NULL OR pasillo_id = '')";

                // Si también se especifica almacén, filtrar por almacén
                if (almacenId.HasValue)
                {
                    query += " AND almacen_id = @almacenId";
                    parameters.Add("@almacenId", almacenId.Value);
                }
            }
            // Caso 2: Buscar racks de un pasillo específico
            else if (pasilloId.HasValue)
            {
                query += " AND pasillo_id = @pasilloId";
                parameters.Add("@pasilloId", pasilloId.Value);
            }
            // Caso 3: Buscar racks por almacén (todos los racks del almacén)
            else if (almacenId.HasValue)
            {
                query += " AND almacen_id = @almacenId";
                parameters.Add("@almacenId", almacenId.Value);
            }

            query += " ORDER BY nombre";

            var result = RunQuery(query, parameters);
            return Json(new { data = result });
        }

        public JsonResult GetNivelULocation(int nivelId)
        {
            try
            {
                string query = @"SELECT ulocation FROM catniveles WHERE id_nivel = @nivelId";
                var parameters = new Dictionary<string, object>
        {
            { "@nivelId", nivelId }
        };

                var result = RunQuery(query, parameters);
                var ulocation = result.FirstOrDefault()?["ulocation"]?.ToString() ?? "";

                return Json(new { ulocation = ulocation });
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message, ulocation = "" });
            }
        }

        public JsonResult Niveles()
        {
            string query = "SELECT id_nivel, cn.nombre, cn.ulocation , c.nombre as cve_columna FROM catniveles cn  " +
                "                inner join catcolumnas c on c.id_columna  = cn.columna_id;";
            var result = RunQuery(query);
            return Json(new { data = result });
        }
        public JsonResult SelectNiveles(int? columnaId = null)
        {
            string query = @"SELECT id_nivel as value, nombre as label 
                     FROM catniveles";

            var parameters = new Dictionary<string, object>();

            if (columnaId.HasValue)
            {
                query += " WHERE columna_id = @columnaId;";
                parameters.Add("@columnaId", columnaId.Value);
            }

            var result = RunQuery(query, parameters);
            return Json(new { data = result });
        }

        public JsonResult Columnas()
        {
            string query = "SELECT id_columna, cc.nombre, c.pasillo_id , c.nombre as cve_rack FROM catcolumnas cc  " +
                "                          inner join catracks c  on c.almacen_id = cc.rack_id;";
            var result = RunQuery(query);
            return Json(new { data = result });
        }
        public JsonResult SelectColumnas(int? rackId = null)
        {
            string query = @"SELECT id_columna as value, nombre as label 
                     FROM catcolumnas";

            var parameters = new Dictionary<string, object>();

            if (rackId.HasValue)
            {
                query += " WHERE rack_id = @rackId;";
                parameters.Add("@rackId", rackId.Value);
            }

            var result = RunQuery(query, parameters);
            return Json(new { data = result });
        }

        public JsonResult Tarimas()
        {
            string query = "SELECT id_tarima, codigo, ulocation, fecha, columna_id, c.nombre as cve_nivel,  " +
                "c5.id_sucursal, c5.cve_sucursal, c4.id_almacen, c4.cve_almacen, c6.id_pasillo, c6.cve_pasillo, " +
                "c3.id_rack, c3.nombre as cve_rack, c2.id_columna, c2.nombre as cve_columa, " +
                "c.id_nivel, c.nombre as cve_nivel FROM cattarimas ct   " +
                "inner join catniveles c on c.id_nivel  = ct.nivel_id " +
                "inner  join catcolumnas c2  on c2.id_columna  =  c.columna_id " +
                "inner join catracks c3  on c3.id_rack  =  c2.rack_id " +
                "inner join catalmacenes c4 on c4.id_almacen  = c3.almacen_id " +
                "inner join catsucursales c5 on c5.id_sucursal  = c4.sucursal_id " +
                "inner join catpasillos c6 on c6.almacen_id  = c4.id_almacen";
            var result = RunQuery(query);
            return Json(new { data = result });
        }
        #endregion

        #region Acciones opciones
        [HttpPost,ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Inventario", Accion = "Edicion de sucursales")]
        public JsonResult EditarSucursal(IFormCollection fc)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                string query = "UPDATE catsucursales " +
                               "SET descripcion=@descripcion, cve_sucursal=@cve_sucursal " +
                               "WHERE id_sucursal=@id_sucursal;";

                parameters.Add("id_sucursal",Convert.ToInt32(fc["id_sucursal"].ToString()));
                parameters.Add("cve_sucursal", fc["cve_sucursal"].ToString());
                parameters.Add("descripcion", fc["descripcion"].ToString());


                RunUpdate(query,parameters);
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new
                { success = false, message = "Error al procesar las opciones.", error = ex.Message });
            }

        }

        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Inventario", Accion = "Guardado de nueva sucursal")]
        public JsonResult GuardarSucursal(IFormCollection fc)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                string query = "INSERT INTO catsucursales " +
                    "(cve_sucursal, descripcion) " +
                    "VALUES(@cve_sucursal, @descripcion);";

                parameters.Add("cve_sucursal", fc["cve_sucursal"].ToString());
                parameters.Add("descripcion", fc["descripcion"].ToString());


                RunUpdate(query, parameters);
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new
                { success = false, message = "Error al procesar las opciones.", error = ex.Message });
            }

        }

        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Inventario", Accion = "Edicion de Almacen")]
        public JsonResult EditarAlmacen(IFormCollection fc)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                string query = "UPDATE catalmacenes " +
                               "SET descripcion = @descripcion, sucursal_id = @sucursal_id, cve_almacen = @cve_almacen, tipo=@tipo " +
                               "WHERE id_almacen = @id_almacen;";

                parameters.Add("id_almacen", Convert.ToInt32(fc["id_almacen"].ToString()));
                parameters.Add("cve_almacen", fc["cve_almacen"].ToString());
                parameters.Add("descripcion", fc["descripcion"].ToString());
                parameters.Add("sucursal_id", Convert.ToInt32(fc["cve_sucursal"].ToString()));
                parameters.Add("tipo", fc["tipo"].ToString());

                RunUpdate(query, parameters);

                // Pasillos
                string jsonPasillos = fc["pasillos"].ToString();
                if (!string.IsNullOrEmpty(jsonPasillos))
                {
                    var pasillos = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(jsonPasillos);

                    // Lo más simple: eliminar pasillos antiguos y volver a insertar
                    string deletePasillos = "DELETE FROM catpasillos WHERE almacen_id=@id_almacen;";
                    RunUpdate(deletePasillos, new Dictionary<string, object> { { "id_almacen", Convert.ToInt32(fc["id_almacen"].ToString()) } });

                    foreach (var pasillo in pasillos)
                    {
                        var paramPasillo = new Dictionary<string, object>();
                        paramPasillo.Add("cve_pasillo", pasillo["cve_pasillo"].ToString());
                        paramPasillo.Add("almacen_id", Convert.ToInt32(fc["id_almacen"].ToString()));

                        string insertPasillo = "INSERT INTO catpasillos (cve_pasillo, almacen_id) " +
                                               "VALUES(@cve_pasillo, @almacen_id);";
                        RunUpdate(insertPasillo, paramPasillo);
                    }
                }

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al editar.", error = ex.Message });
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Inventario", Accion = "Guardado de nuevo almacen")]
        public JsonResult GuardarAlmacen(IFormCollection fc)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                string query = "INSERT INTO catalmacenes " +
                               "(cve_almacen, descripcion, sucursal_id, tipo) " +
                               "VALUES(@cve_almacen, @descripcion, @sucursal_id, @tipo) RETURNING id_almacen;";

                parameters.Add("cve_almacen", fc["cve_almacen"].ToString());
                parameters.Add("descripcion", fc["descripcion"].ToString());
                parameters.Add("sucursal_id", Convert.ToInt32(fc["cve_sucursal"].ToString()));
                parameters.Add("tipo", fc["tipo"].ToString());

                // Insertar almacen y recuperar ID
                var idAlmacen = RunScalar(query, parameters);

                // Pasillos
                string jsonPasillos = fc["pasillos"].ToString();
                if (!string.IsNullOrEmpty(jsonPasillos))
                {
                    var pasillos = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(jsonPasillos);
                    foreach (var pasillo in pasillos)
                    {
                        var paramPasillo = new Dictionary<string, object>();
                        paramPasillo.Add("cve_pasillo", pasillo["cve_pasillo"].ToString());
                        paramPasillo.Add("almacen_id", Convert.ToInt32(idAlmacen));

                        string insertPasillo = "INSERT INTO catpasillos (cve_pasillo, almacen_id) " +
                                               "VALUES(@cve_pasillo, @almacen_id);";
                        RunUpdate(insertPasillo, paramPasillo);
                    }
                }

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al guardar.", error = ex.Message });
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Inventario", Accion = "Edición de Rack")]
        public JsonResult EditarRacks(IFormCollection fc)
        {
            try
            {
                int rackId = Convert.ToInt32(fc["id_rack"].ToString());

                // 1️⃣ Actualizar rack
                string updateRackQuery = @"
            UPDATE catracks
            SET nombre=@nombre, almacen_id=@almacen_id, pasillo_id=@pasillo_id
            WHERE id_rack=@id_rack;";

                var rackParams = new List<Dictionary<string, object>>
        {
            new Dictionary<string, object>
            {
                { "id_rack", rackId },
                { "nombre", fc["nombre"].ToString() },
                { "almacen_id", Convert.ToInt32(fc["cve_almacen"].ToString()) },
                { "pasillo_id", string.IsNullOrWhiteSpace(fc["cve_pasillo"].ToString())? (object)DBNull.Value: (object)Convert.ToInt32(fc["cve_pasillo"].ToString()) }
            }
        };
                RunUpdate(updateRackQuery, rackParams);

                // 2️⃣ Manejo de columnas/niveles JSON
                string jsonColumnas = fc["columnas"].ToString();
                if (!string.IsNullOrEmpty(jsonColumnas))
                {
                    var columnas = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(jsonColumnas);

                    // ✅ Traer columnas actuales de la BD
                    string selectColsQuery = "SELECT id_columna FROM catcolumnas WHERE rack_id=@rack_id;";
                    var dbCols = RunQuery(selectColsQuery, new Dictionary<string, object> { { "rack_id", rackId } })
                                 .Select(r => Convert.ToInt32(r["id_columna"]))
                                 .ToList();

                    // IDs que vienen en JSON
                    var jsonCols = columnas.Where(c => c.ContainsKey("id_columna"))
                                           .Select(c => Convert.ToInt32(c["id_columna"]))
                                           .ToList();

                    // Columnas a eliminar
                    var colsToDelete = dbCols.Except(jsonCols).ToList();
                    foreach (var idCol in colsToDelete)
                    {
                        RunUpdate("DELETE FROM catniveles WHERE columna_id=@columna_id;",
                                  new List<Dictionary<string, object>> { new Dictionary<string, object> { { "columna_id", idCol } } });

                        RunUpdate("DELETE FROM catcolumnas WHERE id_columna=@id_columna;",
                                  new List<Dictionary<string, object>> { new Dictionary<string, object> { { "id_columna", idCol } } });
                    }

                    // Procesar columnas
                    foreach (var col in columnas)
                    {
                        int colId;

                        if (col.ContainsKey("id_columna"))
                        {
                            // Actualizar columna existente
                            string updateColQuery = "UPDATE catcolumnas SET nombre=@nombre WHERE id_columna=@id_columna;";
                            var colParams = new List<Dictionary<string, object>>
                    {
                        new Dictionary<string, object>
                        {
                            { "id_columna", Convert.ToInt32(col["id_columna"]) },
                            { "nombre", col["nombre"] }
                        }
                    };
                            RunUpdate(updateColQuery, colParams);
                            colId = Convert.ToInt32(col["id_columna"]);
                        }
                        else
                        {
                            // Insertar nueva columna
                            string insertColQuery = "INSERT INTO catcolumnas (rack_id, nombre) VALUES (@rack_id, @nombre) RETURNING id_columna;";
                            var colParams = new Dictionary<string, object>
                    {
                        { "rack_id", rackId },
                        { "nombre", col["nombre"] }
                    };
                            colId = Convert.ToInt32(RunScalar(insertColQuery, colParams));
                        }

                        // Manejo de niveles
                        if (col.ContainsKey("niveles"))
                        {
                            var niveles = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(col["niveles"].ToString());

                            // ✅ Traer niveles actuales de BD
                            string selectNivsQuery = "SELECT id_nivel FROM catniveles WHERE columna_id=@columna_id;";
                            var dbNivs = RunQuery(selectNivsQuery, new Dictionary<string, object> { { "columna_id", colId } })
                                         .Select(r => Convert.ToInt32(r["id_nivel"]))
                                         .ToList();

                            // IDs niveles en JSON
                            var jsonNivs = niveles.Where(n => n.ContainsKey("id_nivel"))
                                                  .Select(n => Convert.ToInt32(n["id_nivel"]))
                                                  .ToList();

                            // Niveles a eliminar
                            var nivsToDelete = dbNivs.Except(jsonNivs).ToList();
                            foreach (var idNiv in nivsToDelete)
                            {
                                RunUpdate("DELETE FROM catniveles WHERE id_nivel=@id_nivel;",
                                          new List<Dictionary<string, object>> { new Dictionary<string, object> { { "id_nivel", idNiv } } });
                            }

                            // Insertar/actualizar niveles
                            foreach (var niv in niveles)
                            {
                                if (niv.ContainsKey("id_nivel"))
                                {
                                    // Actualizar nivel
                                    string updateNivelQuery = "UPDATE catniveles SET nombre=@nombre, ulocation=@ulocation WHERE id_nivel=@id_nivel;";
                                    var nivParams = new List<Dictionary<string, object>>
                            {
                                new Dictionary<string, object>
                                {
                                    { "id_nivel", Convert.ToInt32(niv["id_nivel"]) },
                                    { "nombre", niv.ContainsKey("nombre") ? niv["nombre"] : DBNull.Value },
                                    { "ulocation", niv.ContainsKey("ulocation") ? niv["ulocation"] : DBNull.Value }
                                }
                            };
                                    RunUpdate(updateNivelQuery, nivParams);
                                }
                                else
                                {
                                    // Insertar nuevo nivel
                                    string insertNivelQuery = "INSERT INTO catniveles (columna_id, nombre, ulocation) VALUES (@columna_id, @nombre, @ulocation);";
                                    var nivParams = new List<Dictionary<string, object>>
                            {
                                new Dictionary<string, object>
                                {
                                    { "columna_id", colId },
                                    { "nombre", niv.ContainsKey("nombre") ? niv["nombre"] : DBNull.Value },
                                    { "ulocation", niv.ContainsKey("ulocation") ? niv["ulocation"] : DBNull.Value }
                                }
                            };
                                    RunUpdate(insertNivelQuery, nivParams);
                                }
                            }
                        }
                    }
                }

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al editar el rack.", error = ex.Message });
            }
        }


        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Inventario", Accion = "Guardado de nuevo Rack")]
        public JsonResult GuardarRacks(IFormCollection fc)
        {
            try
            {
                // 1️⃣ Guardar rack
                string insertRackQuery = @"
            INSERT INTO catracks (nombre, almacen_id, pasillo_id, tipo, lado)
            VALUES (@nombre, @almacen_id, @pasillo_id, @tipo, @lado)
            RETURNING id_rack;";


                var rackParams = new Dictionary<string, object>
                {
                    { "nombre", fc["nombre"].ToString() },
                    { "almacen_id", Convert.ToInt32(fc["cve_almacen"].ToString()) },
                    { "pasillo_id", string.IsNullOrWhiteSpace(fc["cve_pasillo"].ToString())? (object)DBNull.Value: (object)Convert.ToInt32(fc["cve_pasillo"].ToString())},
                    { "tipo", fc["tipo"].ToString() },
                    { "lado", fc["lado"].ToString() }
                };

                // ⚡ Ejecutamos query que devuelve id_rack
                int newRackId = Convert.ToInt32(RunScalar(insertRackQuery, rackParams));

                // 2️⃣ Guardar columnas y niveles (JSON en el form)
                string jsonColumnas = fc["columnas"].ToString();
                if (!string.IsNullOrEmpty(jsonColumnas))
                {
                    var columnas = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(jsonColumnas);

                    string insertColQuery = @"
                INSERT INTO catcolumnas (rack_id, nombre)
                VALUES (@rack_id, @nombre)
                RETURNING id_columna;";

                    string insertNivelQuery = @"
                INSERT INTO catniveles (columna_id, nombre, ulocation)
                VALUES (@columna_id, @nombre, @ulocation);";

                    foreach (var col in columnas)
                    {
                        var colParams = new Dictionary<string, object>
                {
                   
                        { "rack_id", newRackId },
                        { "nombre", col["nombre"] }

                };

                        int newColId = Convert.ToInt32(RunScalar(insertColQuery, colParams));

                        if (col.ContainsKey("niveles"))
                        {
                            var niveles = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(col["niveles"].ToString());

                            var batchNiveles = new List<Dictionary<string, object>>();
                            foreach (var niv in niveles)
                            {
                                batchNiveles.Add(new Dictionary<string, object>
                        {
                            { "columna_id", newColId },
                            { "nombre", niv.ContainsKey("nombre") ? niv["nombre"] : DBNull.Value },
                            { "ulocation", niv.ContainsKey("ulocation") ? niv["ulocation"] : DBNull.Value }
                        });
                            }

                            if (batchNiveles.Count > 0)
                                RunUpdate(insertNivelQuery, batchNiveles);
                        }
                    }
                }

                return Json(new { success = true, id_rack = newRackId });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al guardar el rack.", error = ex.Message });
            }
        }



        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Inventario", Accion = "Edicion de Nivel")]
        public JsonResult EditarNiveles(IFormCollection fc)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                string query = "UPDATE catniveles " +
                               "SET nombre = @nombre, rack_id = @rack_id " +
                               "WHERE id_nivel = @id_nivel; ";

                parameters.Add("id_nivel", Convert.ToInt32(fc["id_nivel"].ToString()));
                parameters.Add("rack_id", Convert.ToInt32(fc["cve_rack"].ToString()));
                parameters.Add("nombre", fc["nombre"].ToString());


                RunUpdate(query, parameters);
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new
                { success = false, message = "Error al procesar las opciones.", error = ex.Message });
            }

        }

        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Inventario", Accion = "Guardado de nuevo Nivel")]
        public JsonResult GuardarNiveles(IFormCollection fc)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                string query = "INSERT INTO catniveles " +
                    "( nombre, rack_id) " +
                    "VALUES(@nombre, @rack_id);";

                parameters.Add("rack_id", Convert.ToInt32(fc["cve_rack"].ToString()));
                parameters.Add("nombre", fc["nombre"].ToString());


                RunUpdate(query, parameters);
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new
                { success = false, message = "Error al procesar las opciones.", error = ex.Message });
            }

        }
        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Inventario", Accion = "Edicion de Columna")]
        public JsonResult EditarColumnas(IFormCollection fc)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                string query = "UPDATE catcolumnas " +
                               "SET nombre=@nombre, nivel_id= @nivel_id " +
                               "WHERE id_columna=@id_columna;";

                parameters.Add("nivel_id", Convert.ToInt32(fc["cve_nivel"].ToString()));
                parameters.Add("id_columna", Convert.ToInt32(fc["id_columna"].ToString()));
                parameters.Add("nombre", fc["nombre"].ToString());


                RunUpdate(query, parameters);
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new
                { success = false, message = "Error al procesar las opciones.", error = ex.Message });
            }

        }
        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Inventario", Accion = "Guardado de nueva Columna")]
        public JsonResult GuardarColumnas(IFormCollection fc)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                string query = "INSERT INTO catcolumnas " +
                               "( nombre, nivel_id) " +
                               "VALUES(@nombre, @nivel_id);";

                parameters.Add("nivel_id", Convert.ToInt32(fc["cve_nivel"].ToString()));
                parameters.Add("nombre", fc["nombre"].ToString());


                RunUpdate(query, parameters);
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new
                { success = false, message = "Error al procesar las opciones.", error = ex.Message });
            }

        }

        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Inventario", Accion = "Edicion de tarima")]
        public JsonResult EditarTarimas(IFormCollection fc)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                string query = "UPDATE cattarimas " +
                               "SET  columna_id=@cve_columna " +
                               "WHERE id_tarima=@id_tarima;";

                parameters.Add("cve_columna", Convert.ToInt32(fc["cve_columna"].ToString()));
                parameters.Add("id_tarima", Convert.ToInt32(fc["id_tarima"].ToString()));



                RunUpdate(query, parameters);
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new
                { success = false, message = "Error al procesar las opciones.", error = ex.Message });
            }

        }
        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Inventario", Accion = "Guardado de nueva tarima")]
        public JsonResult GuardarTarimas(IFormCollection fc)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                string query = "INSERT INTO cattarimas " +
                    "(codigo, nivel_id) " +
                    "VALUES(@codigo, @nivel_id);";

                parameters.Add("codigo", fc["codigo"].ToString());
                parameters.Add("nivel_id", Convert.ToInt32(fc["cve_nivel"].ToString()));

                RunUpdate(query, parameters);
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new
                { success = false, message = "Error al procesar las opciones.", error = ex.Message });
            }

        }
        #endregion
    }
}
using BOS_ERP.Filters;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace BOS_ERP.Controllers.Inventario
{
    /// <summary>
    /// Endpoints del explorador de estructura del inventario
    /// (Sucursal → Almacén → Pasillo → Rack → Columna → Nivel → Tarima).
    ///
    /// Los métodos del archivo principal siguen intactos: aquí solo se agrega
    /// lo que el explorador necesita y que antes no existía — el árbol completo
    /// en una sola llamada, el alta masiva de columnas/niveles y el borrado con
    /// validación de dependencias.
    /// </summary>
    public partial class AdministracionInventarioController : Utilities
    {
        private const string ALFABETO = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

        #region Árbol completo

        /// <summary>
        /// GET /AdministracionInventario/Arbol
        /// Toda la jerarquía en una sola consulta. Los racks se devuelven bajo su
        /// almacén con su `pasillo_id`; agruparlos por pasillo es cosa del front
        /// (un rack puede no tener pasillo y aun así colgar del almacén).
        /// </summary>
        public IActionResult Arbol()
        {
            try
            {
                string query = @"
SELECT COALESCE(jsonb_agg(t ORDER BY t.cve_sucursal), '[]'::jsonb)
FROM (
    SELECT
        s.id_sucursal, s.cve_sucursal, s.descripcion,
        COALESCE((
            SELECT jsonb_agg(a ORDER BY a.cve_almacen)
            FROM (
                SELECT al.id_almacen, al.cve_almacen, al.descripcion, al.tipo,
                    COALESCE((
                        SELECT jsonb_agg(p ORDER BY p.num_pasillo NULLS LAST, p.cve_pasillo)
                        FROM (
                            SELECT pa.id_pasillo, pa.cve_pasillo, pa.num_pasillo
                            FROM catpasillos pa
                            WHERE pa.almacen_id = al.id_almacen
                        ) p
                    ), '[]'::jsonb) AS pasillos,
                    COALESCE((
                        SELECT jsonb_agg(r ORDER BY r.num_rack NULLS LAST, r.nombre)
                        FROM (
                            SELECT ra.id_rack, ra.nombre, ra.num_rack, ra.tipo,
                                   ra.lado, ra.pasillo_id,
                                COALESCE((
                                    SELECT jsonb_agg(c ORDER BY c.num_col NULLS LAST, c.nombre)
                                    FROM (
                                        SELECT co.id_columna, co.nombre, co.num_col,
                                            COALESCE((
                                                SELECT jsonb_agg(n ORDER BY n.num_nivel NULLS LAST, n.nombre)
                                                FROM (
                                                    SELECT ni.id_nivel, ni.nombre, ni.num_nivel, ni.ulocation,
                                                        COALESCE((
                                                            SELECT jsonb_agg(ta ORDER BY ta.codigo)
                                                            FROM (
                                                                SELECT tr.id_tarima, tr.codigo, tr.fecha,
                                                                    (SELECT COUNT(*)
                                                                       FROM tarima_productos tp
                                                                      WHERE tp.tarima_id = tr.id_tarima) AS productos
                                                                FROM cattarimas tr
                                                                WHERE tr.nivel_id = ni.id_nivel
                                                            ) ta
                                                        ), '[]'::jsonb) AS tarimas
                                                    FROM catniveles ni
                                                    WHERE ni.columna_id = co.id_columna
                                                ) n
                                            ), '[]'::jsonb) AS niveles
                                        FROM catcolumnas co
                                        WHERE co.rack_id = ra.id_rack
                                    ) c
                                ), '[]'::jsonb) AS columnas
                            FROM catracks ra
                            WHERE ra.almacen_id = al.id_almacen
                        ) r
                    ), '[]'::jsonb) AS racks
                FROM catalmacenes al
                WHERE al.sucursal_id = s.id_sucursal
            ) a
        ), '[]'::jsonb) AS almacenes
    FROM catsucursales s
) t;";

                var rows = RunQuery(query);
                string json = rows.Count > 0 ? rows[0].Values.First()?.ToString() : "[]";
                if (string.IsNullOrWhiteSpace(json)) json = "[]";

                // El árbol ya viene armado como jsonb desde Postgres: se reenvía tal cual.
                // Deserializarlo para volver a serializarlo solo costaría tiempo y arriesgaría
                // que el serializador de MVC renombre las claves.
                return Content("{\"ok\":true,\"data\":" + json + "}", "application/json");
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, msg = ex.Message });
            }
        }

        #endregion

        #region Generador de estructura

        /// <summary>
        /// POST /AdministracionInventario/PreviewEstructura
        /// Devuelve exactamente lo que crearía GenerarEstructura, sin escribir nada.
        /// Comparten el mismo constructor de plan para que la vista previa no pueda
        /// mentir sobre las ULocation resultantes.
        /// </summary>
        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult PreviewEstructura(IFormCollection fc)
        {
            try
            {
                var plan = ConstruirPlanEstructura(fc);

                // Se proyecta a mano para que el JSON use las mismas claves en minúscula
                // que el resto de la API (el serializador de MVC camelCasearía RackPlan).
                var racks = plan.Racks.Select(r => new
                {
                    id_rack = r.IdRack,
                    es_nuevo = r.EsNuevo,
                    nombre = r.Nombre,
                    num_rack = r.NumRack,
                    tipo = r.Tipo,
                    lado = r.Lado,
                    columnas = r.Columnas.Select(c => new
                    {
                        nombre = c.Nombre,
                        num_col = c.NumCol,
                        niveles = c.Niveles.Select(n => new
                        {
                            nombre = n.Nombre,
                            num_nivel = n.NumNivel,
                            ulocation = n.ULocation
                        })
                    })
                });

                return Json(new
                {
                    ok = true,
                    racks,
                    avisos = plan.Avisos,
                    totales = new
                    {
                        racks = plan.Racks.Count(r => r.EsNuevo),
                        columnas = plan.Racks.Sum(r => r.Columnas.Count),
                        niveles = plan.Racks.Sum(r => r.Columnas.Sum(c => c.Niveles.Count))
                    }
                });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, msg = ex.Message });
            }
        }

        /// <summary>
        /// POST /AdministracionInventario/GenerarEstructura
        /// Crea racks/columnas/niveles en una sola transacción: o entra todo o no entra nada.
        /// </summary>
        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Inventario", Accion = "Generación masiva de estructura")]
        public JsonResult GenerarEstructura(IFormCollection fc)
        {
            try
            {
                var plan = ConstruirPlanEstructura(fc);

                if (plan.Avisos.Any(a => a.StartsWith("ULocation duplicada")))
                    return Json(new
                    {
                        ok = false,
                        msg = "Hay ULocation que ya existen. Ajusta la numeración antes de generar.",
                        avisos = plan.Avisos
                    });

                int almacenId = LeerInt(fc, "almacen_id") ?? 0;
                int? pasilloId = LeerInt(fc, "pasillo_id");

                int racksCreados = 0, columnasCreadas = 0, nivelesCreados = 0;
                var idsRacks = new List<int>();

                var utils = new Utilities(true);
                using (var conn = new NpgsqlConnection(utils._configuration.GetConnectionString("ERP_SRS")))
                {
                    conn.Open();
                    using (var tx = conn.BeginTransaction())
                    {
                        try
                        {
                            foreach (var rack in plan.Racks)
                            {
                                int rackId;

                                if (rack.EsNuevo)
                                {
                                    rackId = Convert.ToInt32(RunScalar(@"
                                        INSERT INTO catracks (nombre, almacen_id, pasillo_id, tipo, lado, num_rack)
                                        VALUES (@nombre, @almacen_id, @pasillo_id, @tipo, @lado, @num_rack)
                                        RETURNING id_rack;",
                                        new Dictionary<string, object>
                                        {
                                            { "nombre",     rack.Nombre },
                                            { "almacen_id", almacenId },
                                            { "pasillo_id", pasilloId.HasValue ? (object)pasilloId.Value : DBNull.Value },
                                            { "tipo",       rack.Tipo },
                                            { "lado",       rack.Lado },
                                            { "num_rack",   rack.NumRack }
                                        }, false, conn, tx));
                                    racksCreados++;
                                }
                                else
                                {
                                    rackId = rack.IdRack.Value;
                                }

                                idsRacks.Add(rackId);

                                foreach (var col in rack.Columnas)
                                {
                                    int colId = Convert.ToInt32(RunScalar(@"
                                        INSERT INTO catcolumnas (rack_id, nombre, num_col)
                                        VALUES (@rack_id, @nombre, @num_col)
                                        RETURNING id_columna;",
                                        new Dictionary<string, object>
                                        {
                                            { "rack_id", rackId },
                                            { "nombre",  col.Nombre },
                                            { "num_col", col.NumCol }
                                        }, false, conn, tx));
                                    columnasCreadas++;

                                    foreach (var niv in col.Niveles)
                                    {
                                        RunUpdate(@"
                                            INSERT INTO catniveles (columna_id, nombre, ulocation, num_nivel)
                                            VALUES (@columna_id, @nombre, @ulocation, @num_nivel);",
                                            new Dictionary<string, object>
                                            {
                                                { "columna_id", colId },
                                                { "nombre",     niv.Nombre },
                                                { "ulocation",  niv.ULocation },
                                                { "num_nivel",  niv.NumNivel }
                                            }, false, conn, tx);
                                        nivelesCreados++;
                                    }
                                }
                            }

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
                    ok = true,
                    racks = racksCreados,
                    columnas = columnasCreadas,
                    niveles = nivelesCreados,
                    ids_racks = idsRacks
                });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, msg = ex.Message });
            }
        }

        // -----------------------------------------------------------------
        //  Construcción del plan (compartido por preview y alta real)
        // -----------------------------------------------------------------
        private PlanEstructura ConstruirPlanEstructura(IFormCollection fc)
        {
            int almacenId = LeerInt(fc, "almacen_id")
                ?? throw new Exception("Falta el almacén de destino.");
            int? pasilloId = LeerInt(fc, "pasillo_id");
            int? rackId = LeerInt(fc, "rack_id");

            string tipo = LeerTexto(fc, "tipo", "Rack") == "Estante" ? "Estante" : "Rack";
            string lado = LeerTexto(fc, "lado", "I") == "D" ? "D" : "I";

            int racksCantidad = Math.Max(0, LeerInt(fc, "racks_cantidad") ?? 0);
            string racksPrefijo = LeerTexto(fc, "racks_prefijo", "R");
            int? racksDesde = LeerInt(fc, "racks_desde");

            int columnasCantidad = Math.Max(0, LeerInt(fc, "columnas_cantidad") ?? 0);
            string columnasPrefijo = LeerTexto(fc, "columnas_prefijo", "C");

            int nivelesCantidad = Math.Max(0, LeerInt(fc, "niveles_cantidad") ?? 0);
            string nivelesPrefijo = LeerTexto(fc, "niveles_prefijo", "N");

            var plan = new PlanEstructura();

            // num_pasillo: lo necesita la ULocation de los estantes
            int numPasillo = 1;
            if (pasilloId.HasValue)
            {
                var fila = RunQuery(
                    "SELECT num_pasillo FROM catpasillos WHERE id_pasillo = @id;",
                    new Dictionary<string, object> { { "id", pasilloId.Value } }).FirstOrDefault();

                if (fila != null && fila["num_pasillo"] != null)
                    numPasillo = Convert.ToInt32(fila["num_pasillo"]);
                else
                    plan.Avisos.Add($"El pasillo no tiene num_pasillo; se usará {numPasillo} para la ULocation.");
            }
            else if (tipo == "Estante")
            {
                plan.Avisos.Add("Un estante sin pasillo asignado usará P1 en su ULocation.");
            }

            // ---- Racks ----
            if (rackId.HasValue)
            {
                var r = RunQuery(@"
                    SELECT id_rack, nombre, num_rack, tipo, lado, pasillo_id, almacen_id
                    FROM catracks WHERE id_rack = @id;",
                    new Dictionary<string, object> { { "id", rackId.Value } }).FirstOrDefault()
                    ?? throw new Exception("El rack indicado ya no existe.");

                plan.Racks.Add(new RackPlan
                {
                    IdRack = Convert.ToInt32(r["id_rack"]),
                    EsNuevo = false,
                    Nombre = r["nombre"]?.ToString(),
                    NumRack = r["num_rack"] != null ? Convert.ToInt32(r["num_rack"]) : 1,
                    Tipo = r["tipo"]?.ToString() ?? tipo,
                    Lado = r["lado"]?.ToString() ?? lado
                });

                if (r["num_rack"] == null)
                    plan.Avisos.Add($"El rack «{r["nombre"]}» no tiene num_rack; se usará 1 en la ULocation.");
            }
            else
            {
                int baseNum = racksDesde ?? (SiguienteNumero(
                    "SELECT COALESCE(MAX(num_rack), 0) FROM catracks WHERE almacen_id = @id;",
                    new Dictionary<string, object> { { "id", almacenId } }));

                for (int i = 0; i < racksCantidad; i++)
                {
                    int num = baseNum + i;
                    plan.Racks.Add(new RackPlan
                    {
                        IdRack = null,
                        EsNuevo = true,
                        Nombre = $"{racksPrefijo}{num}",
                        NumRack = num,
                        Tipo = tipo,
                        Lado = lado
                    });
                }
            }

            if (plan.Racks.Count == 0)
                throw new Exception("No hay ningún rack sobre el que generar la estructura.");

            // ---- Columnas y niveles ----
            foreach (var rack in plan.Racks)
            {
                int baseCol = rack.EsNuevo
                    ? 1
                    : SiguienteNumero(
                        "SELECT COALESCE(MAX(num_col), 0) FROM catcolumnas WHERE rack_id = @id;",
                        new Dictionary<string, object> { { "id", rack.IdRack.Value } });

                for (int c = 0; c < columnasCantidad; c++)
                {
                    int numCol = baseCol + c;
                    var columna = new ColumnaPlan
                    {
                        Nombre = $"{columnasPrefijo}{numCol}",
                        NumCol = numCol
                    };

                    for (int n = 0; n < nivelesCantidad; n++)
                    {
                        int numNivel = n + 1;
                        columna.Niveles.Add(new NivelPlan
                        {
                            Nombre = $"{nivelesPrefijo}{numNivel}",
                            NumNivel = numNivel,
                            ULocation = ConstruirULocation(rack.Tipo, numPasillo, rack.NumRack, numNivel, numCol, rack.Lado)
                        });
                    }

                    rack.Columnas.Add(columna);
                }
            }

            // ---- Colisiones de ULocation ----
            var ulocs = plan.Racks
                .SelectMany(r => r.Columnas)
                .SelectMany(c => c.Niveles)
                .Select(n => n.ULocation)
                .ToArray();

            if (ulocs.Length > 0)
            {
                var repetidas = RunQuery(
                    "SELECT DISTINCT ulocation FROM catniveles WHERE ulocation = ANY(@ulocs);",
                    new Dictionary<string, object> { { "ulocs", ulocs } });

                foreach (var fila in repetidas)
                    plan.Avisos.Add($"ULocation duplicada: {fila["ulocation"]} ya existe.");
            }

            return plan;
        }

        private static string ConstruirULocation(string tipo, int numPasillo, int numRack, int numNivel, int numCol, string lado)
        {
            string letra = numNivel >= 1 && numNivel <= ALFABETO.Length
                ? ALFABETO[numNivel - 1].ToString()
                : "A";

            return tipo == "Estante"
                ? $"P{numPasillo}-E{numRack}-{letra}{numNivel}-C{numCol}-{(lado == "D" ? "D" : "I")}"
                : $"R{numRack}-{letra}{numNivel}-T{numCol}";
        }

        private int SiguienteNumero(string query, Dictionary<string, object> parameters)
        {
            var valor = RunScalar(query, parameters);
            return (valor == null || valor == DBNull.Value ? 0 : Convert.ToInt32(valor)) + 1;
        }

        #endregion

        #region Alta y edición de nodos sueltos

        /// <summary>POST /AdministracionInventario/GuardarPasillo — upsert de un pasillo.</summary>
        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Inventario", Accion = "Guardado de pasillo")]
        public JsonResult GuardarPasillo(IFormCollection fc)
        {
            try
            {
                int? id = LeerInt(fc, "id_pasillo");
                string cve = LeerTexto(fc, "cve_pasillo", "").Trim();

                if (string.IsNullOrWhiteSpace(cve))
                    return Json(new { ok = false, msg = "El pasillo necesita una clave." });

                if (id.HasValue)
                {
                    RunUpdate("UPDATE catpasillos SET cve_pasillo = @cve WHERE id_pasillo = @id;",
                        new Dictionary<string, object> { { "cve", cve }, { "id", id.Value } });

                    return Json(new { ok = true, id = id.Value });
                }

                int almacenId = LeerInt(fc, "almacen_id")
                    ?? throw new Exception("Falta el almacén del pasillo.");

                int num = SiguienteNumero(
                    "SELECT COALESCE(MAX(num_pasillo), 0) FROM catpasillos WHERE almacen_id = @id;",
                    new Dictionary<string, object> { { "id", almacenId } });

                var nuevo = RunScalar(@"
                    INSERT INTO catpasillos (cve_pasillo, almacen_id, num_pasillo)
                    VALUES (@cve, @almacen_id, @num)
                    RETURNING id_pasillo;",
                    new Dictionary<string, object>
                    {
                        { "cve", cve }, { "almacen_id", almacenId }, { "num", num }
                    });

                return Json(new { ok = true, id = Convert.ToInt32(nuevo) });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, msg = ex.Message });
            }
        }

        /// <summary>POST /AdministracionInventario/GuardarRack — upsert de un rack, sin tocar sus columnas.</summary>
        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Inventario", Accion = "Guardado de rack")]
        public JsonResult GuardarRack(IFormCollection fc)
        {
            try
            {
                int? id = LeerInt(fc, "id_rack");
                string nombre = LeerTexto(fc, "nombre", "").Trim();
                string tipo = LeerTexto(fc, "tipo", "Rack") == "Estante" ? "Estante" : "Rack";
                string lado = LeerTexto(fc, "lado", "I") == "D" ? "D" : "I";
                int? pasilloId = LeerInt(fc, "pasillo_id");

                if (string.IsNullOrWhiteSpace(nombre))
                    return Json(new { ok = false, msg = "El rack necesita un identificador." });

                object pasilloParam = pasilloId.HasValue ? (object)pasilloId.Value : DBNull.Value;

                if (id.HasValue)
                {
                    RunUpdate(@"
                        UPDATE catracks
                           SET nombre = @nombre, tipo = @tipo, lado = @lado, pasillo_id = @pasillo_id
                         WHERE id_rack = @id;",
                        new Dictionary<string, object>
                        {
                            { "nombre", nombre }, { "tipo", tipo }, { "lado", lado },
                            { "pasillo_id", pasilloParam }, { "id", id.Value }
                        });

                    return Json(new { ok = true, id = id.Value });
                }

                int almacenId = LeerInt(fc, "almacen_id")
                    ?? throw new Exception("Falta el almacén del rack.");

                int num = LeerInt(fc, "num_rack") ?? SiguienteNumero(
                    "SELECT COALESCE(MAX(num_rack), 0) FROM catracks WHERE almacen_id = @id;",
                    new Dictionary<string, object> { { "id", almacenId } });

                var nuevo = RunScalar(@"
                    INSERT INTO catracks (nombre, almacen_id, pasillo_id, tipo, lado, num_rack)
                    VALUES (@nombre, @almacen_id, @pasillo_id, @tipo, @lado, @num)
                    RETURNING id_rack;",
                    new Dictionary<string, object>
                    {
                        { "nombre", nombre }, { "almacen_id", almacenId },
                        { "pasillo_id", pasilloParam }, { "tipo", tipo },
                        { "lado", lado }, { "num", num }
                    });

                return Json(new { ok = true, id = Convert.ToInt32(nuevo) });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, msg = ex.Message });
            }
        }

        /// <summary>POST /AdministracionInventario/GuardarColumna — upsert de una columna.</summary>
        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Inventario", Accion = "Guardado de columna")]
        public JsonResult GuardarColumna(IFormCollection fc)
        {
            try
            {
                int? id = LeerInt(fc, "id_columna");
                string nombre = LeerTexto(fc, "nombre", "").Trim();

                if (string.IsNullOrWhiteSpace(nombre))
                    return Json(new { ok = false, msg = "La columna necesita un nombre." });

                if (id.HasValue)
                {
                    RunUpdate("UPDATE catcolumnas SET nombre = @nombre WHERE id_columna = @id;",
                        new Dictionary<string, object> { { "nombre", nombre }, { "id", id.Value } });

                    return Json(new { ok = true, id = id.Value });
                }

                int rackId = LeerInt(fc, "rack_id")
                    ?? throw new Exception("Falta el rack de la columna.");

                int num = LeerInt(fc, "num_col") ?? SiguienteNumero(
                    "SELECT COALESCE(MAX(num_col), 0) FROM catcolumnas WHERE rack_id = @id;",
                    new Dictionary<string, object> { { "id", rackId } });

                var nuevo = RunScalar(@"
                    INSERT INTO catcolumnas (rack_id, nombre, num_col)
                    VALUES (@rack_id, @nombre, @num)
                    RETURNING id_columna;",
                    new Dictionary<string, object>
                    {
                        { "rack_id", rackId }, { "nombre", nombre }, { "num", num }
                    });

                return Json(new { ok = true, id = Convert.ToInt32(nuevo) });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, msg = ex.Message });
            }
        }

        /// <summary>
        /// POST /AdministracionInventario/GuardarNivel — upsert de un nivel.
        /// Si no se manda ULocation se calcula aquí con los mismos números que usa el generador.
        /// </summary>
        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Inventario", Accion = "Guardado de nivel")]
        public JsonResult GuardarNivel(IFormCollection fc)
        {
            try
            {
                int? id = LeerInt(fc, "id_nivel");
                string nombre = LeerTexto(fc, "nombre", "").Trim();
                string ulocation = LeerTexto(fc, "ulocation", "").Trim();

                if (string.IsNullOrWhiteSpace(nombre))
                    return Json(new { ok = false, msg = "El nivel necesita un nombre." });

                if (id.HasValue)
                {
                    RunUpdate(@"
                        UPDATE catniveles SET nombre = @nombre, ulocation = @ulocation
                         WHERE id_nivel = @id;",
                        new Dictionary<string, object>
                        {
                            { "nombre", nombre },
                            { "ulocation", string.IsNullOrWhiteSpace(ulocation) ? (object)DBNull.Value : ulocation },
                            { "id", id.Value }
                        });

                    return Json(new { ok = true, id = id.Value, ulocation });
                }

                int columnaId = LeerInt(fc, "columna_id")
                    ?? throw new Exception("Falta la columna del nivel.");

                int num = LeerInt(fc, "num_nivel") ?? SiguienteNumero(
                    "SELECT COALESCE(MAX(num_nivel), 0) FROM catniveles WHERE columna_id = @id;",
                    new Dictionary<string, object> { { "id", columnaId } });

                if (string.IsNullOrWhiteSpace(ulocation))
                    ulocation = ULocationPara(columnaId, num);

                var nuevo = RunScalar(@"
                    INSERT INTO catniveles (columna_id, nombre, ulocation, num_nivel)
                    VALUES (@columna_id, @nombre, @ulocation, @num)
                    RETURNING id_nivel;",
                    new Dictionary<string, object>
                    {
                        { "columna_id", columnaId }, { "nombre", nombre },
                        { "ulocation", string.IsNullOrWhiteSpace(ulocation) ? (object)DBNull.Value : ulocation },
                        { "num", num }
                    });

                return Json(new { ok = true, id = Convert.ToInt32(nuevo), ulocation });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, msg = ex.Message });
            }
        }

        /// <summary>
        /// GET /AdministracionInventario/SugerirULocation?columnaId=X&amp;numNivel=N
        /// La misma ULocation que se guardaría, para poder mostrarla mientras se escribe.
        /// </summary>
        public JsonResult SugerirULocation(int columnaId, int? numNivel = null)
        {
            try
            {
                int num = numNivel ?? SiguienteNumero(
                    "SELECT COALESCE(MAX(num_nivel), 0) FROM catniveles WHERE columna_id = @id;",
                    new Dictionary<string, object> { { "id", columnaId } });

                return Json(new { ok = true, ulocation = ULocationPara(columnaId, num), num_nivel = num });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, msg = ex.Message });
            }
        }

        private string ULocationPara(int columnaId, int numNivel)
        {
            var fila = RunQuery(@"
                SELECT co.num_col, ra.num_rack, ra.tipo, ra.lado, pa.num_pasillo
                  FROM catcolumnas co
                  JOIN catracks    ra ON ra.id_rack    = co.rack_id
                  LEFT JOIN catpasillos pa ON pa.id_pasillo = ra.pasillo_id
                 WHERE co.id_columna = @id;",
                new Dictionary<string, object> { { "id", columnaId } }).FirstOrDefault();

            if (fila == null) return "";

            int numCol = fila["num_col"] != null ? Convert.ToInt32(fila["num_col"]) : 1;
            int numRack = fila["num_rack"] != null ? Convert.ToInt32(fila["num_rack"]) : 1;
            int numPas = fila["num_pasillo"] != null ? Convert.ToInt32(fila["num_pasillo"]) : 1;
            string tipo = fila["tipo"]?.ToString() ?? "Rack";
            string lado = fila["lado"]?.ToString() ?? "I";

            return ConstruirULocation(tipo, numPas, numRack, numNivel, numCol, lado);
        }

        /// <summary>POST /AdministracionInventario/GuardarTarima — upsert de una tarima.</summary>
        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Inventario", Accion = "Guardado de tarima")]
        public JsonResult GuardarTarima(IFormCollection fc)
        {
            try
            {
                int? id = LeerInt(fc, "id_tarima");
                string codigo = LeerTexto(fc, "codigo", "").Trim();

                if (string.IsNullOrWhiteSpace(codigo))
                    return Json(new { ok = false, msg = "La tarima necesita un código." });

                if (id.HasValue)
                {
                    var parametros = new Dictionary<string, object>
                    {
                        { "codigo", codigo }, { "id", id.Value }
                    };

                    int? nivelId = LeerInt(fc, "nivel_id");
                    if (nivelId.HasValue)
                    {
                        parametros.Add("nivel_id", nivelId.Value);
                        RunUpdate("UPDATE cattarimas SET codigo = @codigo, nivel_id = @nivel_id WHERE id_tarima = @id;", parametros);
                    }
                    else
                    {
                        RunUpdate("UPDATE cattarimas SET codigo = @codigo WHERE id_tarima = @id;", parametros);
                    }

                    return Json(new { ok = true, id = id.Value });
                }

                int nivel = LeerInt(fc, "nivel_id")
                    ?? throw new Exception("Falta el nivel de la tarima.");

                var nuevo = RunScalar(@"
                    INSERT INTO cattarimas (codigo, nivel_id, fecha)
                    VALUES (@codigo, @nivel_id, NOW())
                    RETURNING id_tarima;",
                    new Dictionary<string, object> { { "codigo", codigo }, { "nivel_id", nivel } });

                return Json(new { ok = true, id = Convert.ToInt32(nuevo) });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, msg = ex.Message });
            }
        }

        #endregion

        #region Eliminación con validación de dependencias

        /// <summary>
        /// POST /AdministracionInventario/EliminarNodo
        /// Borra un nodo de la jerarquía. Nunca borra en cascada algo que contenga
        /// inventario: si hay tarimas con producto debajo, responde qué lo impide.
        /// </summary>
        [HttpPost, ValidateAntiForgeryToken]
        [AuditAction(Modulo = "Inventario", Accion = "Eliminación de estructura de inventario")]
        public JsonResult EliminarNodo(IFormCollection fc)
        {
            try
            {
                string tipo = LeerTexto(fc, "tipo", "").ToLowerInvariant();
                int id = LeerInt(fc, "id") ?? throw new Exception("Falta el id del nodo.");

                switch (tipo)
                {
                    case "sucursal":
                        {
                            int almacenes = Contar("SELECT COUNT(*) FROM catalmacenes WHERE sucursal_id = @id;", id);
                            if (almacenes > 0)
                                return Bloqueado($"La sucursal tiene {almacenes} almacén(es). Elimínalos primero.");

                            RunUpdate("DELETE FROM catsucursales WHERE id_sucursal = @id;", Par(id));
                            return Json(new { ok = true });
                        }

                    case "almacen":
                        {
                            int racks = Contar("SELECT COUNT(*) FROM catracks WHERE almacen_id = @id;", id);
                            if (racks > 0)
                                return Bloqueado($"El almacén tiene {racks} rack(s). Elimínalos primero.");

                            RunUpdate("DELETE FROM catpasillos WHERE almacen_id = @id;", Par(id));
                            RunUpdate("DELETE FROM catalmacenes WHERE id_almacen = @id;", Par(id));
                            return Json(new { ok = true });
                        }

                    case "pasillo":
                        {
                            int racks = Contar("SELECT COUNT(*) FROM catracks WHERE pasillo_id = @id;", id);
                            if (racks > 0)
                                return Bloqueado($"El pasillo tiene {racks} rack(s) asignado(s). Muévelos o elimínalos primero.");

                            RunUpdate("DELETE FROM catpasillos WHERE id_pasillo = @id;", Par(id));
                            return Json(new { ok = true });
                        }

                    case "rack":
                        {
                            int tarimas = Contar(@"
                                SELECT COUNT(*) FROM cattarimas t
                                 WHERE t.nivel_id IN (
                                       SELECT n.id_nivel FROM catniveles n
                                        WHERE n.columna_id IN (SELECT c.id_columna FROM catcolumnas c WHERE c.rack_id = @id));", id);
                            if (tarimas > 0)
                                return Bloqueado($"El rack tiene {tarimas} tarima(s) ubicada(s). Vacíalo antes de eliminarlo.");

                            RunUpdate(@"
                                DELETE FROM catniveles
                                 WHERE columna_id IN (SELECT id_columna FROM catcolumnas WHERE rack_id = @id);", Par(id));
                            RunUpdate("DELETE FROM catcolumnas WHERE rack_id = @id;", Par(id));
                            RunUpdate("DELETE FROM catracks WHERE id_rack = @id;", Par(id));
                            return Json(new { ok = true });
                        }

                    case "columna":
                        {
                            int tarimas = Contar(@"
                                SELECT COUNT(*) FROM cattarimas t
                                 WHERE t.nivel_id IN (SELECT n.id_nivel FROM catniveles n WHERE n.columna_id = @id);", id);
                            if (tarimas > 0)
                                return Bloqueado($"La columna tiene {tarimas} tarima(s) ubicada(s). Vacíala antes de eliminarla.");

                            RunUpdate("DELETE FROM catniveles WHERE columna_id = @id;", Par(id));
                            RunUpdate("DELETE FROM catcolumnas WHERE id_columna = @id;", Par(id));
                            return Json(new { ok = true });
                        }

                    case "nivel":
                        {
                            int tarimas = Contar("SELECT COUNT(*) FROM cattarimas WHERE nivel_id = @id;", id);
                            if (tarimas > 0)
                                return Bloqueado($"El nivel tiene {tarimas} tarima(s). Muévelas antes de eliminarlo.");

                            RunUpdate("DELETE FROM catniveles WHERE id_nivel = @id;", Par(id));
                            return Json(new { ok = true });
                        }

                    case "tarima":
                        {
                            int productos = Contar("SELECT COUNT(*) FROM tarima_productos WHERE tarima_id = @id;", id);
                            if (productos > 0)
                                return Bloqueado($"La tarima tiene {productos} producto(s). Vacíala antes de eliminarla.");

                            RunUpdate("DELETE FROM cattarimas WHERE id_tarima = @id;", Par(id));
                            return Json(new { ok = true });
                        }

                    default:
                        return Json(new { ok = false, msg = $"Tipo de nodo desconocido: {tipo}" });
                }
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, msg = ex.Message });
            }
        }

        private JsonResult Bloqueado(string motivo) =>
            Json(new { ok = false, bloqueado = true, msg = motivo });

        private static Dictionary<string, object> Par(int id) =>
            new Dictionary<string, object> { { "id", id } };

        private int Contar(string query, int id) =>
            Convert.ToInt32(RunScalar(query, Par(id)) ?? 0);

        #endregion

        #region Helpers de lectura del form

        private static int? LeerInt(IFormCollection fc, string clave)
        {
            var raw = fc[clave].ToString();
            if (string.IsNullOrWhiteSpace(raw)) return null;
            return int.TryParse(raw, out var valor) ? valor : (int?)null;
        }

        private static string LeerTexto(IFormCollection fc, string clave, string porDefecto)
        {
            var raw = fc[clave].ToString();
            return string.IsNullOrWhiteSpace(raw) ? porDefecto : raw;
        }

        #endregion

        #region Modelo del plan

        private class PlanEstructura
        {
            public List<RackPlan> Racks { get; } = new List<RackPlan>();
            public List<string> Avisos { get; } = new List<string>();
        }

        private class RackPlan
        {
            public int? IdRack { get; set; }
            public bool EsNuevo { get; set; }
            public string Nombre { get; set; }
            public int NumRack { get; set; }
            public string Tipo { get; set; }
            public string Lado { get; set; }
            public List<ColumnaPlan> Columnas { get; } = new List<ColumnaPlan>();
        }

        private class ColumnaPlan
        {
            public string Nombre { get; set; }
            public int NumCol { get; set; }
            public List<NivelPlan> Niveles { get; } = new List<NivelPlan>();
        }

        private class NivelPlan
        {
            public string Nombre { get; set; }
            public int NumNivel { get; set; }
            public string ULocation { get; set; }
        }

        #endregion
    }
}

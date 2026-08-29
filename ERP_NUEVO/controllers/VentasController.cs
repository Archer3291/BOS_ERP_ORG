using BOS_ERP.Models;
using System.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace BOS_ERP.Controllers
{
    public partial class VentasController : Utilities
    {
        public IActionResult Nacional()
        {
            var parameters = new Dictionary<string, object>();
            string query = "SELECT  em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || " +
                "LPAD((COALESCE(NULLIF(em.fol_doc, '')::integer, 0) + 1)::text, 7, '0') || " +
                "CASE  " +
                "        WHEN em.variacion > 0   " +
                "        THEN '-' || num_to_letters(em.variacion)   " +
                "        ELSE ''  " +
                "    END AS folio " +
                "FROM encabezadomov em   " +
                "WHERE nat = 'VNPED' AND em.suc = @suc " +
                "ORDER BY folio DESC;";
            parameters.Add("suc", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));
            var folioPedido = RunScalar(query, parameters);

            query = "SELECT  em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || " +
                "LPAD((COALESCE(NULLIF(em.fol_doc, '')::integer, 0) + 1)::text, 7, '0') || " +
                "CASE  " +
                "        WHEN em.variacion > 0   " +
                "        THEN '-' || num_to_letters(em.variacion)   " +
                "        ELSE ''  " +
                "    END AS folio " +
                "FROM encabezadomov em   " +
                "WHERE nat = 'VNREM' AND em.suc = @suc " +
                "ORDER BY folio DESC;";
            var folioRemision = RunScalar(query, parameters);

            query = "SELECT  em.nat || '-' || " +
                "(COALESCE(NULLIF(em.fol_doc, '')::integer, 0) + 1)::text || " +
                "CASE  " +
                "        WHEN em.variacion > 0   " +
                "        THEN '-' || num_to_letters(em.variacion)   " +
                "        ELSE ''  " +
                "    END AS folio " +
                "FROM encabezadomov em   " +
                "WHERE nat = 'VNFAC' AND em.suc = @suc " +
                "ORDER BY folio DESC;";
            var folioFactura = RunScalar(query, parameters);


            var modelo = new FolioInfoGroup
            {
                ParcialA = new FolioInfo
                {
                    Folio = folioPedido?.ToString() ?? "VN-VNPED-2025-0000001",
                    Sucursal = "Matriz",
                    Almacen = "A1"
                },
                ParcialB = new FolioInfo
                {
                    Folio = folioRemision?.ToString() ?? "VN-VNREM-2025-0000001",
                    Sucursal = "Sucursal Norte",
                    Almacen = "A2"
                },
                ParcialC = new FolioInfo
                {
                    Folio = folioFactura?.ToString() ?? "VNFAC-1",
                    Sucursal = "Sucursal Sur",
                    Almacen = "A3"
                }

            };

            return View(modelo);
        }
        public IActionResult Internacionales()
        {
            var parameters = new Dictionary<string, object>();
            string query = "SELECT  em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || " +
                "LPAD((COALESCE(NULLIF(em.fol_doc, '')::integer, 0) + 1)::text, 7, '0') || " +
                "CASE  " +
                "        WHEN em.variacion > 0   " +
                "        THEN '-' || num_to_letters(em.variacion)   " +
                "        ELSE ''  " +
                "    END AS folio " +
                "FROM encabezadomov em   " +
                "WHERE nat = 'VINPED' " +
                "ORDER BY folio DESC;";
            var folioPedido = RunScalar(query, parameters);

            query = "SELECT  em.gen || '-' || em.nat || '-' || EXTRACT(YEAR FROM em.fch)::text || '-' || " +
                "LPAD((COALESCE(NULLIF(em.fol_doc, '')::integer, 0) + 1)::text, 7, '0') || " +
                "CASE  " +
                "        WHEN em.variacion > 0   " +
                "        THEN '-' || num_to_letters(em.variacion)   " +
                "        ELSE ''  " +
                "    END AS folio " +
                "FROM encabezadomov em   " +
                "WHERE nat = 'VINREM' " +
                "ORDER BY folio DESC;";
            var folioRemision = RunScalar(query, parameters);

            query = "SELECT  em.nat || '-' || " +
                           "(COALESCE(NULLIF(em.fol_doc, '')::integer, 0) + 1)::text || " +
                           "CASE  " +
                           "        WHEN em.variacion > 0   " +
                           "        THEN '-' || num_to_letters(em.variacion)   " +
                           "        ELSE ''  " +
                           "    END AS folio " +
                           "FROM encabezadomov em   " +
                           "WHERE nat = 'VINFAC' " +
                           "ORDER BY (COALESCE(NULLIF(em.fol_doc, '')::integer, 0) + 1) DESC;";


            var folioFactura = RunScalar(query, parameters);


            var modelo = new FolioInfoGroup
            {
                ParcialA = new FolioInfo
                {
                    Folio = folioPedido?.ToString() ?? "VN-VINPED-2025-0000001",
                    Sucursal = "Matriz",
                    Almacen = "A1"
                },
                ParcialB = new FolioInfo
                {
                    Folio = folioRemision?.ToString() ?? "VN-VINREM-2025-0000001",
                    Sucursal = "Sucursal Norte",
                    Almacen = "A2"
                },
                ParcialC = new FolioInfo
                {
                    Folio = folioFactura?.ToString() ?? "VN-VINFAC-2025-0000001",
                    Sucursal = "Sucursal Sur",
                    Almacen = "A3"
                }

            };

            return View(modelo);
        }
        public IActionResult Industrial()
        {
            var parameters = new Dictionary<string, object>();
            string query = "SELECT  em.gen || '-' || em.nat || '-' || TO_CHAR(em.fch, 'YY') || '-' || " +
                "(COALESCE(NULLIF(em.fol_doc, '')::integer, 0) + 1)::text || " +
                "CASE  " +
                "        WHEN em.variacion > 0   " +
                "        THEN '-' || num_to_letters(em.variacion)   " +
                "        ELSE ''  " +
                "    END AS folio " +
                "FROM encabezadomov em   " +
                "WHERE nat = 'VIPED' " +
                "ORDER BY (COALESCE(NULLIF(em.fol_doc, '')::integer, 0) + 1) DESC;";
            var folioPedido = RunScalar(query, parameters);

            query = "SELECT  em.gen || '-' || em.nat || '-' || TO_CHAR(em.fch, 'YY') || '-' || " +
                "(COALESCE(NULLIF(em.fol_doc, '')::integer, 0) + 1)::text || " +
                "CASE  " +
                "        WHEN em.variacion > 0   " +
                "        THEN '-' || num_to_letters(em.variacion)   " +
                "        ELSE ''  " +
                "    END AS folio " +
                "FROM encabezadomov em   " +
                "WHERE nat = 'VIREM' " +
                "ORDER BY (COALESCE(NULLIF(em.fol_doc, '')::integer, 0) + 1) DESC;";
            var folioRemision = RunScalar(query, parameters);

            query = "SELECT  em.nat || '-' || " +
                "(COALESCE(NULLIF(em.fol_doc, '')::integer, 0) + 1)::text || " +
                "CASE  " +
                "        WHEN em.variacion > 0   " +
                "        THEN '-' || num_to_letters(em.variacion)   " +
                "        ELSE ''  " +
                "    END AS folio " +
                "FROM encabezadomov em   " +
                "WHERE nat = 'VIFAC' " +
                "ORDER BY (COALESCE(NULLIF(em.fol_doc, '')::integer, 0) + 1) DESC;";
            var folioFactura = RunScalar(query, parameters);


            var modelo = new FolioInfoGroup
            {
                ParcialA = new FolioInfo
                {
                    Folio = folioPedido?.ToString() ?? "VIND-VIPED-25-0000001",
                    Sucursal = "Matriz",
                    Almacen = "A1"
                },
                ParcialB = new FolioInfo
                {
                    Folio = folioRemision?.ToString() ?? "VIND-VIREM-25-0000001",
                    Sucursal = "Sucursal Norte",
                    Almacen = "A2"
                },
                ParcialC = new FolioInfo
                {
                    Folio = folioFactura?.ToString() ?? "VIND-VIFAC-25-0000001",
                    Sucursal = "Sucursal Sur",
                    Almacen = "A3"
                }

            };

            return View(modelo);
        }
        public IActionResult Sucursales()
        {
            var parameters = new Dictionary<string, object>();
            string query = "SELECT em.gen || '-' || em.nat || '-' || to_char(em.fch, 'YY') || '-' || " +
                "    LPAD((COALESCE(NULLIF(em.fol_doc, '')::integer, 0) + 1)::text, 7, '0') || " +
                "    CASE  " +
                "        WHEN em.variacion > 0   " +
                "        THEN '-' || num_to_letters(em.variacion)   " +
                "        ELSE ''  " +
                "    END AS folio " +
                "FROM encabezadomov em   " +
                "WHERE nat = 'VSPED' " +
                "ORDER BY folio DESC;";
            var folioPedido = RunScalar(query, parameters);

            query = "SELECT em.gen || '-' || em.nat || '-' || to_char(em.fch, 'YY') || '-' || " +
                "    LPAD((COALESCE(NULLIF(em.fol_doc, '')::integer, 0) + 1)::text, 7, '0') || " +
                "    CASE  " +
                "        WHEN em.variacion > 0   " +
                "        THEN '-' || num_to_letters(em.variacion)   " +
                "        ELSE ''  " +
                "    END AS folio " +
                "FROM encabezadomov em   " +
                "WHERE nat = 'VSREM' " +
                "ORDER BY folio DESC;";
            var folioRemision = RunScalar(query, parameters);

            query = "SELECT em.gen || '-' || em.nat || '-' || to_char(em.fch, 'YY') || '-' || " +
                "    LPAD((COALESCE(NULLIF(em.fol_doc, '')::integer, 0) + 1)::text, 7, '0') || " +
                "    CASE  " +
                "        WHEN em.variacion > 0   " +
                "        THEN '-' || num_to_letters(em.variacion)   " +
                "        ELSE ''  " +
                "    END AS folio " +
                "FROM encabezadomov em   " +
                "WHERE nat = 'VSFAC' " +
                "ORDER BY folio DESC;";
            var folioFactura = RunScalar(query, parameters);


            var modelo = new FolioInfoGroup
            {
                ParcialA = new FolioInfo
                {
                    Folio = folioPedido?.ToString() ?? "VSUC-VSPED-2025-0000001",
                    Sucursal = "Matriz",
                    Almacen = "A1"
                },
                ParcialB = new FolioInfo
                {
                    Folio = folioRemision?.ToString() ?? "VSUC-VSREM-2025-0000001",
                    Sucursal = "Sucursal Norte",
                    Almacen = "A2"
                },
                ParcialC = new FolioInfo
                {
                    Folio = folioFactura?.ToString() ?? "VSUC-VSFAC-2025-0000001",
                    Sucursal = "Sucursal Sur",
                    Almacen = "A3"
                }

            };

            return View(modelo);
        }

        public IActionResult PackingList()
        {
            return View();
        }

        public IActionResult PuntoDeVenta()
        {
            var parameters = new Dictionary<string, object>();
            string query = "SELECT COUNT(*) " +
                "FROM encabezadomov " +
                "WHERE nat = 'ACAJA' " +
                "  AND fch >= CURRENT_DATE " +
                "  AND fch < CURRENT_DATE + INTERVAL '1 day' " +
                "  AND suc = @suc;";
            parameters.Add("suc", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));
            int total = Convert.ToInt32(RunScalar(query, parameters));
            ViewBag.ProcesoApertura = total;
            ViewBag.TieneApertura = total > 0; // Para usar más fácil en JavaScript
            return View();
        }

        public IActionResult BitacoraTickets()
        {
            return View();
        }
        public IActionResult AperturaCaja()
        {
            return View();
        }
        public IActionResult CierreCaja()
        {
            var parameters = new Dictionary<string, object>();
            string query = "SELECT COUNT(*) " +
                "FROM encabezadomov " +
                "WHERE tipo_proceso = 'factura_global' " +
                "  AND fch >= CURRENT_DATE " +
                "  AND fch < CURRENT_DATE + INTERVAL '1 day' " +
                "  AND suc = @suc;";
            parameters.Add("suc", Convert.ToInt32(HttpContext.Session.GetInt32("Sucursal")));
            int total = Convert.ToInt32(RunScalar(query, parameters));
            ViewBag.procesoCiere = total;
            return View();
        }
        public IActionResult Cancelacion()
        {
            return View();
        }

        // Mercancía vendida que aún no se entrega. Los datos ya existían en
        // ventas_pendientes pero no había ninguna pantalla que los mostrara.
        public IActionResult PendientesEntrega()
        {
            return View();
        }
        public IActionResult RetiroEfectivo()
        {
            return View();
        }

        // Configuración de las cuentas de banco que puede usar cada forma de pago del
        // punto de venta. Los datos los sirve PuntoVentaAdministracionController
        // (PVAdminFormasPagoBancos.cs); aquí sólo se pinta la pantalla, igual que las
        // demás del módulo administrativo.
        public IActionResult FormasPagoBancos()
        {
            return View();
        }

        public IActionResult NotasCredito()
        {
            var parameters = new Dictionary<string, object>();
            string query = "SELECT  em.nat || '-' || " +
                "(COALESCE(NULLIF(em.fol_doc, '')::integer, 0) + 1)::text || " +
                "CASE  " +
                "        WHEN em.variacion > 0   " +
                "        THEN '-' || num_to_letters(em.variacion)   " +
                "        ELSE ''  " +
                "    END AS folio " +
                "FROM encabezadomov em   " +
                "WHERE nat = 'NT' " +
                "ORDER BY (COALESCE(NULLIF(em.fol_doc, '')::integer, 0) + 1) DESC;";
            var folioFactura = RunScalar(query, parameters);

            var modelo  = new FolioInfo
                {
                    Folio = folioFactura?.ToString() ?? "AF-NT-26-1",
                    Sucursal = "Sucursal Sur",
            };

            return View(modelo);
        }
        public IActionResult ConsultaNotasCredito()
        {
            return View();
        }
        public IActionResult SolicitudNC()
        {
            return View();
        }

        public IActionResult SolicitudRegresoMaterial()
        {
            return View();
        }

        [HttpGet]
        public IActionResult PedidosPendientes()
        {
            string query = @"
        SELECT  ac.id, ac.pedido_id, ac.cliente_id,
                cc.n_cli,
                em.folio,
                ac.credito_limite, ac.credito_usado,
                ac.monto_pedido,   ac.excedente,
                ac.fecha_solicitud, ac.solicitado_por, ac.token
        FROM    autorizaciones_credito ac
        JOIN    catclientes    cc ON cc.id_cliente   = ac.cliente_id
        JOIN    encabezadomov em ON em.id_encabezado = ac.pedido_id
        WHERE   ac.estatus = 'pendiente'
        ORDER BY ac.fecha_solicitud DESC";

            var lista = new List<SolicitudCreditoVM>();
            var dt = RunQuery(query); // devuelve DataTable — adapta a tu helper

            foreach (var row in dt)
            {
                lista.Add(new SolicitudCreditoVM
                {
                    Id = Convert.ToInt32(row["id"]),
                    PedidoId = Convert.ToInt32(row["pedido_id"]),
                    ClienteId = Convert.ToInt32(row["cliente_id"]),
                    NombreCliente = row["n_cli"].ToString(),
                    Folio = row["folio"].ToString(),
                    CreditoLimite = Convert.ToDecimal(row["credito_limite"]),
                    CreditoUsado = Convert.ToDecimal(row["credito_usado"]),
                    MontoPedido = Convert.ToDecimal(row["monto_pedido"]),
                    Excedente = Convert.ToDecimal(row["excedente"]),
                    FechaSolicitud = Convert.ToDateTime(row["fecha_solicitud"]),
                    SolicitadoPor = Convert.ToInt32(row["solicitado_por"]),
                    Token = row["token"].ToString()
                });
            }

            return View(lista);
        }

        public IActionResult AprobacionAperturas()
        {
            return View();
        }

        /// <summary>
        /// Entrada histórica al portal de autofacturación. Se conserva sólo como redirección
        /// para no romper enlaces ya repartidos (menú, correos, tickets impresos).
        ///
        /// Antes servía la vista por su cuenta y generaba su propio token, guardando la
        /// expiración como fecha formateada mientras AutoFacturacionPublicaController la
        /// guarda en ticks. Como ambas escriben la MISMA clave de sesión, entrar por aquí
        /// dejaba "AF_TokenExp" con un valor que long.TryParse no podía leer, y a partir de
        /// ese momento toda validación de token fallaba: el portal respondía "Sesión
        /// inválida" sin importar lo que se buscara.
        /// El dueño del token es ahora AutoFacturacionPublicaController, y sólo él.
        /// </summary>
        [AllowAnonymous]
        public IActionResult AutoFacturacion(string folio = null)
        {
            return RedirectToAction("Index", "AutoFacturacionPublica",
                string.IsNullOrWhiteSpace(folio) ? null : new { folio });
        }
        public IActionResult Refacturacion()
        {
            return View();
        }

    }

}

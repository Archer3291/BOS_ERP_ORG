using BOS_ERP.Controllers;
using BOS_ERP.Models;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Data;

namespace BOS_ERP.services.Facturacion
{
    public interface IFacturaRepository
    {
        int GuardarEncabezado(Factura factura, string? tipoOverride = null,
            NpgsqlConnection? conn = null, NpgsqlTransaction? tx = null);
        int GuardarEncabezadoConDetalles(Factura factura, string? tipoOverride = null,
            NpgsqlConnection? conn = null, NpgsqlTransaction? tx = null);
        int GuardarNotaCredito(Factura notaCredito);
        void GuardarDetalle(int idFactura, DataRow row,
            Dictionary<string, object>? columnasExtra = null,
            NpgsqlConnection? conn = null, NpgsqlTransaction? tx = null);
    }

    public class FacturaRepository : Utilities, IFacturaRepository
    {
        public int GuardarEncabezadoConDetalles(Factura factura, string? tipoOverride = null,
            NpgsqlConnection? conn = null, NpgsqlTransaction? tx = null)
        {
            ArgumentNullException.ThrowIfNull(factura);
            int idFactura = GuardarEncabezado(factura, tipoOverride, conn, tx);
            if (idFactura <= 0)
                throw new InvalidOperationException("No fue posible obtener el identificador de la factura guardada.");

            if (factura.Tproductos != null)
            {
                foreach (DataRow row in factura.Tproductos.Rows)
                    GuardarDetalle(idFactura, row, null, conn, tx);
            }
            return idFactura;
        }

        public int GuardarEncabezado(Factura factura, string? tipoOverride = null,
            NpgsqlConnection? conn = null, NpgsqlTransaction? tx = null)
        {
            int id = 0;

            string strSQL = "INSERT INTO factura ( " +
                "   serie, folio, idtipofactura, idcliente, rfccliente, rsocliente, emlcliente, idemisor, rfcemisor, " +
                "   rsoemisor, idexpedicion, idusuario, fecha, fechatimbrado, statusfactura, mdpfactura, textfactura, " +
                "   idlugarexp, idtipopago, uuid, importe, descuento, subtotal, iva, total, saldo, idpedido, " +
                "   retisr, retiva, moneda, observaciones, idvendedor, usocfdi, idusocfdi, cbb, parcialidad, " +
                "   sellosat, sellocfdi, cadenaoriginal, oc, tdc, anticipo, reg_fisr, reg_fise, cpr, cpe, tipo, encabezado_id) " +
                "VALUES ( " +
                "   @serie, @folio, @idtipofactura, @idcliente, @rfccliente, @rsocliente, @emlcliente, @idemisor, @rfcemisor, " +
                "   @rsoemisor, @idexpedicion, @idusuario, @fecha, @fechatimbrado, @statusfactura, @mdpfactura, @xmlfactura, " +
                "   @idlugarexp, @idtipopago, @uuid, @importe, @descuento, @subtotal, @iva, @total, @saldo, @idpedido, " +
                "   @retisr, @retiva, @moneda, @observaciones, @idvendedor, @usocfdi, @idusocfdi, @cbb, @parcialidad, " +
                "   @sellosat, @sellocfdi, @cadenaoriginal, @oc, @tdc, @anticipo, @reg_fisr, @reg_fise, @cpr, @cpe, @tipo, @encabezado_id) " +
                "RETURNING id";

            var parameters = new Dictionary<string, object>
            {
                ["serie"] = factura.Serie,
                ["folio"] = factura.Folio,
                ["idtipofactura"] = factura.IdTipoFactura,
                ["idcliente"] = factura.IdCliente,
                ["rfccliente"] = factura.RfcCliente,
                ["rsocliente"] = factura.RsoCliente,
                ["emlcliente"] = factura.EmlCliente,
                ["idemisor"] = factura.IdEmisor,
                ["rfcemisor"] = factura.RfcEmisor,
                ["rsoemisor"] = factura.RsoEmisor,
                ["idexpedicion"] = factura.IdExpedicion,
                ["idusuario"] = factura.IdUsuario,
                ["fecha"] = factura.Fecha,
                ["fechatimbrado"] = string.IsNullOrEmpty(factura.FechaTimbrado) ? factura.Fecha : DateTime.Parse(factura.FechaTimbrado),
                ["statusfactura"] = factura.StatusFactura,
                ["mdpfactura"] = factura.MdpFactura,
                ["xmlfactura"] = factura.XmlFactura,
                ["idlugarexp"] = factura.IdLugarExp,
                ["idtipopago"] = Convert.ToInt32(factura.IdTipoPago),
                ["uuid"] = string.IsNullOrWhiteSpace(factura.UUID) ? Guid.NewGuid() : Guid.Parse(factura.UUID),
                ["importe"] = factura.Importe,
                ["descuento"] = factura.Descuento,
                ["subtotal"] = factura.Subtotal,
                ["iva"] = factura.IVA,
                ["total"] = factura.Total,
                ["saldo"] = factura.Saldo,
                ["idpedido"] = factura.IdPedido,
                ["retisr"] = factura.RetISR,
                ["retiva"] = factura.RetIVA,
                ["moneda"] = factura.Moneda,
                ["observaciones"] = factura.Observaciones,
                ["idvendedor"] = factura.IdVendedor,
                ["usocfdi"] = factura.UsoCFDI,
                ["idusocfdi"] = factura.IdUsoCFDI,
                ["cbb"] = factura.Cbb,
                ["parcialidad"] = 0,
                ["sellosat"] = factura.SelloSAT,
                ["sellocfdi"] = factura.SelloCFDI,
                ["cadenaoriginal"] = factura.CadenaOriginal,
                ["oc"] = factura.Oc,
                ["tdc"] = factura.Tdc,
                ["anticipo"] = factura.Anticipo ? 1m : 0m,
                // El modelo tiene dos pares para lo mismo: Rege/Regc son los que arman el
                // CFDI y RegFisE/RegFisR los que se persisten, y nadie los sincroniza. Los
                // controladores llenan sólo el primer par, así que estas dos columnas se
                // guardaban vacías en TODAS las facturas del sistema y el régimen fiscal no
                // aparecía en ninguna reimpresión. Se cae al par que sí viene lleno.
                ["reg_fisr"] = string.IsNullOrWhiteSpace(factura.RegFisR) ? factura.Regc : factura.RegFisR,
                ["reg_fise"] = string.IsNullOrWhiteSpace(factura.RegFisE) ? factura.Rege : factura.RegFisE,
                ["cpr"] = factura.CpR,
                ["cpe"] = factura.CpE,
                ["encabezado_id"] = factura.EncabezadoId,
                ["tipo"] = (tipoOverride ?? factura.TipoFacturacion).ToUpper(),
            };

            var result = RunQuery(strSQL, parameters, false, conn, tx);
            return result?.Count > 0 ? Convert.ToInt32(result[0]["id"]) : 0;
        }

        public void GuardarDetalle(int idFactura, DataRow row,
            Dictionary<string, object>? columnasExtra = null,
            NpgsqlConnection? conn = null, NpgsqlTransaction? tx = null)
        {
            // columnas base que TODOS los controllers insertan
            var columnas = new Dictionary<string, object>
            {
                ["idfac"] = idFactura,
                ["idproducto"] = Valor(row, 1, "prod_id", "idproducto"),
                ["descripcion"] = Valor(row, string.Empty, "descripcion"),
                ["cantidad"] = Valor(row, 0, "cantidad"),
                ["precio"] = Valor(row, 0m, "precioUnit", "precio"),
                ["cpr"] = 0,
                ["descuento"] = Valor(row, 0, "descuento"),
                ["saldo"] = 0,
                ["udm"] = Valor(row, string.Empty, "unidad", "claveUnidad"),
                ["claveprodserv"] = Valor(row, string.Empty, "claveProdServ", "articulo"),
                ["claveprod"] = Valor(row, string.Empty, "numero", "claveprod"),
                ["idndv"] = 0,
                ["lote"] = "-",
                ["pedimento"] = Valor(row, "-", "pedimento"),
                ["cant_ndc"] = 0,
                ["comentario"] = Valor(row, string.Empty, "comentario")
            };

            // cada controller agrega SOLO lo que le corresponde
            if (columnasExtra != null)
                foreach (var kv in columnasExtra)
                    columnas[kv.Key] = kv.Value;

            string cols = string.Join(", ", columnas.Keys);
            string vals = string.Join(", ", columnas.Keys.Select(k => "@" + k));
            RunQuery($"INSERT INTO dfactura ({cols}) VALUES ({vals});", columnas, false, conn, tx);
        }

        public int GuardarNotaCredito(Factura nc)
        {
            int id = 0;

            // ── Encabezado de la NC → tabla factura ──────────────────────────────
            string sql = @"
                INSERT INTO factura
                    (serie, folio, idtipofactura, idcliente, rfccliente, rsocliente, emlcliente,
                    idemisor, rfcemisor, rsoemisor, idexpedicion, idusuario,
                    fecha, fechatimbrado, statusfactura, mdpfactura, textfactura,
                    idlugarexp, idtipopago, uuid,
                    importe, descuento, subtotal, iva, total, saldo,
                    idpedido, retisr, retiva, moneda, observaciones, idvendedor,
                    usocfdi, idusocfdi, cbb, parcialidad,
                    sellosat, sellocfdi, cadenaoriginal,
                    oc, tdc, anticipo,
                    reg_fisr, reg_fise, cpr, cpe,
                    tipo, encabezado_id)
                VALUES
                    (@serie, @folio, @idtipofactura, @idcliente, @rfccliente, @rsocliente, @emlcliente,
                    @idemisor, @rfcemisor, @rsoemisor, @idexpedicion, @idusuario,
                    @fecha, @fechatimbrado, @statusfactura, @mdpfactura, @xmlfactura,
                    @idlugarexp, @idtipopago, @uuid,
                    @importe, @descuento, @subtotal, @iva, @total, @saldo,
                    @idpedido, @retisr, @retiva, @moneda, @observaciones, @idvendedor,
                    @usocfdi, @idusocfdi, @cbb, @parcialidad,
                    @sellosat, @sellocfdi, @cadenaoriginal,
                    @oc, @tdc, @anticipo,
                    @reg_fisr, @reg_fise, @cpr, @cpe,
                    @tipo, @encabezado_id)
                RETURNING id;";

            var parms = new Dictionary<string, object>
            {
                ["serie"] = nc.Serie,
                ["folio"] = nc.FolioCorto,           // NC usa FolioCorto
                ["idtipofactura"] = nc.IdTipoFactura,
                ["idcliente"] = nc.IdCliente,
                ["rfccliente"] = nc.RfcCliente,
                ["rsocliente"] = nc.RsoCliente,
                ["emlcliente"] = nc.EmlCliente ?? "",
                ["idemisor"] = nc.IdEmisor,
                ["rfcemisor"] = nc.RfcEmisor,
                ["rsoemisor"] = nc.RsoEmisor,
                ["idexpedicion"] = nc.IdExpedicion,
                ["idusuario"] = nc.IdUsuario,
                ["fecha"] = nc.Fecha,
                ["fechatimbrado"] = string.IsNullOrEmpty(nc.FechaTimbrado)
                                          ? (object)DateTime.Now
                                          : DateTime.Parse(nc.FechaTimbrado),
                ["statusfactura"] = "TIMBRADA",
                ["mdpfactura"] = nc.metodoPagoTexto,
                ["xmlfactura"] = nc.XmlFactura,
                ["idlugarexp"] = nc.IdLugarExp,
                ["idtipopago"] = Convert.ToInt32(nc.IdTipoPago),
                ["uuid"] = string.IsNullOrWhiteSpace(nc.UUID)
                                          ? Guid.NewGuid()
                                          : Guid.Parse(nc.UUID),
                ["importe"] = nc.Subtotal,             // importe ≈ subtotal antes de IVA
                ["descuento"] = nc.Descuento,
                ["subtotal"] = nc.Subtotal,
                ["iva"] = nc.IVA,
                ["total"] = nc.Total,
                ["saldo"] = nc.Saldo,
                ["idpedido"] = nc.IdPedido,
                ["retisr"] = nc.RetISR,
                ["retiva"] = nc.RetIVA,
                ["moneda"] = nc.Moneda,
                ["observaciones"] = nc.Observaciones ?? "",
                ["idvendedor"] = nc.IdVendedor,
                ["usocfdi"] = nc.UsoCFDI,
                ["idusocfdi"] = nc.IdUsoCFDI,
                ["cbb"] = nc.Cbb ?? "",
                ["parcialidad"] = 0,
                ["sellosat"] = nc.SelloSAT ?? "",
                ["sellocfdi"] = nc.SelloCFDI ?? "",
                ["cadenaoriginal"] = nc.CadenaOriginal ?? "",
                ["oc"] = nc.Oc ?? "",   // ticket
                ["tdc"] = nc.Tdc,
                ["anticipo"] = 0m,
                // Estaban cruzados: reg_fisr (receptor) recibía Rege (emisor) y viceversa,
                // así que toda nota de crédito guardaba los dos regímenes intercambiados.
                // Rege = emisor → reg_fise;  Regc = receptor → reg_fisr.
                ["reg_fisr"] = string.IsNullOrWhiteSpace(nc.RegFisR) ? nc.Regc : nc.RegFisR,
                ["reg_fise"] = string.IsNullOrWhiteSpace(nc.RegFisE) ? nc.Rege : nc.RegFisE,
                ["cpr"] = nc.CpR,
                ["cpe"] = nc.LugarExpedicion ?? nc.CpE,
                // ▼ CLAVE: distingue NC del resto de facturas
                ["tipo"] = nc.TipoFacturacion.ToUpper(),  // "DEVOLUCION"|"DESCUENTO"|etc.
                ["encabezado_id"] = nc.EncabezadoId,
            };

            var dbResult = RunQuery(sql, parms);
            if (dbResult != null && dbResult.Count > 0)
                id = Convert.ToInt32(dbResult[0]["id"]);

            // ── Partidas → tabla dfactura ─────────────────────────────────────────
            foreach (DataRow row in nc.Tproductos.Rows)
            {
                decimal cant = Convert.ToDecimal(row["cantidad"]);
                decimal precio = Convert.ToDecimal(row["precioUnit"]);
                decimal descPct = Convert.ToDecimal(row["descuento"]);
                decimal ivaPct = Convert.ToDecimal(row["iva"]);
                decimal iepsPct = Convert.ToDecimal(row["ieps"]);

                decimal importe = Math.Round(cant * precio, 6);
                decimal desc = Math.Round(importe * descPct / 100m, 6);
                decimal baseGrv = importe - desc;
                decimal ivaImp = Math.Round(baseGrv * ivaPct / 100m, 6);
                decimal iepsImp = Math.Round(baseGrv * iepsPct / 100m, 6);
                decimal subtot = baseGrv + ivaImp + iepsImp;

                RunQuery(@"
INSERT INTO dfactura
    (idfac, idproducto, descripcion, cantidad, precio, cpr,
     descuento, saldo, udm, claveprodserv, claveprod,
     idndv, lote, pedimento, cant_ndc, comentario)
VALUES
    (@idfac, @idproducto, @descripcion, @cantidad, @precio, @cpr,
     @descuento, @saldo, @udm, @claveprodserv, @claveprod,
     @idndv, @lote, @pedimento, @cant_ndc, @comentario);",
                    new Dictionary<string, object>
                    {
                        ["idfac"] = id,
                        ["idproducto"] = 1,
                        ["descripcion"] = row["descripcion"],
                        ["cantidad"] = cant,
                        ["precio"] = precio,
                        ["cpr"] = 0,
                        ["descuento"] = desc,       // importe del descuento, no el %
                        ["saldo"] = subtot,     // total de la partida
                        ["udm"] = row["unidad"],
                        ["claveprodserv"] = row["claveProdServ"],
                        ["claveprod"] = row["numero"],
                        ["idndv"] = 0,
                        ["lote"] = "-",
                        ["pedimento"] = "-",
                        ["cant_ndc"] = 0,
                        ["comentario"] = row.Table.Columns.Contains("comentario")
                                               ? (row["comentario"]?.ToString() ?? "")
                                               : "",
                    });
            }



            // Después de obtener el id del INSERT de factura
            if (!string.IsNullOrWhiteSpace(nc.UUIDsRelacionados))
            {
                // Obtener los IDs internos de las facturas origen por UUID
                var uuids = nc.UUIDsRelacionados.Split(
                    new[] { ',' },
                    StringSplitOptions.RemoveEmptyEntries
                );

                foreach (string uuid in uuids)
                {
                    var rowOrigen = RunQuery(
                        "SELECT id FROM factura WHERE uuid = @uuid::uuid LIMIT 1",
                        new Dictionary<string, object> { ["uuid"] = uuid.Trim() });

                    if (rowOrigen?.Count > 0)
                    {
                        int origenId = Convert.ToInt32(rowOrigen[0]["id"]);
                        RunQuery(@"
                INSERT INTO factura_relacion
                    (factura_origen_id, factura_relacionada_id, tipo_relacion)
                VALUES
                    (@origen, @nc_id, @tipo_rel);",
                            new Dictionary<string, object>
                            {
                                ["origen"] = origenId,
                                ["nc_id"] = id,           // id del INSERT de factura que ya tienes
                                ["tipo_rel"] = nc.TipoRelacion ?? "01"
                            });
                    }
                }
            }

            // Reducir saldo de cada factura origen
            if (!string.IsNullOrWhiteSpace(nc.UUIDsRelacionados))
            {
                var uuids = nc.UUIDsRelacionados.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

                // Si hay una sola factura origen, le descontamos el total de la NC.
                // Si son varias, el descuento proporcional lo calculas según tu regla
                // de negocio; aquí el caso más común: una NC = una factura origen.
                if (uuids.Length == 1)
                {
                    RunQuery(@"
            UPDATE factura
               SET saldo = GREATEST(saldo - @monto, 0)
             WHERE uuid = @uuid::uuid;",
                        new Dictionary<string, object>
                        {
                            ["monto"] = nc.Total,
                            ["uuid"] = uuids[0].Trim()
                        });
                }
            }

            // Actualizar cant_ndc en dfactura de la factura origen
            // para que sepas cuántas unidades ya fueron devueltas
            foreach (DataRow row in nc.Tproductos.Rows)
            {
                string clave = row["numero"]?.ToString() ?? "";
                decimal cantNC = Convert.ToDecimal(row["cantidad"]);

                if (string.IsNullOrWhiteSpace(clave)) continue;

                // Busca la partida en la factura origen y acumula cant_ndc
                RunQuery(@"
        UPDATE dfactura d
           SET cant_ndc = COALESCE(cant_ndc, 0) + @cant
          FROM factura f
         WHERE d.idfac    = f.id
           AND f.uuid     = ANY(@uuids::uuid[])
           AND d.claveprod = @clave;",
                    new Dictionary<string, object>
                    {
                        ["cant"] = cantNC,
                        ["uuids"] = nc.UUIDsRelacionados
                                      .Split(',')
                                      .Select(u => u.Trim())
                                      .ToArray(),
                        ["clave"] = clave
                    });
            }



            return id;
        }

        private static object Valor(DataRow row, object valorPredeterminado, params string[] columnas)
        {
            foreach (string columna in columnas)
            {
                var encontrada = row.Table.Columns.Cast<DataColumn>()
                    .FirstOrDefault(c => c.ColumnName.Equals(columna, StringComparison.OrdinalIgnoreCase));
                if (encontrada != null && row[encontrada] != DBNull.Value)
                    return row[encontrada];
            }
            return valorPredeterminado;
        }
    }
}

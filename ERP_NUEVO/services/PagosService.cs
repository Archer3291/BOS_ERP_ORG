using System;
using System.Collections.Generic;
using BOS_ERP.Controllers;
using System.Linq;
using System.Web;

namespace BOS_ERP.Services
{
    public class PagosService : Utilities
    {
        // GET: PagosService
        public void PagosVencidos()
        {
            //string query = "UPDATE pagos_proveedor SET estado = 'Vencido', fecha_actualizado = NOW() " +
            //    "WHERE fecha_pago <= now() AND estado = 'Pendiente'";
            //RunUpdate(query);
        }
    }
}
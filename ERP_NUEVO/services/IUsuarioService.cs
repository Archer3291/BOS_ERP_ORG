using BOS_ERP.Controllers;
using BOS_ERP.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace BOS_ERP.Services
{
    public interface IUsuarioService
    {
        Task<UsuarioActual?> ObtenerPorNombreUsuarioAsync(string nombreUsuario);
    }
}

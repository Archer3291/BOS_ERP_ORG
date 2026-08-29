using BOS_ERP.Models;

namespace BOS_ERP.Services
{
    public interface IAccesoModuloService
    {
        ValidacionResultado ValidarAcceso(int usuarioId, string moduloId, string password);
    }
}
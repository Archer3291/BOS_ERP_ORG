// Services/Refacturacion/RefacturacionHandlerFactory.cs
using BOS_ERP.Services.Refacturacion;

public class RefacturacionHandlerFactory
{
    private readonly Dictionary<string, IRefacturacionTypeHandler> _handlers;

    public RefacturacionHandlerFactory(IEnumerable<IRefacturacionTypeHandler> handlers)
    {
        _handlers = handlers.ToDictionary(h => h.TipoId);
    }

    public IRefacturacionTypeHandler ObtenerHandler(string tipoId)
    {
        if (!_handlers.TryGetValue(tipoId, out var h))
            throw new InvalidOperationException($"No hay handler registrado para el tipo '{tipoId}'.");
        return h;
    }
}
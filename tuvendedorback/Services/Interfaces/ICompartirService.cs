namespace tuvendedorback.Services.Interfaces;

public interface ICompartirService
{
    Task<string> GenerarHtmlProductoCompartir(int idProducto);
}

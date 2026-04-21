using tuvendedorback.DTOs;

namespace tuvendedorback.Repositories.Interfaces;

public interface ICompartirRepository
{
    Task<ProductoCompartirDto?> ObtenerProductoParaCompartir(int idProducto);
}

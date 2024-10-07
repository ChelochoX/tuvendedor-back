using tuvendedorback.DTOs;

namespace tuvendedorback.Repositories.Interfaces;

public interface IMotoRepository
{
    Task<ProductoDTO> ObtenerProductoConPlanes(string modelo);

    Task<int> ObtenerPrecioBase(string modelo);
}

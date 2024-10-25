using tuvendedorback.DTOs;
using tuvendedorback.Models;

namespace tuvendedorback.Repositories.Interfaces;

public interface IMotoRepository
{
    Task<ProductoDTO> ObtenerProductoConPlanes(string modelo);
    Task<int> ObtenerPrecioBase(string modelo);
    Task<int> GuardarSolicitudCredito(SolicitudCredito solicitud);
    Task<List<ProductosDTOPromo>> ListarProductosConPlanesPromo();
    Task<ProductoDTOPromo> ObtenerProductoConPlanesPromo(string modelo);
}

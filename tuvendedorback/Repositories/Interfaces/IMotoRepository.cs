using tuvendedorback.DTOs;
using tuvendedorback.Models;
using tuvendedorback.Request;
using tuvendedorback.Wrappers;

namespace tuvendedorback.Repositories.Interfaces;

public interface IMotoRepository
{
    Task<ProductoDTO> ObtenerProductoConPlanes(string modelo);
    Task<int> ObtenerPrecioBase(string modelo);
    Task<int> GuardarSolicitudCredito(SolicitudCredito solicitud);
    Task<List<ProductosDTOPromo>> ListarProductosConPlanesPromo();
    Task<ProductoDTOPromo> ObtenerProductoConPlanesPromo(string modelo);
    Task RegistrarVisitaAsync(string page);
    Task<Datos<IEnumerable<SolicitudesdeCreditoDTO>>> ObtenerSolicitudesCredito(SolicitudCreditoRequest request);
    Task<CreditoSolicitudDetalleDto> ObtenerDetalleCreditoSolicitudAsync(int id);
    Task<bool> ActualizarSolicitudCredito(int idSolicitud, SolicitudCredito solicitud);
}

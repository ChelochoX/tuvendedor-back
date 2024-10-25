using tuvendedorback.DTOs;
using tuvendedorback.Models;
using tuvendedorback.Request;

namespace tuvendedorback.Services.Interfaces;

public interface IMotoService
{
    Task<List<ModeloMotosporCategoria>> ObtenerModelosPorCategoriaAsync(string categoria);
    Task<ProductoDTO> ObtenerProductoConPlanes(string modelo);
    Task<decimal> ObtenerMontoCuotaConEntregaMayor(CalculoCuotaRequest request);
    Task<int> GuardarSolicitudCredito(SolicitudCredito solicitud);
    Task GenerarPdfSolicitud(SolicitudCredito solicitud, int idSolicitud);
    Task<List<ModeloMotosporCategoria>> ListarProductosConPlanesPromo();
    Task<ProductoDTOPromo> ObtenerProductoConPlanesPromo(string modelo);


}

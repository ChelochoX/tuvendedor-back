using tuvendedorback.DTOs;
using tuvendedorback.Request;

namespace tuvendedorback.Services.Interfaces;

public interface ISolicitudVisitaService
{
    Task<ResultadoSolicitudVisitaDto> CrearSolicitudVisita(CrearSolicitudVisitaRequest request);

    Task<List<SolicitudVisitaDto>> ObtenerMisSolicitudesVisita(int idUsuarioVendedor);
}

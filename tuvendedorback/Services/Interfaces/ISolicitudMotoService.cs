using tuvendedorback.DTOs;
using tuvendedorback.Request;

namespace tuvendedorback.Services.Interfaces;

public interface ISolicitudMotoService
{
    Task<SolicitudMotoProcesoDto?> ObtenerActiva(
        int idConversacion);

    Task<SolicitudMotoProcesoResultadoDto> Iniciar(
        int idConversacion,
        int idModeloProducto,
        int? idPublicacion,
        string telefono,
        string tipoOperacion,
        CancellationToken cancellationToken = default);

    Task<SolicitudMotoProcesoResultadoDto> ProcesarActiva(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request,
        CancellationToken cancellationToken = default);
}

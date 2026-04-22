using tuvendedorback.DTOs;
using tuvendedorback.Request;

namespace tuvendedorback.Repositories.Interfaces;

public interface ISolicitudVisitaRepository
{
    Task<PublicacionParaVisitaDto?> ObtenerPublicacionParaVisita(int idPublicacion);

    Task<int> CrearSolicitudVisita(
        CrearSolicitudVisitaRequest request,
        PublicacionParaVisitaDto publicacion);

    Task MarcarNotificacionVendedorExitosa(int idSolicitudVisita);

    Task MarcarNotificacionVendedorFallida(
        int idSolicitudVisita,
        string error);

    Task<List<SolicitudVisitaDto>> ObtenerSolicitudesPorVendedor(int idUsuarioVendedor);
}

using tuvendedorback.DTOs;
using tuvendedorback.Request;

namespace tuvendedorback.Services.Interfaces;

public interface IContadoMotoGestionService
{
    Task<IReadOnlyList<ContadoMotoGestionListaDto>> Listar(
        string? estado,
        string? buscar,
        DateTime? fecha);

    Task<ContadoMotoGestionDetalleDto> ObtenerDetalle(
        int idSolicitudContado);

    Task<ContadoMotoGestionDetalleDto> Contactar(
        int idSolicitudContado,
        int? idUsuario);

    Task<ContadoMotoGestionDetalleDto> CambiarEstado(
        int idSolicitudContado,
        int? idUsuario,
        CambiarEstadoContadoMotoRequest request);

    Task<ContadoMotoArchivoDto> ObtenerDocumento(
        int idSolicitudContado,
        int idDocumento);
}

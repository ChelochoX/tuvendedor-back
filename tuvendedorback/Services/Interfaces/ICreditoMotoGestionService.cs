using tuvendedorback.DTOs;
using tuvendedorback.Request;

namespace tuvendedorback.Services.Interfaces;

public interface ICreditoMotoGestionService
{
    Task<IReadOnlyList<CreditoMotoGestionListaDto>> Listar(
        string? estado,
        string? buscar);

    Task<CreditoMotoGestionDetalleDto> ObtenerDetalle(
        int idSolicitudCredito);

    Task<CreditoMotoGestionDetalleDto> TomarSolicitud(
        int idSolicitudCredito,
        int idUsuario);

    Task<CreditoMotoGestionDetalleDto> CambiarEstado(
        int idSolicitudCredito,
        int idUsuario,
        CreditoMotoCambiarEstadoRequest request);

    Task<CreditoMotoArchivoDto> ObtenerDocumento(
        int idSolicitudCredito,
        int idDocumento);
}

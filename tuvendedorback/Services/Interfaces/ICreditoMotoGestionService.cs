using tuvendedorback.DTOs;
using tuvendedorback.Request;

namespace tuvendedorback.Services.Interfaces;

public interface ICreditoMotoGestionService
{
    Task<IReadOnlyList<CreditoMotoGestionListaDto>> Listar(
        string? estado,
        string? buscar,
        DateTime? fecha);

    Task<CreditoMotoGestionDetalleDto> ObtenerDetalle(
        int idSolicitudCredito);

    Task<CreditoMotoGestionDetalleDto> CambiarEstado(
        int idSolicitudCredito,
        int idUsuario,
        CreditoMotoCambiarEstadoRequest request);

    Task<CreditoMotoArchivoDto> ObtenerDocumento(
        int idSolicitudCredito,
        int idDocumento);
}
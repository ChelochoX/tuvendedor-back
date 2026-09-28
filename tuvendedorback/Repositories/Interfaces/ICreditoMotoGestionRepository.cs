using tuvendedorback.DTOs;

namespace tuvendedorback.Repositories.Interfaces;

public interface ICreditoMotoGestionRepository
{
    Task<IReadOnlyList<CreditoMotoGestionListaDto>> Listar(
        string? estado,
        string? buscar,
        DateTime? fecha);

    Task<CreditoMotoGestionDetalleDto?> ObtenerDetalle(
        int idSolicitudCredito);

    Task<bool> CambiarEstado(
        int idSolicitudCredito,
        int idUsuario,
        string estadoAnterior,
        string estadoNuevo,
        string accion,
        string? observacion);

    Task<CreditoMotoDocumentoArchivoDataDto?> ObtenerDocumentoArchivo(
        int idSolicitudCredito,
        int idDocumento);
}
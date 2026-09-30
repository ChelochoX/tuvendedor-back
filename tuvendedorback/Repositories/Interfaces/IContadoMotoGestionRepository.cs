using tuvendedorback.DTOs;

namespace tuvendedorback.Repositories.Interfaces;

public interface IContadoMotoGestionRepository
{
    Task<IReadOnlyList<ContadoMotoGestionListaDto>> Listar(
        string? estado,
        string? buscar,
        DateTime? fecha);

    Task<ContadoMotoGestionDetalleDto?> ObtenerDetalle(
        int idSolicitudContado);

    Task Contactar(
        int idSolicitudContado,
        int? idUsuario);

    Task CambiarEstado(
        int idSolicitudContado,
        int? idUsuario,
        string nuevoEstado,
        string? observacion);

    Task<ContadoMotoDocumentoArchivoDto?> ObtenerDocumento(
        int idSolicitudContado,
        int idDocumento);
}

using tuvendedorback.DTOs;
using tuvendedorback.Exceptions;
using tuvendedorback.Repositories.Interfaces;
using tuvendedorback.Request;
using tuvendedorback.Services.Interfaces;

namespace tuvendedorback.Services;

public class ContadoMotoGestionService
    : IContadoMotoGestionService
{
    private readonly IContadoMotoGestionRepository _repository;


    private static readonly HashSet<string> EstadosValidos =
        new(
            StringComparer.OrdinalIgnoreCase
        )
        {
            "PENDIENTE_CONTACTO",
            "CONTACTADO",
            "CONCRETADA",
            "NO_CONCRETADA"
        };


    public ContadoMotoGestionService(
        IContadoMotoGestionRepository repository)
    {
        _repository =
            repository;
    }


    public Task<
        IReadOnlyList<ContadoMotoGestionListaDto>
    > Listar(
        string? estado,
        string? buscar,
        DateTime? fecha)
    {
        var estadoNormalizado =
            string.IsNullOrWhiteSpace(
                estado)
                ? "TODOS"
                : estado
                    .Trim()
                    .ToUpperInvariant();


        if (
            estadoNormalizado != "TODOS"
            &&
            !EstadosValidos.Contains(
                estadoNormalizado)
        )
        {
            throw new ReglasdeNegocioException(
                "El estado indicado para la bandeja de contado no es válido.");
        }


        return _repository.Listar(
            estadoNormalizado,
            buscar,
            fecha?.Date);
    }


    public async Task<
        ContadoMotoGestionDetalleDto
    > ObtenerDetalle(
        int idSolicitudContado)
    {
        if (
            idSolicitudContado <= 0
        )
        {
            throw new ReglasdeNegocioException(
                "La solicitud indicada no es válida.");
        }


        var detalle =
            await _repository.ObtenerDetalle(
                idSolicitudContado);


        if (
            detalle == null
        )
        {
            throw new ReglasdeNegocioException(
                "No se encontró la solicitud al contado.");
        }


        return detalle;
    }


    public async Task<
        ContadoMotoGestionDetalleDto
    > Contactar(
        int idSolicitudContado,
        int idUsuario)
    {
        if (
            idUsuario <= 0
        )
        {
            throw new ReglasdeNegocioException(
                "No se pudo identificar al usuario que realiza la gestión.");
        }


        var actual =
            await ObtenerDetalle(
                idSolicitudContado);


        if (
            actual.EstadoControl ==
                "CONCRETADA"

            ||

            actual.EstadoControl ==
                "NO_CONCRETADA"
        )
        {
            throw new ReglasdeNegocioException(
                "La gestión de esta compra al contado ya está cerrada.");
        }


        await _repository.Contactar(
            idSolicitudContado,
            idUsuario);


        return await ObtenerDetalle(
            idSolicitudContado);
    }


    public async Task<
        ContadoMotoGestionDetalleDto
    > CambiarEstado(
        int idSolicitudContado,
        int idUsuario,
        CambiarEstadoContadoMotoRequest request)
    {
        if (
            request == null
        )
        {
            throw new ReglasdeNegocioException(
                "Debe indicar el nuevo estado de la gestión.");
        }


        if (
            idUsuario <= 0
        )
        {
            throw new ReglasdeNegocioException(
                "No se pudo identificar al usuario que realiza la gestión.");
        }


        var estado =
            request.Estado?
                .Trim()
                .ToUpperInvariant();


        if (
            string.IsNullOrWhiteSpace(
                estado)

            ||

            !EstadosValidos.Contains(
                estado)

            ||

            estado ==
                "PENDIENTE_CONTACTO"
        )
        {
            throw new ReglasdeNegocioException(
                "El nuevo estado de la gestión no es válido.");
        }


        if (
            estado ==
                "NO_CONCRETADA"

            &&

            string.IsNullOrWhiteSpace(
                request.Observacion)
        )
        {
            throw new ReglasdeNegocioException(
                "Para cerrar como no concretada debe indicar el motivo.");
        }


        await ObtenerDetalle(
            idSolicitudContado);


        await _repository.CambiarEstado(
            idSolicitudContado,
            idUsuario,
            estado,
            request.Observacion);


        return await ObtenerDetalle(
            idSolicitudContado);
    }


    public async Task<
        ContadoMotoArchivoDto
    > ObtenerDocumento(
        int idSolicitudContado,
        int idDocumento)
    {
        if (
            idSolicitudContado <= 0

            ||

            idDocumento <= 0
        )
        {
            throw new ReglasdeNegocioException(
                "El documento indicado no es válido.");
        }


        var documento =
            await _repository.ObtenerDocumento(
                idSolicitudContado,
                idDocumento);


        if (
            documento == null
        )
        {
            throw new ReglasdeNegocioException(
                "No se encontró el documento solicitado.");
        }


        if (
            string.IsNullOrWhiteSpace(
                documento.RutaPrivada)

            ||

            !File.Exists(
                documento.RutaPrivada)
        )
        {
            throw new ReglasdeNegocioException(
                "El archivo físico del documento no está disponible.");
        }


        var bytes =
            await File.ReadAllBytesAsync(
                documento.RutaPrivada);


        return new ContadoMotoArchivoDto
        {
            Contenido =
                bytes,

            NombreArchivo =
                string.IsNullOrWhiteSpace(
                    documento.NombreArchivo)
                    ? $"documento-{idDocumento}"
                    : documento.NombreArchivo,

            MimeType =
                string.IsNullOrWhiteSpace(
                    documento.MimeType)
                    ? "application/octet-stream"
                    : documento.MimeType
        };
    }
}
using tuvendedorback.DTOs;
using tuvendedorback.Exceptions;
using tuvendedorback.Repositories.Interfaces;
using tuvendedorback.Request;
using tuvendedorback.Services.Interfaces;

namespace tuvendedorback.Services;

public class CreditoMotoGestionService : ICreditoMotoGestionService
{
    private static readonly HashSet<string> EstadosValidos =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "PENDIENTE_ENVIO",
            "ENVIADA_EMPRESA"
        };

    private readonly ICreditoMotoGestionRepository _repository;

    public CreditoMotoGestionService(
        ICreditoMotoGestionRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<CreditoMotoGestionListaDto>> Listar(
        string? estado,
        string? buscar,
        DateTime? fecha)
    {
        string? estadoNormalizado = null;

        if (!string.IsNullOrWhiteSpace(estado)
            && !estado.Equals("TODOS", StringComparison.OrdinalIgnoreCase))
        {
            estadoNormalizado = estado.Trim().ToUpperInvariant();

            if (!EstadosValidos.Contains(estadoNormalizado))
            {
                throw new ReglasdeNegocioException(
                    "El estado de control indicado no es válido.");
            }
        }

        if (!string.IsNullOrWhiteSpace(buscar)
            && buscar.Trim().Length > 100)
        {
            throw new ReglasdeNegocioException(
                "El criterio de búsqueda es demasiado largo.");
        }

        return await _repository.Listar(
            estadoNormalizado,
            buscar,
            fecha?.Date);
    }

    public async Task<CreditoMotoGestionDetalleDto> ObtenerDetalle(
        int idSolicitudCredito)
    {
        ValidarIdSolicitud(idSolicitudCredito);

        var solicitud =
            await _repository.ObtenerDetalle(idSolicitudCredito);

        if (solicitud == null)
        {
            throw new ReglasdeNegocioException(
                "No se encontró la solicitud de crédito indicada.");
        }

        return solicitud;
    }

    public async Task<CreditoMotoGestionDetalleDto> CambiarEstado(
        int idSolicitudCredito,
        int idUsuario,
        CreditoMotoCambiarEstadoRequest request)
    {
        ValidarIdSolicitud(idSolicitudCredito);
        ValidarUsuario(idUsuario);

        if (request == null)
        {
            throw new ReglasdeNegocioException(
                "Debe indicar el nuevo estado.");
        }

        var estadoNuevo =
            (request.Estado ?? string.Empty)
            .Trim()
            .ToUpperInvariant();

        if (!EstadosValidos.Contains(estadoNuevo))
        {
            throw new ReglasdeNegocioException(
                "El nuevo estado indicado no es válido.");
        }

        var actual =
            await ObtenerDetalle(idSolicitudCredito);

        var estadoAnterior =
            actual.EstadoControl
                .Trim()
                .ToUpperInvariant();

        if (estadoAnterior == estadoNuevo)
        {
            return actual;
        }

        if (estadoAnterior == "ENVIADA_EMPRESA")
        {
            throw new ReglasdeNegocioException(
                "La solicitud ya fue marcada como enviada a la empresa.");
        }

        if (estadoAnterior != "PENDIENTE_ENVIO"
            || estadoNuevo != "ENVIADA_EMPRESA")
        {
            throw new ReglasdeNegocioException(
                $"No se puede pasar de {estadoAnterior} a {estadoNuevo}.");
        }

        var observacion =
            string.IsNullOrWhiteSpace(request.Observacion)
                ? "Solicitud enviada a la empresa para evaluación de crédito."
                : request.Observacion.Trim();

        var actualizado =
            await _repository.CambiarEstado(
                idSolicitudCredito,
                idUsuario,
                estadoAnterior,
                estadoNuevo,
                "ENVIO_EMPRESA",
                observacion);

        if (!actualizado)
        {
            throw new ReglasdeNegocioException(
                "La solicitud fue modificada por otro usuario. Actualizá la bandeja e intentá nuevamente.");
        }

        return await ObtenerDetalle(idSolicitudCredito);
    }

    public async Task<CreditoMotoArchivoDto> ObtenerDocumento(
        int idSolicitudCredito,
        int idDocumento)
    {
        ValidarIdSolicitud(idSolicitudCredito);

        if (idDocumento <= 0)
        {
            throw new ReglasdeNegocioException(
                "El identificador del documento no es válido.");
        }

        var documento =
            await _repository.ObtenerDocumentoArchivo(
                idSolicitudCredito,
                idDocumento);

        if (documento == null)
        {
            throw new ReglasdeNegocioException(
                "No se encontró el documento solicitado.");
        }

        if (string.IsNullOrWhiteSpace(documento.RutaPrivada))
        {
            throw new ReglasdeNegocioException(
                "El documento no tiene una ruta privada registrada.");
        }

        if (!File.Exists(documento.RutaPrivada))
        {
            throw new ReglasdeNegocioException(
                "El archivo físico del documento no está disponible.");
        }

        var contenido =
            await File.ReadAllBytesAsync(documento.RutaPrivada);

        return new CreditoMotoArchivoDto
        {
            NombreArchivo = documento.NombreArchivo,
            MimeType =
                string.IsNullOrWhiteSpace(documento.MimeType)
                    ? "application/octet-stream"
                    : documento.MimeType,
            Contenido = contenido
        };
    }

    private static void ValidarIdSolicitud(int idSolicitudCredito)
    {
        if (idSolicitudCredito <= 0)
        {
            throw new ReglasdeNegocioException(
                "El identificador de la solicitud no es válido.");
        }
    }

    private static void ValidarUsuario(int idUsuario)
    {
        if (idUsuario <= 0)
        {
            throw new ReglasdeNegocioException(
                "No se pudo identificar al usuario que realiza la gestión.");
        }
    }
}

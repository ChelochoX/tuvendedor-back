using tuvendedorback.DTOs;
using tuvendedorback.Exceptions;
using tuvendedorback.Repositories.Interfaces;
using tuvendedorback.Request;
using tuvendedorback.Services.Interfaces;

namespace tuvendedorback.Services;

public class CreditoMotoGestionService
    : ICreditoMotoGestionService
{
    private static readonly HashSet<string> EstadosValidos =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "PENDIENTE_REVISION",
            "EN_REVISION",
            "OBSERVADA",
            "APROBADA",
            "RECHAZADA"
        };

    private readonly ICreditoMotoGestionRepository _repository;

    public CreditoMotoGestionService(
        ICreditoMotoGestionRepository repository)
    {
        _repository = repository;
    }


    // =========================================================
    // LISTAR BANDEJA
    // =========================================================

    public async Task<IReadOnlyList<CreditoMotoGestionListaDto>> Listar(
        string? estado,
        string? buscar)
    {
        string? estadoNormalizado = null;

        if (!string.IsNullOrWhiteSpace(estado)
            && !estado.Equals(
                "TODOS",
                StringComparison.OrdinalIgnoreCase))
        {
            estadoNormalizado =
                estado.Trim().ToUpperInvariant();

            if (!EstadosValidos.Contains(
                    estadoNormalizado))
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
            buscar);
    }


    // =========================================================
    // OBTENER DETALLE
    // =========================================================

    public async Task<CreditoMotoGestionDetalleDto> ObtenerDetalle(
        int idSolicitudCredito)
    {
        ValidarIdSolicitud(
            idSolicitudCredito);

        var solicitud =
            await _repository.ObtenerDetalle(
                idSolicitudCredito);

        if (solicitud == null)
        {
            throw new ReglasdeNegocioException(
                "No se encontró la solicitud de crédito indicada.");
        }

        return solicitud;
    }


    // =========================================================
    // TOMAR SOLICITUD
    //
    // PENDIENTE_REVISION
    //        ↓
    // EN_REVISION
    // =========================================================

    public async Task<CreditoMotoGestionDetalleDto> TomarSolicitud(
        int idSolicitudCredito,
        int idUsuario)
    {
        ValidarIdSolicitud(
            idSolicitudCredito);

        ValidarUsuario(
            idUsuario);

        var actual =
            await ObtenerDetalle(
                idSolicitudCredito);


        // -----------------------------------------------------
        // IDEMPOTENCIA
        //
        // Si el mismo usuario ya tomó la solicitud,
        // devolverla normalmente.
        // -----------------------------------------------------

        if (actual.EstadoControl.Equals(
                "EN_REVISION",
                StringComparison.OrdinalIgnoreCase)
            &&
            actual.IdUsuarioAsignado == idUsuario)
        {
            return actual;
        }


        // -----------------------------------------------------
        // SOLO SE PUEDE TOMAR UNA SOLICITUD PENDIENTE
        // -----------------------------------------------------

        if (!actual.EstadoControl.Equals(
                "PENDIENTE_REVISION",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ReglasdeNegocioException(
                $"La solicitud no está pendiente de revisión. Estado actual: {actual.EstadoControl}.");
        }


        // -----------------------------------------------------
        // EVITAR QUE DOS USUARIOS TOMEN LA MISMA SOLICITUD
        // -----------------------------------------------------

        if (actual.IdUsuarioAsignado.HasValue
            &&
            actual.IdUsuarioAsignado.Value != idUsuario)
        {
            throw new ReglasdeNegocioException(
                "La solicitud ya está asignada a otro usuario.");
        }


        var tomada =
            await _repository.TomarSolicitud(
                idSolicitudCredito,
                idUsuario);

        if (!tomada)
        {
            throw new ReglasdeNegocioException(
                "La solicitud fue tomada o modificada por otro usuario. Actualizá la bandeja.");
        }


        return await ObtenerDetalle(
            idSolicitudCredito);
    }


    // =========================================================
    // CAMBIAR ESTADO
    // =========================================================

    public async Task<CreditoMotoGestionDetalleDto> CambiarEstado(
        int idSolicitudCredito,
        int idUsuario,
        CreditoMotoCambiarEstadoRequest request)
    {
        ValidarIdSolicitud(
            idSolicitudCredito);

        ValidarUsuario(
            idUsuario);

        if (request == null)
        {
            throw new ReglasdeNegocioException(
                "Debe indicar el nuevo estado.");
        }


        var estadoNuevo =
            (request.Estado ?? string.Empty)
            .Trim()
            .ToUpperInvariant();


        if (!EstadosValidos.Contains(
                estadoNuevo))
        {
            throw new ReglasdeNegocioException(
                "El nuevo estado indicado no es válido.");
        }


        // -----------------------------------------------------
        // PENDIENTE_REVISION ES UN ESTADO AUTOMATICO
        // No se establece manualmente.
        // -----------------------------------------------------

        if (estadoNuevo == "PENDIENTE_REVISION")
        {
            throw new ReglasdeNegocioException(
                "No se puede volver manualmente a PENDIENTE_REVISION.");
        }


        var actual =
            await ObtenerDetalle(
                idSolicitudCredito);

        var estadoAnterior =
            actual.EstadoControl
                .Trim()
                .ToUpperInvariant();


        // -----------------------------------------------------
        // PRIMERO DEBE HABER SIDO TOMADA
        // -----------------------------------------------------

        if (!actual.IdUsuarioAsignado.HasValue)
        {
            throw new ReglasdeNegocioException(
                "Primero tenés que tomar la solicitud para poder gestionarla.");
        }


        // -----------------------------------------------------
        // SOLO EL USUARIO ASIGNADO PUEDE MODIFICARLA
        // -----------------------------------------------------

        if (actual.IdUsuarioAsignado.Value
            != idUsuario)
        {
            throw new ReglasdeNegocioException(
                "La solicitud está asignada a otro usuario.");
        }


        // -----------------------------------------------------
        // APROBADA / RECHAZADA SON ESTADOS FINALES
        // -----------------------------------------------------

        if (estadoAnterior == "APROBADA"
            ||
            estadoAnterior == "RECHAZADA")
        {
            throw new ReglasdeNegocioException(
                "La solicitud ya tiene un resultado final y no puede modificarse.");
        }


        // -----------------------------------------------------
        // SI YA ESTA EN EL MISMO ESTADO
        // -----------------------------------------------------

        if (estadoAnterior == estadoNuevo)
        {
            return actual;
        }


        // -----------------------------------------------------
        // VALIDAR TRANSICION
        // -----------------------------------------------------

        if (!TransicionPermitida(
                estadoAnterior,
                estadoNuevo))
        {
            throw new ReglasdeNegocioException(
                $"No se puede pasar de {estadoAnterior} a {estadoNuevo}.");
        }


        var observacion =
            string.IsNullOrWhiteSpace(
                request.Observacion)
                ? null
                : request.Observacion.Trim();


        // -----------------------------------------------------
        // OBSERVADA REQUIERE MOTIVO
        // -----------------------------------------------------

        if (estadoNuevo == "OBSERVADA"
            &&
            string.IsNullOrWhiteSpace(
                observacion))
        {
            throw new ReglasdeNegocioException(
                "Para observar una solicitud tenés que indicar el motivo.");
        }


        // -----------------------------------------------------
        // RECHAZADA REQUIERE MOTIVO
        // -----------------------------------------------------

        if (estadoNuevo == "RECHAZADA"
            &&
            string.IsNullOrWhiteSpace(
                observacion))
        {
            throw new ReglasdeNegocioException(
                "Para registrar un rechazo tenés que indicar el motivo.");
        }


        var accion =
            ObtenerAccion(
                estadoNuevo);


        var actualizado =
            await _repository.CambiarEstado(
                idSolicitudCredito,
                idUsuario,
                estadoAnterior,
                estadoNuevo,
                accion,
                observacion);


        if (!actualizado)
        {
            throw new ReglasdeNegocioException(
                "La solicitud fue modificada por otro usuario. Actualizá la información e intentá nuevamente.");
        }


        return await ObtenerDetalle(
            idSolicitudCredito);
    }


    // =========================================================
    // OBTENER DOCUMENTO PRIVADO
    // =========================================================

    public async Task<CreditoMotoArchivoDto> ObtenerDocumento(
        int idSolicitudCredito,
        int idDocumento)
    {
        ValidarIdSolicitud(
            idSolicitudCredito);

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


        if (string.IsNullOrWhiteSpace(
                documento.RutaPrivada))
        {
            throw new ReglasdeNegocioException(
                "El documento no tiene una ruta privada registrada.");
        }


        if (!File.Exists(
                documento.RutaPrivada))
        {
            throw new ReglasdeNegocioException(
                "El archivo físico del documento no está disponible.");
        }


        var contenido =
            await File.ReadAllBytesAsync(
                documento.RutaPrivada);


        return new CreditoMotoArchivoDto
        {
            NombreArchivo =
                documento.NombreArchivo,

            MimeType =
                string.IsNullOrWhiteSpace(
                    documento.MimeType)
                    ? "application/octet-stream"
                    : documento.MimeType,

            Contenido =
                contenido
        };
    }


    // =========================================================
    // TRANSICIONES PERMITIDAS
    // =========================================================

    private static bool TransicionPermitida(
        string actual,
        string nuevo)
    {
        return actual switch
        {
            "EN_REVISION" =>
                nuevo is
                    "OBSERVADA"
                    or "APROBADA"
                    or "RECHAZADA",

            "OBSERVADA" =>
                nuevo is
                    "EN_REVISION"
                    or "APROBADA"
                    or "RECHAZADA",

            _ => false
        };
    }


    // =========================================================
    // ACCION PARA HISTORIAL
    // =========================================================

    private static string ObtenerAccion(
        string estadoNuevo)
    {
        return estadoNuevo switch
        {
            "EN_REVISION" =>
                "REANUDACION",

            "OBSERVADA" =>
                "OBSERVACION",

            "APROBADA" =>
                "APROBACION",

            "RECHAZADA" =>
                "RECHAZO",

            _ =>
                "CAMBIO_ESTADO"
        };
    }


    // =========================================================
    // VALIDACIONES
    // =========================================================

    private static void ValidarIdSolicitud(
        int idSolicitudCredito)
    {
        if (idSolicitudCredito <= 0)
        {
            throw new ReglasdeNegocioException(
                "El identificador de la solicitud no es válido.");
        }
    }


    private static void ValidarUsuario(
        int idUsuario)
    {
        if (idUsuario <= 0)
        {
            throw new ReglasdeNegocioException(
                "No se pudo identificar al usuario que realiza la gestión.");
        }
    }
}

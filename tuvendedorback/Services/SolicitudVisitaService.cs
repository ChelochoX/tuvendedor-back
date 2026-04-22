using System.Text;
using tuvendedorback.Common;
using tuvendedorback.DTOs;
using tuvendedorback.Exceptions;
using tuvendedorback.Repositories.Interfaces;
using tuvendedorback.Request;
using tuvendedorback.Services.Interfaces;

namespace tuvendedorback.Services;

public class SolicitudVisitaService : ISolicitudVisitaService
{
    private readonly ISolicitudVisitaRepository _repository;
    private readonly IWhatsAppNotificationService _whatsAppNotification;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SolicitudVisitaService> _logger;

    public SolicitudVisitaService(
        ISolicitudVisitaRepository repository,
        IWhatsAppNotificationService whatsAppNotification,
        IServiceProvider serviceProvider,
        ILogger<SolicitudVisitaService> logger)
    {
        _repository = repository;
        _whatsAppNotification = whatsAppNotification;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<ResultadoSolicitudVisitaDto> CrearSolicitudVisita(CrearSolicitudVisitaRequest request)
    {
        await ValidationHelper.ValidarAsync(request, _serviceProvider);

        var publicacion = await _repository.ObtenerPublicacionParaVisita(request.IdPublicacion);

        if (publicacion == null)
            throw new NoDataFoundException("No se encontró la publicación o no está disponible.");

        var idSolicitud = await _repository.CrearSolicitudVisita(request, publicacion);

        var notificado = false;

        try
        {
            if (string.IsNullOrWhiteSpace(publicacion.WhatsappVendedor))
            {
                await _repository.MarcarNotificacionVendedorFallida(
                    idSolicitud,
                    "El vendedor no tiene WhatsApp configurado.");

                _logger.LogWarning(
                    "Solicitud de visita creada, pero el vendedor no tiene WhatsApp. IdSolicitudVisita={IdSolicitudVisita}",
                    idSolicitud);
            }
            else
            {
                var mensaje = ConstruirMensajeWhatsAppVendedor(
                    idSolicitud,
                    request,
                    publicacion);

                notificado = await _whatsAppNotification.EnviarMensajeAsync(
                    publicacion.WhatsappVendedor,
                    mensaje);

                if (notificado)
                {
                    await _repository.MarcarNotificacionVendedorExitosa(idSolicitud);
                }
                else
                {
                    await _repository.MarcarNotificacionVendedorFallida(
                        idSolicitud,
                        "El servicio de WhatsApp está deshabilitado o no confirmó el envío.");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "La solicitud de visita fue creada, pero falló la notificación al vendedor. IdSolicitudVisita={IdSolicitudVisita}",
                idSolicitud);

            await _repository.MarcarNotificacionVendedorFallida(
                idSolicitud,
                ex.Message);
        }

        return new ResultadoSolicitudVisitaDto
        {
            IdSolicitudVisita = idSolicitud,
            Estado = "Pendiente",
            NotificadoVendedor = notificado
        };
    }

    public async Task<List<SolicitudVisitaDto>> ObtenerMisSolicitudesVisita(int idUsuarioVendedor)
    {
        if (idUsuarioVendedor <= 0)
            throw new UnauthorizedAccessException();

        return await _repository.ObtenerSolicitudesPorVendedor(idUsuarioVendedor);
    }

    private static string ConstruirMensajeWhatsAppVendedor(
        int idSolicitudVisita,
        CrearSolicitudVisitaRequest request,
        PublicacionParaVisitaDto publicacion)
    {
        var sb = new StringBuilder();

        sb.AppendLine("Nueva solicitud de visita 🏡");
        sb.AppendLine();
        sb.AppendLine($"Solicitud N°: {idSolicitudVisita}");
        sb.AppendLine($"Publicación: {publicacion.Titulo}");

        if (!string.IsNullOrWhiteSpace(publicacion.Ubicacion))
            sb.AppendLine($"Ubicación: {publicacion.Ubicacion}");

        sb.AppendLine();
        sb.AppendLine($"Interesado: {request.NombreInteresado.Trim()}");
        sb.AppendLine($"WhatsApp: {request.TelefonoInteresado.Trim()}");
        sb.AppendLine($"Fecha solicitada: {request.FechaVisita:dd/MM/yyyy}");
        sb.AppendLine($"Hora solicitada: {request.HoraVisita:hh\\:mm}");

        if (!string.IsNullOrWhiteSpace(request.Mensaje))
        {
            sb.AppendLine();
            sb.AppendLine("Mensaje:");
            sb.AppendLine(request.Mensaje.Trim());
        }

        sb.AppendLine();
        sb.AppendLine("Contactá al interesado para confirmar disponibilidad.");

        return sb.ToString();
    }
}

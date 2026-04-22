namespace tuvendedorback.Services.Interfaces;

public interface IWhatsAppNotificationService
{
    Task<bool> EnviarMensajeAsync(string telefonoDestino, string mensaje);
}

using System.Text.RegularExpressions;
using tuvendedorback.Services.Interfaces;

namespace tuvendedorback.Services;

public class WhatsAppNotificationService : IWhatsAppNotificationService
{
    private readonly ILogger<WhatsAppNotificationService> _logger;
    private readonly IConfiguration _configuration;

    public WhatsAppNotificationService(
        ILogger<WhatsAppNotificationService> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public Task<bool> EnviarMensajeAsync(string telefonoDestino, string mensaje)
    {
        var habilitado = _configuration.GetValue<bool>("WhatsApp:Enabled");

        var telefonoNormalizado = NormalizarTelefonoParaguay(telefonoDestino);

        if (string.IsNullOrWhiteSpace(telefonoNormalizado))
        {
            _logger.LogWarning(
                "No se pudo generar link WhatsApp. Teléfono inválido: {TelefonoDestino}",
                telefonoDestino);

            return Task.FromResult(false);
        }

        var linkWhatsApp = CrearLinkWaMe(telefonoNormalizado, mensaje);

        if (!habilitado)
        {
            _logger.LogInformation(
                "WhatsApp automático deshabilitado. Link manual generado para vendedor: {LinkWhatsApp}",
                linkWhatsApp);

            return Task.FromResult(false);
        }

        _logger.LogInformation(
            "Link WhatsApp generado. No se envía automáticamente por wa.me. Link: {LinkWhatsApp}",
            linkWhatsApp);

        return Task.FromResult(false);
    }

    private static string CrearLinkWaMe(string telefonoDestino, string mensaje)
    {
        var mensajeEncoded = Uri.EscapeDataString(mensaje);

        return $"https://wa.me/{telefonoDestino}?text={mensajeEncoded}";
    }

    private static string NormalizarTelefonoParaguay(string telefono)
    {
        if (string.IsNullOrWhiteSpace(telefono))
            return string.Empty;

        var soloNumeros = Regex.Replace(telefono, @"\D", "");

        if (soloNumeros.StartsWith("595"))
            return soloNumeros;

        if (soloNumeros.StartsWith("0"))
            return $"595{soloNumeros.Substring(1)}";

        if (soloNumeros.Length == 9)
            return $"595{soloNumeros}";

        return soloNumeros;
    }
}
using System.Net.Http.Json;
using System.Text.Json;
using tuvendedorback.DTOs;
using tuvendedorback.Services.Interfaces;

namespace tuvendedorback.Services;

public sealed class SeguimientoWhatsappSender : ISeguimientoWhatsappSender
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SeguimientoWhatsappSender> _logger;

    public SeguimientoWhatsappSender(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<SeguimientoWhatsappSender> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<SeguimientoWhatsappEnvioResultadoDto> Enviar(
        long idEnvio,
        string telefono,
        string mensaje,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = (
            _configuration["WhatsApp:BridgeUrl"]
            ?? _configuration["WHATSAPP_BRIDGE_URL"]
            ?? "http://tuvendedor_wa:3100"
        ).TrimEnd('/');

        var internalKey =
            _configuration["TUVENDEDOR_INTERNAL_KEY"]
            ?? _configuration["WhatsApp:InternalKey"]
            ?? string.Empty;

        if (string.IsNullOrWhiteSpace(internalKey))
        {
            return new SeguimientoWhatsappEnvioResultadoDto
            {
                Enviado = false,
                Error = "Falta TUVENDEDOR_INTERNAL_KEY para autenticar contra el bridge de WhatsApp."
            };
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{baseUrl}/crm/enviar-seguimiento");

        request.Headers.Add("X-TuVendedor-Internal-Key", internalKey);
        request.Content = JsonContent.Create(new
        {
            idEnvio,
            telefono,
            mensaje
        });

        try
        {
            using var response = await _httpClient.SendAsync(
                request,
                cancellationToken);

            var contenido = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new SeguimientoWhatsappEnvioResultadoDto
                {
                    Enviado = false,
                    Error = $"Bridge WhatsApp HTTP {(int)response.StatusCode}: {Limitar(contenido, 1200)}"
                };
            }

            using var json = JsonDocument.Parse(contenido);
            var root = json.RootElement;

            var success =
                root.TryGetProperty("success", out var successNode)
                && successNode.ValueKind == JsonValueKind.True;

            string? messageId = null;

            if (root.TryGetProperty("messageId", out var messageIdNode))
            {
                messageId = messageIdNode.GetString();
            }

            return new SeguimientoWhatsappEnvioResultadoDto
            {
                Enviado = success,
                MessageId = messageId,
                Error = success ? null : "El bridge no confirmó el envío del seguimiento."
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error llamando al bridge de WhatsApp. IdEnvio={IdEnvio}",
                idEnvio);

            return new SeguimientoWhatsappEnvioResultadoDto
            {
                Enviado = false,
                Error = ex.Message
            };
        }
    }

    private static string Limitar(string valor, int maximo)
        => valor.Length <= maximo ? valor : valor[..maximo];
}

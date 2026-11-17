using System.Net.Http.Headers;
using System.Text.Json;
using tuvendedorback.DTOs;

namespace tuvendedorback.Services;

/// <summary>Publica una imagen con texto en Facebook Page e Instagram Business/Creator.</summary>
public sealed class PanambiMetaPublishingService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<PanambiMetaPublishingService> _logger;

    public PanambiMetaPublishingService(HttpClient http, IConfiguration config,
        ILogger<PanambiMetaPublishingService> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
    }

    public async Task<List<PanambiMetaDestinoResultado>> PublicarAsync(
        PanambiMetaPublicacionRequest request, CancellationToken ct)
    {
        var resultados = new List<PanambiMetaDestinoResultado>();
        if (request.Facebook)
            resultados.Add(await PublicarFacebookAsync(request, ct));
        if (request.Instagram)
            resultados.Add(await PublicarInstagramAsync(request, ct));
        return resultados;
    }

    private async Task<PanambiMetaDestinoResultado> PublicarFacebookAsync(
        PanambiMetaPublicacionRequest req, CancellationToken ct)
    {
        const string red = "FACEBOOK";
        try
        {
            var pageId = ConfigRequerida("MetaPublishing:FacebookPageId");
            var token = ConfigRequerida("MetaPublishing:FacebookPageToken");
            using var data = await PostAsync($"{pageId}/photos", token, new()
            {
                ["url"] = req.ImagenUrl,
                ["caption"] = req.Texto,
                ["published"] = "true"
            }, ct);
            return new() { Red = red, Exito = true, Estado = "PUBLICADO",
                IdPublicacion = LeerCampo(data, "post_id") ?? LeerCampo(data, "id") };
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException)
        {
            _logger.LogWarning("Error publicando en {Red}: {Tipo}", red, ex.GetType().Name);
            return new() { Red = red, Estado = "ERROR", Error = MensajeSeguro(ex) };
        }
    }

    private async Task<PanambiMetaDestinoResultado> PublicarInstagramAsync(
        PanambiMetaPublicacionRequest req, CancellationToken ct)
    {
        const string red = "INSTAGRAM";
        try
        {
            var igId = ConfigRequerida("MetaPublishing:InstagramAccountId");
            var token = ConfigRequerida("MetaPublishing:InstagramToken");
            using var container = await PostAsync($"{igId}/media", token, new()
            {
                ["image_url"] = req.ImagenUrl,
                ["caption"] = req.Texto
            }, ct);
            var creationId = LeerCampo(container, "id") ??
                throw new InvalidOperationException("Meta no devolvió el ID de creación.");

            // Esperar el procesamiento del contenedor antes de solicitar la publicación.
            var listo = false;
            for (var intento = 0; intento < 10; intento++)
            {
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
                using var estado = await GetAsync($"{creationId}?fields=status_code", token, ct);
                var status = LeerCampo(estado, "status_code");
                if (status == "FINISHED") { listo = true; break; }
                if (status is "ERROR" or "EXPIRED")
                    throw new InvalidOperationException($"Contenedor Instagram: {status}.");
            }
            if (!listo)
                throw new InvalidOperationException("Instagram aún procesa la imagen; no se solicitó publicar. Revisar el contenedor.");

            using var publicado = await PostAsync($"{igId}/media_publish", token, new()
            {
                ["creation_id"] = creationId
            }, ct);
            return new() { Red = red, Exito = true, Estado = "PUBLICADO",
                IdPublicacion = LeerCampo(publicado, "id") };
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException)
        {
            _logger.LogWarning("Error publicando en {Red}: {Tipo}", red, ex.GetType().Name);
            return new() { Red = red, Estado = "ERROR", Error = MensajeSeguro(ex) };
        }
    }

    private async Task<JsonDocument> PostAsync(string path, string token,
        Dictionary<string, string> fields, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, GraphUrl(path));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        message.Content = new FormUrlEncodedContent(fields);
        using var response = await _http.SendAsync(message, ct);
        return await ParsearRespuestaAsync(response, ct);
    }

    private async Task<JsonDocument> GetAsync(string path, string token, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, GraphUrl(path));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _http.SendAsync(message, ct);
        return await ParsearRespuestaAsync(response, ct);
    }

    private static async Task<JsonDocument> ParsearRespuestaAsync(
        HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        JsonDocument? doc = null;
        try { doc = JsonDocument.Parse(body); }
        catch (JsonException)
        {
            throw new HttpRequestException($"Meta devolvió HTTP {(int)response.StatusCode} sin JSON válido.");
        }
        if (!response.IsSuccessStatusCode)
        {
            string? codigo = null;
            if (doc.RootElement.TryGetProperty("error", out var err) &&
                err.TryGetProperty("code", out var code)) codigo = code.ToString();
            doc.Dispose();
            throw new HttpRequestException($"Meta HTTP {(int)response.StatusCode}" +
                (codigo is null ? "." : $", código {codigo}."));
        }
        return doc;
    }

    private string GraphUrl(string path)
    {
        var version = _config["MetaPublishing:ApiVersion"] ?? "v27.0";
        if (!System.Text.RegularExpressions.Regex.IsMatch(version, @"^v\d+\.\d+$"))
            throw new InvalidOperationException("Versión de Meta Graph API inválida.");
        return $"https://graph.facebook.com/{version}/{path}";
    }

    private string ConfigRequerida(string key) =>
        !string.IsNullOrWhiteSpace(_config[key]) ? _config[key]! :
        throw new InvalidOperationException($"Falta configuración {key}.");

    private static string? LeerCampo(JsonDocument doc, string field) =>
        doc.RootElement.TryGetProperty(field, out var value) ? value.ToString() : null;

    private static string MensajeSeguro(Exception ex) => ex is HttpRequestException or InvalidOperationException
        ? ex.Message : "Respuesta no válida de Meta.";
}

using System.Text.Json;
using System.Text.Json.Serialization;
using tuvendedorback.DTOs;
using tuvendedorback.Services.Interfaces;

namespace tuvendedorback.Services;

public class OllamaService
    : IOllamaService
{
    private readonly HttpClient
        _httpClient;

    private readonly ILogger<OllamaService>
        _logger;

    private readonly string
        _baseUrl;

    private readonly string
        _modelo;


    public OllamaService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<OllamaService> logger)
    {
        _httpClient =
            httpClient;

        _logger =
            logger;


        _baseUrl =
            configuration[
                "IA:OllamaBaseUrl"]
            ??
            "http://localhost:11434";


        _modelo =
            configuration[
                "IA:Modelo"]
            ??
            "qwen3.5:4b";


        var timeoutSegundos =
            configuration.GetValue<int?>(
                "IA:TimeoutSeconds")
            ??
            120;


        _httpClient.Timeout =
            TimeSpan.FromSeconds(
                timeoutSegundos);
    }


    public async Task<string> GenerarRespuesta(
        string systemPrompt,
        IReadOnlyList<MensajeConversacionHistorialDto> historial,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var mensajes =
                new List<OllamaMessage>
                {
                    new()
                    {
                        Role =
                            "system",

                        Content =
                            systemPrompt
                    }
                };


            foreach (
                var mensaje
                in historial)
            {
                var role =
                    EsMensajeIA(
                        mensaje.Emisor)

                        ? "assistant"
                        : "user";


                mensajes.Add(
                    new OllamaMessage
                    {
                        Role =
                            role,

                        Content =
                            mensaje.Mensaje
                    });
            }


            var request =
                new OllamaChatRequest
                {
                    Model =
                        _modelo,

                    Stream =
                        false,

                    Think =
                        false,

                    Messages =
                        mensajes,

                    Options =
                        new OllamaOptions
                        {
                            Temperature =
                                0.25,

                            NumCtx =
                                4096
                        }
                };


            var url =
                $"{_baseUrl.TrimEnd('/')}/api/chat";


            _logger.LogInformation(
                "Consultando Ollama. Modelo={Modelo}",
                _modelo);


            using var response =
                await _httpClient.PostAsJsonAsync(
                    url,
                    request,
                    cancellationToken);


            var body =
                await response.Content
                    .ReadAsStringAsync(
                        cancellationToken);


            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Ollama respondió HTTP {StatusCode}. Body={Body}",
                    (int)response.StatusCode,
                    body);


                throw new InvalidOperationException(
                    $"Ollama respondió HTTP {(int)response.StatusCode}.");
            }


            var result =
                JsonSerializer.Deserialize<
                    OllamaChatResponse>(
                    body);


            var respuesta =
                result?
                    .Message?
                    .Content?
                    .Trim();


            if (
                string.IsNullOrWhiteSpace(
                    respuesta)
            )
            {
                throw new InvalidOperationException(
                    "Ollama devolvió una respuesta vacía.");
            }


            return respuesta;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(
                ex,
                "Timeout consultando Ollama.");


            throw new InvalidOperationException(
                "La IA tardó demasiado en responder.",
                ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error comunicando con Ollama.");


            throw;
        }
    }


    private static bool EsMensajeIA(
        string emisor)
    {
        return
            emisor.Equals(
                "IA",
                StringComparison.OrdinalIgnoreCase)

            ||

            emisor.Equals(
                "ASISTENTE",
                StringComparison.OrdinalIgnoreCase)

            ||

            emisor.Equals(
                "BOT",
                StringComparison.OrdinalIgnoreCase);
    }


    private class OllamaChatRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } =
            string.Empty;


        [JsonPropertyName("messages")]
        public List<OllamaMessage> Messages
        {
            get;
            set;
        } = new();


        [JsonPropertyName("stream")]
        public bool Stream { get; set; }


        [JsonPropertyName("think")]
        public bool Think { get; set; }


        [JsonPropertyName("options")]
        public OllamaOptions Options
        {
            get;
            set;
        } = new();
    }


    private class OllamaMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } =
            string.Empty;


        [JsonPropertyName("content")]
        public string Content { get; set; } =
            string.Empty;
    }


    private class OllamaOptions
    {
        [JsonPropertyName("temperature")]
        public double Temperature
        {
            get;
            set;
        }


        [JsonPropertyName("num_ctx")]
        public int NumCtx
        {
            get;
            set;
        }
    }


    private class OllamaChatResponse
    {
        [JsonPropertyName("message")]
        public OllamaMessage? Message
        {
            get;
            set;
        }
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;
using tuvendedorback.DTOs;
using tuvendedorback.Services.Interfaces;

namespace tuvendedorback.Services;

public class OllamaService
    : IOllamaService
{
    /*
     * El VPS tiene un único runner de Ollama.
     * Evitamos acumular muchas peticiones simultáneas:
     * si ya está ocupado, devolvemos control al backend
     * para que use una respuesta comercial segura.
     */
    private static readonly SemaphoreSlim
        _semaforoOllama =
            new(
                1,
                1);

    private readonly HttpClient
        _httpClient;

    private readonly ILogger<OllamaService>
        _logger;

    private readonly string
        _baseUrl;

    private readonly string
        _modelo;

    private readonly int
        _esperaColaSegundos;

    private readonly string
        _keepAlive;


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
            30;

        _esperaColaSegundos =
            configuration.GetValue<int?>(
                "IA:QueueWaitSeconds")
            ??
            2;

        _keepAlive =
            configuration[
                "IA:KeepAlive"]
            ??
            "30m";


        _httpClient.Timeout =
            TimeSpan.FromSeconds(
                timeoutSegundos);
    }


    public async Task<string> GenerarRespuesta(
        string systemPrompt,
        IReadOnlyList<MensajeConversacionHistorialDto> historial,
        CancellationToken cancellationToken = default)
    {
        var obtuvoTurno =
            false;

        try
        {
            /*
             * No dejamos que varias conversaciones formen una cola
             * larga detrás de un único runner CPU.
             */
            obtuvoTurno =
                await _semaforoOllama
                    .WaitAsync(
                        TimeSpan.FromSeconds(
                            _esperaColaSegundos),
                        cancellationToken);

            if (!obtuvoTurno)
            {
                _logger.LogWarning(
                    "Ollama ocupado. Se omite IA generativa para responder con fallback seguro.");

                return string.Empty;
            }

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

                    KeepAlive =
                        _keepAlive,

                    Messages =
                        mensajes,

                    Options =
                        new OllamaOptions
                        {
                            Temperature =
                                0.25,

                            NumCtx =
                                3072,

                            NumPredict =
                                180
                        }
                };


            var url =
                $"{_baseUrl.TrimEnd('/')}/api/chat";


            _logger.LogInformation(
                "Consultando Ollama. Modelo={Modelo}, Mensajes={CantidadMensajes}",
                _modelo,
                mensajes.Count);


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

                return string.Empty;
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
                _logger.LogWarning(
                    "Ollama devolvió una respuesta vacía.");

                return string.Empty;
            }


            return respuesta;
        }
        catch (OperationCanceledException)
            when (
                cancellationToken
                    .IsCancellationRequested
            )
        {
            /*
             * Si el cliente/caller canceló realmente la petición,
             * respetamos la cancelación.
             */
            throw;
        }
        catch (OperationCanceledException ex)
        {
            /*
             * Timeout del HttpClient.
             * NO propagamos 500: MotoConversacionService utilizará
             * una respuesta comercial segura.
             */
            _logger.LogWarning(
                ex,
                "Timeout consultando Ollama. Se utilizará fallback seguro.");

            return string.Empty;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(
                ex,
                "No se pudo conectar con Ollama. Se utilizará fallback seguro.");

            return string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error comunicando con Ollama. Se utilizará fallback seguro.");

            return string.Empty;
        }
        finally
        {
            if (obtuvoTurno)
            {
                _semaforoOllama
                    .Release();
            }
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


        [JsonPropertyName("keep_alive")]
        public string KeepAlive { get; set; } =
            "30m";


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


        [JsonPropertyName("num_predict")]
        public int NumPredict
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

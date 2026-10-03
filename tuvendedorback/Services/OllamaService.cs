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
     * Texto y visión comparten el mismo semáforo para no saturar
     * CPU/RAM ni formar colas largas.
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

    private readonly string
        _modeloVision;

    private readonly int
        _esperaColaSegundos;

    private readonly string
        _keepAlive;

    private readonly string
        _keepAliveVision;

    private readonly bool
        _visionHabilitada;

    private readonly int
        _maxVisionImageBytes;


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

        _modeloVision =
            configuration[
                "IA:VisionModel"]
            ??
            "qwen3-vl:4b";

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

        _keepAliveVision =
            configuration[
                "IA:VisionKeepAlive"]
            ??
            "2m";

        _visionHabilitada =
            configuration.GetValue<bool?>(
                "IA:VisionEnabled")
            ??
            true;

        _maxVisionImageBytes =
            configuration.GetValue<int?>(
                "IA:MaxVisionImageBytes")
            ??
            6 * 1024 * 1024;

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

            var body =
                await EjecutarChat(
                    request,
                    cancellationToken);

            if (
                string.IsNullOrWhiteSpace(
                    body)
            )
            {
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
            throw;
        }
        catch (OperationCanceledException ex)
        {
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


    public async Task<AnalisisImagenMotoDto?> AnalizarImagenMoto(
        string mediaBase64,
        string? mediaMimeType,
        string? textoAcompaniante,
        CancellationToken cancellationToken = default)
    {
        if (!_visionHabilitada)
        {
            return null;
        }

        if (
            string.IsNullOrWhiteSpace(
                mediaBase64)
            ||
            !EsMimeImagen(
                mediaMimeType)
        )
        {
            return null;
        }

        var imagenBase64 =
            LimpiarBase64(
                mediaBase64);

        if (
            string.IsNullOrWhiteSpace(
                imagenBase64)
        )
        {
            return null;
        }

        var bytesEstimados =
            (long)imagenBase64.Length
            *
            3
            /
            4;

        if (
            bytesEstimados
            >
            _maxVisionImageBytes
        )
        {
            _logger.LogWarning(
                "Imagen omitida para visión por tamaño. BytesEstimados={BytesEstimados}, Max={Max}",
                bytesEstimados,
                _maxVisionImageBytes);

            return null;
        }

        var obtuvoTurno =
            false;

        try
        {
            obtuvoTurno =
                await _semaforoOllama
                    .WaitAsync(
                        TimeSpan.FromSeconds(
                            _esperaColaSegundos),
                        cancellationToken);

            if (!obtuvoTurno)
            {
                _logger.LogWarning(
                    "Ollama ocupado. Se omite análisis visual y se usa fallback seguro.");

                return null;
            }

            var textoExtra =
                string.IsNullOrWhiteSpace(
                    textoAcompaniante)

                    ? "(sin texto adicional)"
                    : textoAcompaniante.Trim();

            var prompt =
                $"""
                Analizá esta imagen únicamente para asistir una venta de motocicletas de TuVendedor.

                Texto que acompañó la imagen:
                {textoExtra}

                Respondé SOLO JSON válido, sin markdown y con esta forma exacta:
                {{
                  "esMoto": true,
                  "marca": "marca visible o inferida con prudencia, o null",
                  "modelo": "modelo visible o inferido con prudencia, o null",
                  "textoVisible": "texto útil visible en la imagen, o null",
                  "confianza": 0.0,
                  "motivo": "explicación muy breve"
                }}

                Reglas:
                - esMoto=true solo si la imagen muestra claramente una motocicleta, scooter o material/publicación comercial de una moto.
                - No inventes marca ni modelo. Si no se distingue, usá null.
                - confianza debe estar entre 0 y 1.
                - Si la imagen no corresponde a motos, esMoto=false.
                - No respondas preguntas generales ni describas personas, documentos u otros datos sensibles.
                """;

            var request =
                new OllamaChatRequest
                {
                    Model =
                        _modeloVision,

                    Stream =
                        false,

                    Think =
                        false,

                    KeepAlive =
                        _keepAliveVision,

                    Format =
                        "json",

                    Messages =
                        new List<OllamaMessage>
                        {
                            new()
                            {
                                Role =
                                    "user",

                                Content =
                                    prompt,

                                Images =
                                    new List<string>
                                    {
                                        imagenBase64
                                    }
                            }
                        },

                    Options =
                        new OllamaOptions
                        {
                            Temperature =
                                0.0,

                            NumCtx =
                                2048,

                            NumPredict =
                                180
                        }
                };

            var body =
                await EjecutarChat(
                    request,
                    cancellationToken);

            if (
                string.IsNullOrWhiteSpace(
                    body)
            )
            {
                return null;
            }

            var result =
                JsonSerializer.Deserialize<OllamaChatResponse>(
                    body);

            var contenido =
                result?
                    .Message?
                    .Content?
                    .Trim();

            if (
                string.IsNullOrWhiteSpace(
                    contenido)
            )
            {
                return null;
            }

            contenido =
                LimpiarBloqueJson(
                    contenido);

            var analisis =
                JsonSerializer.Deserialize<AnalisisImagenMotoDto>(
                    contenido,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive =
                            true
                    });

            if (analisis is null)
            {
                return null;
            }

            analisis.Confianza =
                Math.Clamp(
                    analisis.Confianza,
                    0,
                    1);

            return analisis;
        }
        catch (OperationCanceledException)
            when (
                cancellationToken
                    .IsCancellationRequested
            )
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "No se pudo analizar la imagen con el modelo visual. Se usa fallback seguro.");

            return null;
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


    private async Task<string?> EjecutarChat(
        OllamaChatRequest request,
        CancellationToken cancellationToken)
    {
        var url =
            $"{_baseUrl.TrimEnd('/')}/api/chat";

        _logger.LogInformation(
            "Consultando Ollama. Modelo={Modelo}, Mensajes={CantidadMensajes}, Vision={Vision}",
            request.Model,
            request.Messages.Count,
            request.Messages.Any(
                x => x.Images is { Count: > 0 }));

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
            _logger.LogWarning(
                "Ollama respondió HTTP {StatusCode}. Modelo={Modelo}. Body={Body}",
                (int)response.StatusCode,
                request.Model,
                body);

            return null;
        }

        return body;
    }


    private static bool EsMimeImagen(
        string? mimeType)
    {
        return
            !string.IsNullOrWhiteSpace(
                mimeType)
            &&
            mimeType.StartsWith(
                "image/",
                StringComparison.OrdinalIgnoreCase);
    }


    private static string LimpiarBase64(
        string valor)
    {
        var limpio =
            valor.Trim();

        if (
            limpio.StartsWith(
                "data:",
                StringComparison.OrdinalIgnoreCase)
        )
        {
            var indiceComa =
                limpio.IndexOf(',');

            if (
                indiceComa >= 0
                &&
                indiceComa < limpio.Length - 1
            )
            {
                limpio =
                    limpio[(indiceComa + 1)..];
            }
        }

        return limpio
            .Replace("\r", string.Empty)
            .Replace("\n", string.Empty)
            .Trim();
    }


    private static string LimpiarBloqueJson(
        string contenido)
    {
        var texto =
            contenido.Trim();

        if (
            texto.StartsWith("```", StringComparison.Ordinal)
        )
        {
            var primerSalto =
                texto.IndexOf('\n');

            if (primerSalto >= 0)
            {
                texto =
                    texto[(primerSalto + 1)..];
            }

            var ultimoCierre =
                texto.LastIndexOf("```", StringComparison.Ordinal);

            if (ultimoCierre >= 0)
            {
                texto =
                    texto[..ultimoCierre];
            }
        }

        return texto.Trim();
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

        [JsonPropertyName("format")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Format { get; set; }

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

        [JsonPropertyName("images")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? Images { get; set; }
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

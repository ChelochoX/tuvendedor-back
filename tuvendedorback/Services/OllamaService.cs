using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
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

    private readonly int
        _timeoutSegundos;

    private readonly int
        _visionTimeoutSegundos;

    private readonly int
        _visionNumCtx;

    private readonly int
        _visionNumPredict;

    private readonly int
        _visionMaxDimension;


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

        _timeoutSegundos =
            configuration.GetValue<int?>(
                "IA:TimeoutSeconds")
            ??
            30;

        _visionTimeoutSegundos =
            configuration.GetValue<int?>(
                "IA:VisionTimeoutSeconds")
            ??
            120;

        _visionNumCtx =
            configuration.GetValue<int?>(
                "IA:VisionNumCtx")
            ??
            4096;

        _visionNumPredict =
            Math.Clamp(
                configuration.GetValue<int?>(
                    "IA:VisionNumPredict")
                ??
                160,
                96,
                512);

        _visionMaxDimension =
            Math.Clamp(
                configuration.GetValue<int?>(
                    "IA:VisionMaxDimension")
                ??
                1024,
                640,
                1600);

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

        /*
         * Los tiempos de texto y visión son distintos.
         * La visión necesita más tiempo cuando Ollama debe cargar qwen3-vl
         * o cambiar desde el modelo de texto. Se desactiva el timeout global
         * del HttpClient y cada request aplica su propio límite.
         */
        _httpClient.Timeout =
            Timeout.InfiniteTimeSpan;
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
                    _timeoutSegundos,
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

        try
        {
            imagenBase64 =
                OptimizarImagenParaVision(
                    imagenBase64,
                    _visionMaxDimension,
                    out var anchoOriginal,
                    out var altoOriginal,
                    out var anchoFinal,
                    out var altoFinal,
                    out var bytesFinales);

            _logger.LogInformation(
                "Imagen preparada para visión. Original={AnchoOriginal}x{AltoOriginal}, Final={AnchoFinal}x{AltoFinal}, BytesOriginalesAprox={BytesOriginales}, BytesFinales={BytesFinales}",
                anchoOriginal,
                altoOriginal,
                anchoFinal,
                altoFinal,
                bytesEstimados,
                bytesFinales);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "No se pudo optimizar la imagen para visión. Se enviará la imagen original a Ollama.");
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
                $$"""
                Analizá la imagen para clasificarla dentro de TuVendedor.

                Texto que acompañó la imagen:
                {{textoExtra}}

                Respondé ÚNICAMENTE un JSON válido, compacto y completo. No uses markdown.
                Usá exactamente estas propiedades:
                {"esMoto":true,"tipoContenido":"MOTO","marca":null,"modelo":null,"textoVisible":null,"confianza":0.0}

                Reglas:
                - tipoContenido: MOTO, OTRO_PRODUCTO, OTRO_CONTENIDO o INCIERTO.
                - MOTO: motocicleta, scooter o publicidad/material comercial de una moto.
                - OTRO_PRODUCTO: inmueble, terreno, casa, auto u otro producto comercial que no sea moto.
                - OTRO_CONTENIDO: meme, persona, paisaje u otra imagen no comercial.
                - INCIERTO: no se puede decidir con seguridad.
                - esMoto=true solo cuando tipoContenido=MOTO.
                - Si se leen marca/modelo en la imagen, devolvelos. No inventes.
                - textoVisible: solo texto útil para identificar el producto, máximo 100 caracteres.
                - confianza: número decimal entre 0 y 1.
                - Mantené la respuesta breve para asegurar que el JSON cierre correctamente.
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
                                _visionNumCtx,

                            NumPredict =
                                _visionNumPredict
                        }
                };

            var body =
                await EjecutarChat(
                    request,
                    _visionTimeoutSegundos,
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

            _logger.LogInformation(
                "Respuesta visual de Ollama. Modelo={Modelo}, Contenido={Contenido}",
                _modeloVision,
                TruncarParaLog(
                    contenido,
                    1200));

            if (
                !IntentarParsearAnalisisImagen(
                    contenido,
                    out var analisis)
                ||
                analisis is null
            )
            {
                _logger.LogWarning(
                    "No se pudo interpretar la respuesta visual. Modelo={Modelo}, Respuesta={Respuesta}",
                    _modeloVision,
                    TruncarParaLog(
                        contenido,
                        1200));

                return null;
            }

            analisis.Confianza =
                Math.Clamp(
                    analisis.Confianza,
                    0,
                    1);

            analisis.TipoContenido =
                NormalizarTipoContenidoImagen(
                    analisis.TipoContenido,
                    analisis.EsMoto);

            analisis.EsMoto =
                string.Equals(
                    analisis.TipoContenido,
                    "MOTO",
                    StringComparison.OrdinalIgnoreCase);

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
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(
                ex,
                "Timeout analizando imagen con Ollama. Modelo={Modelo}, TimeoutSegundos={TimeoutSegundos}, NumCtx={NumCtx}",
                _modeloVision,
                _visionTimeoutSegundos,
                _visionNumCtx);

            return null;
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


    private static string NormalizarTipoContenidoImagen(
        string? tipoContenido,
        bool esMoto)
    {
        var tipo =
            (tipoContenido ?? string.Empty)
                .Trim()
                .ToUpperInvariant();

        return tipo switch
        {
            "MOTO" =>
                "MOTO",

            "OTRO_PRODUCTO" =>
                "OTRO_PRODUCTO",

            "OTRO_CONTENIDO" =>
                "OTRO_CONTENIDO",

            "INCIERTO" =>
                "INCIERTO",

            _ when esMoto =>
                "MOTO",

            _ =>
                "INCIERTO"
        };
    }


    private async Task<string?> EjecutarChat(
        OllamaChatRequest request,
        int timeoutSegundos,
        CancellationToken cancellationToken)
    {
        var url =
            $"{_baseUrl.TrimEnd('/')}/api/chat";

        var esVision =
            request.Messages.Any(
                x => x.Images is { Count: > 0 });

        _logger.LogInformation(
            "Consultando Ollama. Modelo={Modelo}, Mensajes={CantidadMensajes}, Vision={Vision}, NumCtx={NumCtx}, TimeoutSegundos={TimeoutSegundos}",
            request.Model,
            request.Messages.Count,
            esVision,
            request.Options.NumCtx,
            timeoutSegundos);

        using var timeoutCts =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        timeoutCts.CancelAfter(
            TimeSpan.FromSeconds(
                timeoutSegundos));

        using var response =
            await _httpClient.PostAsJsonAsync(
                url,
                request,
                timeoutCts.Token);

        var body =
            await response.Content
                .ReadAsStringAsync(
                    timeoutCts.Token);

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


    private static string OptimizarImagenParaVision(
        string imagenBase64,
        int maxDimension,
        out int anchoOriginal,
        out int altoOriginal,
        out int anchoFinal,
        out int altoFinal,
        out int bytesFinales)
    {
        var bytes =
            Convert.FromBase64String(
                imagenBase64);

        using var image =
            Image.Load(
                bytes);

        anchoOriginal =
            image.Width;

        altoOriginal =
            image.Height;

        if (
            image.Width > maxDimension
            ||
            image.Height > maxDimension
        )
        {
            image.Mutate(
                x =>
                    x.Resize(
                        new ResizeOptions
                        {
                            Size =
                                new Size(
                                    maxDimension,
                                    maxDimension),

                            Mode =
                                ResizeMode.Max
                        }));
        }

        anchoFinal =
            image.Width;

        altoFinal =
            image.Height;

        using var salida =
            new MemoryStream();

        image.Save(
            salida,
            new JpegEncoder
            {
                Quality = 82
            });

        var optimizada =
            salida.ToArray();

        bytesFinales =
            optimizada.Length;

        return Convert.ToBase64String(
            optimizada);
    }


    private static bool IntentarParsearAnalisisImagen(
        string contenido,
        out AnalisisImagenMotoDto? analisis)
    {
        analisis =
            null;

        try
        {
            analisis =
                JsonSerializer.Deserialize<AnalisisImagenMotoDto>(
                    contenido,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive =
                            true
                    });

            if (analisis is not null)
            {
                return true;
            }
        }
        catch (JsonException)
        {
            // El modelo puede devolver un JSON útil pero truncado al final.
            // En ese caso rescatamos únicamente campos completos y seguros.
        }

        var esMoto =
            ExtraerBoolJson(
                contenido,
                "esMoto");

        var tipoContenido =
            ExtraerStringJson(
                contenido,
                "tipoContenido");

        var marca =
            ExtraerStringJson(
                contenido,
                "marca");

        var modelo =
            ExtraerStringJson(
                contenido,
                "modelo");

        var textoVisible =
            ExtraerStringJson(
                contenido,
                "textoVisible");

        var confianza =
            ExtraerDoubleJson(
                contenido,
                "confianza");

        if (
            esMoto is null
            &&
            string.IsNullOrWhiteSpace(
                tipoContenido)
            &&
            string.IsNullOrWhiteSpace(
                marca)
            &&
            string.IsNullOrWhiteSpace(
                modelo)
            &&
            string.IsNullOrWhiteSpace(
                textoVisible)
            &&
            confianza is null
        )
        {
            return false;
        }

        var esMotoRecuperada =
            esMoto
            ??
            string.Equals(
                tipoContenido,
                "MOTO",
                StringComparison.OrdinalIgnoreCase);

        var confianzaRecuperada =
            confianza
            ??
            (
                esMotoRecuperada
                &&
                (
                    !string.IsNullOrWhiteSpace(
                        modelo)
                    ||
                    !string.IsNullOrWhiteSpace(
                        textoVisible)
                )
                    ? 0.75
                    : 0.0
            );

        analisis =
            new AnalisisImagenMotoDto
            {
                EsMoto =
                    esMotoRecuperada,

                TipoContenido =
                    string.IsNullOrWhiteSpace(
                        tipoContenido)
                        ? esMotoRecuperada
                            ? "MOTO"
                            : "INCIERTO"
                        : tipoContenido,

                Marca =
                    marca,

                Modelo =
                    modelo,

                TextoVisible =
                    textoVisible,

                Confianza =
                    confianzaRecuperada,

                Motivo =
                    "Respuesta visual recuperada de forma tolerante."
            };

        return true;
    }


    private static bool? ExtraerBoolJson(
        string contenido,
        string propiedad)
    {
        var match =
            Regex.Match(
                contenido,
                $"\\\"{Regex.Escape(propiedad)}\\\"\\s*:\\s*(true|false)",
                RegexOptions.IgnoreCase
                |
                RegexOptions.Singleline);

        if (!match.Success)
        {
            return null;
        }

        return bool.TryParse(
            match.Groups[1].Value,
            out var valor)
                ? valor
                : null;
    }


    private static double? ExtraerDoubleJson(
        string contenido,
        string propiedad)
    {
        var match =
            Regex.Match(
                contenido,
                $"\\\"{Regex.Escape(propiedad)}\\\"\\s*:\\s*\\\"?(?<valor>-?\\d+(?:[\\.,]\\d+)?)(?<porcentaje>%?)\\\"?",
                RegexOptions.IgnoreCase
                |
                RegexOptions.Singleline);

        if (!match.Success)
        {
            return null;
        }

        var raw =
            match.Groups["valor"]
                .Value
                .Replace(
                    ',',
                    '.');

        if (
            !double.TryParse(
                raw,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var valor)
        )
        {
            return null;
        }

        if (
            match.Groups["porcentaje"].Value == "%"
            ||
            valor > 1
        )
        {
            valor /=
                100d;
        }

        return valor;
    }


    private static string? ExtraerStringJson(
        string contenido,
        string propiedad)
    {
        var matchNull =
            Regex.Match(
                contenido,
                $"\\\"{Regex.Escape(propiedad)}\\\"\\s*:\\s*null",
                RegexOptions.IgnoreCase
                |
                RegexOptions.Singleline);

        if (matchNull.Success)
        {
            return null;
        }

        var match =
            Regex.Match(
                contenido,
                $"\\\"{Regex.Escape(propiedad)}\\\"\\s*:\\s*\\\"(?<valor>(?:\\\\.|[^\\\"\\\\])*)\\\"",
                RegexOptions.IgnoreCase
                |
                RegexOptions.Singleline);

        if (!match.Success)
        {
            return null;
        }

        var raw =
            $"\"{match.Groups["valor"].Value}\"";

        try
        {
            return JsonSerializer.Deserialize<string>(
                raw);
        }
        catch
        {
            return match.Groups["valor"]
                .Value;
        }
    }


    private static string TruncarParaLog(
        string valor,
        int maxLength)
    {
        if (
            string.IsNullOrWhiteSpace(
                valor)
            ||
            valor.Length <= maxLength
        )
        {
            return valor;
        }

        return valor[..maxLength]
            +
            " ...[truncado]";
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

using OpenAI.Chat;
using tuvendedorback.DTOs;
using tuvendedorback.Exceptions;
using tuvendedorback.Repositories.Interfaces;
using tuvendedorback.Response;
using tuvendedorback.Services.IA;
using tuvendedorback.Services.Interfaces;

namespace tuvendedorback.Services;

public class AgenteIAService : IAgenteIAService
{
    private readonly PromptBuilder _promptBuilder;
    private readonly IConversacionRepository _conversacionRepo;
    private readonly ILogger<AgenteIAService> _logger;
    private readonly ChatClient? _chatClient;
    private readonly bool _iaEnabled;

    public AgenteIAService(
        PromptBuilder promptBuilder,
        IConversacionRepository conversacionRepo,
        IConfiguration config,
        ILogger<AgenteIAService> logger)
    {
        _promptBuilder = promptBuilder;
        _conversacionRepo = conversacionRepo;
        _logger = logger;

        _iaEnabled = config.GetValue<bool>("OpenAI:Enabled");

        if (_iaEnabled)
        {
            _chatClient = new ChatClient(
                model: config["OpenAI:Model"],
                apiKey: config["OpenAI:ApiKey"]
            );
        }
    }

    public async Task<IAResponse> GenerarRespuesta(
        int idConversacion,
        string mensajeUsuario,
        ConversacionContextoDto? contexto)
    {
        try
        {
            var historial = await _conversacionRepo
                .ObtenerHistorialIA(idConversacion, 10);

            var prompt = await _promptBuilder
                .BuildAsync(mensajeUsuario, contexto, historial);

            _logger.LogInformation("🧠 Prompt generado con historial");

            if (!_iaEnabled || _chatClient == null)
            {
                return new IAResponse
                {
                    Texto = "Perfecto 🙌 ¿Querés financiación o contado?",
                    NuevoPaso = "ESPERANDO_DECISION",
                    Intencion = "CONSULTA_PRECIO"
                };
            }

            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(
                    "Sos un asesor comercial experto en motos Kenton en Paraguay."
                ),
                new UserChatMessage(prompt)
            };

            var result = await _chatClient.CompleteChatAsync(messages);

            var texto = result?.Value?.Content?.FirstOrDefault()?.Text;

            if (string.IsNullOrWhiteSpace(texto))
            {
                throw new ServiceException("La IA no devolvió respuesta válida");
            }

            return new IAResponse
            {
                Texto = texto,
                NuevoPaso = contexto?.PasoActual ?? "INICIO",
                Intencion = "VENTA_MOTO"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error en AgenteIAService");

            throw new ServiceException(
                "No se pudo generar respuesta con IA",
                ex
            );
        }
    }
}

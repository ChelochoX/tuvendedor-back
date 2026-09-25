using tuvendedorback.DTOs;

namespace tuvendedorback.Services.Interfaces;

public interface IOllamaService
{
    Task<string> GenerarRespuesta(
        string systemPrompt,
        IReadOnlyList<MensajeConversacionHistorialDto> historial,
        CancellationToken cancellationToken = default);
}

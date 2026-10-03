using tuvendedorback.DTOs;

namespace tuvendedorback.Services.Interfaces;

public interface IOllamaService
{
    Task<string> GenerarRespuesta(
        string systemPrompt,
        IReadOnlyList<MensajeConversacionHistorialDto> historial,
        CancellationToken cancellationToken = default);

    Task<AnalisisImagenMotoDto?> AnalizarImagenMoto(
        string mediaBase64,
        string? mediaMimeType,
        string? textoAcompaniante,
        CancellationToken cancellationToken = default);
}

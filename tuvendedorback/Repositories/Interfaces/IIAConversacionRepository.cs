using tuvendedorback.DTOs;

namespace tuvendedorback.Repositories.Interfaces;

public interface IIAConversacionRepository
{
    Task<int> ObtenerOCrearConversacion(
        string identificadorExterno);

    Task<int?> ObtenerIdPublicacionContexto(
        int idConversacion);

    Task ActualizarContexto(
        int idConversacion,
        int idPublicacion,
        string codigoPrompt);

    Task RegistrarMensaje(
        int idConversacion,
        string emisor,
        string mensaje);

    Task<IReadOnlyList<MensajeConversacionHistorialDto>>
        ObtenerUltimosMensajes(
            int idConversacion,
            int limite);

    Task<string?> ObtenerPromptActivo(
        string codigo);
}

using tuvendedorback.DTOs;

namespace tuvendedorback.Repositories.Interfaces;

public interface IIAConversacionRepository
{
    Task<int> ObtenerOCrearConversacion(
        string identificadorExterno);


    Task<int?> ObtenerIdPublicacionContexto(
        int idConversacion);


    Task<int?> ObtenerIdModeloActual(
        int idConversacion);


    Task ActualizarContexto(
        int idConversacion,
        int? idPublicacion,
        int? idModeloProducto,
        string? codigoPrompt);


    Task LimpiarProductoContexto(
        int idConversacion);


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


    Task<IReadOnlyList<MotoModeloCandidatoDto>>
        ObtenerModelosMotoActivos();


    Task<int?> ObtenerPublicacionActivaPorModelo(
        int idModeloProducto);

    Task<string> ObtenerModoConversacion(
    int idConversacion);

    Task CambiarModoConversacion(
        int idConversacion,
        string modo);
}

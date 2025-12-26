using tuvendedorback.DTOs;

namespace tuvendedorback.Repositories.Interfaces;

public interface IConversacionRepository
{
    Task<int> ObtenerOCrearConversacion(string canal, string identificador);
    Task RegistrarMensaje(int idConversacion, string emisor, string mensaje);
    Task<ConversacionContextoDto?> ObtenerContexto(int idConversacion);
    Task ActualizarContexto(int idConversacion, string pasoActual, string? intencion = null, int? idPublicacion = null, string codigoPrompt = null);
    Task<IEnumerable<MensajeConversacionDto>> ObtenerHistorialIA(int idConversacion, int limite = 10);

}

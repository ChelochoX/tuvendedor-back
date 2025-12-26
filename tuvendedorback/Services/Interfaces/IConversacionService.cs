using tuvendedorback.Request;

namespace tuvendedorback.Services.Interfaces;

public interface IConversacionService
{
    Task<string> ProcesarMensaje(ProcesarMensajeRequest request);
}

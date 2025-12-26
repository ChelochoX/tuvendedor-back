using tuvendedorback.DTOs;
using tuvendedorback.Response;

namespace tuvendedorback.Services.Interfaces;

public interface IAgenteIAService
{
    Task<IAResponse> GenerarRespuesta(int idConversacion, string mensajeUsuario, ConversacionContextoDto? contexto);
}

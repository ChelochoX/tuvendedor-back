using tuvendedorback.Repositories.Interfaces;
using tuvendedorback.Request;
using tuvendedorback.Services.Interfaces;

namespace tuvendedorback.Services;

public class ConversacionService : IConversacionService
{
    private readonly IConversacionRepository _repo;
    private readonly IAgenteIAService _ia;
    private readonly ILogger<ConversacionService> _logger;

    public ConversacionService(IConversacionRepository repo, IAgenteIAService ia, ILogger<ConversacionService> logger)
    {
        _repo = repo;
        _ia = ia;
        _logger = logger;
    }

    public async Task<string> ProcesarMensaje(ProcesarMensajeRequest request)
    {
        var idConv = await _repo.ObtenerOCrearConversacion(
            request.Canal,
            request.IdentificadorExterno);

        await _repo.RegistrarMensaje(idConv, "CLIENTE", request.Mensaje);

        var contexto = await _repo.ObtenerContexto(idConv);

        try
        {

            var respuesta = await _ia.GenerarRespuesta(
            idConv,
            request.Mensaje,
            contexto
            );

            _logger.LogInformation("RESPUESTA IA (BACK): {@Respuesta}", respuesta);

            if (respuesta == null || string.IsNullOrWhiteSpace(respuesta.Texto))
            {
                _logger.LogError("IA devolvió respuesta nula");
                return "En breve te responde un asesor.";
            }

            await _repo.RegistrarMensaje(idConv, "IA", respuesta.Texto);

            await _repo.ActualizarContexto(
                 idConv,
                 respuesta.NuevoPaso,
                 respuesta.Intencion,
                 respuesta.IdPublicacion,
                 contexto?.CodigoPrompt ?? "GENERIC"
             );

            return respuesta.Texto;

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fallback a atención humana");

            // 🔔 ALERTA INTERNA (hook)
            //NotificarAsesor(idConv, request);

            // ✅ MENSAJE AMIGABLE AL CLIENTE
            return @"👋 ¡Hola! Gracias por comunicarte con TU VENDEDOR 🙌  
            En breve te estaré atendiendo personalmente.

            Para poder brindarte una atención más rápida y personalizada,  
            ¿me podrías indicar tu nombre y apellido, por favor? 🙌

            Mientras tanto podés seguir viendo nuestras promos acá:
            www.tuvendedor.com.py";

        }
    }

}

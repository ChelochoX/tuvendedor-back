using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using System.Security.Cryptography;
using System.Text;
using tuvendedorback.DTOs;
using tuvendedorback.Request;
using tuvendedorback.Services.Interfaces;
using tuvendedorback.Repositories.Interfaces;
using tuvendedorback.Wrappers;

namespace tuvendedorback.Controllers;

[ApiController]
[Route("api/ia/motos")]
public class MotoConversacionController
    : ControllerBase
{
    private readonly IMotoConversacionService
        _service;

    private readonly IConfiguration
        _configuration;
    private readonly IIAConversacionRepository _conversaciones;


    public MotoConversacionController(
        IMotoConversacionService service,
        IConfiguration configuration,
        IIAConversacionRepository conversaciones)
    {
        _service = service;
        _configuration = configuration;
        _conversaciones = conversaciones;
    }


    [AllowAnonymous]
    [HttpPost("conversacion")]
    [SwaggerOperation(
        Summary =
            "Procesa conversación comercial de motos con IA",
        Description =
            "Endpoint interno utilizado por el puente de WhatsApp de TuVendedor.")]
    public async Task<IActionResult>
        Conversacion(
            [FromBody]
            MotoConversacionRequest request,
            CancellationToken cancellationToken)
    {
        if (!ClaveInternaValida())
        {
            return Unauthorized(
                new Response<object>
                {
                    Success =
                        false,

                    StatusCode =
                        401,

                    Message =
                        "Acceso interno no autorizado."
                });
        }


        var data = await _service.ProcesarMensaje(request, cancellationToken);

        // Segundo control anti-carrera: si el vendedor tomó el chat mientras
        // se calculaba una respuesta, NO devolverla al bridge para su envío.
        if (data.IdConversacion > 0 && string.Equals(
                await _conversaciones.ObtenerModoConversacion(data.IdConversacion),
                "HUMANO", StringComparison.OrdinalIgnoreCase))
        {
            // Admitir solamente el aviso de derivación que acaba de emitir
            // el propio flujo; nunca enviar ofertas calculadas cuando un
            // operador haya tomado el control del chat.
            var avisoDerivacion =
                data.Respuesta.Contains("te paso con", StringComparison.OrdinalIgnoreCase)
                || data.Respuesta.Contains("te va a responder una persona", StringComparison.OrdinalIgnoreCase)
                || data.Respuesta.Contains("Tu consulta queda para atención personal", StringComparison.OrdinalIgnoreCase);
            if (!avisoDerivacion)
                data.Respuesta = string.Empty;
        }

        return Ok(
            new Response<
                MotoConversacionResponseDto>
            {
                Success =
                    true,

                StatusCode =
                    200,

                Message =
                    "Mensaje procesado correctamente.",

                Data =
                    data
            });
    }


    private bool ClaveInternaValida()
    {
        var claveEsperada =
            _configuration[
                "IA:InternalKey"];


        if (
            string.IsNullOrWhiteSpace(
                claveEsperada)
        )
        {
            return false;
        }


        if (
            !Request.Headers
                .TryGetValue(
                    "X-TuVendedor-Internal-Key",
                    out var header)
        )
        {
            return false;
        }


        var claveRecibida =
            header.FirstOrDefault();


        if (
            string.IsNullOrWhiteSpace(
                claveRecibida)
        )
        {
            return false;
        }


        var expectedBytes =
            Encoding.UTF8.GetBytes(
                claveEsperada);


        var receivedBytes =
            Encoding.UTF8.GetBytes(
                claveRecibida);


        if (
            expectedBytes.Length
            !=
            receivedBytes.Length)
        {
            return false;
        }


        return CryptographicOperations
            .FixedTimeEquals(
                expectedBytes,
                receivedBytes);
    }
}

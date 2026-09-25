using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using System.Security.Cryptography;
using System.Text;
using tuvendedorback.DTOs;
using tuvendedorback.Request;
using tuvendedorback.Services.Interfaces;
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


    public MotoConversacionController(
        IMotoConversacionService service,
        IConfiguration configuration)
    {
        _service =
            service;

        _configuration =
            configuration;
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


        var data =
            await _service
                .ProcesarMensaje(
                    request,
                    cancellationToken);


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

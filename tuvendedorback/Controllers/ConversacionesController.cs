using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using tuvendedorback.Request;
using tuvendedorback.Services.Interfaces;
using tuvendedorback.Wrappers;

namespace tuvendedorback.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ConversacionesController : ControllerBase
{
    private readonly IConversacionService _service;

    public ConversacionesController(IConversacionService service)
    {
        _service = service;
    }

    [AllowAnonymous]
    [HttpPost("procesar-mensaje")]
    [SwaggerOperation(
     Summary = "Procesa un mensaje entrante",
     Description = "Procesa mensajes desde WhatsApp o chat interno usando IA.")]
    public async Task<IActionResult> ProcesarMensaje(
     [FromBody] ProcesarMensajeRequest request)
    {
        var respuesta = await _service.ProcesarMensaje(request);

        return Ok(new Response<string>
        {
            Success = true,
            Data = respuesta
        });
    }
}

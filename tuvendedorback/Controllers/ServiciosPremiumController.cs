using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using tuvendedorback.Common;
using tuvendedorback.Request;
using tuvendedorback.Services.Interfaces;
using tuvendedorback.Wrappers;

namespace tuvendedorback.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ServiciosPremiumController : ControllerBase
{
    private readonly IServicioPremiumService _service;
    private readonly UserContext _userContext;

    public ServiciosPremiumController(
        IServicioPremiumService service,
        UserContext userContext)
    {
        _service = service;
        _userContext = userContext;
    }

    /// <summary>
    /// Registra una solicitud de servicio Premium.
    /// </summary>
    /// <remarks>
    /// Este endpoint es utilizado por vendedores autenticados.
    ///
    /// Permite solicitar:
    /// - Vitrina profesional.
    /// - Publicación destacada.
    /// - Participación en una campaña especial.
    ///
    /// La solicitud se registra antes de abrir WhatsApp.
    /// Posteriormente, un administrador confirma el pago y activa el servicio.
    /// </remarks>
    [HttpPost("solicitar")]
    [SwaggerOperation(
        Summary = "Registra una solicitud Premium",
        Description = "Guarda el interés comercial de un vendedor antes de abrir WhatsApp."
    )]
    public async Task<IActionResult> CrearSolicitud(
        [FromBody] CrearSolicitudServicioPremiumRequest request)
    {
        var idUsuario = _userContext.IdUsuario;

        if (idUsuario == null || idUsuario == 0)
            throw new UnauthorizedAccessException();

        var idServicio =
            await _service.CrearSolicitud(
                request,
                idUsuario.Value);

        return Ok(new Response<int>
        {
            Success = true,
            Data = idServicio,
            Message = "Solicitud Premium registrada correctamente."
        });
    }
}

using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using tuvendedorback.Common;
using tuvendedorback.Request;
using tuvendedorback.Services.Interfaces;
using tuvendedorback.Wrappers;

namespace tuvendedorback.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PublicacionesController : ControllerBase
{
    private readonly IPublicacionService _service;
    private readonly ILogger<PublicacionesController> _logger;
    private readonly UserContext _userContext;

    public PublicacionesController(IPublicacionService service, ILogger<PublicacionesController> logger, UserContext userContext)
    {
        _service = service;
        _logger = logger;
        _userContext = userContext;
    }

    [HttpPost("crear-publicacion")]
    [Consumes("multipart/form-data")]
    [SwaggerOperation(
        Summary = "Crea una nueva publicación",
        Description = "Permite al usuario vendedor crear una publicación con imágenes.")]
    public async Task<IActionResult> Crear([FromForm] CrearPublicacionRequest request)
    {
        var idUsuario = _userContext.IdUsuario;

        if (idUsuario == null || idUsuario == 0)
        {
            return Unauthorized(new Response<object>
            {
                Success = false,
                Errors = new List<string> { "Usuario no autenticado." },
                StatusCode = 401
            });
        }

        var publicacionId = await _service.CrearPublicacion(request, idUsuario.Value);

        return Ok(new Response<object>
        {
            Success = true,
            Message = "Publicación creada correctamente",
            Data = new { Id = publicacionId }
        });
    }
}

using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using tuvendedorback.Models;
using tuvendedorback.Request;
using tuvendedorback.Services.Interfaces;
using tuvendedorback.Wrappers;

namespace tuvendedorback.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsuariosController : ControllerBase
{
    private readonly IUsuarioService _usuarioService;

    public UsuariosController(IUsuarioService usuarioService)
    {
        _usuarioService = usuarioService;
    }

    [HttpPost("validar-credenciales")]
    [SwaggerOperation(
            Summary = "Valida credenciales de acceso",
            Description = "Permite validar el usuario y contraseña, devolviendo los datos del usuario si son correctos")]
    public async Task<IActionResult> ValidarCredenciales([FromBody] LoginRequest request)
    {
        var usuarioDb = await _usuarioService.ValidarCredenciales(request);

        if (usuarioDb == null)
        {
            return Unauthorized(new Response<object>
            {
                Success = false,
                Errors = new List<string> { "Credenciales inválidas." }
            });
        }

        return Ok(new Response<Usuario>
        {
            Success = true,
            Data = usuarioDb
        });
    }
}

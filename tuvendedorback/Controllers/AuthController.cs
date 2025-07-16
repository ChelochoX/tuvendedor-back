using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using tuvendedorback.Request;
using tuvendedorback.Services;
using tuvendedorback.Services.Interfaces;
using tuvendedorback.Wrappers;

namespace tuvendedorback.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IUsuarioService _usuarioService;
    private readonly JwtService _jwtService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IUsuarioService usuarioService, JwtService jwtService, ILogger<AuthController> logger)
    {
        _usuarioService = usuarioService;
        _jwtService = jwtService;
        _logger = logger;
    }

    [HttpPost("login")]
    [SwaggerOperation(
        Summary = "Inicia sesión con email y clave",
        Description = "Valida las credenciales del usuario, verifica su estado y retorna un JWT si todo es válido")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        _logger.LogInformation("Intento de login para el email: {Email}", request.Email);

        var usuario = await _usuarioService.ValidarCredenciales(request);

        if (usuario == null)
        {
            _logger.LogWarning("Login fallido: credenciales inválidas para {Email}", request.Email);
            return Unauthorized(new Response<object>
            {
                Success = false,
                Errors = new List<string> { "Credenciales inválidas." }
            });
        }

        if (usuario.Estado != "Activo")
        {
            _logger.LogWarning("Usuario inactivo: {Email}", request.Email);
            return Unauthorized(new Response<object>
            {
                Success = false,
                Errors = new List<string> { "Usuario inactivo. Contacte al administrador." }
            });
        }

        var roles = await _usuarioService.ObtenerRolesPorUsuario(usuario.Id);

        var token = _jwtService.GenerarToken(
            usuario.Id,
            usuario.NombreUsuario,
            roles
        );

        _logger.LogInformation("Login exitoso para {Email}, roles: {Roles}", request.Email, string.Join(", ", roles));

        return Ok(new Response<object>
        {
            Success = true,
            Data = new
            {
                parTokens = new { bearerToken = token },
                parUsuario = new
                {
                    usuario.Id,
                    usuario.NombreUsuario,
                    usuario.Email,
                    usuario.Estado
                }
            }
        });
    }


    [HttpPost("registro")]
    [SwaggerOperation(Summary = "Registra un nuevo usuario")]
    public async Task<IActionResult> Registrar([FromBody] RegistroRequest request)
    {
        var idUsuario = await _usuarioService.RegistrarUsuario(request);

        return Ok(new Response<object>
        {
            Success = true,
            Data = new { IdUsuario = idUsuario, Mensaje = "Usuario registrado correctamente." }
        });
    }


    [HttpPost("cambiar-clave")]
    [SwaggerOperation(Summary = "Permite a un usuario cambiar su contraseña")]
    public async Task<IActionResult> CambiarClave([FromBody] CambiarClaveRequest request)
    {
        var exito = await _usuarioService.CambiarClave(request);

        if (!exito)
        {
            return BadRequest(new Response<object>
            {
                Success = false,
                Errors = new List<string> { "No se pudo cambiar la contraseña. Verifique sus datos." }
            });
        }

        return Ok(new Response<object>
        {
            Success = true,
            Data = new { Mensaje = "Contraseña actualizada correctamente." }
        });
    }


}

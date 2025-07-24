using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using tuvendedorback.Models;
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
       Summary = "Inicia sesión con email y clave o con proveedor externo",
       Description = "Valida las credenciales o identifica si el usuario existe, y retorna el token o indica si es nuevo.")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        _logger.LogInformation("Intento de login para el email: {Email} usando {TipoLogin}", request.Email, request.TipoLogin);

        Usuario? usuario;

        if (request.TipoLogin == "google" || request.TipoLogin == "facebook")
        {
            usuario = await _usuarioService.ObtenerUsuarioPorProveedor(request);

            if (usuario == null)
            {
                // Usuario nuevo -> se devuelve para mostrar modal registrar
                return Ok(new Response<object>
                {
                    Success = true,
                    Data = new
                    {
                        esNuevo = true,
                        datosPrevios = new
                        {
                            Email = request.Email,
                            Nombre = request.Nombre ?? "",
                            FotoUrl = request.FotoUrl ?? "",
                            TipoLogin = request.TipoLogin,
                            Proveedor = request.TipoLogin,
                            ProveedorId = request.ProveedorId
                        }
                    },
                    Message = "Usuario nuevo detectado por proveedor externo",
                    StatusCode = 200
                });
            }
        }
        else if (request.TipoLogin == "clasico")
        {
            usuario = await _usuarioService.ValidarCredenciales(request);

            if (usuario == null)
            {
                return Unauthorized(new Response<object>
                {
                    Success = false,
                    Errors = new List<string> { "Credenciales inválidas." },
                    StatusCode = 401
                });
            }

            if (usuario.Estado != "Activo")
            {
                return Unauthorized(new Response<object>
                {
                    Success = false,
                    Errors = new List<string> { "Usuario inactivo. Contacte al administrador." },
                    StatusCode = 401
                });
            }
        }
        else
        {
            return BadRequest(new Response<object>
            {
                Success = false,
                Errors = new List<string> { "TipoLogin no soportado." },
                StatusCode = 400
            });
        }

        // Si llegamos acá, el usuario ya está registrado y activo (proveedor o clásico)
        var roles = await _usuarioService.ObtenerRolesPorUsuario(usuario.Id);
        var token = _jwtService.GenerarToken(usuario.Id, usuario.NombreUsuario, roles);

        return Ok(new Response<object>
        {
            Success = true,
            Data = new
            {
                esNuevo = false,
                parTokens = new { bearerToken = token },
                parUsuario = new
                {
                    usuario.Id,
                    usuario.NombreUsuario,
                    usuario.Email,
                    usuario.Estado
                }
            },
            Message = "Login exitoso",
            StatusCode = 200
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

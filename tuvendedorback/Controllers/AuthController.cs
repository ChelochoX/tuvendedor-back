using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using tuvendedorback.Exceptions;
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
                return Ok(new Response<object>
                {
                    Success = false,
                    StatusCode = 401,
                    Message = "Usuario o contraseña incorrectos."
                });

            if (usuario.Estado != "Activo")
                throw new ReglasdeNegocioException("Usuario inactivo. Contacte al administrador.");

        }
        else
        {
            throw new ReglasdeNegocioException("TipoLogin no soportado.");
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
                    usuario.Estado,
                    usuario.FotoPerfil,
                    usuario.Telefono,
                    usuario.Ciudad,
                    usuario.Direccion,
                    Roles = roles
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
            Data = new
            {
                IdUsuario = idUsuario,
                Mensaje = "Usuario registrado correctamente."
            }
        });
    }

    [HttpPost("cambiar-clave")]
    [SwaggerOperation(Summary = "Permite a un usuario cambiar su contraseña")]
    public async Task<IActionResult> CambiarClave([FromBody] CambiarClaveRequest request)
    {
        _logger.LogInformation("Solicitud de cambio de clave recibida para: {EmailOrUser}",
            request.Email ?? request.UsuarioLogin);

        await _usuarioService.CambiarClave(request);

        return Ok(new Response<object>
        {
            Success = true,
            Data = new { Mensaje = "Contraseña actualizada correctamente." }
        });
    }


    [HttpGet("verificar-usuario-login")]
    [SwaggerOperation(Summary = "Verifica si un usuarioLogin ya existe en el sistema")]
    public async Task<IActionResult> VerificarUsuarioLogin([FromQuery] string usuarioLogin)
    {
        if (string.IsNullOrWhiteSpace(usuarioLogin))
            throw new ReglasdeNegocioException("El nombre de usuario no puede estar vacío.");

        var existe = await _usuarioService.ExisteUsuarioLogin(usuarioLogin);
        var disponible = !existe; // 👈 invertir la lógica

        return Ok(new Response<object>
        {
            Success = true,
            Data = new
            {
                UsuarioLogin = usuarioLogin,
                Disponible = disponible
            },
            Message = disponible
                ? "El nombre de usuario está disponible."
                : "El nombre de usuario ya está en uso."
        });
    }
}

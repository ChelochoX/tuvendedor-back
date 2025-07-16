using tuvendedorback.Models;
using tuvendedorback.Request;
using tuvendedorback.Services.Interfaces;
using tuvendedorback.Wrappers;

namespace tuvendedorback.Services;

public class AuthService : IAuthService
{
    private readonly IUsuariosRepository _repo;
    private readonly ILogger<AuthService> _logger;

    public AuthService(ILogger<AuthService> logger, IUsuariosRepository repo)
    {
        _logger = logger;
        _repo = repo;
    }

    public Task<Response<Usuario>> Login(LoginRequest request)
    {
        var response = new Response<Usuario>();

        try
        {
            var usuario = await _repo.ValidarCredenciales(request.Usuario, request.Clave);
            if (usuario == null)
            {
                response.Success = false;
                response.Message = "Credenciales inválidas.";
                response.StatusCode = 401;
                response.Errors.Add("Usuario o contraseña incorrectos.");
                return response;
            }

            response.Data = usuario;
            response.Success = true;
            response.Message = "Login exitoso.";
            response.StatusCode = 200;
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error en login");
            response.Success = false;
            response.Message = "Error interno del servidor.";
            response.StatusCode = 500;
            response.Errors.Add(ex.Message);
            return response;
        }
    }
}
}

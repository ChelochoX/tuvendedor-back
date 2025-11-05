using Microsoft.AspNetCore.Identity;
using tuvendedorback.Common;
using tuvendedorback.Exceptions;
using tuvendedorback.Models;
using tuvendedorback.Repositories.Interfaces;
using tuvendedorback.Request;
using tuvendedorback.Services.Interfaces;

namespace tuvendedorback.Services;

public class UsuarioService : IUsuarioService
{
    private readonly IUsuariosRepository _usuarioRepository;
    private readonly ILogger<UsuarioService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IPasswordHasher<string> _hasher;

    public UsuarioService(IUsuariosRepository usuarioRepository, ILogger<UsuarioService> logger, IServiceProvider serviceProvider, IPasswordHasher<string> hasher)
    {
        _usuarioRepository = usuarioRepository;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _hasher = hasher;
    }

    public async Task<Usuario?> ValidarCredenciales(LoginRequest request)
    {
        await ValidationHelper.ValidarAsync(request, _serviceProvider);

        // Detectamos si es un email o un nombre de usuario
        if (!string.IsNullOrEmpty(request.Email) && request.Email.Contains("@"))
        {
            return await _usuarioRepository.ValidarCredencialesPorEmailYClave(request.Email, request.Clave);
        }
        else if (!string.IsNullOrEmpty(request.UsuarioLogin))
        {
            return await _usuarioRepository.ValidarCredencialesPorUsuarioLoginYClave(request.UsuarioLogin, request.Clave);
        }

        return null;
    }
    public async Task<Usuario?> ObtenerUsuarioPorProveedor(LoginRequest request)
    {
        await ValidationHelper.ValidarAsync(request, _serviceProvider);

        return await _usuarioRepository.ObtenerUsuarioPorProveedor(request);
    }
    public async Task<List<string>> ObtenerRolesPorUsuario(int idUsuario)
    {
        return await _usuarioRepository.ObtenerRolesPorUsuario(idUsuario);
    }
    public async Task<int> RegistrarUsuario(RegistroRequest request)
    {
        await ValidationHelper.ValidarAsync(request, _serviceProvider);

        return await _usuarioRepository.InsertarUsuarioConRol(request);
    }
    public async Task<bool> CambiarClave(CambiarClaveRequest request)
    {
        await ValidationHelper.ValidarAsync(request, _serviceProvider);
        _logger.LogInformation("Iniciando proceso de cambio de clave para usuario: {EmailOrUser}",
            request.Email ?? request.UsuarioLogin);

        try
        {
            // Buscar usuario por email o usuarioLogin
            Usuario? usuario = null;

            if (!string.IsNullOrEmpty(request.Email))
            {
                _logger.LogDebug("Buscando usuario por email: {Email}", request.Email);
                usuario = await _usuarioRepository.ObtenerUsuarioPorEmail(request.Email);
            }
            else if (!string.IsNullOrEmpty(request.UsuarioLogin))
            {
                _logger.LogDebug("Buscando usuario por login: {UsuarioLogin}", request.UsuarioLogin);
                usuario = await _usuarioRepository.ObtenerUsuarioPorLogin(request.UsuarioLogin);
            }

            if (usuario == null)
            {
                _logger.LogWarning("No se encontró el usuario: {EmailOrUser}", request.Email ?? request.UsuarioLogin);
                throw new NoDataFoundException("No se encontró un usuario con los datos proporcionados.");
            }

            if (usuario.Estado != "Activo")
            {
                _logger.LogWarning("El usuario {Usuario} no está activo. Cambio de clave no permitido.", usuario.Email);
                throw new ReglasdeNegocioException("El usuario no está activo. No se puede cambiar la clave.");
            }

            // Hash de la nueva clave
            var nuevaClaveHash = _hasher.HashPassword(null, request.NuevaClave);

            _logger.LogInformation("Actualizando clave en la base de datos para el usuario ID: {IdUsuario}", usuario.Id);
            var cambioExitoso = await _usuarioRepository.ActualizarClaveUsuario(usuario.Id, nuevaClaveHash);

            if (!cambioExitoso)
            {
                _logger.LogWarning("No se pudo actualizar la clave del usuario ID: {IdUsuario}", usuario.Id);
                throw new RepositoryException("Error_CambioClave_Fallido", "No se pudo actualizar la clave del usuario.");
            }

            _logger.LogInformation("Clave actualizada exitosamente para el usuario ID: {IdUsuario}", usuario.Id);
            return true;
        }
        catch (RepositoryException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado durante el cambio de clave para {EmailOrUser}",
                request.Email ?? request.UsuarioLogin);
            throw new ServiceException("Ocurrió un error inesperado al cambiar la contraseña.", ex);
        }
    }


    public async Task<bool> ExisteUsuarioLogin(string usuarioLogin)
    {
        return await _usuarioRepository.ExisteUsuarioLogin(usuarioLogin);
    }
}

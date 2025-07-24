using Microsoft.AspNetCore.Identity;
using tuvendedorback.Common;
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

        return await _usuarioRepository.ValidarCredencialesPorEmailYClave(request.Email, request.Clave);
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
        _logger.LogInformation("Iniciando proceso de cambio de clave para: {Email}", request.Email);

        await ValidationHelper.ValidarAsync(request, _serviceProvider);
        _logger.LogDebug("Validación Fluent completada para: {Email}", request.Email);

        //var usuario = await _usuarioRepository.ObtenerUsuarioPorEmail(request.Email);

        var usuario = new Usuario();

        if (usuario == null)
        {
            _logger.LogWarning("No se encontró un usuario con el email: {Email}", request.Email);
            return false;
        }

        if (usuario.Estado != "Activo")
        {
            _logger.LogWarning("El usuario {Email} no está activo. Cambio de clave no permitido.", request.Email);
            return false;
        }

        var resultado = _hasher.VerifyHashedPassword(null, usuario.ClaveHash, request.ClaveActual);
        if (resultado != PasswordVerificationResult.Success)
        {
            _logger.LogWarning("Clave actual incorrecta para el usuario: {Email}", request.Email);
            return false;
        }

        var nuevaClaveHash = _hasher.HashPassword(null, request.NuevaClave);

        var cambio = await _usuarioRepository.ActualizarClaveUsuario(usuario.Id, nuevaClaveHash);

        if (cambio)
            _logger.LogInformation("Clave actualizada exitosamente para el usuario ID: {IdUsuario}", usuario.Id);
        else
            _logger.LogWarning("No se pudo actualizar la clave para el usuario ID: {IdUsuario}", usuario.Id);

        return cambio;
    }


}

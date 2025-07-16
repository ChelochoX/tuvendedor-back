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

    public UsuarioService(IUsuariosRepository usuarioRepository, ILogger<UsuarioService> logger, IServiceProvider serviceProvider)
    {
        _usuarioRepository = usuarioRepository;
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public async Task<Usuario?> ValidarCredenciales(LoginRequest request)
    {
        await ValidationHelper.ValidarAsync(request, _serviceProvider);

        return await _usuarioRepository.ValidarCredenciales(request);

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
}

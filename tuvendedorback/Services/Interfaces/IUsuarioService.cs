using tuvendedorback.Models;
using tuvendedorback.Request;

namespace tuvendedorback.Services.Interfaces;

public interface IUsuarioService
{
    Task<Usuario?> ValidarCredenciales(LoginRequest request);
    Task<List<string>> ObtenerRolesPorUsuario(int idUsuario);
    Task<int> RegistrarUsuario(RegistroRequest request);
    Task<bool> CambiarClave(CambiarClaveRequest request);

}

using tuvendedorback.Models;
using tuvendedorback.Request;

namespace tuvendedorback.Repositories.Interfaces;

public interface IUsuariosRepository
{
    Task<Usuario?> ValidarCredenciales(LoginRequest request);
    Task<List<string>> ObtenerRolesPorUsuario(int idUsuario);
    Task<int> InsertarUsuarioConRol(RegistroRequest request);
    Task<bool> ActualizarClaveUsuario(int idUsuario, string nuevaClaveHash);
    Task<Usuario?> ObtenerUsuarioPorEmail(string email);
    Task<Usuario?> ObtenerUsuarioActivoPorId(int id);


}

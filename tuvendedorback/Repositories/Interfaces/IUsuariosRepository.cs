using tuvendedorback.Models;
using tuvendedorback.Request;

namespace tuvendedorback.Repositories.Interfaces;

public interface IUsuariosRepository
{
    Task<Usuario?> ValidarCredencialesPorEmailYClave(string email, string clave);
    Task<List<string>> ObtenerRolesPorUsuario(int idUsuario);
    Task<int> InsertarUsuarioConRol(RegistroRequest request);
    Task<bool> ActualizarClaveUsuario(int idUsuario, string nuevaClaveHash);
    Task<Usuario?> ObtenerUsuarioPorProveedor(LoginRequest request);
    Task<Usuario?> ObtenerUsuarioActivoPorId(int id);


}

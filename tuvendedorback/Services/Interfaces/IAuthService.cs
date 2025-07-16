using tuvendedorback.Models;
using tuvendedorback.Request;
using tuvendedorback.Wrappers;

namespace tuvendedorback.Services.Interfaces;

public interface IAuthService
{
    Task<Response<Usuario>> Login(LoginRequest request);
}

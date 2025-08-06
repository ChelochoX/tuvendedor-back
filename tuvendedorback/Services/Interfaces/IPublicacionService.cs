using tuvendedorback.Request;

namespace tuvendedorback.Services.Interfaces;

public interface IPublicacionService
{
    Task<int> CrearPublicacion(CrearPublicacionRequest request, int idUsuario);
}

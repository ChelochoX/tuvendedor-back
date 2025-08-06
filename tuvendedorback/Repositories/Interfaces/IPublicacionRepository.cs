using tuvendedorback.Request;

namespace tuvendedorback.Repositories.Interfaces;

public interface IPublicacionRepository
{
    Task<int> InsertarPublicacion(CrearPublicacionRequest request, int idUsuario, List<string> imagenes);
}

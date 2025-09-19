using tuvendedorback.Models;
using tuvendedorback.Request;

namespace tuvendedorback.Repositories.Interfaces;

public interface IPublicacionRepository
{
    Task<int> InsertarPublicacion(CrearPublicacionRequest request, int idUsuario, List<string> imagenes);
    Task<List<Publicacion>> ObtenerPublicaciones(string? categoria, string? nombre);
}

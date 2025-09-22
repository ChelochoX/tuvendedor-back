using tuvendedorback.DTOs;
using tuvendedorback.Models;
using tuvendedorback.Request;

namespace tuvendedorback.Repositories.Interfaces;

public interface IPublicacionRepository
{
    Task<int> InsertarPublicacion(CrearPublicacionRequest request, int idUsuario, List<ImagenDto> imagenes);
    Task<List<Publicacion>> ObtenerPublicaciones(string? categoria, string? nombre);
}

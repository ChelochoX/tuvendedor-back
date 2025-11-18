using tuvendedorback.DTOs;
using tuvendedorback.Models;
using tuvendedorback.Request;

namespace tuvendedorback.Repositories.Interfaces;

public interface IPublicacionRepository
{
    Task<int> InsertarPublicacion(CrearPublicacionRequest request, int idUsuario, List<ImagenDto> imagenes);
    Task<List<Publicacion>> ObtenerPublicaciones(string? categoria, string? nombre);
    Task<int> EliminarPublicacion(int idPublicacion, int idUsuario);
    Task<IEnumerable<ImagenDto>> ObtenerImagenesPorPublicacion(int idPublicacion, int idUsuario);
    Task<List<Publicacion>> ObtenerMisPublicaciones(int idUsuario);
    Task<List<CategoriaDto>> ObtenerCategoriasActivas();
    Task<bool> EsPublicacionDeUsuario(int idPublicacion, int idUsuario);
    Task CrearOActualizarDestacado(int idPublicacion, DateTime fechaInicio, DateTime fechaFin);
    Task<bool> EstaPublicacionDestacada(int idPublicacion);
}

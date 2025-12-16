using tuvendedorback.DTOs;
using tuvendedorback.Request;

namespace tuvendedorback.Services.Interfaces;

public interface IPublicacionService
{
    Task<int> CrearPublicacion(CrearPublicacionRequest request, int idUsuario);
    Task<List<ProductoDto>> ObtenerPublicaciones(string? categoria, string? nombre);
    Task EliminarPublicacion(int idPublicacion);
    Task<List<ProductoDto>> ObtenerMisPublicaciones(int idUsuario);
    Task<List<CategoriaDto>> ObtenerCategoriasActivas();
    Task DestacarPublicacion(DestacarPublicacionRequest request, int idUsuario);
    Task QuitarDestacadoPublicacion(int idPublicacion, int idUsuario);
    Task ActivarTemporada(ActivarTemporadaRequest request, int idUsuario);
    Task DesactivarTemporada(int idPublicacion, int idUsuario);
    Task<List<TemporadaDto>> ObtenerTemporadasActivas();
    Task<int> CrearSugerencia(CrearSugerenciaRequest request, int? idUsuario);
    Task MarcarComoVendido(int idPublicacion, int idUsuario);
}

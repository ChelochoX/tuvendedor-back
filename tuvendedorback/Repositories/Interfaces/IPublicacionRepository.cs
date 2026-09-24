using tuvendedorback.DTOs;
using tuvendedorback.Request;

namespace tuvendedorback.Repositories.Interfaces;

public interface IPublicacionRepository
{
    Task<int> InsertarPublicacion(
        CrearPublicacionRequest request,
        int idUsuario,
        List<ImagenDto> imagenes);

    Task<List<Publicacion>> ObtenerPublicaciones(
        string? categoria,
        string? nombre,
        int? idUsuario,
        string? visitorId);

    Task<int> EliminarPublicacion(
        int idPublicacion,
        int idUsuario);

    Task<IEnumerable<ImagenDto>>
        ObtenerImagenesPorPublicacion(
            int idPublicacion,
            int idUsuario);

    Task<List<Publicacion>>
        ObtenerMisPublicaciones(
            int idUsuario);

    Task<List<CategoriaDto>>
        ObtenerCategoriasActivas();

    Task<bool> EsPublicacionDeUsuario(
        int idPublicacion,
        int idUsuario);

    Task CrearOActualizarDestacado(
        int idPublicacion,
        DateTime fechaInicio,
        DateTime fechaFin);

    Task QuitarDestacado(
        int idPublicacion);

    Task<bool> EstaPublicacionDestacada(
        int idPublicacion);

    Task ActivarTemporada(
        ActivarTemporadaRequest request);

    Task DesactivarTemporada(
        int idPublicacion);

    Task<bool> UsuarioTienePermiso(
        int idUsuario,
        string nombrePermiso);

    Task<bool> EstaPublicacionEnTemporada(
        int idPublicacion);

    Task<List<TemporadaDto>>
        ObtenerTemporadasActivas();

    Task<int> CrearSugerencia(
        int? idUsuario,
        string comentario);

    Task<bool> PublicacionEstaVendida(
        int idPublicacion);

    Task MarcarComoVendido(
        int idPublicacion);

    Task<bool> EsAdministrador(
        int? idUsuario);

    Task<int> ActualizarPublicacion(
        int idPublicacion,
        int idUsuario,
        ActualizarPublicacionRequest request,
        List<ImagenDto> nuevasImagenes,
        List<string>? imagenesConservar);

    Task<ProductoSharePreviewDto?>
        ObtenerProductoSharePreview(
            int idPublicacion);

    Task<bool> PuedeCrearPublicaciones(
        int idUsuario);

    /*
     * Valida si el vendedor puede utilizar
     * publicaciones del canal VITRINA.
     */
    Task<bool> PuedePublicarEnVitrina(
        int idUsuario);

    /*
     * Se utiliza principalmente al editar:
     * si el front viejo no manda el canal,
     * conservamos el canal actual.
     */
    Task<string?> ObtenerCanalPublicacionDeUsuario(
        int idPublicacion,
        int idUsuario);
}
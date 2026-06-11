using tuvendedorback.DTOs;
using tuvendedorback.Request;

namespace tuvendedorback.Repositories.Interfaces;

public interface IBannerPublicitarioRepository
{
    Task<int> Crear(
        CrearBannerPublicitarioRequest request,
        int idUsuario,
        string imagenDesktopUrl,
        string? imagenMobileUrl);

    Task<int> Actualizar(
        int id,
        ActualizarBannerPublicitarioRequest request,
        int idUsuario,
        string imagenDesktopUrl,
        string? imagenMobileUrl);

    Task<int> CambiarEstado(
        int id,
        string estado,
        int idUsuario);

    Task<int> Eliminar(
        int id,
        int idUsuario);

    Task<BannerPublicitarioDto?> ObtenerPorId(int id);

    Task<(
        List<BannerPublicitarioDto> Items,
        int TotalRegistros
    )> ListarAdmin(FiltroBannersPublicitariosRequest filtro);

    Task<List<BannerPublicitarioPublicoDto>> ObtenerActivosHome();

    Task<ResumenBannersPublicitariosDto> ObtenerResumen();

    Task<int> RegistrarEvento(
        RegistrarBannerEventoRequest request,
        string? userAgent);

    Task<bool> EsAdministrador(int idUsuario);
}

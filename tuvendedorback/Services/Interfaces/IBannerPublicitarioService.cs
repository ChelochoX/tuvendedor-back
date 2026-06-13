using tuvendedorback.DTOs;
using tuvendedorback.Request;
using tuvendedorback.Wrappers;

namespace tuvendedorback.Services.Interfaces;

public interface IBannerPublicitarioService
{
    Task<int> Crear(
       CrearBannerPublicitarioRequest request,
       int idUsuario);

    Task Actualizar(
        int id,
        ActualizarBannerPublicitarioRequest request,
        int idUsuario);

    Task CambiarEstado(
        int id,
        CambiarEstadoBannerPublicitarioRequest request,
        int idUsuario);

    Task Eliminar(
        int id,
        int idUsuario);

    Task<BannerPublicitarioDto> ObtenerPorId(
        int id,
        int idUsuario);

    Task<Datos<List<BannerPublicitarioDto>>> ListarAdmin(
        FiltroBannersPublicitariosRequest filtro,
        int idUsuario);

    Task<ResumenBannersPublicitariosDto> ObtenerResumen(
        int idUsuario);

    Task<List<BannerPublicitarioArchivoDto>> ObtenerArchivos(
        int id,
        int idUsuario);

    Task<LimpiezaBannerArchivosDto>
        LimpiarArchivosCloudinary(
            int limite,
            int idUsuario);

    Task<BannerConfiguracionAdminDto>
        ObtenerConfiguracionAdmin(
            int idUsuario);

    Task<BannersHomeDto> ObtenerActivosHome();

    Task RegistrarEvento(
        RegistrarBannerEventoRequest request,
        string? userAgent);
}

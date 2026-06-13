using tuvendedorback.DTOs;
using tuvendedorback.Request;

namespace tuvendedorback.Repositories.Interfaces;

public interface IBannerPublicitarioRepository
{
    Task<int> Crear(
        CrearBannerPublicitarioRequest request,
        int idUsuario,
        Guid storageKey,
        BannerArchivoUploadResultDto imagenDesktop,
        BannerArchivoUploadResultDto imagenMobile);

    Task<int> Actualizar(
        int id,
        ActualizarBannerPublicitarioRequest request,
        int idUsuario,
        BannerArchivoUploadResultDto? imagenDesktop,
        BannerArchivoUploadResultDto? imagenMobile,
        int diasRetencionArchivos);

    Task<int> CambiarEstado(
        int id,
        string estado,
        int idUsuario);

    Task<int> Eliminar(
        int id,
        int idUsuario,
        int diasRetencionArchivos);

    Task<BannerPublicitarioDto?> ObtenerPorId(int id);

    Task<(
        List<BannerPublicitarioDto> Items,
        int TotalRegistros
    )> ListarAdmin(
        FiltroBannersPublicitariosRequest filtro);

    Task<List<BannerPublicitarioPublicoDto>>
        ObtenerActivosHome();

    Task<ResumenBannersPublicitariosDto>
        ObtenerResumen();

    Task<int> RegistrarEvento(
        RegistrarBannerEventoRequest request,
        string? userAgent);

    Task<bool> EsAdministrador(int idUsuario);

    Task<int> ObtenerSiguienteRevision(
        int idBanner,
        string tipoDispositivo);

    Task<List<BannerPublicitarioArchivoDto>>
        ObtenerArchivos(int idBanner);

    Task<List<BannerArchivoPendienteEliminacionDto>>
        ObtenerArchivosPendientesEliminacion(
            int limite);

    Task MarcarArchivoEliminado(
        long idArchivo);

    Task MarcarErrorEliminacion(
        long idArchivo,
        string error);
}

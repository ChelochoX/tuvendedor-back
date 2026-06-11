using tuvendedorback.Common;
using tuvendedorback.DTOs;
using tuvendedorback.Exceptions;
using tuvendedorback.Repositories.Interfaces;
using tuvendedorback.Request;
using tuvendedorback.Services.Interfaces;
using tuvendedorback.Wrappers;

namespace tuvendedorback.Services;

public class BannerPublicitarioService
    : IBannerPublicitarioService
{
    private readonly IBannerPublicitarioRepository _repository;

    private readonly IImageStorageService _imageStorage;

    private readonly IServiceProvider _serviceProvider;

    private readonly ILogger<BannerPublicitarioService> _logger;

    public BannerPublicitarioService(
        IBannerPublicitarioRepository repository,
        IImageStorageService imageStorage,
        IServiceProvider serviceProvider,
        ILogger<BannerPublicitarioService> logger)
    {
        _repository = repository;
        _imageStorage = imageStorage;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<int> Crear(
        CrearBannerPublicitarioRequest request,
        int idUsuario)
    {
        await ValidarAdministrador(idUsuario);

        await ValidationHelper.ValidarAsync(
            request,
            _serviceProvider);

        Normalizar(request);

        string? imagenDesktopUrl = null;

        string? imagenMobileUrl = null;

        try
        {
            var carpetaDestino =
                $"banners-publicitarios/{request.Ubicacion.ToLowerInvariant()}";

            var desktop =
                await _imageStorage.SubirImagenOptimizada(
                    request.ImagenDesktop,
                    carpetaDestino,
                    width: 1800,
                    height: 1000,
                    calidad: 92);

            imagenDesktopUrl = desktop.MainUrl;

            if (
                request.ImagenMobile != null
                && request.ImagenMobile.Length > 0
            )
            {
                var mobile =
                    await _imageStorage.SubirImagenOptimizada(
                        request.ImagenMobile,
                        carpetaDestino,
                        width: 1080,
                        height: 1350,
                        calidad: 92);

                imagenMobileUrl = mobile.MainUrl;
            }

            return await _repository.Crear(
                request,
                idUsuario,
                imagenDesktopUrl,
                imagenMobileUrl);
        }
        catch
        {
            await EliminarArchivosNuevosSilenciosamente(
                imagenDesktopUrl,
                imagenMobileUrl);

            throw;
        }
    }

    public async Task Actualizar(
        int id,
        ActualizarBannerPublicitarioRequest request,
        int idUsuario)
    {
        await ValidarAdministrador(idUsuario);

        await ValidationHelper.ValidarAsync(
            request,
            _serviceProvider);

        var actual =
            await _repository.ObtenerPorId(id);

        if (actual == null)
        {
            throw new NoDataFoundException(
                "No se encontró el banner publicitario.");
        }

        Normalizar(request);

        var imagenDesktopUrl =
            actual.ImagenDesktopUrl;

        var imagenMobileUrl =
            actual.ImagenMobileUrl;

        string? nuevaImagenDesktopUrl = null;

        string? nuevaImagenMobileUrl = null;

        try
        {
            var carpetaDestino =
                $"banners-publicitarios/{request.Ubicacion.ToLowerInvariant()}";

            if (
                request.ImagenDesktop != null
                && request.ImagenDesktop.Length > 0
            )
            {
                var desktop =
                    await _imageStorage.SubirImagenOptimizada(
                        request.ImagenDesktop,
                        carpetaDestino,
                        width: 1800,
                        height: 1000,
                        calidad: 92);

                nuevaImagenDesktopUrl =
                    desktop.MainUrl;

                imagenDesktopUrl =
                    desktop.MainUrl;
            }

            if (
                request.ImagenMobile != null
                && request.ImagenMobile.Length > 0
            )
            {
                var mobile =
                    await _imageStorage.SubirImagenOptimizada(
                        request.ImagenMobile,
                        carpetaDestino,
                        width: 1080,
                        height: 1350,
                        calidad: 92);

                nuevaImagenMobileUrl =
                    mobile.MainUrl;

                imagenMobileUrl =
                    mobile.MainUrl;
            }
            else if (request.EliminarImagenMobile)
            {
                imagenMobileUrl = null;
            }

            var filas =
                await _repository.Actualizar(
                    id,
                    request,
                    idUsuario,
                    imagenDesktopUrl,
                    imagenMobileUrl);

            if (filas == 0)
            {
                throw new NoDataFoundException(
                    "No se encontró el banner publicitario.");
            }

            if (
                !string.IsNullOrWhiteSpace(
                    nuevaImagenDesktopUrl
                )
                && !string.Equals(
                    actual.ImagenDesktopUrl,
                    nuevaImagenDesktopUrl,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                await EliminarArchivoAnteriorSilenciosamente(
                    actual.ImagenDesktopUrl);
            }

            var debeEliminarImagenMobileAnterior =
                request.EliminarImagenMobile
                ||
                (
                    !string.IsNullOrWhiteSpace(
                        nuevaImagenMobileUrl
                    )
                    && !string.Equals(
                        actual.ImagenMobileUrl,
                        nuevaImagenMobileUrl,
                        StringComparison.OrdinalIgnoreCase
                    )
                );

            if (debeEliminarImagenMobileAnterior)
            {
                await EliminarArchivoAnteriorSilenciosamente(
                    actual.ImagenMobileUrl);
            }
        }
        catch
        {
            await EliminarArchivosNuevosSilenciosamente(
                nuevaImagenDesktopUrl,
                nuevaImagenMobileUrl);

            throw;
        }
    }

    public async Task CambiarEstado(
        int id,
        CambiarEstadoBannerPublicitarioRequest request,
        int idUsuario)
    {
        await ValidarAdministrador(idUsuario);

        await ValidationHelper.ValidarAsync(
            request,
            _serviceProvider);

        var estado =
            request.Estado
                .Trim()
                .ToUpperInvariant();

        var filas =
            await _repository.CambiarEstado(
                id,
                estado,
                idUsuario);

        if (filas == 0)
        {
            throw new NoDataFoundException(
                "No se encontró el banner publicitario.");
        }
    }

    public async Task Eliminar(
        int id,
        int idUsuario)
    {
        await ValidarAdministrador(idUsuario);

        var filas =
            await _repository.Eliminar(
                id,
                idUsuario);

        if (filas == 0)
        {
            throw new NoDataFoundException(
                "No se encontró el banner publicitario.");
        }
    }

    public async Task<BannerPublicitarioDto> ObtenerPorId(
        int id,
        int idUsuario)
    {
        await ValidarAdministrador(idUsuario);

        var banner =
            await _repository.ObtenerPorId(id);

        if (banner == null)
        {
            throw new NoDataFoundException(
                "No se encontró el banner publicitario.");
        }

        return banner;
    }

    public async Task<Datos<List<BannerPublicitarioDto>>>
        ListarAdmin(
            FiltroBannersPublicitariosRequest filtro,
            int idUsuario)
    {
        await ValidarAdministrador(idUsuario);

        await ValidationHelper.ValidarAsync(
            filtro,
            _serviceProvider);

        Normalizar(filtro);

        var resultado =
            await _repository.ListarAdmin(filtro);

        return new Datos<List<BannerPublicitarioDto>>
        {
            Items = resultado.Items,

            TotalRegistros =
                resultado.TotalRegistros
        };
    }

    public async Task<ResumenBannersPublicitariosDto>
        ObtenerResumen(int idUsuario)
    {
        await ValidarAdministrador(idUsuario);

        return await _repository.ObtenerResumen();
    }

    public async Task<BannersHomeDto>
        ObtenerActivosHome()
    {
        var banners =
            await _repository.ObtenerActivosHome();

        return new BannersHomeDto
        {
            HomeTop =
                AplicarReglaExclusividad(
                    banners,
                    BannerPublicitarioConstantes.HomeTop),

            HomeInline =
                AplicarReglaExclusividad(
                    banners,
                    BannerPublicitarioConstantes.HomeInline)
        };
    }

    public async Task RegistrarEvento(
        RegistrarBannerEventoRequest request,
        string? userAgent)
    {
        await ValidationHelper.ValidarAsync(
            request,
            _serviceProvider);

        request.TipoEvento =
            request.TipoEvento
                .Trim()
                .ToUpperInvariant();

        request.Ubicacion =
            request.Ubicacion
                .Trim()
                .ToUpperInvariant();

        request.Dispositivo =
            request.Dispositivo?
                .Trim()
                .ToUpperInvariant();

        request.Pagina =
            LimpiarNullable(request.Pagina);

        request.VisitorId =
            LimpiarNullable(request.VisitorId);

        var filas =
            await _repository.RegistrarEvento(
                request,
                Recortar(userAgent, 500));

        if (filas == 0)
        {
            throw new ReglasdeNegocioException(
                "El banner indicado no existe, no está vigente o no corresponde a la ubicación informada.");
        }
    }

    private async Task ValidarAdministrador(int idUsuario)
    {
        if (idUsuario <= 0)
            throw new UnauthorizedAccessException();

        var esAdministrador =
            await _repository.EsAdministrador(idUsuario);

        if (!esAdministrador)
            throw new UnauthorizedAccessException();
    }

    private static List<BannerPublicitarioPublicoDto>
        AplicarReglaExclusividad(
            IEnumerable<BannerPublicitarioPublicoDto> banners,
            string ubicacion)
    {
        var bannersUbicacion =
            banners
                .Where(
                    banner =>
                        banner.Ubicacion.Equals(
                            ubicacion,
                            StringComparison.OrdinalIgnoreCase
                        )
                )
                .OrderByDescending(
                    banner => banner.Prioridad)
                .ThenBy(
                    banner => banner.Orden)
                .ThenByDescending(
                    banner => banner.Id)
                .ToList();

        var exclusivos =
            bannersUbicacion
                .Where(
                    banner => banner.EsExclusivo)
                .ToList();

        return exclusivos.Any()
            ? exclusivos
            : bannersUbicacion;
    }

    private static void Normalizar(
        IBannerPublicitarioRequestBase request)
    {
        request.NombreCliente =
            request.NombreCliente.Trim();

        request.Ubicacion =
            request.Ubicacion
                .Trim()
                .ToUpperInvariant();

        request.Titulo =
            request.Titulo.Trim();

        request.Subtitulo =
            LimpiarNullable(request.Subtitulo);

        request.Descripcion =
            LimpiarNullable(request.Descripcion);

        request.Etiqueta =
            string.IsNullOrWhiteSpace(request.Etiqueta)
                ? "Publicidad"
                : request.Etiqueta.Trim();

        request.TextoBoton =
            LimpiarNullable(request.TextoBoton);

        request.UrlDestino =
            LimpiarNullable(request.UrlDestino);

        request.WhatsappUrl =
            LimpiarNullable(request.WhatsappUrl);

        request.Estado =
            request.Estado
                .Trim()
                .ToUpperInvariant();
    }

    private static void Normalizar(
        FiltroBannersPublicitariosRequest filtro)
    {
        filtro.Busqueda =
            LimpiarNullable(filtro.Busqueda);

        filtro.Ubicacion =
            LimpiarNullable(filtro.Ubicacion)?
                .ToUpperInvariant();

        filtro.Estado =
            LimpiarNullable(filtro.Estado)?
                .ToUpperInvariant();
    }

    private async Task
        EliminarArchivosNuevosSilenciosamente(
            params string?[] urls)
    {
        foreach (
            var url
            in urls
                .Where(
                    valor =>
                        !string.IsNullOrWhiteSpace(valor))
                .Distinct()
        )
        {
            try
            {
                await _imageStorage.EliminarArchivo(url!);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "No se pudo limpiar archivo recién subido. Url={Url}",
                    url);
            }
        }
    }

    private async Task
        EliminarArchivoAnteriorSilenciosamente(
            string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        try
        {
            await _imageStorage.EliminarArchivo(url);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "No se pudo eliminar archivo anterior. Url={Url}",
                url);
        }
    }

    private static string? LimpiarNullable(
        string? valor)
    {
        return string.IsNullOrWhiteSpace(valor)
            ? null
            : valor.Trim();
    }

    private static string? Recortar(
        string? valor,
        int longitudMaxima)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return null;

        var texto =
            valor.Trim();

        return texto.Length <= longitudMaxima
            ? texto
            : texto[..longitudMaxima];
    }
}

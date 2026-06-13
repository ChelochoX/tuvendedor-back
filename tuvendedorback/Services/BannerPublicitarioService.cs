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

    private readonly IBannerImageStorageService _bannerStorage;

    private readonly IServiceProvider _serviceProvider;

    private readonly ILogger<BannerPublicitarioService> _logger;

    private readonly int _diasRetencionArchivos;

    public BannerPublicitarioService(
        IBannerPublicitarioRepository repository,
        IBannerImageStorageService bannerStorage,
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<BannerPublicitarioService> logger)
    {
        _repository = repository;

        _bannerStorage = bannerStorage;

        _serviceProvider = serviceProvider;

        _logger = logger;

        _diasRetencionArchivos =
            Math.Clamp(
                configuration.GetValue<int?>(
                    "Cloudinary:BannerRetentionDays")
                ?? BannerPublicitarioConstantes
                    .DiasRetencionArchivosDefault,
                1,
                90);
    }

    /*
      ==========================================================
      CREAR BANNER
      ==========================================================
    */
    public async Task<int> Crear(
        CrearBannerPublicitarioRequest request,
        int idUsuario)
    {
        await ValidarAdministrador(idUsuario);

        await ValidationHelper.ValidarAsync(
            request,
            _serviceProvider);

        Normalizar(request);

        var storageKey =
            Guid.NewGuid();

        var assetFolder =
            _bannerStorage.ConstruirAssetFolder(
                storageKey,
                request.NombreCliente,
                request.Ubicacion);

        BannerArchivoUploadResultDto? desktop =
            null;

        BannerArchivoUploadResultDto? mobile =
            null;

        try
        {
            desktop =
                await _bannerStorage.SubirImagen(
                    request.ImagenDesktop,
                    assetFolder,
                    storageKey,
                    BannerPublicitarioConstantes
                        .DispositivoDesktop,
                    revision: 1,
                    BannerPublicitarioConstantes
                        .ObtenerDimensionEsperada(
                            request.Ubicacion,
                            BannerPublicitarioConstantes
                                .DispositivoDesktop));

            mobile =
                await _bannerStorage.SubirImagen(
                    request.ImagenMobile,
                    assetFolder,
                    storageKey,
                    BannerPublicitarioConstantes
                        .DispositivoMobile,
                    revision: 1,
                    BannerPublicitarioConstantes
                        .ObtenerDimensionEsperada(
                            request.Ubicacion,
                            BannerPublicitarioConstantes
                                .DispositivoMobile));

            return await _repository.Crear(
                request,
                idUsuario,
                storageKey,
                desktop,
                mobile);
        }
        catch
        {
            /*
              Si Cloudinary subió una imagen, pero luego falla SQL
              o falla la segunda imagen, limpiamos únicamente los
              archivos nuevos. Nunca tocamos imágenes activas.
            */
            await EliminarNuevosSilenciosamente(
                desktop,
                mobile);

            throw;
        }
    }

    /*
      ==========================================================
      ACTUALIZAR DATOS Y REEMPLAZAR IMÁGENES OPCIONALES
      ==========================================================
    */
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

        var cambioUbicacion =
            !actual.Ubicacion.Equals(
                request.Ubicacion,
                StringComparison.OrdinalIgnoreCase);

        /*
          HOME_TOP y HOME_INLINE tienen medidas diferentes.
          Si cambia la ubicación, necesitamos ambos artes nuevos.
        */
        if (
            cambioUbicacion
            && (
                request.ImagenDesktop == null
                || request.ImagenMobile == null
            )
        )
        {
            throw new ReglasdeNegocioException(
                "Al cambiar la ubicación del banner debés " +
                "adjuntar nuevamente las imágenes desktop y " +
                "mobile con las medidas de la nueva posición.");
        }

        /*
          Conservamos la carpeta estable de la campaña.
          Si cambia de posición, generamos una carpeta ordenada
          para la nueva ubicación.
        */
        var assetFolder =
            cambioUbicacion
                ? _bannerStorage.ConstruirAssetFolder(
                    actual.StorageKey,
                    request.NombreCliente,
                    request.Ubicacion)
                : !string.IsNullOrWhiteSpace(
                    actual.CloudinaryAssetFolder)
                    ? actual.CloudinaryAssetFolder
                    : _bannerStorage.ConstruirAssetFolder(
                        actual.StorageKey,
                        actual.NombreCliente,
                        actual.Ubicacion);

        BannerArchivoUploadResultDto? desktop =
            null;

        BannerArchivoUploadResultDto? mobile =
            null;

        try
        {
            if (
                request.ImagenDesktop != null
                && request.ImagenDesktop.Length > 0
            )
            {
                var revision =
                    await _repository
                        .ObtenerSiguienteRevision(
                            id,
                            BannerPublicitarioConstantes
                                .DispositivoDesktop);

                desktop =
                    await _bannerStorage.SubirImagen(
                        request.ImagenDesktop,
                        assetFolder,
                        actual.StorageKey,
                        BannerPublicitarioConstantes
                            .DispositivoDesktop,
                        revision,
                        BannerPublicitarioConstantes
                            .ObtenerDimensionEsperada(
                                request.Ubicacion,
                                BannerPublicitarioConstantes
                                    .DispositivoDesktop));
            }

            if (
                request.ImagenMobile != null
                && request.ImagenMobile.Length > 0
            )
            {
                var revision =
                    await _repository
                        .ObtenerSiguienteRevision(
                            id,
                            BannerPublicitarioConstantes
                                .DispositivoMobile);

                mobile =
                    await _bannerStorage.SubirImagen(
                        request.ImagenMobile,
                        assetFolder,
                        actual.StorageKey,
                        BannerPublicitarioConstantes
                            .DispositivoMobile,
                        revision,
                        BannerPublicitarioConstantes
                            .ObtenerDimensionEsperada(
                                request.Ubicacion,
                                BannerPublicitarioConstantes
                                    .DispositivoMobile));
            }

            var filas =
                await _repository.Actualizar(
                    id,
                    request,
                    idUsuario,
                    desktop,
                    mobile,
                    _diasRetencionArchivos);

            if (filas == 0)
            {
                throw new NoDataFoundException(
                    "No se encontró el banner publicitario.");
            }
        }
        catch
        {
            /*
              Si no logramos confirmar SQL, eliminamos solamente
              la revisión nueva. La campaña conserva la imagen
              anterior y no queda rota.
            */
            await EliminarNuevosSilenciosamente(
                desktop,
                mobile);

            throw;
        }
    }

    /*
      ==========================================================
      CAMBIAR ESTADO
      ==========================================================
    */
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

    /*
      ==========================================================
      ELIMINACIÓN LÓGICA
      ==========================================================
    */
    public async Task Eliminar(
        int id,
        int idUsuario)
    {
        await ValidarAdministrador(idUsuario);

        var filas =
            await _repository.Eliminar(
                id,
                idUsuario,
                _diasRetencionArchivos);

        if (filas == 0)
        {
            throw new NoDataFoundException(
                "No se encontró el banner publicitario.");
        }
    }

    /*
      ==========================================================
      CONSULTAS ADMINISTRATIVAS
      ==========================================================
    */
    public async Task<BannerPublicitarioDto> ObtenerPorId(
        int id,
        int idUsuario)
    {
        await ValidarAdministrador(idUsuario);

        return await _repository.ObtenerPorId(id)
            ?? throw new NoDataFoundException(
                "No se encontró el banner publicitario.");
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
            Items =
                resultado.Items,

            TotalRegistros =
                resultado.TotalRegistros
        };
    }

    public async Task<ResumenBannersPublicitariosDto>
        ObtenerResumen(
            int idUsuario)
    {
        await ValidarAdministrador(idUsuario);

        return await _repository.ObtenerResumen();
    }

    public async Task<List<BannerPublicitarioArchivoDto>>
        ObtenerArchivos(
            int id,
            int idUsuario)
    {
        await ValidarAdministrador(idUsuario);

        if (
            await _repository.ObtenerPorId(id)
            == null
        )
        {
            throw new NoDataFoundException(
                "No se encontró el banner publicitario.");
        }

        return await _repository.ObtenerArchivos(id);
    }

    public async Task<BannerConfiguracionAdminDto>
        ObtenerConfiguracionAdmin(
            int idUsuario)
    {
        await ValidarAdministrador(idUsuario);

        return BannerPublicitarioConstantes
            .ObtenerConfiguracionAdmin();
    }

    /*
      ==========================================================
      LIMPIEZA DIFERIDA DE CLOUDINARY
      ==========================================================
    */
    public async Task<LimpiezaBannerArchivosDto>
        LimpiarArchivosCloudinary(
            int limite,
            int idUsuario)
    {
        await ValidarAdministrador(idUsuario);

        if (
            limite < 1
            || limite > 500
        )
        {
            throw new ReglasdeNegocioException(
                "El límite debe estar entre 1 y 500.");
        }

        var pendientes =
            await _repository
                .ObtenerArchivosPendientesEliminacion(
                    limite);

        var resultado =
            new LimpiezaBannerArchivosDto
            {
                Revisados =
                    pendientes.Count
            };

        foreach (var archivo in pendientes)
        {
            try
            {
                /*
                  Los archivos demo cargados manualmente antes del
                  administrador no poseen PublicId en SQL.
                  No los borramos automáticamente.
                */
                if (
                    string.IsNullOrWhiteSpace(
                        archivo.CloudinaryPublicId)
                )
                {
                    throw new ReglasdeNegocioException(
                        "El archivo no posee PublicId porque fue " +
                        "cargado manualmente antes del " +
                        "administrador. Eliminá ese archivo legacy " +
                        "manualmente desde Cloudinary.");
                }

                await _bannerStorage.EliminarImagen(
                    archivo.CloudinaryPublicId);

                await _repository.MarcarArchivoEliminado(
                    archivo.Id);

                resultado.Eliminados++;
            }
            catch (Exception ex)
            {
                await _repository.MarcarErrorEliminacion(
                    archivo.Id,
                    ex.Message);

                resultado.ConError++;
            }
        }

        return resultado;
    }

    /*
      ==========================================================
      CONSULTA PÚBLICA DEL HOME
      ==========================================================
    */
    public async Task<BannersHomeDto> ObtenerActivosHome()
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

    /*
      ==========================================================
      REGISTRO PÚBLICO DE MÉTRICAS
      ==========================================================
    */
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
            LimpiarNullable(
                request.Pagina);

        request.VisitorId =
            LimpiarNullable(
                request.VisitorId);

        var filas =
            await _repository.RegistrarEvento(
                request,
                Recortar(
                    userAgent,
                    500));

        if (filas == 0)
        {
            throw new ReglasdeNegocioException(
                "El banner indicado no existe, no está vigente " +
                "o no corresponde a la ubicación informada.");
        }
    }

    /*
      ==========================================================
      HELPERS PRIVADOS
      ==========================================================
    */
    private async Task ValidarAdministrador(
        int idUsuario)
    {
        if (
            idUsuario <= 0
            || !await _repository
                .EsAdministrador(idUsuario)
        )
        {
            throw new UnauthorizedAccessException();
        }
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
                            StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(
                    banner =>
                        banner.Prioridad)
                .ThenBy(
                    banner =>
                        banner.Orden)
                .ThenByDescending(
                    banner =>
                        banner.Id)
                .ToList();

        var exclusivos =
            bannersUbicacion
                .Where(
                    banner =>
                        banner.EsExclusivo)
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
            LimpiarNullable(
                request.Subtitulo);

        request.Descripcion =
            LimpiarNullable(
                request.Descripcion);

        request.Etiqueta =
            string.IsNullOrWhiteSpace(
                request.Etiqueta)
                ? "Publicidad"
                : request.Etiqueta.Trim();

        request.TextoBoton =
            LimpiarNullable(
                request.TextoBoton);

        request.TipoDestino =
            request.TipoDestino
                .Trim()
                .ToUpperInvariant();

        request.UrlDestino =
            NormalizarDestino(
                request.TipoDestino,
                request.UrlDestino);

        request.WhatsappUrl =
            NormalizarWhatsapp(
                request.WhatsappUrl);

        request.TextoBotonWhatsapp =
            string.IsNullOrWhiteSpace(
                request.TextoBotonWhatsapp)
                ? "Escribir por WhatsApp"
                : request
                    .TextoBotonWhatsapp
                    .Trim();

        request.Estado =
            request.Estado
                .Trim()
                .ToUpperInvariant();
    }

    private static void Normalizar(
        FiltroBannersPublicitariosRequest filtro)
    {
        filtro.Busqueda =
            LimpiarNullable(
                filtro.Busqueda);

        filtro.Ubicacion =
            LimpiarNullable(
                filtro.Ubicacion)?
                .ToUpperInvariant();

        filtro.Estado =
            LimpiarNullable(
                filtro.Estado)?
                .ToUpperInvariant();
    }

    private static string? NormalizarDestino(
        string tipoDestino,
        string? valor)
    {
        var destino =
            LimpiarNullable(valor);

        if (
            tipoDestino
            ==
            BannerPublicitarioConstantes
                .TipoDestinoWhatsapp
        )
        {
            return NormalizarWhatsapp(
                destino);
        }

        return destino;
    }

    private static string? NormalizarWhatsapp(
        string? valor)
    {
        var texto =
            LimpiarNullable(valor);

        if (texto == null)
        {
            return null;
        }

        if (
            Uri.TryCreate(
                texto,
                UriKind.Absolute,
                out var uri)
            && (
                uri.Scheme
                    == Uri.UriSchemeHttp
                || uri.Scheme
                    == Uri.UriSchemeHttps
            )
        )
        {
            return uri.AbsoluteUri;
        }

        var numero =
            new string(
                texto
                    .Where(char.IsDigit)
                    .ToArray());

        return string.IsNullOrWhiteSpace(
            numero)
            ? null
            : $"https://wa.me/{numero}";
    }

    private async Task EliminarNuevosSilenciosamente(
        params BannerArchivoUploadResultDto?[] archivos)
    {
        foreach (
            var archivo
            in archivos
                .Where(
                    item =>
                        item != null)
                .DistinctBy(
                    item =>
                        item!.PublicId)
        )
        {
            try
            {
                await _bannerStorage.EliminarImagen(
                    archivo!.PublicId);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "No se pudo limpiar imagen recién subida. " +
                    "PublicId={PublicId}",
                    archivo!.PublicId);
            }
        }
    }

    private static string? LimpiarNullable(
        string? valor)
    {
        return string.IsNullOrWhiteSpace(
            valor)
            ? null
            : valor.Trim();
    }

    private static string? Recortar(
        string? valor,
        int longitudMaxima)
    {
        if (
            string.IsNullOrWhiteSpace(
                valor)
        )
        {
            return null;
        }

        var texto =
            valor.Trim();

        return texto.Length
            <= longitudMaxima
            ? texto
            : texto[..longitudMaxima];
    }
}

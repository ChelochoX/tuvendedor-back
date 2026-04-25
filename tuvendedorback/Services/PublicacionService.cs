using AutoMapper;
using tuvendedorback.Common;
using tuvendedorback.DTOs;
using tuvendedorback.Exceptions;
using tuvendedorback.Repositories.Interfaces;
using tuvendedorback.Request;
using tuvendedorback.Services.Interfaces;

namespace tuvendedorback.Services;

public class PublicacionService : IPublicacionService
{
    private readonly IImageStorageService _imageStorage;
    private readonly IPublicacionRepository _repository;
    private readonly IServiceProvider _serviceProvider;
    private readonly IMapper _mapper;
    private readonly UserContext _userContext;
    private readonly ILogger<PublicacionService> _logger;

    public PublicacionService(IImageStorageService imageStorage, IPublicacionRepository repository, IServiceProvider provider, IMapper mapper, UserContext userContext, ILogger<PublicacionService> logger)
    {
        _imageStorage = imageStorage;
        _repository = repository;
        _serviceProvider = provider;
        _mapper = mapper;
        _userContext = userContext;
        _logger = logger;
    }

    public async Task<int> CrearPublicacion(CrearPublicacionRequest request, int idUsuario)
    {
        await ValidationHelper.ValidarAsync(request, _serviceProvider);

        var imagenes = new List<ImagenDto>();

        var carpetaDestino = $"vendedores/{idUsuario}/publicaciones";

        foreach (var img in request.Imagenes)
        {
            var result = await _imageStorage.SubirArchivo(
                img,
                carpetaDestino,
                generarMiniatura: false
            );

            imagenes.Add(new ImagenDto
            {
                MainUrl = result.MainUrl,
                ThumbUrl = result.ThumbUrl
            });
        }

        return await _repository.InsertarPublicacion(request, idUsuario, imagenes);
    }

    public async Task<List<ProductoDto>> ObtenerPublicaciones(string? categoria, string? nombre)
    {
        var publicaciones = await _repository.ObtenerPublicaciones(categoria, nombre);
        return _mapper.Map<List<ProductoDto>>(publicaciones);
    }

    public async Task EliminarPublicacion(int idPublicacion)
    {
        var idUsuario = _userContext.IdUsuario;

        if (idUsuario == null || idUsuario == 0)
            throw new UnauthorizedAccessException();

        await ValidarAccesoPublicacion(
            idPublicacion,
            idUsuario.Value,
            "EliminarPublicacion"
        );

        var imagenes = await _repository.ObtenerImagenesPorPublicacion(
            idPublicacion,
            idUsuario.Value
        );

        if (imagenes != null && imagenes.Any())
        {
            foreach (var img in imagenes)
            {
                if (!string.IsNullOrWhiteSpace(img.MainUrl))
                {
                    await _imageStorage.EliminarArchivo(img.MainUrl);
                }

                if (!string.IsNullOrWhiteSpace(img.ThumbUrl) &&
                    !string.Equals(img.ThumbUrl, img.MainUrl, StringComparison.OrdinalIgnoreCase))
                {
                    await _imageStorage.EliminarArchivo(img.ThumbUrl);
                }
            }
        }

        var filasAfectadas = await _repository.EliminarPublicacion(
            idPublicacion,
            idUsuario.Value
        );

        if (filasAfectadas == 0)
            throw new ReglasdeNegocioException(
                "No se encontró la publicación o no tienes permiso para eliminarla.");
    }

    public async Task<List<ProductoDto>> ObtenerMisPublicaciones(int idUsuario)
    {
        var publicaciones = await _repository.ObtenerMisPublicaciones(idUsuario);
        return _mapper.Map<List<ProductoDto>>(publicaciones);
    }

    public async Task<List<CategoriaDto>> ObtenerCategoriasActivas()
    {
        return await _repository.ObtenerCategoriasActivas();
    }

    public async Task DestacarPublicacion(DestacarPublicacionRequest request, int idUsuario)
    {
        // ✅ Validar el request con FluentValidation
        await ValidationHelper.ValidarAsync(request, _serviceProvider);

        await ValidarAccesoPublicacion(
            request.IdPublicacion,
            idUsuario,
            "CrearPublicacionDestacada"
        );

        //Validar si YA ESTÁ destacada actualmente
        var yaEstaDestacada = await _repository.EstaPublicacionDestacada(request.IdPublicacion);

        if (yaEstaDestacada)
            throw new ReglasdeNegocioException("Esta publicación ya está destacada actualmente.");

        var fechaInicio = DateTime.Now;
        var fechaFin = fechaInicio.AddDays(request.DuracionDias);

        //Registrar o actualizar el destacado
        await _repository.CrearOActualizarDestacado(request.IdPublicacion, fechaInicio, fechaFin);
    }

    public async Task QuitarDestacadoPublicacion(int idPublicacion, int idUsuario)
    {
        await ValidarAccesoPublicacion(
            idPublicacion,
            idUsuario,
            "QuitarPublicacionDestacada"
        );

        // Validar si está destacada actualmente
        var estaDestacada = await _repository.EstaPublicacionDestacada(idPublicacion);

        if (!estaDestacada)
            throw new ReglasdeNegocioException(
                "La publicación no se encuentra destacada."
            );

        // Quitar destacado
        await _repository.QuitarDestacado(idPublicacion);
    }

    public async Task ActivarTemporada(ActivarTemporadaRequest request, int idUsuario)
    {
        //Validar request
        await ValidationHelper.ValidarAsync(request, _serviceProvider);

        await ValidarAccesoPublicacion(
            request.IdPublicacion,
            idUsuario,
            "CrearPublicacionTemporada"
        );


        //Registrar
        await _repository.ActivarTemporada(request);
    }

    public async Task DesactivarTemporada(int idPublicacion, int idUsuario)
    {
        //Validar con FluentValidation
        await ValidationHelper.ValidarAsync(new DesactivarTemporadaRequest { IdPublicacion = idPublicacion }, _serviceProvider);

        await ValidarAccesoPublicacion(
            idPublicacion,
            idUsuario,
            "QuitarPublicacionTemporada"
        );

        //Ejecutar acción
        await _repository.DesactivarTemporada(idPublicacion);
    }

    public async Task<List<TemporadaDto>> ObtenerTemporadasActivas()
    {
        return await _repository.ObtenerTemporadasActivas();
    }

    public async Task<int> CrearSugerencia(CrearSugerenciaRequest request, int? idUsuario)
    {
        await ValidationHelper.ValidarAsync(request, _serviceProvider);

        return await _repository.CrearSugerencia(idUsuario, request.Comentario);
    }

    public async Task MarcarComoVendido(int idPublicacion, int idUsuario)
    {
        // 1️⃣ Validar que la publicación sea del usuario
        var esDeUsuario = await _repository.EsPublicacionDeUsuario(idPublicacion, idUsuario);

        if (!esDeUsuario)
            throw new ReglasdeNegocioException("No puedes marcar como vendida una publicación que no te pertenece.");

        // 2️⃣ Ver si ya está vendida
        var yaVendida = await _repository.PublicacionEstaVendida(idPublicacion);
        if (yaVendida)
            throw new ReglasdeNegocioException("La publicación ya está marcada como vendida.");

        // 3️⃣ Actualizar estado
        await _repository.MarcarComoVendido(idPublicacion);
    }

    private async Task ValidarAccesoPublicacion(int idPublicacion, int idUsuario, string permisoRequerido)
    {
        //ADMIN → puede TODO
        var esAdmin = await _repository.EsAdministrador(idUsuario);
        if (esAdmin)
            return;

        //Permiso requerido
        var tienePermiso = await _repository.UsuarioTienePermiso(idUsuario, permisoRequerido);
        if (!tienePermiso)
            throw new ReglasdeNegocioException(
                $"No tienes permiso para realizar esta acción ({permisoRequerido})."
            );

        //Debe ser dueño
        var esDeUsuario = await _repository.EsPublicacionDeUsuario(idPublicacion, idUsuario);
        if (!esDeUsuario)
            throw new ReglasdeNegocioException(
                "No puedes realizar esta acción sobre una publicación que no te pertenece."
            );
    }

    public async Task ActualizarPublicacion(int idPublicacion, ActualizarPublicacionRequest request)
    {
        var idUsuario = _userContext.IdUsuario;

        if (idUsuario == null || idUsuario == 0)
            throw new UnauthorizedAccessException();

        await ValidationHelper.ValidarAsync(request, _serviceProvider);

        await ValidarAccesoPublicacion(
            idPublicacion,
            idUsuario.Value,
            "ActualizarPublicacion"
        );

        request.Moneda = string.IsNullOrWhiteSpace(request.Moneda)
        ? "PYG"
        : request.Moneda.Trim().ToUpper();

        var esInmueble = EsCategoriaInmobiliaria(request.Categoria);

        if (esInmueble)
        {
            request.Ubicacion = string.IsNullOrWhiteSpace(request.Ubicacion)
                ? _userContext.Ubicacion ?? ""
                : request.Ubicacion.Trim();

            request.GoogleMapsUrl = request.GoogleMapsUrl?.Trim();

            if (string.IsNullOrWhiteSpace(request.GoogleMapsUrl)
                && request.Latitud.HasValue
                && request.Longitud.HasValue)
            {
                request.GoogleMapsUrl =
                    $"https://www.google.com/maps?q={request.Latitud.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)},{request.Longitud.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
            }
        }
        else
        {
            request.Ubicacion = _userContext.Ubicacion ?? "";
            request.Latitud = null;
            request.Longitud = null;
            request.GoogleMapsUrl = null;
        }

        var nuevasImagenes = new List<ImagenDto>();

        try
        {
            if (request.Imagenes != null && request.Imagenes.Any())
            {
                foreach (var archivo in request.Imagenes)
                {
                    var resultado = await _imageStorage.SubirArchivo(
                        archivo,
                        carpetaDestino: "publicaciones",
                        generarMiniatura: true);

                    nuevasImagenes.Add(new ImagenDto
                    {
                        MainUrl = resultado.MainUrl,
                        ThumbUrl = resultado.ThumbUrl
                    });
                }
            }

            var filasAfectadas = await _repository.ActualizarPublicacion(
                idPublicacion,
                idUsuario.Value,
                request,
                nuevasImagenes);

            if (filasAfectadas == 0)
                throw new ReglasdeNegocioException(
                    "No se encontró la publicación o no tienes permiso para actualizarla.");
        }
        catch
        {
            if (nuevasImagenes.Any())
            {
                foreach (var img in nuevasImagenes)
                {
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(img.MainUrl))
                            await _imageStorage.EliminarArchivo(img.MainUrl);

                        if (!string.IsNullOrWhiteSpace(img.ThumbUrl) &&
                            !string.Equals(img.ThumbUrl, img.MainUrl, StringComparison.OrdinalIgnoreCase))
                        {
                            await _imageStorage.EliminarArchivo(img.ThumbUrl);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex,
                            "Error al limpiar imagen subida luego de fallo en actualización. IdPublicacion={IdPublicacion}, MainUrl={MainUrl}",
                            idPublicacion,
                            img.MainUrl);
                    }
                }
            }

            throw;
        }
    }

    private static bool EsCategoriaInmobiliaria(string? categoria)
    {
        if (string.IsNullOrWhiteSpace(categoria))
            return false;

        var texto = categoria.Trim().ToLower();

        string[] categoriasInmobiliarias =
        {
        "inmueble",
        "inmuebles",
        "terreno",
        "terrenos",
        "casa",
        "casas",
        "departamento",
        "departamentos",
        "dúplex",
        "duplex",
        "salon",
        "salón",
        "salones",
        "local",
        "locales",
        "oficina",
        "oficinas",
        "quinta",
        "quintas",
        "lote",
        "lotes",
        "deposito",
        "depósito",
        "depósitos",
        "tinglado",
        "tinglados",
        "campo",
        "campos",
        "alquiler",
        "alquileres",
        "propiedad",
        "propiedades",
        "monoambiente",
        "monoambientes",
        "habitacion",
        "habitación",
        "habitaciones",
        "garaje",
        "garajes"
    };

        return categoriasInmobiliarias.Any(x => texto.Contains(x));
    }
}

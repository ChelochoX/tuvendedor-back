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

    public PublicacionService(IImageStorageService imageStorage, IPublicacionRepository repository, IServiceProvider provider, IMapper mapper, UserContext userContext)
    {
        _imageStorage = imageStorage;
        _repository = repository;
        _serviceProvider = provider;
        _mapper = mapper;
        _userContext = userContext;
    }

    public async Task<int> CrearPublicacion(CrearPublicacionRequest request, int idUsuario)
    {
        await ValidationHelper.ValidarAsync(request, _serviceProvider);

        var imagenes = new List<ImagenDto>();

        foreach (var img in request.Imagenes)
        {
            var result = await _imageStorage.SubirArchivo(img);
            imagenes.Add(new ImagenDto
            {
                MainUrl = result.MainUrl,
                ThumbUrl = result.ThumbUrl
            });
        }

        return await _repository.InsertarPublicacion(request, idUsuario, imagenes);

    }

    public async Task<List<ProductoDto>> ObtenerPublicaciones(string? categoria, string? nombre, int? idUsuario)
    {
        var publicaciones = await _repository.ObtenerPublicaciones(categoria, nombre, idUsuario);
        return _mapper.Map<List<ProductoDto>>(publicaciones);
    }

    public async Task EliminarPublicacion(int idPublicacion)
    {
        // 🔹 Validar existencia y propiedad del usuario
        var idUsuario = _userContext.IdUsuario;
        if (idUsuario == null || idUsuario == 0)
            throw new UnauthorizedAccessException();

        await ValidarAccesoPublicacion(
            idPublicacion,
            idUsuario.Value,
            "EliminarPublicacion"
        );

        // 🔹 Obtener imágenes asociadas
        var imagenes = await _repository.ObtenerImagenesPorPublicacion(idPublicacion, idUsuario.Value);
        if (imagenes == null || !imagenes.Any())

            // 🔹 Eliminar archivos de Cloudinary
            foreach (var img in imagenes)
            {
                await _imageStorage.EliminarArchivo(img.MainUrl);
                if (!string.IsNullOrWhiteSpace(img.ThumbUrl))
                    await _imageStorage.EliminarArchivo(img.ThumbUrl);
            }

        // 🔹 Eliminar registros de la base
        var filasAfectadas = await _repository.EliminarPublicacion(idPublicacion, idUsuario.Value);

        if (filasAfectadas == 0)
            throw new ReglasdeNegocioException("No se encontró la publicación o no tienes permiso para eliminarla.");
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

    public async Task EditarPublicacion(EditarPublicacionRequest request, int idUsuario)
    {
        await ValidationHelper.ValidarAsync(request, _serviceProvider);

        // 🔐 Validar acceso
        await ValidarAccesoPublicacion(request.IdPublicacion, idUsuario, "EditarPublicacion");

        // 📸 Subir nuevas imágenes
        var nuevasImagenes = new List<ImagenDto>();

        if (request.NuevasImagenes != null)
        {
            foreach (var img in request.NuevasImagenes)
            {
                var result = await _imageStorage.SubirArchivo(img);
                nuevasImagenes.Add(new ImagenDto
                {
                    MainUrl = result.MainUrl,
                    ThumbUrl = result.ThumbUrl
                });
            }
        }

        // ❌ Eliminar imágenes solicitadas
        if (request.ImagenesAEliminar != null)
        {
            foreach (var url in request.ImagenesAEliminar)
            {
                await _imageStorage.EliminarArchivo(url);
            }
        }

        await _repository.EditarPublicacion(
            request,
            nuevasImagenes,
            request.ImagenesAEliminar
        );
    }


}

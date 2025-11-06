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

    public async Task<List<ProductoDto>> ObtenerPublicaciones(string? categoria, string? nombre)
    {
        var publicaciones = await _repository.ObtenerPublicaciones(categoria, nombre);
        return _mapper.Map<List<ProductoDto>>(publicaciones);
    }

    public async Task EliminarPublicacion(int idPublicacion)
    {
        // 🔹 Validar existencia y propiedad del usuario
        var idUsuario = _userContext.IdUsuario;
        if (idUsuario == null || idUsuario == 0)
            throw new UnauthorizedAccessException();

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


}

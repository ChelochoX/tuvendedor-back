using AutoMapper;
using tuvendedorback.Common;
using tuvendedorback.DTOs;
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

    public PublicacionService(IImageStorageService imageStorage, IPublicacionRepository repository, IServiceProvider provider, IMapper mapper)
    {
        _imageStorage = imageStorage;
        _repository = repository;
        _serviceProvider = provider;
        _mapper = mapper;
    }

    public async Task<int> CrearPublicacion(CrearPublicacionRequest request, int idUsuario)
    {
        await ValidationHelper.ValidarAsync(request, _serviceProvider);

        var urls = new List<string>();

        foreach (var img in request.Imagenes)
        {
            var url = await _imageStorage.SubirArchivo(img);
            urls.Add(url);
        }

        return await _repository.InsertarPublicacion(request, idUsuario, urls);
    }

    public async Task<List<ProductoDto>> ObtenerPublicaciones(string? categoria, string? nombre)
    {
        var publicaciones = await _repository.ObtenerPublicaciones(categoria, nombre);
        return _mapper.Map<List<ProductoDto>>(publicaciones);
    }
}

using tuvendedorback.Common;
using tuvendedorback.Repositories.Interfaces;
using tuvendedorback.Request;
using tuvendedorback.Services.Interfaces;

namespace tuvendedorback.Services;

public class PublicacionService : IPublicacionService
{
    private readonly IImageStorageService _imageStorage;
    private readonly IPublicacionRepository _repository;
    private readonly IServiceProvider _serviceProvider;

    public PublicacionService(IImageStorageService imageStorage, IPublicacionRepository repository, IServiceProvider provider)
    {
        _imageStorage = imageStorage;
        _repository = repository;
        _serviceProvider = provider;
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
}

using tuvendedorback.Common;
using tuvendedorback.DTOs;
using tuvendedorback.Exceptions;
using tuvendedorback.Repositories.Interfaces;
using tuvendedorback.Request;
using tuvendedorback.Services.Interfaces;

namespace tuvendedorback.Services;

public class ModeloProductoService : IModeloProductoService
{
    private readonly IModeloProductoRepository _repo;
    private readonly IServiceProvider _provider;

    public ModeloProductoService(IModeloProductoRepository repo, IServiceProvider provider)
    {
        _repo = repo;
        _provider = provider;
    }

    public async Task<int> CrearModelo(CrearModeloProductoRequest request)
    {
        await ValidationHelper.ValidarAsync(request, _provider);

        var existe = await _repo.ExisteModelo(request.IdMarca, request.NombreModelo);
        if (existe)
            throw new ReglasdeNegocioException("Ya existe un modelo con ese nombre para la marca.");

        return await _repo.CrearModelo(request);
    }

    public async Task EditarModelo(EditarModeloProductoRequest request)
    {
        await ValidationHelper.ValidarAsync(request, _provider);

        var existe = await _repo.ExisteModelo(
            request.IdMarca,
            request.NombreModelo,
            request.Id
        );

        await _repo.EditarModelo(
            request.Id,
            request.NombreModelo,
            request.CodigoReferencia
        );
    }


    public Task ActivarModelo(int id) => _repo.ActivarModelo(id);
    public Task DesactivarModelo(int id) => _repo.DesactivarModelo(id);

    public Task<List<ModeloProductoDto>> ObtenerModelos(int? idMarca, bool soloActivos)
        => _repo.ObtenerModelos(idMarca, soloActivos);

}

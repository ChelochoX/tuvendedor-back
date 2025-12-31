using tuvendedorback.Common;
using tuvendedorback.DTOs;
using tuvendedorback.Exceptions;
using tuvendedorback.Repositories.Interfaces;
using tuvendedorback.Request;
using tuvendedorback.Services.Interfaces;

namespace tuvendedorback.Services;

public class MarcaService : IMarcaService
{
    private readonly IMarcaRepository _repo;
    private readonly IServiceProvider _provider;

    public MarcaService(IMarcaRepository repo, IServiceProvider provider)
    {
        _repo = repo;
        _provider = provider;
    }

    public async Task<int> CrearMarca(CrearMarcaRequest request)
    {
        await ValidationHelper.ValidarAsync(request, _provider);

        var existe = await _repo.ExisteMarcaConNombre(request.Nombre);
        if (existe)
            throw new ReglasdeNegocioException("Ya existe una marca con ese nombre.");

        return await _repo.CrearMarca(request.Nombre);
    }

    public async Task EditarMarca(EditarMarcaRequest request)
    {
        await ValidationHelper.ValidarAsync(request, _provider);

        var existe = await _repo.ExisteMarcaConNombre(request.Nombre, request.Id);
        if (existe)
            throw new ReglasdeNegocioException("Ya existe otra marca con ese nombre.");

        await _repo.EditarMarca(request.Id, request.Nombre);
    }

    public async Task ActivarMarca(int id) => await _repo.ActivarMarca(id);
    public async Task DesactivarMarca(int id) => await _repo.DesactivarMarca(id);

    public async Task<List<MarcaDto>> ObtenerMarcas(bool soloActivas)
        => await _repo.ObtenerMarcas(soloActivas);
}

using tuvendedorback.Repositories.Interfaces;
using tuvendedorback.Services.Interfaces;

namespace tuvendedorback.Services;

public class PermisosService : IPermisosService
{
    private readonly IPermisosRepository _repository;

    public PermisosService(IPermisosRepository repository)
    {
        _repository = repository;
    }

    public async Task<bool> TienePermiso(int idUsuario, string entidad, string recurso)
    {

        return await _repository.TienePermiso(idUsuario, entidad, recurso);
    }
}

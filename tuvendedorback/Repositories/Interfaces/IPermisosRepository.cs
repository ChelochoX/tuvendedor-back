namespace tuvendedorback.Repositories.Interfaces;

public interface IPermisosRepository
{
    Task<bool> TienePermiso(int idUsuario, string entidad, string recurso);
}

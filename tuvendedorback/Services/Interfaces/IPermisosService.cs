namespace tuvendedorback.Services.Interfaces;

public interface IPermisosService
{
    Task<bool> TienePermiso(int idUsuario, string entidad, string recurso);
}

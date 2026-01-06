using tuvendedorback.DTOs;
using tuvendedorback.Request;

namespace tuvendedorback.Repositories.Interfaces;

public interface IModeloProductoRepository
{
    Task<int> CrearModelo(CrearModeloProductoRequest request);
    Task EditarModelo(int id, string nombreModelo, string? codigoReferencia);
    Task ActivarModelo(int id);
    Task DesactivarModelo(int id);
    Task<bool> ExisteModelo(int idMarca, string nombreModelo, int? excluirId = null);
    Task<List<ModeloProductoDto>> ObtenerModelos(int? idMarca, bool soloActivos);
}

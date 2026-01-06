using tuvendedorback.DTOs;
using tuvendedorback.Request;

namespace tuvendedorback.Services.Interfaces;

public interface IModeloProductoService
{
    Task<int> CrearModelo(CrearModeloProductoRequest request);
    Task EditarModelo(EditarModeloProductoRequest request);
    Task ActivarModelo(int id);
    Task DesactivarModelo(int id);
    Task<List<ModeloProductoDto>> ObtenerModelos(int? idMarca, bool soloActivos);

}

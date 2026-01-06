using tuvendedorback.DTOs;
using tuvendedorback.Request;

namespace tuvendedorback.Services.Interfaces;

public interface IPrecioProductoService
{
    Task<int> CrearModeloProducto(CrearModeloProductoRequest request);
    Task<int> CrearListaPrecioProducto(CrearListaPrecioProductoRequest request);
    Task<int> CrearPlanFinanciacionProducto(CrearPlanFinanciacionProductoRequest request);
    Task<PrecioVigenteProductoDto> ObtenerPrecioVigentePorCodigo(string rubro, string codigoReferencia, DateTime? fechaActual = null);
    Task<IEnumerable<ModeloProductoDto>> ListarModelos();
    Task<IEnumerable<ModeloPrecioDto>> ListadoPrecios();
    Task EditarListaPrecio(EditarListaPrecioProductoRequest request);
    Task ActivarListaPrecio(int id);
    Task DesactivarListaPrecio(int id);
    Task EditarPlanFinanciacion(EditarPlanFinanciacionProductoRequest request);
    Task ActivarPlanFinanciacion(int id);
    Task DesactivarPlanFinanciacion(int id);

}

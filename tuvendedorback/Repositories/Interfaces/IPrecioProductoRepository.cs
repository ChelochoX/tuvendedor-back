using tuvendedorback.DTOs;
using tuvendedorback.Request;

namespace tuvendedorback.Repositories.Interfaces;

public interface IPrecioProductoRepository
{
    Task<int> CrearModeloProducto(CrearModeloProductoRequest request);
    Task<int> CrearListaPrecioProducto(CrearListaPrecioProductoRequest request);
    Task<int> CrearPlanFinanciacionProducto(CrearPlanFinanciacionProductoRequest request);

    Task<bool> ExisteModeloPorCodigo(string rubro, string codigoReferencia);
    Task<int?> ObtenerIdModeloPorCodigo(string rubro, string codigoReferencia);

    Task<bool> ExisteSolapamientoListaPrecio(
        int idModeloProducto,
        DateTime fechaDesde,
        DateTime? fechaHasta,
        bool esPromo);
    Task<PrecioVigenteProductoDto?> ObtenerPrecioVigentePorCodigo(string rubro, string codigoReferencia, DateTime fechaActual);
    Task<List<PlanFinanciacionDto>> ObtenerPlanesPorListaPrecio(int idListaPrecio);

    Task<IEnumerable<ModeloProductoDto>> ListarModelos();

}

using tuvendedorback.Common;
using tuvendedorback.DTOs;
using tuvendedorback.Exceptions;
using tuvendedorback.Repositories.Interfaces;
using tuvendedorback.Request;
using tuvendedorback.Services.Interfaces;

namespace tuvendedorback.Services;

public class PrecioProductoService : IPrecioProductoService
{
    private readonly IPrecioProductoRepository _repository;
    private readonly IServiceProvider _provider;

    public PrecioProductoService(IPrecioProductoRepository repo, IServiceProvider provider)
    {
        _repository = repo;
        _provider = provider;
    }

    public async Task<int> CrearModeloProducto(CrearModeloProductoRequest request)
    {
        await ValidationHelper.ValidarAsync(request, _provider);

        var existe = await _repository.ExisteModeloPorCodigo(request.Rubro, request.CodigoReferencia);
        if (existe)
            throw new ReglasdeNegocioException("Ya existe un modelo con ese código para ese rubro.");

        return await _repository.CrearModeloProducto(request);
    }

    public async Task<int> CrearListaPrecioProducto(CrearListaPrecioProductoRequest request)
    {
        //Fecha automática para precio normal
        if (!request.EsPromo)
        {
            request.FechaDesde = DateTime.Today;
            request.FechaHasta = null;
        }
        await ValidationHelper.ValidarAsync(request, _provider);

        return await _repository.CrearListaPrecioProducto(request);
    }

    public async Task<int> CrearPlanFinanciacionProducto(CrearPlanFinanciacionProductoRequest request)
    {
        await ValidationHelper.ValidarAsync(request, _provider);
        var interes = DecimalHelper.ParseInteres(request.Interes);
        request.InteresParam = interes;

        return await _repository.CrearPlanFinanciacionProducto(request);
    }

    public async Task<PrecioVigenteProductoDto> ObtenerPrecioVigentePorCodigo(string rubro, string codigoReferencia, DateTime? fechaActual = null)
    {
        var fecha = (fechaActual ?? DateTime.Today).Date;

        var data = await _repository.ObtenerPrecioVigentePorCodigo(rubro, codigoReferencia, fecha);
        if (data == null)
            throw new ReglasdeNegocioException("No se encontró un precio vigente para ese código y rubro.");

        data.Planes = await _repository.ObtenerPlanesPorListaPrecio(data.IdListaPrecio);
        return data;
    }

    public async Task<IEnumerable<ModeloProductoDto>> ListarModelos()
    {
        return await _repository.ListarModelos();
    }

    public async Task<IEnumerable<ModeloPrecioDto>> ListadoPrecios()
    {
        return await _repository.ListadoPrecios();
    }

    public async Task EditarListaPrecio(EditarListaPrecioProductoRequest request)
    {
        await ValidationHelper.ValidarAsync(request, _provider);
        await _repository.EditarListaPrecio(request);
    }

    public Task ActivarListaPrecio(int id)
        => _repository.ActivarListaPrecio(id);

    public Task DesactivarListaPrecio(int id)
        => _repository.DesactivarListaPrecio(id);

    public async Task EditarPlanFinanciacion(EditarPlanFinanciacionProductoRequest request)
    {
        await ValidationHelper.ValidarAsync(request, _provider);
        var interes = DecimalHelper.ParseInteres(request.Interes);
        request.InteresParam = interes;
        await _repository.EditarPlanFinanciacion(request);
    }

    public Task ActivarPlanFinanciacion(int id)
        => _repository.ActivarPlanFinanciacion(id);

    public Task DesactivarPlanFinanciacion(int id)
        => _repository.DesactivarPlanFinanciacion(id);

}

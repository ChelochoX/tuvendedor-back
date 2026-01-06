using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using tuvendedorback.DTOs;
using tuvendedorback.Request;
using tuvendedorback.Services.Interfaces;
using tuvendedorback.Wrappers;

namespace tuvendedorback.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PreciosProductosController : Controller
{
    private readonly IPrecioProductoService _service;
    private readonly ILogger<PreciosProductosController> _logger;

    public PreciosProductosController(IPrecioProductoService service, ILogger<PreciosProductosController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpPost("crear-modelo")]
    [SwaggerOperation(Summary = "Crea un modelo de producto", Description = "Crea un modelo (rubro + código) para resolver precios desde WA.")]
    public async Task<IActionResult> CrearModelo([FromBody] CrearModeloProductoRequest request)
    {
        var id = await _service.CrearModeloProducto(request);

        return Ok(new Response<object>
        {
            Success = true,
            Message = "Modelo creado correctamente",
            Data = new { Id = id }
        });
    }

    [HttpPost("crear-lista-precio")]
    [SwaggerOperation(Summary = "Crea una lista de precios", Description = "Crea un precio con vigencia. Promos = precios por fecha.")]
    public async Task<IActionResult> CrearListaPrecio([FromBody] CrearListaPrecioProductoRequest request)
    {
        var id = await _service.CrearListaPrecioProducto(request);

        return Ok(new Response<object>
        {
            Success = true,
            Message = "Lista de precios creada correctamente",
            Data = new { Id = id }
        });
    }

    [HttpPost("crear-plan")]
    [SwaggerOperation(Summary = "Crea un plan de financiación", Description = "Registra planes (cuotas) asociados a una lista de precios.")]
    public async Task<IActionResult> CrearPlan([FromBody] CrearPlanFinanciacionProductoRequest request)
    {
        var id = await _service.CrearPlanFinanciacionProducto(request);

        return Ok(new Response<object>
        {
            Success = true,
            Message = "Plan creado correctamente",
            Data = new { Id = id }
        });
    }

    [HttpGet("precio-vigente")]
    [SwaggerOperation(Summary = "Obtiene precio vigente por código", Description = "Resuelve precio vigente por (rubro + código) y trae planes.")]
    public async Task<IActionResult> ObtenerPrecioVigente([FromQuery] string rubro, [FromQuery] string codigo, [FromQuery] DateTime? fecha = null)
    {
        var data = await _service.ObtenerPrecioVigentePorCodigo(rubro, codigo, fecha);

        return Ok(new Response<PrecioVigenteProductoDto>
        {
            Success = true,
            Data = data,
            Message = "Precio vigente obtenido correctamente"
        });
    }

    [HttpGet("listar-modelos")]
    [SwaggerOperation(
    Summary = "Lista modelos de producto",
    Description = "Obtiene el listado de modelos de productos activos con su marca, modelo, código y rubro.")]
    public async Task<IActionResult> ListarModelos()
    {
        var data = await _service.ListarModelos();

        return Ok(new Response<IEnumerable<ModeloProductoDto>>
        {
            Success = true,
            Data = data,
            Message = "Modelos obtenidos correctamente"
        });
    }

    [HttpGet("listado-precios")]
    [SwaggerOperation(
    Summary = "Listado de precios por modelo",
    Description = "Devuelve modelos con sus listas de precios y cuotas")]
    public async Task<IActionResult> ListadoPrecios()
    {
        var data = await _service.ListadoPrecios();

        return Ok(new Response<IEnumerable<ModeloPrecioDto>>
        {
            Success = true,
            Data = data
        });
    }

    // ===============================
    // LISTAS DE PRECIOS
    // ===============================

    [HttpPut("editar-lista-precio")]
    [SwaggerOperation(
        Summary = "Edita una lista de precios",
        Description = "Permite modificar precios, fechas de vigencia y valores base de una lista de precios existente.")]
    public async Task<IActionResult> EditarListaPrecio(
        [FromBody] EditarListaPrecioProductoRequest request)
    {
        await _service.EditarListaPrecio(request);

        return Ok(new Response<object>
        {
            Success = true,
            Message = "Lista de precios actualizada correctamente"
        });
    }

    [HttpPost("desactivar-lista-precio/{id}")]
    [SwaggerOperation(
        Summary = "Desactiva una lista de precios",
        Description = "Inactiva una lista de precios. No se elimina físicamente, solo cambia el estado.")]
    public async Task<IActionResult> DesactivarListaPrecio(int id)
    {
        await _service.DesactivarListaPrecio(id);

        return Ok(new Response<object>
        {
            Success = true,
            Message = "Lista de precios desactivada correctamente"
        });
    }

    [HttpPost("activar-lista-precio/{id}")]
    [SwaggerOperation(
        Summary = "Activa una lista de precios",
        Description = "Reactiva una lista de precios previamente desactivada.")]
    public async Task<IActionResult> ActivarListaPrecio(int id)
    {
        await _service.ActivarListaPrecio(id);

        return Ok(new Response<object>
        {
            Success = true,
            Message = "Lista de precios activada correctamente"
        });
    }

    // ===============================
    // PLANES DE FINANCIACIÓN
    // ===============================

    [HttpPut("editar-plan")]
    [SwaggerOperation(
        Summary = "Edita un plan de financiación",
        Description = "Actualiza cuotas, importe, interés y código del plan. El código se guarda en mayúsculas.")]
    public async Task<IActionResult> EditarPlan(
        [FromBody] EditarPlanFinanciacionProductoRequest request)
    {
        await _service.EditarPlanFinanciacion(request);

        return Ok(new Response<object>
        {
            Success = true,
            Message = "Plan de financiación actualizado correctamente"
        });
    }

    [HttpPost("desactivar-plan/{id}")]
    [SwaggerOperation(
        Summary = "Desactiva un plan de financiación",
        Description = "Inactiva un plan de financiación sin eliminarlo de la base de datos.")]
    public async Task<IActionResult> DesactivarPlan(int id)
    {
        await _service.DesactivarPlanFinanciacion(id);

        return Ok(new Response<object>
        {
            Success = true,
            Message = "Plan de financiación desactivado correctamente"
        });
    }

    [HttpPost("activar-plan/{id}")]
    [SwaggerOperation(
        Summary = "Activa un plan de financiación",
        Description = "Reactiva un plan de financiación previamente desactivado.")]
    public async Task<IActionResult> ActivarPlan(int id)
    {
        await _service.ActivarPlanFinanciacion(id);

        return Ok(new Response<object>
        {
            Success = true,
            Message = "Plan de financiación activado correctamente"
        });
    }

}

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


}

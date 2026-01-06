using Microsoft.AspNetCore.Mvc;
using tuvendedorback.DTOs;
using tuvendedorback.Request;
using tuvendedorback.Services.Interfaces;
using tuvendedorback.Wrappers;

namespace tuvendedorback.Controllers;

[ApiController]
[Route("api/modelos-producto")]
public class ModelosProductoController : ControllerBase
{
    private readonly IModeloProductoService _service;

    public ModelosProductoController(IModeloProductoService service)
    {
        _service = service;
    }

    [HttpPost("crear")]
    public async Task<IActionResult> Crear([FromBody] CrearModeloProductoRequest request)
    {
        var id = await _service.CrearModelo(request);
        return Ok(new Response<object> { Success = true, Data = new { Id = id } });
    }

    [HttpPut("editar")]
    public async Task<IActionResult> Editar([FromBody] EditarModeloProductoRequest request)
    {
        await _service.EditarModelo(request);
        return Ok(new Response<object> { Success = true });
    }

    [HttpGet("listar")]
    public async Task<IActionResult> Listar([FromQuery] int? idMarca, [FromQuery] bool soloActivos = true)
    {
        var data = await _service.ObtenerModelos(idMarca, soloActivos);
        return Ok(new Response<List<ModeloProductoDto>> { Success = true, Data = data });
    }

    [HttpPost("activar/{id}")]
    public async Task<IActionResult> Activar(int id)
    {
        await _service.ActivarModelo(id);
        return Ok(new Response<object> { Success = true });
    }

    [HttpPost("desactivar/{id}")]
    public async Task<IActionResult> Desactivar(int id)
    {
        await _service.DesactivarModelo(id);
        return Ok(new Response<object> { Success = true });
    }
}

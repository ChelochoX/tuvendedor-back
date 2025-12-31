using Microsoft.AspNetCore.Mvc;
using tuvendedorback.DTOs;
using tuvendedorback.Request;
using tuvendedorback.Services.Interfaces;
using tuvendedorback.Wrappers;

namespace tuvendedorback.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MarcasController : Controller
{
    private readonly IMarcaService _service;

    public MarcasController(IMarcaService service)
    {
        _service = service;
    }

    [HttpPost("crear")]
    public async Task<IActionResult> Crear([FromBody] CrearMarcaRequest request)
    {
        var id = await _service.CrearMarca(request);

        return Ok(new Response<object>
        {
            Success = true,
            Message = "Marca creada correctamente",
            Data = new { Id = id }
        });
    }

    [HttpPut("editar")]
    public async Task<IActionResult> Editar([FromBody] EditarMarcaRequest request)
    {
        await _service.EditarMarca(request);

        return Ok(new Response<object>
        {
            Success = true,
            Message = "Marca actualizada correctamente"
        });
    }

    [HttpGet("listar")]
    public async Task<IActionResult> Listar([FromQuery] bool soloActivas = true)
    {
        var data = await _service.ObtenerMarcas(soloActivas);

        return Ok(new Response<List<MarcaDto>>
        {
            Success = true,
            Data = data
        });
    }

    [HttpPost("activar/{id}")]
    public async Task<IActionResult> Activar(int id)
    {
        await _service.ActivarMarca(id);
        return Ok(new Response<object> { Success = true });
    }

    [HttpPost("desactivar/{id}")]
    public async Task<IActionResult> Desactivar(int id)
    {
        await _service.DesactivarMarca(id);
        return Ok(new Response<object> { Success = true });
    }
}

using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using tuvendedorback.Common;
using tuvendedorback.DTOs;
using tuvendedorback.Request;
using tuvendedorback.Services.Interfaces;
using tuvendedorback.Wrappers;

namespace tuvendedorback.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ClientesController : ControllerBase
{
    private readonly IClientesService _service;
    private readonly UserContext _userContext;

    public ClientesController(UserContext userContext, IClientesService service)
    {
        _userContext = userContext;
        _service = service;
    }

    [HttpPost("registrar-interesados")]
    [Consumes("multipart/form-data")]
    [SwaggerOperation(Summary = "Registrar un nuevo interesado")]
    public async Task<IActionResult> Registrar([FromForm] InteresadoRequest request)
    {
        var idUsuario = _userContext.IdUsuario;

        if (idUsuario == null || idUsuario == 0)
        {
            return Unauthorized(new Response<object>
            {
                Success = false,
                Errors = new List<string> { "Usuario no autenticado." },
                StatusCode = 401
            });
        }

        var id = await _service.RegistrarInteresado(request, idUsuario.Value);

        return Ok(new Response<object>
        {
            Success = true,
            Data = new { Id = id },
            Message = "Interesado registrado correctamente"
        });
    }

    [HttpPost("registrar-seguimiento")]
    [SwaggerOperation(Summary = "Agregar seguimiento a un interesado existente")]
    public async Task<IActionResult> AgregarSeguimiento([FromBody] SeguimientoRequest request)
    {
        var idUsuario = _userContext.IdUsuario;

        if (idUsuario == null || idUsuario == 0)
        {
            return Unauthorized(new Response<object>
            {
                Success = false,
                Errors = new List<string> { "Usuario no autenticado." },
                StatusCode = 401
            });
        }

        var id = await _service.AgregarSeguimiento(request, idUsuario.Value);

        return Ok(new Response<object>
        {
            Success = true,
            Data = new { Id = id },
            Message = "Seguimiento agregado correctamente"
        });
    }

    [HttpGet("obtener-interesados")]
    [SwaggerOperation(
    Summary = "Obtiene el listado de interesados",
    Description = "Devuelve los interesados registrados, permitiendo filtrar por nombre, estado o rango de fechas, con soporte de paginación.")]
    public async Task<IActionResult> ObtenerInteresados([FromQuery] FiltroInteresadosRequest filtro)
    {
        var (items, total) = await _service.ObtenerInteresados(filtro);

        var resultado = new
        {
            TotalRegistros = total,
            PaginaActual = filtro.NumeroPagina,
            RegistrosPorPagina = filtro.RegistrosPorPagina,
            Items = items
        };

        return Ok(new Response<object>
        {
            Success = true,
            Data = resultado,
            Message = "Interesados obtenidos correctamente",
            StatusCode = 200
        });
    }

    [HttpGet("obtener-seguimientos")]
    [SwaggerOperation(Summary = "Obtiene los seguimientos de un interesado")]
    public async Task<IActionResult> ObtenerSeguimientos([FromQuery] int idInteresado)
    {
        var lista = await _service.ObtenerSeguimientosPorInteresado(idInteresado);

        return Ok(new Response<List<SeguimientoDto>>
        {
            Success = true,
            Data = lista,
            Message = "Seguimientos obtenidos correctamente",
            StatusCode = 200
        });
    }

}

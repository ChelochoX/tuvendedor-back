using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using tuvendedorback.DTOs;
using tuvendedorback.Request;
using tuvendedorback.Services.Interfaces;
using tuvendedorback.Wrappers;

namespace tuvendedorback.Controllers;

[Authorize]
[ApiController]
[Route("api/creditos/motos/descuentos-contado")]
public class DescuentoContadoMotoController : ControllerBase
{
    private readonly IDescuentoContadoMotoService _service;

    public DescuentoContadoMotoController(
        IDescuentoContadoMotoService service)
    {
        _service = service;
    }


    // =========================================================
    // MARCAS
    // =========================================================

    [HttpGet("marcas")]
    public async Task<IActionResult> ListarMarcas()
    {
        var data =
            await _service.ListarMarcas();


        return Ok(
            new Response<IReadOnlyList<DescuentoContadoMarcaDto>>
            {
                Success = true,
                StatusCode = 200,
                Message =
                    "Marcas obtenidas correctamente.",
                Data = data
            });
    }


    // =========================================================
    // CONFIGURACION DE UN MES
    //
    // GET:
    // /api/creditos/motos/descuentos-contado/configuracion
    // ?idMarca=1&anio=2026&mes=9
    // =========================================================

    [HttpGet("configuracion")]
    public async Task<IActionResult> ObtenerConfiguracion(
        [FromQuery] int idMarca,
        [FromQuery] int anio,
        [FromQuery] int mes)
    {
        var data =
            await _service.ObtenerConfiguracion(
                idMarca,
                anio,
                mes);


        return Ok(
            new Response<DescuentoContadoConfiguracionDto>
            {
                Success = true,
                StatusCode = 200,
                Message =
                    "Configuración de descuentos obtenida correctamente.",
                Data = data
            });
    }


    // =========================================================
    // GUARDAR MES COMPLETO
    // =========================================================

    [HttpPut("configuracion")]
    public async Task<IActionResult> GuardarConfiguracion(
        [FromBody] GuardarDescuentoContadoMesRequest request)
    {
        var data =
            await _service.GuardarConfiguracion(
                request);


        return Ok(
            new Response<DescuentoContadoConfiguracionDto>
            {
                Success = true,
                StatusCode = 200,
                Message =
                    "Configuración mensual de descuentos guardada correctamente.",
                Data = data
            });
    }

    // =========================================================
    // CREAR MODELO + EXCEPCION
    //
    // POST
    // /api/creditos/motos/descuentos-contado/modelos-excepcion
    // =========================================================

    [HttpPost("modelos-excepcion")]
    public async Task<IActionResult> CrearModeloConExcepcion(
        [FromBody] CrearModeloConExcepcionDescuentoRequest request)
    {
        var data =
            await _service
                .CrearModeloConExcepcion(
                    request);


        return Ok(
            new Response<DescuentoContadoConfiguracionDto>
            {
                Success = true,
                StatusCode = 200,
                Message =
                    "Modelo y excepción creados correctamente.",
                Data = data
            });
    }
}
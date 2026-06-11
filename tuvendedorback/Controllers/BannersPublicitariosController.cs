using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using tuvendedorback.DTOs;
using tuvendedorback.Request;
using tuvendedorback.Services.Interfaces;
using tuvendedorback.Wrappers;

namespace tuvendedorback.Controllers;

[ApiController]
[Route("api/banners-publicitarios")]
public class BannersPublicitariosController
    : ControllerBase
{
    private readonly IBannerPublicitarioService _service;

    public BannersPublicitariosController(
        IBannerPublicitarioService service)
    {
        _service = service;
    }

    [HttpGet("home")]
    [SwaggerOperation(
        Summary = "Obtiene banners activos del home",
        Description =
            "Devuelve banners vigentes para HOME_TOP y HOME_INLINE. Si existe un banner exclusivo para una posición, devuelve únicamente los exclusivos.")]
    public async Task<IActionResult> ObtenerBannersHome()
    {
        var data =
            await _service.ObtenerActivosHome();

        return Ok(
            new Response<BannersHomeDto>
            {
                Success = true,

                Data = data,

                Message =
                    "Banners publicitarios del home obtenidos correctamente."
            });
    }

    [HttpPost("eventos")]
    [SwaggerOperation(
        Summary = "Registra una interacción con un banner",
        Description =
            "Registra impresiones, clics o aperturas de WhatsApp para generar métricas comerciales.")]
    public async Task<IActionResult> RegistrarEvento(
        [FromBody] RegistrarBannerEventoRequest request)
    {
        var userAgent =
            Request
                .Headers["User-Agent"]
                .ToString();

        await _service.RegistrarEvento(
            request,
            userAgent);

        return Ok(
            new Response<object>
            {
                Success = true,

                Message =
                    "Evento del banner registrado correctamente."
            });
    }
}

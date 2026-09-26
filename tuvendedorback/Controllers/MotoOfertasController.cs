using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using tuvendedorback.DTOs;
using tuvendedorback.Services.Interfaces;
using tuvendedorback.Wrappers;

namespace tuvendedorback.Controllers;

[Authorize]
[ApiController]
[Route("api/ia/motos")]
public class MotoOfertasController : ControllerBase
{
    private readonly IMotoOfertaService _service;


    public MotoOfertasController(
        IMotoOfertaService service)
    {
        _service = service;
    }


    [HttpGet("publicaciones/{idPublicacion:int}/oferta")]
    [SwaggerOperation(
        Summary = "Obtiene la oferta comercial de una moto por publicación",
        Description =
            "Obtiene modelo, precio contado y financiación vigente a partir de una publicación.")]
    public async Task<IActionResult> ObtenerOfertaPorPublicacion(
        int idPublicacion)
    {
        var data =
            await _service.ObtenerOfertaPorPublicacion(
                idPublicacion);

        return Ok(
            new Response<MotoOfertaDto>
            {
                Success = true,
                StatusCode = 200,
                Message =
                    "Oferta comercial obtenida correctamente.",
                Data = data
            });
    }


    [HttpGet("modelos/{idModeloProducto:int}/oferta")]
    [SwaggerOperation(
        Summary = "Obtiene la oferta comercial de una moto por modelo",
        Description =
            "Obtiene precio contado y financiación vigente directamente desde el modelo, sin exigir una publicación activa.")]
    public async Task<IActionResult> ObtenerOfertaPorModelo(
        int idModeloProducto)
    {
        var data =
            await _service.ObtenerOfertaPorModelo(
                idModeloProducto);

        return Ok(
            new Response<MotoOfertaDto>
            {
                Success = true,
                StatusCode = 200,
                Message =
                    "Oferta comercial del modelo obtenida correctamente.",
                Data = data
            });
    }
}

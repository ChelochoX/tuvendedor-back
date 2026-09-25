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
        Summary = "Obtiene la oferta comercial de una moto",
        Description =
            "Obtiene modelo, precio contado, descuento y financiación vigente a partir de una publicación.")]
    public async Task<IActionResult> ObtenerOferta(
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
}
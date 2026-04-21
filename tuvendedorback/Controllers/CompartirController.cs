using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using tuvendedorback.Services.Interfaces;

namespace tuvendedorback.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CompartirController : ControllerBase
{
    private readonly ICompartirService _service;
    private readonly ILogger<CompartirController> _logger;

    public CompartirController(
        ICompartirService service,
        ILogger<CompartirController> logger)
    {
        _service = service;
        _logger = logger;
    }

    /// <summary>
    /// Genera una página HTML pública con metadatos Open Graph para compartir una publicación.
    /// </summary>
    [HttpGet("producto/{idProducto:int}")]
    [AllowAnonymous]
    [SwaggerOperation(
        Summary = "Genera vista previa para compartir producto",
        Description = "Devuelve HTML con Open Graph para que WhatsApp y redes puedan mostrar imagen, título y descripción.")]
    public async Task<IActionResult> CompartirProducto([FromRoute] int idProducto)
    {
        try
        {
            var html = await _service.GenerarHtmlProductoCompartir(idProducto);

            return Content(html, "text/html; charset=utf-8");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al generar HTML para compartir producto. IdProducto={IdProducto}",
                idProducto
            );

            return StatusCode(500, "No se pudo generar la vista previa del producto.");
        }
    }
}

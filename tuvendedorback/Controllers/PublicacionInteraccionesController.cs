using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using tuvendedorback.DTOs;
using tuvendedorback.Request;
using tuvendedorback.Services.Interfaces;
using tuvendedorback.Wrappers;

namespace tuvendedorback.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PublicacionInteraccionesController : ControllerBase
{
    private readonly IPublicacionInteraccionService _service;

    public PublicacionInteraccionesController(IPublicacionInteraccionService service)
    {
        _service = service;
    }

    [AllowAnonymous]
    [HttpPost("favorito")]
    [SwaggerOperation(
        Summary = "Marca o quita una publicación como favorita",
        Description = "Permite guardar o quitar una publicación como favorita. Funciona con usuario autenticado o visitante anónimo usando VisitorId.")]
    public async Task<IActionResult> ToggleFavorito([FromBody] PublicacionInteraccionRequest request)
    {
        var data = await _service.ToggleFavorito(request);

        return Ok(new Response<PublicacionFavoritoResponseDto>
        {
            Success = true,
            Message = data.EsFavorito
                ? "Publicación agregada a favoritos."
                : "Publicación quitada de favoritos.",
            Data = data
        });
    }

    [AllowAnonymous]
    [HttpPost("registrar-vista")]
    [SwaggerOperation(
        Summary = "Registra una vista de publicación",
        Description = "Registra una vista diaria por visitante o usuario para evitar conteos duplicados excesivos.")]
    public async Task<IActionResult> RegistrarVista([FromBody] PublicacionInteraccionRequest request)
    {
        await _service.RegistrarVista(request);

        return Ok(new Response<object>
        {
            Success = true,
            Message = "Vista registrada correctamente."
        });
    }

    [AllowAnonymous]
    [HttpPost("registrar-click-whatsapp")]
    [SwaggerOperation(
        Summary = "Registra un click en WhatsApp",
        Description = "Registra cuando un visitante toca el botón de WhatsApp de una publicación.")]
    public async Task<IActionResult> RegistrarClickWhatsapp([FromBody] PublicacionInteraccionRequest request)
    {
        await _service.RegistrarClickWhatsapp(request);

        return Ok(new Response<object>
        {
            Success = true,
            Message = "Click de WhatsApp registrado correctamente."
        });
    }
}

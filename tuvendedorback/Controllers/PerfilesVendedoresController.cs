using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using tuvendedorback.DTOs;
using tuvendedorback.Services.Interfaces;
using tuvendedorback.Wrappers;

namespace tuvendedorback.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PerfilesVendedoresController : ControllerBase
{
    private readonly IPerfilVendedorService _service;

    public PerfilesVendedoresController(IPerfilVendedorService service)
    {
        _service = service;
    }

    /// <summary>
    /// Obtiene el perfil público de un vendedor a partir de su slug.
    /// </summary>
    /// <remarks>
    /// Este endpoint devuelve la información necesaria para construir la vista pública
    /// del perfil comercial de un vendedor dentro del marketplace.
    ///
    /// <b>¿Qué retorna?</b>
    /// - Datos principales del vendedor.
    /// - Información visual del perfil, como foto y banner.
    /// - Datos de contacto públicos configurados.
    /// - Indicadores del perfil, como visibilidad pública y condición premium.
    /// - Cantidad total de publicaciones activas.
    /// - Listado de publicaciones activas asociadas al vendedor.
    ///
    /// <b>Comportamiento:</b>
    /// - La búsqueda se realiza utilizando el <b>slug</b> del vendedor.
    /// - Solo se exponen perfiles marcados como <b>públicos</b>.
    /// - Solo se consideran usuarios con estado <b>Activo</b>.
    /// - Solo se incluyen publicaciones con estado <b>Activo</b>.
    /// - Las publicaciones destacadas se priorizan en el orden de visualización.
    ///
    /// <b>Uso esperado:</b>
    /// Este endpoint está pensado para alimentar la pantalla pública del vendedor,
    /// por ejemplo una URL como:
    /// <c>/api/PerfilesVendedores/angelacaceres</c>
    ///
    /// <b>Importante:</b>
    /// Si el slug no existe, o el perfil no está habilitado para exposición pública,
    /// el sistema no devolverá información del vendedor.
    /// </remarks>
    [HttpGet("{slug}")]
    [SwaggerOperation(
        Summary = "Obtiene el perfil público de un vendedor",
        Description = "Devuelve la cabecera pública del vendedor y sus publicaciones activas."
    )]
    public async Task<IActionResult> ObtenerPerfilPublico(string slug)
    {
        var data = await _service.ObtenerPerfilPublicoPorSlug(slug);

        return Ok(new Response<PerfilPublicoVendedorDto>
        {
            Success = true,
            Data = data,
            Message = "Perfil público obtenido correctamente."
        });
    }
}

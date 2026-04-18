using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using tuvendedorback.Common;
using tuvendedorback.DTOs;
using tuvendedorback.Services.Interfaces;
using tuvendedorback.Wrappers;

namespace tuvendedorback.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PerfilesVendedoresController : ControllerBase
{
    private readonly IPerfilVendedorService _service;
    private readonly UserContext _userContext;

    public PerfilesVendedoresController(IPerfilVendedorService service, UserContext userContext)
    {
        _service = service;
        _userContext = userContext;
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

    /// <summary>
    /// Obtiene el perfil vendedor del usuario autenticado.
    /// </summary>
    /// <remarks>
    /// Este endpoint devuelve la información editable del perfil comercial asociado
    /// al usuario autenticado.
    ///
    /// <b>¿Para qué se utiliza?</b>
    /// - Cargar el formulario de edición del perfil público.
    /// - Mostrar los datos actuales del vendedor antes de modificarlos.
    /// - Permitir que el vendedor revise cómo está configurada su vitrina pública.
    ///
    /// <b>Información retornada:</b>
    /// - Nombre comercial.
    /// - Slug público.
    /// - Biografía o descripción.
    /// - Rubro.
    /// - Ciudad visible.
    /// - Foto de perfil.
    /// - Banner o portada.
    /// - WhatsApp y redes sociales.
    /// - Estado de visibilidad pública.
    /// - Configuración para mostrar u ocultar teléfono.
    ///
    /// <b>Importante:</b>
    /// Este endpoint requiere que el usuario esté autenticado y tenga un perfil vendedor
    /// asociado en la tabla <b>Vendedores</b>.
    /// </remarks>
    [HttpGet("mi-perfil")]
    [SwaggerOperation(
        Summary = "Obtiene mi perfil vendedor",
        Description = "Devuelve el perfil vendedor asociado al usuario autenticado."
    )]
    public async Task<IActionResult> ObtenerMiPerfil()
    {
        var idUsuario = _userContext.IdUsuario;

        if (idUsuario == null || idUsuario == 0)
            throw new UnauthorizedAccessException();

        var data = await _service.ObtenerMiPerfilVendedor(idUsuario.Value);

        return Ok(new Response<PerfilPublicoVendedorDto>
        {
            Success = true,
            Data = data,
            Message = "Perfil vendedor obtenido correctamente."
        });
    }

    /// <summary>
    /// Actualiza el perfil público del vendedor autenticado.
    /// </summary>
    /// <remarks>
    /// Este endpoint permite actualizar la información pública y comercial del perfil
    /// vendedor asociado al usuario autenticado.
    ///
    /// <b>Campos editables:</b>
    /// - Nombre comercial del vendedor.
    /// - Slug público utilizado en la URL.
    /// - Rubro principal.
    /// - Biografía o descripción del perfil.
    /// - Ciudad visible.
    /// - WhatsApp.
    /// - Instagram.
    /// - Facebook.
    /// - Visibilidad pública del perfil.
    /// - Opción para mostrar u ocultar el teléfono.
    /// - Foto de perfil.
    /// - Imagen o video de portada.
    ///
    /// <b>Comportamiento:</b>
    /// - Si se envía una nueva foto de perfil, se actualiza <b>Usuarios.FotoPerfil</b>.
    /// - Si se envía una nueva portada, se actualiza <b>Vendedores.BannerUrl</b>.
    /// - Si la portada enviada es video, se guarda <b>BannerTipo = VIDEO</b>.
    /// - Si la portada enviada es imagen, se guarda <b>BannerTipo = IMAGEN</b>.
    /// - Si se modifica el slug, se valida que no esté usado por otro vendedor.
    /// - Los campos que no se envían conservan su valor actual.
    ///
    /// <b>Formato requerido:</b>
    /// Este endpoint consume <b>multipart/form-data</b>, ya que permite enviar archivos
    /// junto con los datos del formulario.
    ///
    /// <b>Importante:</b>
    /// El usuario debe estar autenticado y debe tener un perfil vendedor asociado.
    /// La condición premium no se modifica desde este endpoint.
    /// </remarks>
    [HttpPut("mi-perfil")]
    [Consumes("multipart/form-data")]
    [SwaggerOperation(
        Summary = "Actualiza mi perfil vendedor",
        Description = "Actualiza los datos públicos del perfil vendedor autenticado."
    )]
    public async Task<IActionResult> ActualizarMiPerfil([FromForm] ActualizarMiPerfilVendedorRequest request)
    {
        var idUsuario = _userContext.IdUsuario;

        if (idUsuario == null || idUsuario == 0)
            throw new UnauthorizedAccessException();

        var data = await _service.ActualizarMiPerfilVendedor(request, idUsuario.Value);

        return Ok(new Response<PerfilPublicoVendedorDto>
        {
            Success = true,
            Data = data,
            Message = "Perfil vendedor actualizado correctamente."
        });
    }
}

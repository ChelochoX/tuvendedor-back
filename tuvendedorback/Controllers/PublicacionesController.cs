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
public class PublicacionesController : ControllerBase
{
    private readonly IPublicacionService _service;
    private readonly ILogger<PublicacionesController> _logger;
    private readonly UserContext _userContext;

    public PublicacionesController(IPublicacionService service, ILogger<PublicacionesController> logger, UserContext userContext)
    {
        _service = service;
        _logger = logger;
        _userContext = userContext;
    }


    [HttpPost("crear-publicacion")]
    [Consumes("multipart/form-data")]
    [SwaggerOperation(
        Summary = "Crea una nueva publicación",
        Description = "Permite al usuario vendedor crear una publicación con imágenes.")]
    public async Task<IActionResult> Crear([FromForm] CrearPublicacionRequest request)
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
        request.Ubicacion = _userContext.Ubicacion ?? "";
        var publicacionId = await _service.CrearPublicacion(request, idUsuario.Value);

        return Ok(new Response<object>
        {
            Success = true,
            Message = "Publicación creada correctamente",
            Data = new { Id = publicacionId }
        });
    }


    [HttpGet("obtener-publicaciones")]
    [SwaggerOperation(
        Summary = "Obtiene el listado de publicaciones",
        Description = "Devuelve las publicaciones creadas por los vendedores incluyendo imágenes, información del vendedor y planes de crédito. "
                    + "Permite filtrar opcionalmente por categoría y por nombre del producto.")]
    public async Task<IActionResult> ObtenerPublicaciones(
        [FromQuery] string? categoria = null, string? nombre = null)
    {

        var idUsuario = _userContext.IdUsuario;

        var publicaciones = await _service.ObtenerPublicaciones(categoria, nombre, idUsuario);

        return Ok(new Response<List<ProductoDto>>
        {
            Success = true,
            Data = publicaciones,
            Message = "Publicaciones obtenidas correctamente"
        });
    }


    [HttpDelete("eliminar-publicacion/{id}")]
    [SwaggerOperation(
    Summary = "Elimina una publicación",
    Description = "Elimina una publicación junto con sus imágenes en Cloudinary y sus planes de crédito asociados.")]
    public async Task<IActionResult> EliminarPublicacion(int id)
    {
        await _service.EliminarPublicacion(id);

        return Ok(new Response<object>
        {
            Success = true,
            Message = "Publicación eliminada correctamente"
        });
    }

    [HttpGet("mis-publicaciones")]
    [SwaggerOperation(
    Summary = "Obtiene las publicaciones del usuario autenticado",
    Description = "Devuelve solo las publicaciones creadas por el usuario logueado.")]
    public async Task<IActionResult> ObtenerMisPublicaciones()
    {
        var idUsuario = _userContext.IdUsuario;

        if (idUsuario == null || idUsuario == 0)
            throw new UnauthorizedAccessException();

        var publicaciones = await _service.ObtenerMisPublicaciones(idUsuario.Value);

        return Ok(new Response<List<ProductoDto>>
        {
            Success = true,
            Data = publicaciones,
            Message = "Publicaciones del usuario obtenidas correctamente"
        });
    }


    [HttpGet("listar-categorias")]
    [SwaggerOperation(
       Summary = "Obtiene todas las categorías activas",
       Description = "Devuelve categorías de la base de datos con estado = Activo.")]
    public async Task<IActionResult> ObtenerCategorias()
    {
        var categorias = await _service.ObtenerCategoriasActivas();

        return Ok(new Response<List<CategoriaDto>>
        {
            Success = true,
            Data = categorias,
            Message = "Categorías obtenidas correctamente"
        });
    }

    [HttpPost("destacar-publicacion")]
    [SwaggerOperation(
      Summary = "Destaca una publicación existente",
      Description = "Permite al usuario vendedor destacar una publicación ya creada por un período determinado (en días)." +
        " Solo el dueño de la publicación puede destacarla.")]
    public async Task<IActionResult> DestacarPublicacion([FromBody] DestacarPublicacionRequest request)
    {
        var idUsuario = _userContext.IdUsuario;

        if (idUsuario == null || idUsuario == 0)
            throw new UnauthorizedAccessException();

        await _service.DestacarPublicacion(request, idUsuario.Value);

        return Ok(new Response<object>
        {
            Success = true,
            Message = "La publicación fue destacada correctamente."
        });
    }

    [HttpPost("quitar-destacado-publicacion")]
    [SwaggerOperation(
      Summary = "Quita el destacado de una publicación",
      Description = "Permite quitar manualmente el estado destacado de una publicación activa.")]
    public async Task<IActionResult> QuitarDestacadoPublicacion([FromBody] QuitarDestacadoPublicacionRequest request)
    {
        var idUsuario = _userContext.IdUsuario;

        if (idUsuario == null || idUsuario == 0)
            throw new UnauthorizedAccessException();

        await _service.QuitarDestacadoPublicacion(request.IdPublicacion, idUsuario.Value);

        return Ok(new Response<object>
        {
            Success = true,
            Message = "La publicación dejó de estar destacada."
        });
    }


    [HttpPost("activar-temporada")]
    [SwaggerOperation(
    Summary = "Activa una publicación como temporada",
    Description = "Permite a vendedores Premium o administradores activar una publicación como oferta de temporada. "
                + "Solo el dueño de la publicación o un administrador puede activarla.")]
    public async Task<IActionResult> ActivarTemporada([FromBody] ActivarTemporadaRequest request)
    {
        var idUsuario = _userContext.IdUsuario;

        if (idUsuario == null || idUsuario == 0)
            throw new UnauthorizedAccessException();

        await _service.ActivarTemporada(request, idUsuario.Value);

        return Ok(new Response<object>
        {
            Success = true,
            Message = "Publicación activada como temporada correctamente.",
            Data = new { request.IdPublicacion }
        });
    }


    [HttpPost("desactivar-temporada")]
    [SwaggerOperation(
    Summary = "Desactiva una publicación de temporada",
    Description = "Permite a administradores o vendedores premium desactivar una publicación marcada como temporada.")]
    public async Task<IActionResult> DesactivarTemporada([FromBody] DesactivarTemporadaRequest request)
    {
        var idUsuario = _userContext.IdUsuario;

        if (idUsuario == null || idUsuario == 0)
            throw new UnauthorizedAccessException();

        await _service.DesactivarTemporada(request.IdPublicacion, idUsuario.Value);

        return Ok(new Response<object>
        {
            Success = true,
            Message = "La publicación fue desactivada de temporada correctamente.",
            Data = new { request.IdPublicacion }
        });
    }


    [HttpGet("listar-temporadas")]
    [SwaggerOperation(
    Summary = "Obtiene el listado de temporadas activas",
    Description = "Devuelve todas las temporadas configuradas por el administrador que están en estado 'Activo'. "
                + "Incluye nombre, colores del badge y rango de fechas definido para la temporada.")]
    public async Task<IActionResult> ListarTemporadas()
    {
        var data = await _service.ObtenerTemporadasActivas();

        return Ok(new Response<List<TemporadaDto>>
        {
            Success = true,
            Data = data,
            Message = "Temporadas activas obtenidas correctamente."
        });
    }


    [HttpPost("crear-sugerencia")]
    [SwaggerOperation(
       Summary = "Crea una sugerencia",
       Description = "Guarda un comentario, feedback o sugerencia del usuario en la base de datos.")]
    public async Task<IActionResult> CrearSugerencia([FromBody] CrearSugerenciaRequest request)
    {
        var idUsuario = _userContext.IdUsuario;

        var id = await _service.CrearSugerencia(request, idUsuario);

        return Ok(new Response<object>
        {
            Success = true,
            Message = "Sugerencia enviada correctamente",
            Data = new { Id = id }
        });
    }

    [HttpPost("marcar-vendido")]
    [SwaggerOperation(
    Summary = "Marca una publicación como vendida",
    Description = "Permite al usuario dueño de la publicación marcarla con estado VENDIDO.")]
    public async Task<IActionResult> MarcarComoVendido([FromBody] MarcarVendidoRequest request)
    {
        var idUsuario = _userContext.IdUsuario;

        if (idUsuario == null || idUsuario == 0)
            throw new UnauthorizedAccessException();

        await _service.MarcarComoVendido(request.IdPublicacion, idUsuario.Value);

        return Ok(new Response<object>
        {
            Success = true,
            Message = "La publicación fue marcada como vendida correctamente.",
            Data = new { request.IdPublicacion }
        });
    }

}

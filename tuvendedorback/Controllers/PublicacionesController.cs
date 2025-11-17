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
                    + "Permite filtrar opcionalmente por categoría y por nombre del producto."
    )]
    public async Task<IActionResult> ObtenerPublicaciones(
        [FromQuery] string? categoria = null, string? nombre = null)
    {
        var publicaciones = await _service.ObtenerPublicaciones(categoria, nombre);

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

}

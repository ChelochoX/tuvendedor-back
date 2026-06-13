using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using tuvendedorback.Common;
using tuvendedorback.DTOs;
using tuvendedorback.Request;
using tuvendedorback.Services.Interfaces;
using tuvendedorback.Wrappers;

namespace tuvendedorback.Controllers;

[ApiController]
[Authorize]
[Route("api/admin/banners-publicitarios")]
public class BannersPublicitariosAdminController
    : ControllerBase
{
    private readonly IBannerPublicitarioService _service;

    private readonly UserContext _userContext;

    public BannersPublicitariosAdminController(
        IBannerPublicitarioService service,
        UserContext userContext)
    {
        _service = service;

        _userContext = userContext;
    }

    [HttpGet]
    [SwaggerOperation(
        Summary = "Lista banners para administración")]
    public async Task<IActionResult> Listar(
        [FromQuery]
        FiltroBannersPublicitariosRequest filtro)
    {
        var data =
            await _service.ListarAdmin(
                filtro,
                ObtenerIdUsuario());

        return Ok(
            new Response<
                Datos<List<BannerPublicitarioDto>>
            >
            {
                Success = true,

                Data = data,

                Message =
                    "Banners publicitarios obtenidos " +
                    "correctamente."
            });
    }

    [HttpGet("configuracion")]
    [SwaggerOperation(
        Summary =
            "Obtiene posiciones, destinos, estados y " +
            "medidas permitidas")]
    public async Task<IActionResult> ObtenerConfiguracion()
    {
        var data =
            await _service.ObtenerConfiguracionAdmin(
                ObtenerIdUsuario());

        return Ok(
            new Response<
                BannerConfiguracionAdminDto
            >
            {
                Success = true,

                Data = data,

                Message =
                    "Configuración de banners obtenida " +
                    "correctamente."
            });
    }

    [HttpGet("resumen")]
    [SwaggerOperation(
        Summary =
            "Obtiene resumen comercial de banners")]
    public async Task<IActionResult> ObtenerResumen()
    {
        var data =
            await _service.ObtenerResumen(
                ObtenerIdUsuario());

        return Ok(
            new Response<
                ResumenBannersPublicitariosDto
            >
            {
                Success = true,

                Data = data,

                Message =
                    "Resumen de banners obtenido correctamente."
            });
    }

    [HttpGet("{id:int}")]
    [SwaggerOperation(
        Summary =
            "Obtiene un banner por ID")]
    public async Task<IActionResult> ObtenerPorId(
        int id)
    {
        var data =
            await _service.ObtenerPorId(
                id,
                ObtenerIdUsuario());

        return Ok(
            new Response<
                BannerPublicitarioDto
            >
            {
                Success = true,

                Data = data,

                Message =
                    "Banner publicitario obtenido " +
                    "correctamente."
            });
    }

    [HttpGet("{id:int}/archivos")]
    [SwaggerOperation(
        Summary =
            "Obtiene historial de imágenes del banner")]
    public async Task<IActionResult> ObtenerArchivos(
        int id)
    {
        var data =
            await _service.ObtenerArchivos(
                id,
                ObtenerIdUsuario());

        return Ok(
            new Response<
                List<BannerPublicitarioArchivoDto>
            >
            {
                Success = true,

                Data = data,

                Message =
                    "Historial de imágenes obtenido " +
                    "correctamente."
            });
    }

    [HttpPost]
    [Consumes("multipart/form-data")]
    [SwaggerOperation(
        Summary =
            "Crea un banner publicitario")]
    public async Task<IActionResult> Crear(
        [FromForm]
        CrearBannerPublicitarioRequest request)
    {
        var id =
            await _service.Crear(
                request,
                ObtenerIdUsuario());

        return Ok(
            new Response<object>
            {
                Success = true,

                Data =
                    new
                    {
                        Id = id
                    },

                Message =
                    "Banner publicitario creado correctamente."
            });
    }

    [HttpPut("{id:int}")]
    [Consumes("multipart/form-data")]
    [SwaggerOperation(
        Summary =
            "Actualiza un banner publicitario")]
    public async Task<IActionResult> Actualizar(
        int id,
        [FromForm]
        ActualizarBannerPublicitarioRequest request)
    {
        await _service.Actualizar(
            id,
            request,
            ObtenerIdUsuario());

        return Ok(
            new Response<object>
            {
                Success = true,

                Data =
                    new
                    {
                        Id = id
                    },

                Message =
                    "Banner publicitario actualizado " +
                    "correctamente."
            });
    }

    [HttpPatch("{id:int}/estado")]
    [SwaggerOperation(
        Summary =
            "Cambia estado del banner")]
    public async Task<IActionResult> CambiarEstado(
        int id,
        [FromBody]
        CambiarEstadoBannerPublicitarioRequest request)
    {
        await _service.CambiarEstado(
            id,
            request,
            ObtenerIdUsuario());

        return Ok(
            new Response<object>
            {
                Success = true,

                Data =
                    new
                    {
                        Id = id,
                        request.Estado
                    },

                Message =
                    "Estado del banner actualizado " +
                    "correctamente."
            });
    }

    [HttpDelete("{id:int}")]
    [SwaggerOperation(
        Summary =
            "Elimina lógicamente un banner publicitario")]
    public async Task<IActionResult> Eliminar(
        int id)
    {
        await _service.Eliminar(
            id,
            ObtenerIdUsuario());

        return Ok(
            new Response<object>
            {
                Success = true,

                Message =
                    "Banner eliminado lógicamente. Los archivos " +
                    "quedarán disponibles durante el período " +
                    "de seguridad."
            });
    }

    [HttpPost("limpieza-cloudinary")]
    [SwaggerOperation(
        Summary =
            "Elimina de Cloudinary revisiones vencidas")]
    public async Task<IActionResult> LimpiarCloudinary(
        [FromQuery]
        int limite = 100)
    {
        var data =
            await _service.LimpiarArchivosCloudinary(
                limite,
                ObtenerIdUsuario());

        return Ok(
            new Response<
                LimpiezaBannerArchivosDto
            >
            {
                Success = true,

                Data = data,

                Message =
                    "Limpieza de archivos ejecutada " +
                    "correctamente."
            });
    }

    private int ObtenerIdUsuario()
    {
        var idUsuario =
            _userContext.IdUsuario;

        if (
            idUsuario == null
            || idUsuario <= 0
        )
        {
            throw new UnauthorizedAccessException();
        }

        return idUsuario.Value;
    }
}

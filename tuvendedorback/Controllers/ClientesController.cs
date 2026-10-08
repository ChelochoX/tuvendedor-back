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
public class ClientesController : ControllerBase
{
    private readonly IClientesService _service;
    private readonly UserContext _userContext;

    public ClientesController(
        UserContext userContext,
        IClientesService service)
    {
        _userContext = userContext;
        _service = service;
    }


    [HttpPost("registrar-interesados")]
    [Consumes("multipart/form-data")]
    [SwaggerOperation(
        Summary = "Registrar un nuevo interesado")]
    public async Task<IActionResult> Registrar(
        [FromForm] InteresadoRequest request)
    {
        var idUsuario =
            _userContext.IdUsuario;

        if (idUsuario == null || idUsuario == 0)
        {
            return Unauthorized(
                new Response<object>
                {
                    Success = false,
                    Errors =
                        new List<string>
                        {
                            "Usuario no autenticado."
                        },
                    StatusCode = 401
                });
        }

        var id =
            await _service.RegistrarInteresado(
                request,
                idUsuario.Value);

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
                    "Interesado registrado correctamente"
            });
    }


    [HttpPost("registrar-seguimiento")]
    [SwaggerOperation(
        Summary = "Agregar seguimiento a un interesado existente")]
    public async Task<IActionResult> AgregarSeguimiento(
        [FromBody] SeguimientoRequest request)
    {
        var idUsuario =
            _userContext.IdUsuario;

        if (idUsuario == null || idUsuario == 0)
        {
            return Unauthorized(
                new Response<object>
                {
                    Success = false,
                    Errors =
                        new List<string>
                        {
                            "Usuario no autenticado."
                        },
                    StatusCode = 401
                });
        }

        var id =
            await _service.AgregarSeguimiento(
                request,
                idUsuario.Value);

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
                    "Seguimiento agregado correctamente"
            });
    }


    [HttpPut("actualizar-seguimiento/{id:int}")]
    [SwaggerOperation(
        Summary = "Actualizar próxima gestión/seguimiento de un interesado")]
    public async Task<IActionResult> ActualizarSeguimiento(
        int id,
        [FromBody] ActualizarSeguimientoInteresadoRequest request)
    {
        var idUsuario =
            _userContext.IdUsuario;

        if (idUsuario is null or 0)
        {
            return Unauthorized(
                new Response<object>
                {
                    Success = false,
                    Errors =
                        new List<string>
                        {
                            "Usuario no autenticado."
                        },
                    StatusCode = 401
                });
        }

        await _service.ActualizarSeguimientoInteresado(
            id,
            request,
            idUsuario.Value);

        return Ok(
            new Response<object>
            {
                Success = true,
                Message =
                    "Seguimiento actualizado correctamente.",
                StatusCode = 200
            });
    }



    [HttpPost("sincronizar-whatsapp-dia")]
    [SwaggerOperation(
        Summary = "Sincroniza los chats activos de WhatsApp del día con el CRM de interesados",
        Description =
            "Importa/actualiza contactos de WhatsApp sin duplicarlos y conserva los seguimientos manuales existentes.")]
    public async Task<IActionResult> SincronizarWhatsAppDia(
        [FromQuery] DateTime? fecha = null)
    {
        var idUsuario =
            _userContext.IdUsuario;

        if (idUsuario is null or 0)
        {
            return Unauthorized(
                new Response<object>
                {
                    Success = false,
                    Errors =
                        new List<string>
                        {
                            "Usuario no autenticado."
                        },
                    StatusCode = 401
                });
        }

        var data =
            await _service.SincronizarWhatsAppDia(
                fecha,
                idUsuario.Value);

        return Ok(
            new Response<SincronizacionWhatsAppResultadoDto>
            {
                Success = true,
                Data = data,
                Message =
                    "WhatsApp sincronizado correctamente.",
                StatusCode = 200
            });
    }


    [HttpGet("obtener-interesados")]
    [SwaggerOperation(
        Summary = "Obtiene el listado de interesados",
        Description =
            "Devuelve los interesados manuales y los registrados automáticamente desde WhatsApp.")]
    public async Task<IActionResult> ObtenerInteresados(
        [FromQuery] FiltroInteresadosRequest filtro)
    {
        var (items, total) =
            await _service.ObtenerInteresados(
                filtro);

        var resultado =
            new
            {
                TotalRegistros =
                    total,

                PaginaActual =
                    filtro.NumeroPagina,

                RegistrosPorPagina =
                    filtro.RegistrosPorPagina,

                Items =
                    items
            };

        return Ok(
            new Response<object>
            {
                Success = true,
                Data = resultado,
                Message =
                    "Interesados obtenidos correctamente",
                StatusCode = 200
            });
    }


    [HttpGet("resumen-interesados")]
    [SwaggerOperation(
        Summary = "Resumen comercial de interesados para dashboard")]
    public async Task<IActionResult> ObtenerResumen(
        [FromQuery] DateTime? fecha = null)
    {
        var data =
            await _service.ObtenerResumenInteresados(
                fecha);

        return Ok(
            new Response<InteresadosResumenDto>
            {
                Success = true,
                Data = data,
                Message =
                    "Resumen de interesados obtenido correctamente.",
                StatusCode = 200
            });
    }


    [HttpGet("detalle-interesado/{id:int}")]
    [SwaggerOperation(
        Summary = "Detalle completo del interesado, modelos consultados y conversación")]
    public async Task<IActionResult> ObtenerDetalle(
        int id)
    {
        var data =
            await _service.ObtenerDetalleInteresado(
                id);

        return Ok(
            new Response<InteresadoDetalleDto>
            {
                Success = true,
                Data = data,
                Message =
                    "Detalle del interesado obtenido correctamente.",
                StatusCode = 200
            });
    }


    [HttpGet("obtener-seguimientos")]
    [SwaggerOperation(
        Summary = "Obtiene los seguimientos de un interesado")]
    public async Task<IActionResult> ObtenerSeguimientos(
        [FromQuery] int idInteresado)
    {
        var lista =
            await _service.ObtenerSeguimientosPorInteresado(
                idInteresado);

        return Ok(
            new Response<List<SeguimientoDto>>
            {
                Success = true,
                Data = lista,
                Message =
                    "Seguimientos obtenidos correctamente",
                StatusCode = 200
            });
    }


    [HttpPut("actualizar-interesado/{id}")]
    [Consumes("multipart/form-data")]
    [SwaggerOperation(
        Summary = "Actualizar datos de un interesado existente")]
    public async Task<IActionResult> Actualizar(
        int id,
        [FromForm] InteresadoRequest request)
    {
        var idUsuario =
            _userContext.IdUsuario;

        if (idUsuario is null or 0)
        {
            return Unauthorized(
                new Response<object>
                {
                    Success = false,
                    Errors =
                        new List<string>
                        {
                            "Usuario no autenticado."
                        },
                    StatusCode = 401
                });
        }

        await _service.ActualizarInteresado(
            id,
            request,
            idUsuario.Value);

        return Ok(
            new Response<object>
            {
                Success = true,
                Message =
                    "Interesado actualizado correctamente",
                StatusCode = 200
            });
    }
}
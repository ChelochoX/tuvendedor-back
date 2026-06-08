using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using tuvendedorback.Common;
using tuvendedorback.DTOs;
using tuvendedorback.Request;
using tuvendedorback.Services.Interfaces;
using tuvendedorback.Wrappers;

namespace tuvendedorback.Controllers;

[ApiController]
[Route("api/admin/servicios-premium")]
public class AdminServiciosPremiumController :
    ControllerBase
{
    private readonly IServicioPremiumService
        _service;

    private readonly UserContext
        _userContext;

    public AdminServiciosPremiumController(
        IServicioPremiumService service,
        UserContext userContext)
    {
        _service = service;
        _userContext = userContext;
    }

    /// <summary>
    /// Lista servicios Premium con filtros y paginación.
    /// </summary>
    /// <remarks>
    /// Endpoint exclusivo para administradores.
    ///
    /// Filtros disponibles:
    /// - Estado.
    /// - Tipo de servicio.
    /// - Cliente o nombre de negocio.
    /// - Fecha desde.
    /// - Fecha hasta.
    /// - Página.
    /// - Tamaño de página.
    /// </remarks>
    [HttpGet]
    [SwaggerOperation(
        Summary =
            "Lista servicios Premium",

        Description =
            "Lista solicitudes y servicios Premium con filtros y paginación."
    )]
    public async Task<IActionResult>
        ObtenerServicios(
            [FromQuery]
            FiltrosServiciosPremiumRequest filtros)
    {
        var idUsuarioAdmin =
            ObtenerIdUsuario();

        var data =
            await _service
                .ObtenerServiciosParaAdministrador(
                    filtros,
                    idUsuarioAdmin);

        return Ok(
            new Response<
                Datos<
                    List<ServicioPremiumDto>
                >
            >
            {
                Success =
                    true,

                Data =
                    data,

                Message =
                    "Servicios Premium obtenidos correctamente."
            });
    }

    /// <summary>
    /// Obtiene un servicio Premium por identificador.
    /// </summary>
    [HttpGet("{idServicio:int}")]
    [SwaggerOperation(
        Summary =
            "Obtiene un servicio Premium",

        Description =
            "Devuelve los datos completos de una solicitud o servicio Premium."
    )]
    public async Task<IActionResult>
        ObtenerServicioPorId(
            int idServicio)
    {
        var idUsuarioAdmin =
            ObtenerIdUsuario();

        var data =
            await _service
                .ObtenerServicioPorIdParaAdministrador(
                    idServicio,
                    idUsuarioAdmin);

        return Ok(
            new Response<
                ServicioPremiumDto
            >
            {
                Success =
                    true,

                Data =
                    data,

                Message =
                    "Servicio Premium obtenido correctamente."
            });
    }

    /// <summary>
    /// Obtiene el resumen superior del dashboard.
    /// </summary>
    [HttpGet("resumen")]
    [SwaggerOperation(
        Summary =
            "Obtiene el resumen Premium",

        Description =
            "Devuelve solicitudes pendientes, servicios activos, próximos vencimientos y total histórico cobrado."
    )]
    public async Task<IActionResult>
        ObtenerResumen()
    {
        var idUsuarioAdmin =
            ObtenerIdUsuario();

        var data =
            await _service
                .ObtenerResumenParaAdministrador(
                    idUsuarioAdmin);

        return Ok(
            new Response<
                ResumenServiciosPremiumDto
            >
            {
                Success =
                    true,

                Data =
                    data,

                Message =
                    "Resumen Premium obtenido correctamente."
            });
    }

    /// <summary>
    /// Confirma el pago y activa el beneficio.
    /// </summary>
    [HttpPost("{idServicio:int}/activar")]
    [SwaggerOperation(
        Summary =
            "Confirma el pago y activa un servicio Premium",

        Description =
            "Registra los datos comerciales y aplica técnicamente el beneficio correspondiente."
    )]
    public async Task<IActionResult>
        ActivarServicio(
            int idServicio,
            [FromBody]
            ActivarServicioPremiumRequest request)
    {
        var idUsuarioAdmin =
            ObtenerIdUsuario();

        await _service
            .ActivarServicio(
                idServicio,
                request,
                idUsuarioAdmin);

        return Ok(
            new Response<bool>
            {
                Success =
                    true,

                Data =
                    true,

                Message =
                    "Pago registrado y servicio Premium activado correctamente."
            });
    }

    /// <summary>
    /// Cancela o desactiva un servicio Premium.
    /// </summary>
    [HttpPost("{idServicio:int}/cancelar")]
    [SwaggerOperation(
        Summary =
            "Cancela un servicio Premium",

        Description =
            "Cancela la solicitud y retira el beneficio cuando corresponda."
    )]
    public async Task<IActionResult>
        CancelarServicio(
            int idServicio,
            [FromBody]
            CancelarServicioPremiumRequest request)
    {
        var idUsuarioAdmin =
            ObtenerIdUsuario();

        await _service
            .CancelarServicio(
                idServicio,
                request,
                idUsuarioAdmin);

        return Ok(
            new Response<bool>
            {
                Success =
                    true,

                Data =
                    true,

                Message =
                    "Servicio Premium cancelado correctamente."
            });
    }

    private int ObtenerIdUsuario()
    {
        var idUsuario =
            _userContext
                .IdUsuario;

        if (
            idUsuario ==
            null
            ||
            idUsuario ==
            0)
        {
            throw new UnauthorizedAccessException();
        }

        return idUsuario
            .Value;
    }
}

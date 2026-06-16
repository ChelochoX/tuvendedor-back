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
[Route("api/admin/dashboard-comercial")]
public class AdminDashboardComercialController
    : ControllerBase
{
    private readonly IComercialDashboardService _service;

    private readonly UserContext _userContext;

    public AdminDashboardComercialController(
        IComercialDashboardService service,
        UserContext userContext)
    {
        _service =
            service;

        _userContext =
            userContext;
    }

    [HttpGet]
    [SwaggerOperation(
        Summary = "Obtiene el dashboard comercial",
        Description = "Devuelve métricas comerciales del marketplace, publicaciones, banners, WhatsApp, solicitudes y rubros.")]
    public async Task<IActionResult> ObtenerDashboard(
        [FromQuery] FiltroDashboardComercialRequest filtro)
    {
        var data =
            await _service.ObtenerDashboard(
                filtro,
                ObtenerIdUsuario());

        return Ok(
            new Response<ComercialDashboardDto>
            {
                Success =
                    true,

                Data =
                    data,

                Message =
                    "Dashboard comercial obtenido correctamente."
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

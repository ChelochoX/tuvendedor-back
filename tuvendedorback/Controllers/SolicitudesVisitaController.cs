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
public class SolicitudesVisitaController : ControllerBase
{
    private readonly ISolicitudVisitaService _service;
    private readonly UserContext _userContext;

    public SolicitudesVisitaController(
        ISolicitudVisitaService service,
        UserContext userContext)
    {
        _service = service;
        _userContext = userContext;
    }

    [HttpPost("crear")]
    [SwaggerOperation(
        Summary = "Crea una solicitud de visita",
        Description = "Permite a un interesado solicitar una visita para una publicación inmobiliaria. No requiere autenticación del cliente.")]
    public async Task<IActionResult> Crear([FromBody] CrearSolicitudVisitaRequest request)
    {
        var data = await _service.CrearSolicitudVisita(request);

        return Ok(new Response<ResultadoSolicitudVisitaDto>
        {
            Success = true,
            Data = data,
            Message = "Solicitud enviada. El vendedor recibió tu solicitud de visita. Te contactará para confirmar la disponibilidad."
        });
    }

    [HttpGet("mis-solicitudes")]
    [SwaggerOperation(
        Summary = "Obtiene mis solicitudes de visita",
        Description = "Devuelve las solicitudes de visita recibidas por el vendedor autenticado.")]
    public async Task<IActionResult> ObtenerMisSolicitudes()
    {
        var idUsuario = _userContext.IdUsuario;

        if (idUsuario == null || idUsuario == 0)
            throw new UnauthorizedAccessException();

        var data = await _service.ObtenerMisSolicitudesVisita(idUsuario.Value);

        return Ok(new Response<List<SolicitudVisitaDto>>
        {
            Success = true,
            Data = data,
            Message = "Solicitudes de visita obtenidas correctamente."
        });
    }
}

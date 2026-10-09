using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using tuvendedorback.DTOs;
using tuvendedorback.Request;
using tuvendedorback.Services.Interfaces;
using tuvendedorback.Wrappers;

namespace tuvendedorback.Controllers;

[Authorize]
[ApiController]
[Route("api/clientes/seguimiento-whatsapp")]
public sealed class SeguimientoWhatsappController : ControllerBase
{
    private readonly ISeguimientoWhatsappService _service;

    public SeguimientoWhatsappController(
        ISeguimientoWhatsappService service)
    {
        _service = service;
    }

    [HttpGet("configuracion")]
    public async Task<IActionResult> ObtenerConfiguracion()
    {
        var data = await _service.ObtenerConfiguracion();

        return Ok(
            new Response<SeguimientoWhatsappConfiguracionDto>
            {
                Success = true,
                StatusCode = 200,
                Message = "Configuración de seguimiento WhatsApp obtenida correctamente.",
                Data = data
            });
    }

    [HttpPut("configuracion")]
    public async Task<IActionResult> ActualizarConfiguracion(
        [FromBody] ActualizarSeguimientoWhatsappConfiguracionRequest request)
    {
        try
        {
            var data = await _service.ActualizarConfiguracion(
                request,
                ObtenerUsuarioId());

            return Ok(
                new Response<SeguimientoWhatsappConfiguracionDto>
                {
                    Success = true,
                    StatusCode = 200,
                    Message = "Configuración de seguimiento WhatsApp actualizada correctamente.",
                    Data = data
                });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(
                new Response<object>
                {
                    Success = false,
                    StatusCode = 400,
                    Message = ex.Message,
                    Data = null
                });
        }
    }

    [HttpGet("envios")]
    public async Task<IActionResult> ListarEnvios(
        [FromQuery] string? estado = null,
        [FromQuery] int limite = 200)
    {
        var data = await _service.ListarEnvios(estado, limite);

        return Ok(
            new Response<IReadOnlyList<SeguimientoWhatsappEnvioDto>>
            {
                Success = true,
                StatusCode = 200,
                Message = "Cola de seguimiento WhatsApp obtenida correctamente.",
                Data = data
            });
    }

    [HttpPost("procesar-ahora")]
    public async Task<IActionResult> ProcesarAhora(CancellationToken cancellationToken)
    {
        await _service.ProcesarCiclo(cancellationToken);

        var data = await _service.ListarEnvios(null, 200);

        return Ok(
            new Response<IReadOnlyList<SeguimientoWhatsappEnvioDto>>
            {
                Success = true,
                StatusCode = 200,
                Message = "Ciclo de seguimiento procesado. En SIMULACION solo se prepara la cola; no se envían mensajes.",
                Data = data
            });
    }

    private int? ObtenerUsuarioId()
    {
        var valor =
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("nameid")
            ?? User.FindFirstValue("sub")
            ?? User.FindFirstValue("id")
            ?? User.FindFirstValue("IdUsuario");

        return int.TryParse(valor, out var idUsuario) && idUsuario > 0
            ? idUsuario
            : null;
    }
}

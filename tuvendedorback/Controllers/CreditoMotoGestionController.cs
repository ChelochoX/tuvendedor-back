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
[Route("api/creditos/motos/solicitudes")]
public class CreditoMotoGestionController : ControllerBase
{
    private readonly ICreditoMotoGestionService _service;

    public CreditoMotoGestionController(
        ICreditoMotoGestionService service)
    {
        _service = service;
    }


    // =========================================================
    // 1) BANDEJA
    // GET /api/creditos/motos/solicitudes
    // =========================================================

    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] string? estado = null,
        [FromQuery] string? buscar = null)
    {
        var data =
            await _service.Listar(
                estado,
                buscar);

        return Ok(
            new Response<IReadOnlyList<CreditoMotoGestionListaDto>>
            {
                Success = true,
                StatusCode = 200,
                Message =
                    "Solicitudes de crédito obtenidas correctamente.",
                Data = data
            });
    }


    // =========================================================
    // 2) DETALLE COMPLETO
    // GET /api/creditos/motos/solicitudes/{id}
    // =========================================================

    [HttpGet("{idSolicitudCredito:int}")]
    public async Task<IActionResult> ObtenerDetalle(
        int idSolicitudCredito)
    {
        var data =
            await _service.ObtenerDetalle(
                idSolicitudCredito);

        return Ok(
            new Response<CreditoMotoGestionDetalleDto>
            {
                Success = true,
                StatusCode = 200,
                Message =
                    "Solicitud de crédito obtenida correctamente.",
                Data = data
            });
    }


    // =========================================================
    // 3) TOMAR SOLICITUD
    // POST /api/creditos/motos/solicitudes/{id}/tomar
    // =========================================================

    [HttpPost("{idSolicitudCredito:int}/tomar")]
    public async Task<IActionResult> TomarSolicitud(
        int idSolicitudCredito)
    {
        if (!TryObtenerUsuarioId(
                out var idUsuario))
        {
            return Unauthorized(
                new
                {
                    message =
                        "No se pudo identificar al usuario autenticado."
                });
        }

        var data =
            await _service.TomarSolicitud(
                idSolicitudCredito,
                idUsuario);

        return Ok(
            new Response<CreditoMotoGestionDetalleDto>
            {
                Success = true,
                StatusCode = 200,
                Message =
                    "Solicitud tomada para revisión correctamente.",
                Data = data
            });
    }


    // =========================================================
    // 4) CAMBIAR ESTADO
    // PATCH /api/creditos/motos/solicitudes/{id}/estado
    // =========================================================

    [HttpPatch("{idSolicitudCredito:int}/estado")]
    public async Task<IActionResult> CambiarEstado(
        int idSolicitudCredito,
        [FromBody] CreditoMotoCambiarEstadoRequest request)
    {
        if (!TryObtenerUsuarioId(
                out var idUsuario))
        {
            return Unauthorized(
                new
                {
                    message =
                        "No se pudo identificar al usuario autenticado."
                });
        }

        var data =
            await _service.CambiarEstado(
                idSolicitudCredito,
                idUsuario,
                request);

        return Ok(
            new Response<CreditoMotoGestionDetalleDto>
            {
                Success = true,
                StatusCode = 200,
                Message =
                    "Estado de la solicitud actualizado correctamente.",
                Data = data
            });
    }


    // =========================================================
    // 5) DOCUMENTO PRIVADO
    //
    // GET
    // /api/creditos/motos/solicitudes/{id}/documentos/{idDocumento}/archivo
    // =========================================================

    [HttpGet(
        "{idSolicitudCredito:int}/documentos/{idDocumento:int}/archivo")]
    public async Task<IActionResult> ObtenerDocumento(
        int idSolicitudCredito,
        int idDocumento)
    {
        var archivo =
            await _service.ObtenerDocumento(
                idSolicitudCredito,
                idDocumento);

        return File(
            archivo.Contenido,
            archivo.MimeType,
            archivo.NombreArchivo);
    }


    // =========================================================
    // USUARIO AUTENTICADO
    // =========================================================

    private bool TryObtenerUsuarioId(
        out int idUsuario)
    {
        idUsuario = 0;

        var valor =
            User.FindFirstValue(
                ClaimTypes.NameIdentifier)
            ??
            User.FindFirstValue("nameid")
            ??
            User.FindFirstValue("sub")
            ??
            User.FindFirstValue("id")
            ??
            User.FindFirstValue("IdUsuario");

        return int.TryParse(
                   valor,
                   out idUsuario)
               &&
               idUsuario > 0;
    }
}

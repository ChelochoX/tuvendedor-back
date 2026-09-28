using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using tuvendedorback.Common;
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
    private readonly UserContext _userContext;

    public CreditoMotoGestionController(
        ICreditoMotoGestionService service,
        UserContext userContext)
    {
        _service = service;
        _userContext = userContext;
    }


    // =========================================================
    // LISTAR SOLICITUDES
    // =========================================================

    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] string? estado = null,
        [FromQuery] string? buscar = null,
        [FromQuery] DateTime? fecha = null)
    {
        var data =
            await _service.Listar(
                estado,
                buscar,
                fecha);

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
    // DETALLE
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
    // MARCAR COMO ENVIADA A LA EMPRESA
    // =========================================================

    [HttpPatch("{idSolicitudCredito:int}/estado")]
    public async Task<IActionResult> CambiarEstado(
        int idSolicitudCredito,
        [FromBody] CreditoMotoCambiarEstadoRequest request)
    {
        var idUsuario =
            ObtenerIdUsuario();

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
                    "Solicitud marcada como enviada a la empresa correctamente.",
                Data = data
            });
    }


    // =========================================================
    // VER DOCUMENTO PRIVADO
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

    private int ObtenerIdUsuario()
    {
        var idUsuario =
            _userContext.IdUsuario;

        if (
            idUsuario == null
            || idUsuario <= 0
        )
        {
            throw new UnauthorizedAccessException(
                "No se pudo identificar al usuario autenticado.");
        }

        return idUsuario.Value;
    }
}
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
public class CreditoMotoGestionController
    : ControllerBase
{
    private readonly ICreditoMotoGestionService _service;

    private readonly ICreditoMotoPdfService _pdfService;


    public CreditoMotoGestionController(
        ICreditoMotoGestionService service,
        ICreditoMotoPdfService pdfService)
    {
        _service =
            service;

        _pdfService =
            pdfService;
    }


    // =========================================================
    // LISTAR
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
            new Response<
                IReadOnlyList<CreditoMotoGestionListaDto>
            >
            {
                Success =
                    true,

                StatusCode =
                    200,

                Message =
                    "Solicitudes de crédito obtenidas correctamente.",

                Data =
                    data
            });
    }


    // =========================================================
    // DETALLE
    // =========================================================

    [HttpGet(
        "{idSolicitudCredito:int}")]
    public async Task<IActionResult> ObtenerDetalle(
        int idSolicitudCredito)
    {
        var data =
            await _service.ObtenerDetalle(
                idSolicitudCredito);

        return Ok(
            new Response<
                CreditoMotoGestionDetalleDto
            >
            {
                Success =
                    true,

                StatusCode =
                    200,

                Message =
                    "Solicitud de crédito obtenida correctamente.",

                Data =
                    data
            });
    }


    // =========================================================
    // PDF PARA CHACOMER
    //
    // GET
    // /api/creditos/motos/solicitudes/{id}/pdf
    // =========================================================

    [HttpGet(
        "{idSolicitudCredito:int}/pdf")]
    public async Task<IActionResult> GenerarPdf(
        int idSolicitudCredito)
    {
        var archivo =
            await _pdfService.GenerarPdf(
                idSolicitudCredito);

        return File(
            archivo.Contenido,
            archivo.MimeType,
            archivo.NombreArchivo);
    }


    // =========================================================
    // CAMBIAR ESTADO
    // =========================================================

    [HttpPatch(
        "{idSolicitudCredito:int}/estado")]
    public async Task<IActionResult> CambiarEstado(
        int idSolicitudCredito,
        [FromBody]
        CreditoMotoCambiarEstadoRequest request)
    {
        if (
            !TryObtenerUsuarioId(
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
            new Response<
                CreditoMotoGestionDetalleDto
            >
            {
                Success =
                    true,

                StatusCode =
                    200,

                Message =
                    "Estado de la solicitud actualizado correctamente.",

                Data =
                    data
            });
    }


    // =========================================================
    // DOCUMENTOS
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
    // USUARIO
    // =========================================================

    private bool TryObtenerUsuarioId(
        out int idUsuario)
    {
        idUsuario =
            0;

        var valor =
            User.FindFirstValue(
                ClaimTypes.NameIdentifier)
            ??
            User.FindFirstValue(
                "nameid")
            ??
            User.FindFirstValue(
                "sub")
            ??
            User.FindFirstValue(
                "id")
            ??
            User.FindFirstValue(
                "IdUsuario");

        return
            int.TryParse(
                valor,
                out idUsuario)
            &&
            idUsuario > 0;
    }
}

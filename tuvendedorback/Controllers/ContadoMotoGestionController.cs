using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using tuvendedorback.DTOs;
using tuvendedorback.Request;
using tuvendedorback.Services.Interfaces;
using tuvendedorback.Wrappers;

namespace tuvendedorback.Controllers;

[Authorize]
[ApiController]
[Route("api/ventas/motos/contado")]
public class ContadoMotoGestionController
    : ControllerBase
{
    private readonly IContadoMotoGestionService _service;

    public ContadoMotoGestionController(
        IContadoMotoGestionService service)
    {
        _service = service;
    }


    [HttpGet("solicitudes")]
    public async Task<IActionResult> Listar(
        [FromQuery] string? estado = "TODOS",
        [FromQuery] string? buscar = null,
        [FromQuery] DateTime? fecha = null)
    {
        var data =
            await _service.Listar(
                estado,
                buscar,
                fecha);

        return Ok(
            new Response<IReadOnlyList<ContadoMotoGestionListaDto>>
            {
                Success = true,
                StatusCode = 200,
                Message =
                    "Solicitudes al contado obtenidas correctamente.",
                Data = data
            });
    }


    [HttpGet("solicitudes/{idSolicitudContado:int}")]
    public async Task<IActionResult> ObtenerDetalle(
        int idSolicitudContado)
    {
        var data =
            await _service.ObtenerDetalle(
                idSolicitudContado);

        return Ok(
            new Response<ContadoMotoGestionDetalleDto>
            {
                Success = true,
                StatusCode = 200,
                Message =
                    "Solicitud al contado obtenida correctamente.",
                Data = data
            });
    }


    // =========================================================
    // CONTACTAR
    // =========================================================
    // IMPORTANTE:
    // [Authorize] sigue protegiendo el endpoint.
    //
    // Ya NO devolvemos 401 solamente porque el JWT no tenga
    // un claim numérico que podamos convertir a IdUsuario.
    //
    // En ese caso la gestión se registra con IdUsuario = NULL.
    // =========================================================

    [HttpPost("solicitudes/{idSolicitudContado:int}/contactar")]
    public async Task<IActionResult> Contactar(
        int idSolicitudContado)
    {
        var idUsuario =
            ObtenerIdUsuarioOpcional();

        var data =
            await _service.Contactar(
                idSolicitudContado,
                idUsuario);

        return Ok(
            new Response<ContadoMotoGestionDetalleDto>
            {
                Success = true,
                StatusCode = 200,
                Message =
                    "La oportunidad quedó marcada como contactada.",
                Data = data
            });
    }


    [HttpPatch("solicitudes/{idSolicitudContado:int}/estado")]
    public async Task<IActionResult> CambiarEstado(
        int idSolicitudContado,
        [FromBody] CambiarEstadoContadoMotoRequest request)
    {
        var idUsuario =
            ObtenerIdUsuarioOpcional();

        var data =
            await _service.CambiarEstado(
                idSolicitudContado,
                idUsuario,
                request);

        return Ok(
            new Response<ContadoMotoGestionDetalleDto>
            {
                Success = true,
                StatusCode = 200,
                Message =
                    "Estado de la solicitud al contado actualizado correctamente.",
                Data = data
            });
    }


    [HttpGet(
        "solicitudes/{idSolicitudContado:int}/documentos/{idDocumento:int}")]
    public async Task<IActionResult> ObtenerDocumento(
        int idSolicitudContado,
        int idDocumento)
    {
        var archivo =
            await _service.ObtenerDocumento(
                idSolicitudContado,
                idDocumento);

        return File(
            archivo.Contenido,
            archivo.MimeType,
            archivo.NombreArchivo);
    }


    // =========================================================
    // USUARIO OPCIONAL
    // =========================================================

    private int? ObtenerIdUsuarioOpcional()
    {
        string[] tiposPreferidos =
        {
            ClaimTypes.NameIdentifier,
            "nameid",
            "idUsuario",
            "IdUsuario",
            "usuarioId",
            "UsuarioId",
            "userId",
            "UserId",
            "userid",
            "id",
            "Id",
            "uid",
            "sub"
        };

        foreach (var tipo in tiposPreferidos)
        {
            var claim =
                User.Claims.FirstOrDefault(
                    c => string.Equals(
                        c.Type,
                        tipo,
                        StringComparison.OrdinalIgnoreCase));

            if (
                claim != null
                &&
                int.TryParse(
                    claim.Value,
                    out var idUsuario)
                &&
                idUsuario > 0
            )
            {
                return idUsuario;
            }
        }

        foreach (var claim in User.Claims)
        {
            var tipo =
                claim.Type ?? string.Empty;

            var pareceIdUsuario =
                tipo.EndsWith(
                    "/nameidentifier",
                    StringComparison.OrdinalIgnoreCase)
                ||
                tipo.EndsWith(
                    "/userid",
                    StringComparison.OrdinalIgnoreCase)
                ||
                tipo.EndsWith(
                    "/idusuario",
                    StringComparison.OrdinalIgnoreCase)
                ||
                tipo.Contains(
                    "userid",
                    StringComparison.OrdinalIgnoreCase)
                ||
                tipo.Contains(
                    "idusuario",
                    StringComparison.OrdinalIgnoreCase);

            if (
                pareceIdUsuario
                &&
                int.TryParse(
                    claim.Value,
                    out var idUsuario)
                &&
                idUsuario > 0
            )
            {
                return idUsuario;
            }
        }

        return null;
    }
}

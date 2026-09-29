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
        _service =
            service;
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
            new Response<
                IReadOnlyList<ContadoMotoGestionListaDto>
            >
            {
                Success = true,

                StatusCode = 200,

                Message =
                    "Solicitudes al contado obtenidas correctamente.",

                Data =
                    data
            });
    }


    [HttpGet(
        "solicitudes/{idSolicitudContado:int}"
    )]
    public async Task<IActionResult> ObtenerDetalle(
        int idSolicitudContado)
    {
        var data =
            await _service.ObtenerDetalle(
                idSolicitudContado);


        return Ok(
            new Response<
                ContadoMotoGestionDetalleDto
            >
            {
                Success = true,

                StatusCode = 200,

                Message =
                    "Solicitud al contado obtenida correctamente.",

                Data =
                    data
            });
    }


    [HttpPost(
        "solicitudes/{idSolicitudContado:int}/contactar"
    )]
    public async Task<IActionResult> Contactar(
        int idSolicitudContado)
    {
        var idUsuario =
            ObtenerIdUsuario();


        if (
            !idUsuario.HasValue
        )
        {
            return Unauthorized();
        }


        var data =
            await _service.Contactar(
                idSolicitudContado,
                idUsuario.Value);


        return Ok(
            new Response<
                ContadoMotoGestionDetalleDto
            >
            {
                Success = true,

                StatusCode = 200,

                Message =
                    "La oportunidad quedó marcada como contactada.",

                Data =
                    data
            });
    }


    [HttpPatch(
        "solicitudes/{idSolicitudContado:int}/estado"
    )]
    public async Task<IActionResult> CambiarEstado(
        int idSolicitudContado,
        [FromBody]
        CambiarEstadoContadoMotoRequest request)
    {
        var idUsuario =
            ObtenerIdUsuario();


        if (
            !idUsuario.HasValue
        )
        {
            return Unauthorized();
        }


        var data =
            await _service.CambiarEstado(
                idSolicitudContado,
                idUsuario.Value,
                request);


        return Ok(
            new Response<
                ContadoMotoGestionDetalleDto
            >
            {
                Success = true,

                StatusCode = 200,

                Message =
                    "Estado de la solicitud al contado actualizado correctamente.",

                Data =
                    data
            });
    }


    [HttpGet(
        "solicitudes/{idSolicitudContado:int}/documentos/{idDocumento:int}"
    )]
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


    private int? ObtenerIdUsuario()
    {
        var valor =
            User.FindFirst(
                ClaimTypes.NameIdentifier
            )?.Value

            ??

            User.FindFirst(
                "idUsuario"
            )?.Value

            ??

            User.FindFirst(
                "IdUsuario"
            )?.Value

            ??

            User.FindFirst(
                "id"
            )?.Value

            ??

            User.FindFirst(
                "sub"
            )?.Value;


        return int.TryParse(
            valor,
            out var idUsuario)
                ? idUsuario
                : null;
    }
}
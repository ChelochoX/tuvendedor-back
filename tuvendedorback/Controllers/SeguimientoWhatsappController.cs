using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using tuvendedorback.DTOs;
using tuvendedorback.Request;
using tuvendedorback.Services.Interfaces;
using tuvendedorback.Services.Seguimiento;
using tuvendedorback.Wrappers;

namespace tuvendedorback.Controllers;

public sealed class IniciarPruebaWhatsappRequest
{
    public string? Telefono { get; set; }
    public long IdEnvio { get; set; }
    public int IntervaloMinutos { get; set; } = 5;
    public bool ConsentimientoConfirmado { get; set; }
}

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

    // Prueba independiente: nunca crea seguimientos comerciales ni modifica la cola.
    // El destinatario se ingresa en el panel; se exige consentimiento explícito en cada prueba.
    [HttpGet("prueba-controlada/estado")]
    public IActionResult EstadoPrueba() => Ok(new { Success = true, Data = PruebaControladaWhatsapp.Estado() });

    [HttpPost("prueba-controlada/iniciar")]
    public async Task<IActionResult> IniciarPrueba([FromBody] IniciarPruebaWhatsappRequest request)
    {
        var configuracion = await _service.ObtenerConfiguracion();
        if (configuracion.ModoEnvio != "SIMULACION" || configuracion.Activo)
            return BadRequest(new { Success = false, Message = "Para pruebas aisladas, dejá el motor comercial apagado y en SIMULACION." });

        var numero = new string((request.Telefono ?? "").Where(char.IsDigit).ToArray());
        if (!request.ConsentimientoConfirmado)
            return BadRequest(new { Success = false, Message = "Confirmá que el destinatario autorizó expresamente esta prueba." });
        if (!System.Text.RegularExpressions.Regex.IsMatch(numero, @"^5959\d{8}$"))
            return BadRequest(new { Success = false, Message = "Ingresá un celular de Paraguay válido en formato 5959XXXXXXXX." });
        if (request.IntervaloMinutos is not (1 or 5 or 10))
            return BadRequest(new { Success = false, Message = "Intervalo permitido: 1, 5 o 10 minutos." });

        // Solo una fila REAL generada por el motor. No se permite modelo inventado.
        var cola = await _service.ListarEnvios(null, 500);
        var origen = cola.SingleOrDefault(x => x.Id == request.IdEnvio);
        if (origen is null)
            return NotFound(new { Success = false, Message = "No existe ese registro en la cola. Generá primero la simulación." });
        if (origen.Estado != "PENDIENTE")
            return BadRequest(new { Success = false, Message = "La prueba solo admite registros PENDIENTE." });
        if (string.IsNullOrWhiteSpace(origen.ProductoInteres) || string.IsNullOrWhiteSpace(origen.Mensaje))
            return BadRequest(new { Success = false, Message = "Este registro no tiene modelo o mensaje confirmado; no se inventarán datos." });
        if (string.IsNullOrWhiteSpace(origen.Telefono) || !origen.Telefono.StartsWith("595"))
            return BadRequest(new { Success = false, Message = "El registro origen no tiene teléfono WhatsApp válido." });

        var reglas = configuracion.Reglas.Where(x => x.Activo && x.Orden >= origen.NumeroSeguimiento)
            .OrderBy(x => x.Orden).Take(3).ToArray();
        if (reglas.Length == 0 || reglas[0].Orden != origen.NumeroSeguimiento)
            return BadRequest(new { Success = false, Message = "No hay una secuencia válida para ese registro." });

        // Primer mensaje: exactamente el texto guardado en la cola comercial.
        // Siguientes: mismas plantillas vigentes y datos reales del registro.
        var mensajes = new List<string> { origen.Mensaje };
        foreach (var regla in reglas.Skip(1))
        {
            var saludo = string.IsNullOrWhiteSpace(origen.Cliente) ? "" : " " + origen.Cliente.Trim();
            var texto = regla.Mensaje
                .Replace("{saludo}", saludo, StringComparison.OrdinalIgnoreCase)
                .Replace("{moto}", origen.ProductoInteres, StringComparison.OrdinalIgnoreCase);
            mensajes.Add(texto);
        }
        var sender = HttpContext.RequestServices.GetRequiredService<ISeguimientoWhatsappSender>();
        var iniciado = PruebaControladaWhatsapp.Iniciar(numero, request.IntervaloMinutos, origen, mensajes.ToArray(), sender);
        return iniciado ? Ok(new { Success = true, Data = PruebaControladaWhatsapp.Estado() })
            : Conflict(new { Success = false, Message = "Ya hay una prueba en ejecución. Cancelala o esperá que termine." });
    }

    [HttpPost("prueba-controlada/cancelar")]
    public IActionResult CancelarPrueba()
    {
        PruebaControladaWhatsapp.Cancelar();
        return Ok(new { Success = true, Data = PruebaControladaWhatsapp.Estado() });
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

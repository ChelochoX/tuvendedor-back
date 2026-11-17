using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using tuvendedorback.DTOs;
using tuvendedorback.Services;

namespace tuvendedorback.Controllers;

/// <summary>Prueba controlada de publicación: simulación por defecto, nunca automática.</summary>
[ApiController]
[AllowAnonymous]
[Route("api/internal/panambi-social")]
public sealed class PanambiMetaPublishingController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly PanambiMetaPublishingService _service;

    public PanambiMetaPublishingController(IConfiguration config, PanambiMetaPublishingService service)
    {
        _config = config;
        _service = service;
    }

    [HttpPost("publicar-meta")]
    public async Task<IActionResult> Publicar(
        [FromBody] PanambiMetaPublicacionRequest request, CancellationToken ct)
    {
        if (!ClaveInternaValida()) return Unauthorized(new { success = false, message = "No autorizado" });
        if (!NumeroAutorizado(request.NumeroWhatsapp))
            return StatusCode(403, new { success = false, message = "Número no autorizado" });

        if (!request.Facebook && !request.Instagram)
            return BadRequest(new { success = false, message = "Elegí Facebook y/o Instagram." });
        if (string.IsNullOrWhiteSpace(request.Texto) || request.Texto.Length > 2200)
            return BadRequest(new { success = false, message = "El texto debe tener entre 1 y 2200 caracteres." });
        if (!Uri.TryCreate(request.ImagenUrl, UriKind.Absolute, out var imagen) ||
            imagen.Scheme != Uri.UriSchemeHttps || imagen.UserInfo.Length > 0 ||
            imagen.IsLoopback || imagen.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            System.Net.IPAddress.TryParse(imagen.Host, out _))
            return BadRequest(new { success = false, message = "La imagen debe tener una URL HTTPS pública con dominio válido." });

        if (request.Simulacion)
            return Ok(new { success = true, estado = "SIMULACION", publicado = false,
                message = "Validación local correcta. No se llamó a Meta.",
                destinos = new[] { request.Facebook ? "FACEBOOK" : null, request.Instagram ? "INSTAGRAM" : null }
                    .Where(x => x is not null) });

        // Dos barreras explícitas para evitar publicaciones accidentales.
        if (!string.Equals(request.Confirmacion, "PUBLICAR", StringComparison.Ordinal) ||
            !bool.TryParse(_config["MetaPublishing:Enabled"], out var habilitado) || !habilitado)
            return StatusCode(403, new { success = false, message =
                "Publicación real bloqueada: requiere Confirmacion=PUBLICAR y MetaPublishing__Enabled=true." });

        var resultados = await _service.PublicarAsync(request, ct);
        return Ok(new { success = resultados.All(r => r.Exito), estado = "EJECUTADO",
            resultados });
    }

    private bool ClaveInternaValida()
    {
        var esperada = _config["IA:InternalKey"];
        var recibida = Request.Headers["X-TuVendedor-Internal-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(esperada) || string.IsNullOrWhiteSpace(recibida)) return false;
        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(esperada)),
            SHA256.HashData(Encoding.UTF8.GetBytes(recibida)));
    }

    private bool NumeroAutorizado(string? numero)
    {
        static string Digitos(string? s) => new((s ?? "").Where(char.IsDigit).ToArray());
        var permitido = (_config["PanambiSocial:AllowedPhones"] ?? "").Split(',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Digitos).Where(n => n.Length >= 9).ToHashSet();
        return permitido.Count > 0 && permitido.Contains(Digitos(numero));
    }
}

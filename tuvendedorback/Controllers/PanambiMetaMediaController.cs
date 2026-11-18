using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace tuvendedorback.Controllers;

// Medios temporales accesibles por Meta. Nunca exponemos el directorio del bridge ni listados.
[ApiController]
[AllowAnonymous]
[Route("api/internal/panambi-social/media")]
public sealed class PanambiMetaMediaController : ControllerBase
{
    private readonly IConfiguration _config;
    public PanambiMetaMediaController(IConfiguration config) => _config = config;

    private string DirectoryPath => _config["MetaPublishing:MediaDirectory"]
        ?? "/app/private/panambi-meta";

    public sealed class SubidaRequest
    {
        public string NumeroWhatsapp { get; set; } = "";
        public string MediaBase64 { get; set; } = "";
    }

    [HttpPost]
    [RequestSizeLimit(11_000_000)]
    public async Task<IActionResult> Subir([FromBody] SubidaRequest request, CancellationToken ct)
    {
        if (!ClaveValida()) return Unauthorized();
        var permitido = (_config["PanambiSocial:AllowedPhones"] ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Digitos);
        if (!permitido.Contains(Digitos(request.NumeroWhatsapp))) return Forbid();
        var baseUrl = (_config["MetaPublishing:PublicBaseUrl"] ?? "").TrimEnd('/');
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo))
            return StatusCode(503, new { message = "Falta MetaPublishing__PublicBaseUrl HTTPS." });
        byte[] bytes;
        try { bytes = Convert.FromBase64String(request.MediaBase64); }
        catch (FormatException) { return BadRequest(new { message = "Imagen Base64 inválida." }); }
        if (bytes.Length < 100 || bytes.Length > 7_000_000 ||
            bytes[0] != 0xff || bytes[1] != 0xd8 || bytes[^2] != 0xff || bytes[^1] != 0xd9)
            return BadRequest(new { message = "Para Instagram se requiere flyer JPEG válido (máximo 7 MB)." });
        System.IO.Directory.CreateDirectory(DirectoryPath);
        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        await System.IO.File.WriteAllBytesAsync(Path.Combine(DirectoryPath, id + ".jpg"), bytes, ct);
        return Ok(new { imagenUrl = $"{baseUrl}/api/internal/panambi-social/media/{id}" });
    }

    [HttpGet("{id}")]
    [HttpHead("{id}")]
    public IActionResult Descargar(string id)
    {
        if (id.Length != 48 || !id.All(c => char.IsAsciiHexDigit(c))) return NotFound();
        var archivo = Path.Combine(DirectoryPath, id.ToLowerInvariant() + ".jpg");
        if (!System.IO.File.Exists(archivo)) return NotFound();
        // El enlace opaco existe solamente para ser recuperado por Meta. No contiene datos personales.
        Response.Headers.CacheControl = "public, max-age=3600";
        return PhysicalFile(archivo, "image/jpeg", enableRangeProcessing: false);
    }

    private static string Digitos(string? s) => new((s ?? "").Where(char.IsDigit).ToArray());
    private bool ClaveValida()
    {
        var esperada = _config["IA:InternalKey"] ?? _config["TUVENDEDOR_INTERNAL_KEY"];
        var recibida = Request.Headers["X-TuVendedor-Internal-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(esperada) || string.IsNullOrWhiteSpace(recibida)) return false;
        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(esperada)),
            SHA256.HashData(Encoding.UTF8.GetBytes(recibida)));
    }
}

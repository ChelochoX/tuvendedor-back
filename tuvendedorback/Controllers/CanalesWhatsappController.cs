using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using tuvendedorback.Common;
using tuvendedorback.Repositories.Interfaces;

namespace tuvendedorback.Controllers;

public sealed class NuevoCanalWhatsappRequest
{
    public string Nombre { get; set; } = string.Empty;
}

public sealed class ConfigurarCanalWhatsappRequest
{
    public bool Activo { get; set; }
}

/// <summary>
/// Control exclusivo de administración. El QR y la llave del bridge no se publican
/// directamente en Internet: el navegador solo accede a esta API con JWT.
/// </summary>
[Authorize]
[ApiController]
[Route("api/admin/canales-whatsapp")]
public sealed class CanalesWhatsappController : ControllerBase
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly UserContext _usuario;
    private readonly IComercialDashboardRepository _permisos;

    public CanalesWhatsappController(HttpClient http, IConfiguration config,
        UserContext usuario, IComercialDashboardRepository permisos)
    {
        _http = http;
        _config = config;
        _usuario = usuario;
        _permisos = permisos;
        _http.Timeout = TimeSpan.FromSeconds(12);
    }

    private async Task<bool> EsAdmin() =>
        _usuario.IdUsuario is int id && id > 0 && await _permisos.EsAdministrador(id);

    private async Task<IActionResult> Reenviar(HttpMethod metodo, string ruta, object? cuerpo, CancellationToken ct)
    {
        if (!await EsAdmin()) return Forbid();
        var baseUrl = _config["WhatsApp:ManagerUrl"] ?? _config["WHATSAPP_MANAGER_URL"]
            ?? "http://tuvendedor_wa_manager:3200";
        var llave = _config["TUVENDEDOR_INTERNAL_KEY"] ?? _config["WhatsApp:InternalKey"];
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(llave))
            return StatusCode(503, new { success = false, message = "Falta TUVENDEDOR_INTERNAL_KEY para acceder al administrador." });
        // La URL se configura únicamente desde el servidor. Jamás aceptamos URLs del navegador.
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return StatusCode(503, new { success = false, message = "URL interna de administración inválida." });
        try
        {
            using var req = new HttpRequestMessage(metodo, baseUrl.TrimEnd('/') + ruta);
            req.Headers.TryAddWithoutValidation("X-TuVendedor-Internal-Key", llave);
            if (cuerpo is not null) req.Content = JsonContent.Create(cuerpo);
            using var res = await _http.SendAsync(req, ct);
            var contenido = await res.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(contenido))
                return StatusCode((int)res.StatusCode, new { success = false, message = "El administrador no devolvió información." });
            using var json = JsonDocument.Parse(contenido);
            return StatusCode((int)res.StatusCode, json.RootElement.Clone());
        }
        catch (HttpRequestException)
        {
            return StatusCode(503, new { success = false, message = "El administrador de WhatsApp no responde. Revisá el contenedor." });
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return StatusCode(504, new { success = false, message = "Tiempo de espera agotado al consultar los canales." });
        }
    }

    [HttpGet]
    public Task<IActionResult> Listar(CancellationToken ct) =>
        Reenviar(HttpMethod.Get, "/api/channels", null, ct);

    [HttpPost]
    public Task<IActionResult> Crear([FromBody] NuevoCanalWhatsappRequest dto, CancellationToken ct) =>
        Reenviar(HttpMethod.Post, "/api/channels", new { nombre = dto.Nombre }, ct);

    [HttpGet("{id}/qr")]
    public Task<IActionResult> ObtenerQr([FromRoute] string id, CancellationToken ct) =>
        Reenviar(HttpMethod.Get, $"/api/channels/{Uri.EscapeDataString(id)}/qr", null, ct);

    [HttpPut("{id}/atencion")]
    public Task<IActionResult> ConfigurarAtencion([FromRoute] string id,
        [FromBody] ConfigurarCanalWhatsappRequest dto, CancellationToken ct) =>
        Reenviar(HttpMethod.Put, $"/api/channels/{Uri.EscapeDataString(id)}/attention", new { activo = dto.Activo }, ct);

    [HttpPost("{id}/reiniciar")]
    public Task<IActionResult> Reiniciar([FromRoute] string id, CancellationToken ct) =>
        Reenviar(HttpMethod.Post, $"/api/channels/{Uri.EscapeDataString(id)}/restart", null, ct);

    public sealed class ConfigurarConexionCanalRequest
    {
        public bool Conectar { get; set; }
    }

    [HttpPost("{id}/conexion")]
    public Task<IActionResult> ConfigurarConexion([FromRoute] string id,
        [FromBody] ConfigurarConexionCanalRequest dto, CancellationToken ct) =>
        Reenviar(HttpMethod.Post, $"/api/channels/{Uri.EscapeDataString(id)}/connection",
            new { conectar = dto.Conectar }, ct);

    [HttpPost("{id}/cerrar-sesion")]
    public Task<IActionResult> CerrarSesion([FromRoute] string id, CancellationToken ct) =>
        Reenviar(HttpMethod.Post, $"/api/channels/{Uri.EscapeDataString(id)}/logout", null, ct);
}

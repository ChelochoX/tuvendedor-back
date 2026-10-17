using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using tuvendedorback.Common;
using tuvendedorback.Repositories;
using tuvendedorback.Repositories.Interfaces;
using System.Text.RegularExpressions;

namespace tuvendedorback.Controllers;

/// <summary>Edición de instrucciones comerciales; solo administradores autenticados.</summary>
[ApiController, Authorize]
[Route("api/admin/prompts-panambi")]
public sealed class AdminPromptsPanambiController : ControllerBase
{
    private readonly PanambiPromptsEditorRepository _prompts;
    private readonly UserContext _user;
    private readonly IComercialDashboardRepository _permisos;
    public AdminPromptsPanambiController(PanambiPromptsEditorRepository prompts,
        UserContext user, IComercialDashboardRepository permisos)
    { _prompts = prompts; _user = user; _permisos = permisos; }

    private async Task<bool> EsAdmin() => _user.IdUsuario is int id && id > 0 && await _permisos.EsAdministrador(id);
    private static bool CodigoValido(string codigo) =>
        !string.IsNullOrWhiteSpace(codigo) && Regex.IsMatch(codigo, @"^[A-Z0-9_]{3,80}$");

    public sealed class PublicarRequest
    {
        public string Contenido { get; set; } = "";
        public int? VersionEsperada { get; set; }
    }
    public sealed class RestaurarRequest
    {
        public int Version { get; set; }
        public int? VersionEsperada { get; set; }
    }
    private static readonly Dictionary<string, (string Titulo, string Descripcion)> Predefinidos = new()
    {
        ["MARKETPLACE_SALUDO"] = ("Saludo para búsquedas", "Se muestra antes de las opciones encontradas; la lista siempre proviene de SQL Server."),
        ["MARKETPLACE_CIERRE"] = ("Cierre de resultados", "Texto al final de opciones reales. No prometer visitas confirmadas."),
        ["MARKETPLACE_SIN_RESULTADOS"] = ("Sin resultados", "Aviso breve cuando SQL Server no encontró coincidencias."),
        ["CONVERSACION_GENERAL"] = ("Conversación general", "Instrucciones adicionales para Ollama fuera de las fichas verificadas."),
        ["MOTO_VENTAS_BASE"] = ("Ventas de motos", "Reemplaza la redacción base del motor de motos, no sus reglas de seguridad.")
    };

    [HttpGet]
    public async Task<IActionResult> Listar()
    {
        if (!await EsAdmin()) return Forbid();
        var publicados = await _prompts.ListarPublicados();
        var legados = await _prompts.ListarLegados();
        var codigos = Predefinidos.Keys.Concat(legados.Where(x => !x.Codigo.Contains("AUTORIZACION", StringComparison.OrdinalIgnoreCase)).Select(x => x.Codigo))
            .Concat(publicados.Select(x => x.Codigo)).Distinct().OrderBy(x => x).ToArray();
        var lista = codigos.Select(codigo =>
        {
            var publicado = publicados.FirstOrDefault(x => x.Codigo == codigo);
            var legado = legados.FirstOrDefault(x => x.Codigo == codigo);
            var meta = Predefinidos.TryGetValue(codigo, out var p) ? p : (codigo, "Prompt de estrategia comercial por marca");
            var contenido = publicado?.Contenido ?? legado?.Contenido ?? ValoresPorDefecto(codigo);
            return new
            {
                codigo,
                titulo = meta.Item1,
                descripcion = meta.Item2,
                contenido,
                version = publicado?.Version ?? 0,
                origen = publicado is not null ? "EDITOR" : legado is not null ? "LEGADO" : "PREDETERMINADO"
            };
        });
        return Ok(new { success = true, data = lista });
    }

    [HttpGet("{codigo}/historial")]
    public async Task<IActionResult> Historial(string codigo)
    {
        if (!await EsAdmin()) return Forbid();
        codigo = codigo.Trim().ToUpperInvariant();
        if (!CodigoValido(codigo)) return BadRequest(new { message = "Código inválido" });
        return Ok(new { success = true, data = await _prompts.Historial(codigo) });
    }

    [HttpPut("{codigo}")]
    public async Task<IActionResult> Publicar(string codigo, [FromBody] PublicarRequest request)
    {
        if (!await EsAdmin()) return Forbid();
        codigo = codigo.Trim().ToUpperInvariant();
        if (codigo.Contains("AUTORIZACION", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Los textos legales de autorización no se editan desde este panel." });
        if (!CodigoValido(codigo) || string.IsNullOrWhiteSpace(request.Contenido) || request.Contenido.Length > 12000)
            return BadRequest(new { message = "Código inválido o contenido vacío/demasiado largo (máximo 12.000 caracteres)." });
        try
        {
            var resultado = await _prompts.Publicar(codigo, request.Contenido.Trim(), _user.IdUsuario!.Value, request.VersionEsperada);
            return Ok(new { success = true, data = resultado, message = "Prompt publicado. Aplica a nuevos mensajes sin redeploy." });
        }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }

    [HttpPost("{codigo}/restaurar")]
    public async Task<IActionResult> Restaurar(string codigo, [FromBody] RestaurarRequest request)
    {
        if (!await EsAdmin()) return Forbid();
        codigo = codigo.Trim().ToUpperInvariant();
        if (codigo.Contains("AUTORIZACION", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Los textos legales de autorización no se editan desde este panel." });
        if (!CodigoValido(codigo) || request.Version < 1) return BadRequest(new { message = "Versión inválida" });
        var previa = await _prompts.ObtenerVersion(codigo, request.Version);
        if (previa is null) return NotFound(new { message = "Versión no encontrada" });
        try
        {
            var publicada = await _prompts.Publicar(codigo, previa.Contenido, _user.IdUsuario!.Value, request.VersionEsperada);
            return Ok(new { success = true, data = publicada, message = "Versión restaurada como una publicación nueva." });
        }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }

    private static string ValoresPorDefecto(string codigo) => codigo switch
    {
        "MARKETPLACE_SALUDO" => "¡Hola! 😊 Encontré {opciones} en TuVendedor que coinciden con tu búsqueda:",
        "MARKETPLACE_CIERRE" => "¿Cuál te interesa? Decime el número y te cuento los detalles. 😊",
        "MARKETPLACE_SIN_RESULTADOS" => "¡Hola! 😊 Revisé las publicaciones activas de TuVendedor.",
        "CONVERSACION_GENERAL" => "Saludá amablemente cuando corresponda. Respondé únicamente a la consulta actual, de forma breve, sin repetir enlaces ni ofrecer productos que no estén verificados.",
        _ => ""
    };
}

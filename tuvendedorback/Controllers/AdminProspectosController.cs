using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using tuvendedorback.Common;
using tuvendedorback.Prospeccion;

namespace tuvendedorback.Controllers;

[ApiController]
[Authorize]
[Route("api/admin/prospectos")]
public sealed class AdminProspectosController : ControllerBase
{
    private readonly ProspeccionService _service;
    private readonly UserContext _user;
    private readonly IConfiguration _configuration;

    public AdminProspectosController(ProspeccionService service, UserContext user, IConfiguration configuration)
    { _service = service; _user = user; _configuration = configuration; }

    private static string? ValidarRubro(string? rubro) =>
        rubro?.ToUpperInvariant() is "INMUEBLES" or "MOTOS" or "OTROS" ? rubro.ToUpperInvariant() : null;

    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] string rubro = "INMUEBLES", [FromQuery] string? estado = null,
        [FromQuery] string? buscar = null, [FromQuery] int pagina = 1, [FromQuery] int porPagina = 20)
    {
        await _service.ValidarAdminAsync(_user.IdUsuario);
        var rubroValido = ValidarRubro(rubro);
        if (rubroValido == null) return BadRequest(new { message = "Rubro inválido." });
        if (estado != null && estado is not ("NUEVO" or "EN_REVISION" or "CONTACTADO" or "DESCARTADO"))
            return BadRequest(new { message = "Estado inválido." });
        if (pagina < 1 || pagina > 100000 || porPagina is < 1 or > 100)
            return BadRequest(new { message = "Paginación fuera de rango." });
        return Ok(new { success = true, data = await _service.ListarAsync(rubroValido, estado, buscar, pagina, porPagina) });
    }

    [HttpPatch("{id:long}/gestion")]
    public async Task<IActionResult> Gestion(long id, [FromQuery] string rubro, [FromBody] CambiarGestionRequest request)
    {
        await _service.ValidarAdminAsync(_user.IdUsuario);
        var rubroValido = ValidarRubro(rubro);
        if (id <= 0 || rubroValido == null ||
            request.EstadoGestion is not ("NUEVO" or "EN_REVISION" or "CONTACTADO" or "DESCARTADO") ||
            request.NotasGestion?.Length > 2000)
            return BadRequest(new { message = "Estado, rubro o notas inválidos." });
        return await _service.CambiarGestionAsync(id, rubroValido, request.EstadoGestion, request.NotasGestion)
            ? Ok(new { success = true }) : NotFound(new { message = "Prospecto no encontrado." });
    }

    [HttpPost("sincronizar")]
    public async Task<IActionResult> Sincronizar(CancellationToken ct)
    {
        await _service.ValidarAdminAsync(_user.IdUsuario);
        if (!_configuration.GetValue<bool>("GoogleDriveProspectos:Enabled"))
            return Conflict(new { message = "La sincronización Drive aún no está habilitada en el servidor." });
        try
        {
            return Ok(new { success = true, data = await _service.SincronizarAsync(ct) });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("en curso"))
        { return Conflict(new { message = "La sincronización ya se está ejecutando." }); }
        catch (Exception)
        { return StatusCode(502, new { message = "No se pudo sincronizar Drive. Revisá la configuración y los logs del servidor." }); }
    }
}

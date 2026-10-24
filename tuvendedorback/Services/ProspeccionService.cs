using tuvendedorback.Repositories.Interfaces;

namespace tuvendedorback.Prospeccion;

public sealed class ProspeccionService
{
    private readonly ProspeccionRepository _repo;
    private readonly GoogleProspeccionReader _google;
    private readonly IComercialDashboardRepository _roles;
    private readonly ILogger<ProspeccionService> _logger;
    private static readonly SemaphoreSlim SyncMutex = new(1, 1);

    public ProspeccionService(ProspeccionRepository repo, GoogleProspeccionReader google,
        IComercialDashboardRepository roles, ILogger<ProspeccionService> logger)
    { _repo = repo; _google = google; _roles = roles; _logger = logger; }

    public async Task ValidarAdminAsync(int? idUsuario)
    {
        if (idUsuario is not > 0 || !await _roles.EsAdministrador(idUsuario.Value))
            throw new UnauthorizedAccessException("Acceso exclusivo de administración.");
    }

    public Task<ProspectoListadoDto> ListarAsync(string rubro, string? estado, string? q, int pagina, int porPagina)
        => _repo.ListarAsync(rubro, estado, q, pagina, porPagina);

    public Task<bool> CambiarGestionAsync(long id, string rubro, string estado, string? notas)
        => _repo.CambiarGestionAsync(id, rubro, estado, notas);

    public async Task<ResultadoSincronizacion> SincronizarAsync(CancellationToken ct)
    {
        if (!await SyncMutex.WaitAsync(0, ct))
            throw new InvalidOperationException("Ya hay una sincronización en curso.");
        long logId = 0;
        try
        {
            logId = await _repo.IniciarSincronizacionAsync();
            // Falla completa si Google no responde: jamás se convierten errores de lectura en datos vacíos.
            var (items, archivos, leidas, omitidas) = await _google.LeerAsync(ct);
            var (nuevos, repetidos) = await _repo.GuardarLoteAsync(items, ct);
            var resultado = new ResultadoSincronizacion
            {
                Planillas = archivos,
                FilasLeidas = leidas,
                FilasNuevas = nuevos,
                FilasRepetidas = repetidos + leidas - omitidas - items.Count,
                FilasOmitidas = omitidas
            };
            await _repo.FinalizarSincronizacionAsync(logId, resultado, null);
            return resultado;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo sincronizar la prospección desde Drive.");
            // Resumen sanitizado: no filtramos URLs privadas ni credenciales por API.
            if (logId > 0) await _repo.FinalizarSincronizacionAsync(logId, null,
                "No se pudo completar la lectura de Drive o el guardado en la base de datos.");
            throw;
        }
        finally { SyncMutex.Release(); }
    }
}

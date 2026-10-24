namespace tuvendedorback.Prospeccion;

/// <summary>Consulta Drive cada 5 minutos, sin esperar el informe diario.</summary>
public sealed class ProspeccionBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _config;
    private readonly ILogger<ProspeccionBackgroundService> _log;

    public ProspeccionBackgroundService(IServiceScopeFactory scopes, IConfiguration config, ILogger<ProspeccionBackgroundService> log)
    { _scopes = scopes; _config = config; _log = log; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_config.GetValue<bool>("GoogleDriveProspectos:Enabled")) return;
        // En varios réplicas solo configurar Enabled=true en una, o usar un lock distribuido.
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        do
        {
            try
            {
                using var scope = _scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ProspeccionService>().SincronizarAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _log.LogWarning(ex, "Sincronización Drive pendiente de corrección."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

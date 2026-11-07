namespace tuvendedorback.Prospeccion;

/// <summary>
/// Sincroniza Drive una vez al día en horario de Paraguay.
/// No sincroniza al iniciar el proceso. El endpoint manual permanece independiente.
/// </summary>
public sealed class ProspeccionBackgroundService : BackgroundService
{
    private readonly ProspeccionSyncCoordinator _coordinator;
    private readonly IConfiguration _config;
    private readonly ILogger<ProspeccionBackgroundService> _log;

    public ProspeccionBackgroundService(ProspeccionSyncCoordinator coordinator, IConfiguration config, ILogger<ProspeccionBackgroundService> log)
    { _coordinator = coordinator; _config = config; _log = log; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_config.GetValue<bool>("GoogleDriveProspectos:Enabled")) return;
        if (!_config.GetValue("GoogleDriveProspectos:AutoSyncEnabled", true))
        {
            _log.LogInformation("Sincronización automática de Drive deshabilitada; se permite la manual.");
            return;
        }

        // No depender del timezone del contenedor (generalmente UTC).
        var zona = TimeZoneInfo.FindSystemTimeZoneById("America/Asuncion");
        var hora = _config.GetValue("GoogleDriveProspectos:AutoSyncHour", 7);
        var minuto = _config.GetValue("GoogleDriveProspectos:AutoSyncMinute", 0);
        if (hora is < 0 or > 23 || minuto is < 0 or > 59)
            throw new InvalidOperationException("AutoSyncHour debe estar entre 0 y 23 y AutoSyncMinute entre 0 y 59.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var ahora = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zona);
            var proximaFecha = ahora.Date.Add(new TimeSpan(hora, minuto, 0));
            if (proximaFecha <= ahora.DateTime) proximaFecha = proximaFecha.AddDays(1);
            var proxima = new DateTimeOffset(proximaFecha, zona.GetUtcOffset(proximaFecha));
            var espera = proxima.ToUniversalTime() - DateTimeOffset.UtcNow;
            if (espera < TimeSpan.Zero) espera = TimeSpan.Zero;
            _log.LogInformation("Próxima sincronización automática de Drive: {Fecha} (America/Asuncion)", proxima);

            try { await Task.Delay(espera, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }

            if (_coordinator.Encolar("automatico"))
                _log.LogInformation("Sincronización diaria de Drive encolada.");
            else
                _log.LogInformation("Sincronización diaria omitida: existe otra pendiente o ejecutándose.");
        }
    }
}

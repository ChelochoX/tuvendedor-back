using System.Threading.Channels;

namespace tuvendedorback.Prospeccion;

/// <summary>Cola de capacidad uno: una ejecución manual o automática a la vez.</summary>
public sealed class ProspeccionSyncCoordinator
{
    private readonly Channel<string> _queue = Channel.CreateBounded<string>(new BoundedChannelOptions(1)
    {
        SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait
    });
    private readonly object _gate = new();
    private string _estado = "inactivo";
    private DateTimeOffset? _inicioUtc;
    private DateTimeOffset? _finUtc;
    private string? _error;
    private ResultadoSincronizacion? _resultado;

    public bool Encolar(string origen)
    {
        lock (_gate)
        {
            if (_estado is "pendiente" or "ejecutando") return false;
            _estado = "pendiente";
            _error = null;
            _resultado = null;
            _inicioUtc = null;
            _finUtc = null;
            if (_queue.Writer.TryWrite(origen)) return true;
            _estado = "inactivo";
            return false;
        }
    }

    public object Estado()
    {
        lock (_gate)
            return new { estado = _estado, inicioUtc = _inicioUtc, finUtc = _finUtc,
                error = _error, resultado = _resultado };
    }

    internal IAsyncEnumerable<string> Trabajos(CancellationToken ct) => _queue.Reader.ReadAllAsync(ct);

    internal void Iniciar()
    {
        lock (_gate) { _estado = "ejecutando"; _inicioUtc = DateTimeOffset.UtcNow; }
    }

    internal void Finalizar(ResultadoSincronizacion? resultado, string? error)
    {
        lock (_gate)
        {
            _resultado = resultado;
            _error = error;
            _estado = error == null ? "completado" : "fallido";
            _finUtc = DateTimeOffset.UtcNow;
        }
    }
}

public sealed class ProspeccionSyncWorker : BackgroundService
{
    private readonly ProspeccionSyncCoordinator _coordinator;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<ProspeccionSyncWorker> _logger;

    public ProspeccionSyncWorker(ProspeccionSyncCoordinator coordinator,
        IServiceScopeFactory scopes, ILogger<ProspeccionSyncWorker> logger)
    { _coordinator = coordinator; _scopes = scopes; _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var origen in _coordinator.Trabajos(stoppingToken))
        {
            _coordinator.Iniciar();
            try
            {
                using var scope = _scopes.CreateScope();
                var resultado = await scope.ServiceProvider.GetRequiredService<ProspeccionService>()
                    .SincronizarAsync(stoppingToken);
                _coordinator.Finalizar(resultado, null);
                _logger.LogInformation("Sincronización Drive finalizada. Origen: {Origen}; Nuevos: {Nuevos}.",
                    origen, resultado.FilasNuevas);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _coordinator.Finalizar(null, "Interrumpida por apagado del servicio.");
                break;
            }
            catch (Exception ex)
            {
                _coordinator.Finalizar(null, "La sincronización falló. Revisá los logs del backend.");
                _logger.LogError(ex, "Sincronización Drive fallida. Origen: {Origen}.", origen);
            }
        }
    }
}

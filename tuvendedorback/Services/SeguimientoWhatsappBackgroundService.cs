using tuvendedorback.Services.Interfaces;

namespace tuvendedorback.Services;

public sealed class SeguimientoWhatsappBackgroundService : BackgroundService
{
    private static readonly TimeSpan IntervaloRevision = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SeguimientoWhatsappBackgroundService> _logger;

    public SeguimientoWhatsappBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<SeguimientoWhatsappBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();

                var service = scope.ServiceProvider
                    .GetRequiredService<ISeguimientoWhatsappService>();

                await service.ProcesarCiclo(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error en ciclo de seguimiento automático WhatsApp.");
            }

            try
            {
                await Task.Delay(IntervaloRevision, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}

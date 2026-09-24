using tuvendedorback.Repositories.Interfaces;

namespace tuvendedorback.Services;

public class PremiumVencimientosBackgroundService :
    BackgroundService
{
    private readonly IServiceScopeFactory
        _scopeFactory;

    private readonly ILogger<
        PremiumVencimientosBackgroundService
    > _logger;

    private static readonly TimeSpan
        Intervalo =
            TimeSpan.FromMinutes(15);

    public PremiumVencimientosBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<
            PremiumVencimientosBackgroundService
        > logger)
    {
        _scopeFactory =
            scopeFactory;

        _logger =
            logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        await EjecutarSincronizacion(
            stoppingToken);

        using var timer =
            new PeriodicTimer(
                Intervalo);

        while (
            await timer
                .WaitForNextTickAsync(
                    stoppingToken))
        {
            await EjecutarSincronizacion(
                stoppingToken);
        }
    }

    private async Task EjecutarSincronizacion(
        CancellationToken stoppingToken)
    {
        try
        {
            using var scope =
                _scopeFactory
                    .CreateScope();

            var repository =
                scope
                    .ServiceProvider
                    .GetRequiredService<
                        IServicioPremiumRepository
                    >();

            await repository
                .SincronizarVencimientos();
        }
        catch (
            OperationCanceledException)
            when (
                stoppingToken
                    .IsCancellationRequested)
        {
            // Cierre normal de la aplicación.
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error en la sincronización automática de vencimientos Premium.");
        }
    }
}
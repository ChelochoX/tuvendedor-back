using tuvendedorback.DTOs;
using tuvendedorback.Services.Interfaces;

namespace tuvendedorback.Services.Seguimiento;

// Prueba aislada basada en una fila REAL de la cola; nunca envía al destinatario original.
// Ejecución de prueba en memoria: se cancela al reiniciar el backend.
// NO altera tablas, estados, ni los destinatarios de la cola comercial.
public static class PruebaControladaWhatsapp
{
    private static readonly object Mutex = new();
    private static CancellationTokenSource? _cts;
    private static string _estado = "INACTIVA";
    private static string _detalle = "";
    private static int _enviados;
    private static int _total;
    private static DateTimeOffset? _proximo;
    private static long? _idOrigen;
    private static string? _cliente;
    private static string? _producto;

    public static object Estado()
    {
        lock (Mutex) return new { estado = _estado, detalle = _detalle, enviados = _enviados, total = _total, proximo = _proximo, idOrigen = _idOrigen, cliente = _cliente, producto = _producto };
    }

    public static bool Iniciar(string numero, int intervalo, SeguimientoWhatsappEnvioDto origen, string[] mensajes, ISeguimientoWhatsappSender sender)
    {
        CancellationTokenSource cts;
        lock (Mutex)
        {
            if (_estado is "EJECUTANDO" or "ESPERANDO") return false;
            _cts?.Dispose();
            _cts = cts = new CancellationTokenSource();
            _estado = "EJECUTANDO";
            _detalle = "Preparando primer mensaje de prueba";
            _enviados = 0;
            _total = mensajes.Length;
            _proximo = DateTimeOffset.UtcNow;
            _idOrigen = origen.Id; _cliente = origen.Cliente; _producto = origen.ProductoInteres;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                for (var i = 0; i < mensajes.Length; i++)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    var texto = mensajes[i];
                    texto = $"[PRUEBA CONTROLADA {i + 1}/{mensajes.Length}]\n" +
                        $"Origen: #{origen.Id} | {origen.Cliente} | {origen.ProductoInteres}\n" + texto;
                    // IDs únicos para deduplicación WA, no relacionados con IDs de tablas comerciales.
                    var idPrueba = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 10 + i;
                    var resultado = await sender.Enviar(idPrueba, numero, texto, cts.Token);
                    if (!resultado.Enviado)
                    {
                        lock (Mutex) { _estado = "ERROR"; _detalle = resultado.Error ?? "Envío no confirmado"; _proximo = null; }
                        return;
                    }
                    lock (Mutex) { _enviados++; _detalle = $"Mensaje {_enviados} confirmado por el bridge"; }
                    if (i < mensajes.Length - 1)
                    {
                        lock (Mutex) { _estado = "ESPERANDO"; _proximo = DateTimeOffset.UtcNow.AddMinutes(intervalo); }
                        await Task.Delay(TimeSpan.FromMinutes(intervalo), cts.Token);
                        lock (Mutex) { _estado = "EJECUTANDO"; _proximo = DateTimeOffset.UtcNow; }
                    }
                }
                lock (Mutex) { _estado = "FINALIZADA"; _detalle = "Todos los mensajes confirmados por el bridge"; _proximo = null; }
            }
            catch (OperationCanceledException)
            {
                lock (Mutex) { _estado = "CANCELADA"; _detalle = "Prueba cancelada"; _proximo = null; }
            }
            catch (Exception ex)
            {
                lock (Mutex) { _estado = "ERROR"; _detalle = ex.Message; _proximo = null; }
            }
        });
        return true;
    }

    public static void Cancelar()
    {
        lock (Mutex)
        {
            _cts?.Cancel();
            if (_estado is "ESPERANDO") { _estado = "CANCELADA"; _proximo = null; }
        }
    }
}

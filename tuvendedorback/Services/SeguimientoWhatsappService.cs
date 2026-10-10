using tuvendedorback.DTOs;
using tuvendedorback.Helpers;
using tuvendedorback.Repositories.Interfaces;
using tuvendedorback.Request;
using tuvendedorback.Services.Interfaces;

namespace tuvendedorback.Services;

public sealed class SeguimientoWhatsappService : ISeguimientoWhatsappService
{
    private const int LIMITE_CANDIDATOS_POR_CICLO = 200;
    private const int MINUTOS_RECUPERAR_PROCESANDO = 15;

    private readonly ISeguimientoWhatsappRepository _repository;
    private readonly ISeguimientoWhatsappSender _sender;
    private readonly ILogger<SeguimientoWhatsappService> _logger;

    public SeguimientoWhatsappService(
        ISeguimientoWhatsappRepository repository,
        ISeguimientoWhatsappSender sender,
        ILogger<SeguimientoWhatsappService> logger)
    {
        _repository = repository;
        _sender = sender;
        _logger = logger;
    }

    public Task<SeguimientoWhatsappConfiguracionDto> ObtenerConfiguracion()
        => _repository.ObtenerConfiguracion();

    public async Task<SeguimientoWhatsappConfiguracionDto> ActualizarConfiguracion(
        ActualizarSeguimientoWhatsappConfiguracionRequest request,
        int? idUsuario)
    {
        ValidarConfiguracion(request);

        request.ModoEnvio = request.ModoEnvio.Trim().ToUpperInvariant();

        foreach (var regla in request.Reglas)
        {
            regla.DemoraUnidad = regla.DemoraUnidad.Trim().ToUpperInvariant();
            regla.Mensaje = regla.Mensaje.Trim();
        }

        // Protección contra un despliegue que active históricos accidentalmente.
        // Si se activa sin elegir fecha, solamente entran conversaciones desde ahora.
        if (request.Activo && !request.FechaDesdeElegibilidad.HasValue)
        {
            request.FechaDesdeElegibilidad = DateTime.Now;
        }

        await _repository.GuardarConfiguracion(request, idUsuario);
        return await _repository.ObtenerConfiguracion();
    }

    public async Task ProcesarCiclo(CancellationToken cancellationToken = default)
    {
        var configuracion = await _repository.ObtenerConfiguracion();

        if (!configuracion.Activo || !configuracion.FechaDesdeElegibilidad.HasValue)
        {
            return;
        }

        await _repository.RecuperarProcesandoVencidos(MINUTOS_RECUPERAR_PROCESANDO);
        await _repository.AplicarBajasAutomaticas();

        var reglas = configuracion.Reglas
            .Where(x => x.Activo)
            .OrderBy(x => x.Orden)
            .ToList();

        if (reglas.Count == 0)
        {
            return;
        }

        var candidatos = await _repository.ObtenerCandidatos(
            configuracion.FechaDesdeElegibilidad.Value,
            LIMITE_CANDIDATOS_POR_CICLO);

        foreach (var candidato in candidatos)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Defensa para registros históricos: se interpretan expresiones explícitas
            // del último mensaje del cliente sin obligar a un UPDATE masivo del CRM.
            var decisionHistorica = PoliticaSeguimientoCliente.Evaluar(
                candidato.UltimoMensajeCliente);
            if (decisionHistorica.NoContactar || decisionHistorica.Desiste ||
                decisionHistorica.PausaIndefinida ||
                PoliticaSeguimientoCliente.EsConsultaInmueble(candidato.UltimoMensajeCliente))
                continue;

            DateTime? fechaEsperarHasta = null;
            if (decisionHistorica.PausaValor.HasValue)
            {
                var cantidad = decisionHistorica.PausaValor.Value;
                fechaEsperarHasta = decisionHistorica.PausaUnidad switch
                {
                    "DIA" => candidato.FechaUltimoMensajeCliente.AddDays(cantidad),
                    "SEMANA" => candidato.FechaUltimoMensajeCliente.AddDays(cantidad * 7),
                    "MES" => candidato.FechaUltimoMensajeCliente.AddMonths(cantidad),
                    "ANIO" => candidato.FechaUltimoMensajeCliente.AddYears(cantidad),
                    "HORA" => candidato.FechaUltimoMensajeCliente.AddHours(cantidad),
                    _ => null
                };
                if (fechaEsperarHasta > DateTime.Now)
                    continue;
            }

            var ultimoOrden = candidato.UltimoNumeroSeguimientoEnviado ?? 0;
            var siguienteRegla = reglas.FirstOrDefault(x => x.Orden > ultimoOrden);

            if (siguienteRegla is null)
            {
                continue;
            }

            var fechaBase = ultimoOrden == 0
                ? candidato.FechaUltimoMensajeCliente
                : candidato.FechaUltimoSeguimientoEnviado
                    ?? candidato.FechaUltimoMensajeCliente;

            var programadoPara = CalcularFecha(
                fechaBase,
                siguienteRegla.DemoraValor,
                siguienteRegla.DemoraUnidad);

            // Respetar una fecha de espera solicitada, incluso en consultas históricas.
            if (candidato.FechaPausaHasta.HasValue &&
                programadoPara < candidato.FechaPausaHasta.Value)
                programadoPara = candidato.FechaPausaHasta.Value;
            if (fechaEsperarHasta.HasValue && programadoPara < fechaEsperarHasta.Value)
                programadoPara = fechaEsperarHasta.Value;

            var mensaje = ConstruirMensaje(
                siguienteRegla.Mensaje,
                candidato.Nombre,
                candidato.ProductoInteres);

            await _repository.CrearPendienteSiNoExiste(
                candidato,
                siguienteRegla,
                programadoPara,
                mensaje);
        }

        // SIMULACION prepara y muestra la cola, pero no envía nada.
        if (!string.Equals(
                configuracion.ModoEnvio,
                "ACTIVO",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!EstaDentroDeHorario(
                DateTime.Now.TimeOfDay,
                configuracion.HoraInicio,
                configuracion.HoraFin))
        {
            return;
        }

        var envio = await _repository.TomarSiguientePendiente(
            configuracion.SeparacionEnviosMinutos);

        if (envio is null)
        {
            return;
        }

        var vigencia = await _repository.ValidarVigencia(envio.Id);

        if (!vigencia.Vigente)
        {
            // No cancelar una cola válida por un apagado/cambio a simulación.
            if (vigencia.MotivoCancelacion == "El motor comercial no está habilitado para enviar.")
            {
                await _repository.LiberarPendiente(envio.Id);
                return;
            }

            await _repository.MarcarCancelado(
                envio.Id,
                vigencia.MotivoCancelacion
                    ?? "El seguimiento dejó de ser aplicable antes del envío.");
            return;
        }

        // Comprobación adicional antes de salir a WhatsApp, también para colas
        // antiguas creadas antes de incorporar la interpretación de rechazos/pausas.
        var ultimaDecision = PoliticaSeguimientoCliente.Evaluar(vigencia.UltimoMensajeCliente);
        var esperarHasta = ultimaDecision.PausaValor.HasValue &&
                           vigencia.FechaUltimoMensajeCliente.HasValue
            ? ultimaDecision.PausaUnidad switch
            {
                "DIA" => vigencia.FechaUltimoMensajeCliente.Value.AddDays(ultimaDecision.PausaValor.Value),
                "SEMANA" => vigencia.FechaUltimoMensajeCliente.Value.AddDays(ultimaDecision.PausaValor.Value * 7),
                "MES" => vigencia.FechaUltimoMensajeCliente.Value.AddMonths(ultimaDecision.PausaValor.Value),
                "ANIO" => vigencia.FechaUltimoMensajeCliente.Value.AddYears(ultimaDecision.PausaValor.Value),
                "HORA" => vigencia.FechaUltimoMensajeCliente.Value.AddHours(ultimaDecision.PausaValor.Value),
                _ => (DateTime?)null
            }
            : null;

        if (ultimaDecision.NoContactar || ultimaDecision.Desiste ||
            ultimaDecision.PausaIndefinida ||
            (esperarHasta.HasValue && esperarHasta.Value > DateTime.Now) ||
            PoliticaSeguimientoCliente.EsConsultaInmueble(vigencia.UltimoMensajeCliente))
        {
            await _repository.MarcarCancelado(
                envio.Id,
                "Se detectó rechazo, espera solicitada o consulta ajena a motos en el último mensaje del cliente.");
            return;
        }

        try
        {
            var resultado = await _sender.Enviar(
                envio.Id,
                envio.Telefono,
                envio.Mensaje,
                cancellationToken);

            if (!resultado.Enviado)
            {
                await _repository.MarcarError(
                    envio.Id,
                    resultado.Error ?? "El bridge de WhatsApp no confirmó el envío.",
                    configuracion.MinutosReintentoTecnico,
                    configuracion.MaximoReintentosTecnicos);
                return;
            }

            await _repository.MarcarEnviado(
                envio.Id,
                resultado.MessageId);

            _logger.LogInformation(
                "Seguimiento WhatsApp enviado. IdEnvio={IdEnvio}, IdInteresado={IdInteresado}, Numero={Numero}",
                envio.Id,
                envio.IdInteresado,
                envio.NumeroSeguimiento);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error enviando seguimiento WhatsApp. IdEnvio={IdEnvio}",
                envio.Id);

            await _repository.MarcarError(
                envio.Id,
                ex.Message,
                configuracion.MinutosReintentoTecnico,
                configuracion.MaximoReintentosTecnicos);
        }
    }

    public Task<IReadOnlyList<SeguimientoWhatsappEnvioDto>> ListarEnvios(
        string? estado,
        int limite = 200)
        => _repository.ListarEnvios(estado, Math.Clamp(limite, 1, 500));

    private static void ValidarConfiguracion(
        ActualizarSeguimientoWhatsappConfiguracionRequest request)
    {
        var modo = request.ModoEnvio?.Trim().ToUpperInvariant();

        if (modo is not ("SIMULACION" or "ACTIVO"))
        {
            throw new ArgumentException(
                "ModoEnvio debe ser SIMULACION o ACTIVO.");
        }

        if (request.SeparacionEnviosMinutos is < 1 or > 1440)
        {
            throw new ArgumentException(
                "La separación entre envíos debe estar entre 1 y 1440 minutos.");
        }

        if (request.MinutosReintentoTecnico is < 1 or > 1440)
        {
            throw new ArgumentException(
                "El reintento técnico debe estar entre 1 y 1440 minutos.");
        }

        if (request.MaximoReintentosTecnicos is < 1 or > 10)
        {
            throw new ArgumentException(
                "El máximo de reintentos técnicos debe estar entre 1 y 10.");
        }

        if (request.Reglas is null || request.Reglas.Count == 0)
        {
            throw new ArgumentException(
                "Debe existir al menos una regla de seguimiento.");
        }

        var ordenes = new HashSet<int>();

        foreach (var regla in request.Reglas)
        {
            if (regla.Orden is < 1 or > 20 || !ordenes.Add(regla.Orden))
            {
                throw new ArgumentException(
                    "Las reglas deben tener un orden único entre 1 y 20.");
            }

            if (regla.DemoraValor is < 1 or > 525600)
            {
                throw new ArgumentException(
                    $"La demora de la regla {regla.Orden} es inválida.");
            }

            var unidad = regla.DemoraUnidad?.Trim().ToUpperInvariant();

            if (unidad is not ("MINUTO" or "HORA" or "DIA"))
            {
                throw new ArgumentException(
                    $"La unidad de la regla {regla.Orden} debe ser MINUTO, HORA o DIA.");
            }

            if (string.IsNullOrWhiteSpace(regla.Mensaje) || regla.Mensaje.Trim().Length > 1000)
            {
                throw new ArgumentException(
                    $"El mensaje de la regla {regla.Orden} debe tener entre 1 y 1000 caracteres.");
            }
        }

        if (!request.Reglas.Any(x => x.Activo))
        {
            throw new ArgumentException(
                "Debe quedar por lo menos una regla activa.");
        }
    }

    private static DateTime CalcularFecha(
        DateTime fechaBase,
        int valor,
        string unidad)
        => unidad.ToUpperInvariant() switch
        {
            "MINUTO" => fechaBase.AddMinutes(valor),
            "HORA" => fechaBase.AddHours(valor),
            _ => fechaBase.AddDays(valor)
        };

    private static bool EstaDentroDeHorario(
        TimeSpan ahora,
        TimeSpan inicio,
        TimeSpan fin)
    {
        if (inicio <= fin)
        {
            return ahora >= inicio && ahora <= fin;
        }

        // Permite una ventana que cruce medianoche.
        return ahora >= inicio || ahora <= fin;
    }

    private static string ConstruirMensaje(
        string plantilla,
        string? nombre,
        string? productoInteres)
    {
        var nombreCorto = string.IsNullOrWhiteSpace(nombre) ||
                          nombre.Trim().Equals("Cliente WhatsApp", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : nombre.Trim()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ?? string.Empty;

        var saludo = string.IsNullOrWhiteSpace(nombreCorto)
            ? string.Empty
            : $" {nombreCorto}";

        var moto = string.IsNullOrWhiteSpace(productoInteres)
            ? "moto que estabas viendo"
            : productoInteres.Trim();

        return plantilla
            .Replace("{saludo}", saludo, StringComparison.OrdinalIgnoreCase)
            .Replace("{nombre}", nombreCorto, StringComparison.OrdinalIgnoreCase)
            .Replace("{moto}", moto, StringComparison.OrdinalIgnoreCase)
            .Trim();
    }
}

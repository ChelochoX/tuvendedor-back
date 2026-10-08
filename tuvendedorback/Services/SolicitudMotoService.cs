using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using tuvendedorback.DTOs;
using tuvendedorback.Exceptions;
using tuvendedorback.Repositories.Interfaces;
using tuvendedorback.Request;
using tuvendedorback.Services.Interfaces;

namespace tuvendedorback.Services;

public class SolicitudMotoService : ISolicitudMotoService
{
    private const string VERSION_AUTORIZACION = "2026-10";

    private readonly ISolicitudMotoRepository _repository;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SolicitudMotoService> _logger;

    public SolicitudMotoService(
        ISolicitudMotoRepository repository,
        IConfiguration configuration,
        ILogger<SolicitudMotoService> logger)
    {
        _repository = repository;
        _configuration = configuration;
        _logger = logger;
    }

    public Task<SolicitudMotoProcesoDto?> ObtenerActiva(
        int idConversacion)
        => _repository.ObtenerActivaPorConversacion(idConversacion);

    public async Task<SolicitudMotoProcesoResultadoDto> Iniciar(
        int idConversacion,
        int idModeloProducto,
        int? idPublicacion,
        string telefono,
        string tipoOperacion,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var tipo = tipoOperacion.Trim().ToUpperInvariant();

        if (tipo != "CREDITO" && tipo != "CONTADO")
        {
            throw new ReglasdeNegocioException(
                "El tipo de operación debe ser CONTADO o CREDITO.");
        }

        var activa = await _repository.ObtenerActivaPorConversacion(idConversacion);
        if (activa != null)
        {
            return Resultado(
                activa,
                "Ya tengo tu solicitud registrada. Seguimos desde donde quedamos.");
        }

        var idContacto = await _repository.ObtenerOCrearContacto(telefono);

        if (tipo == "CREDITO")
        {
            var id = await _repository.CrearCredito(
                idConversacion,
                idModeloProducto,
                idPublicacion,
                idContacto);

            var respuestaInicio = await ObtenerMensajeFlujo(
                "CREDITO_INICIO_IPS",
                "Hola, soy Panambí, asistente de TuVendedor. Con gusto te voy a ayudar a completar la solicitud de crédito. Voy a ir pidiéndote los datos de a poco para que sea sencillo. Para comenzar, ¿actualmente aportás a IPS?");

            return new SolicitudMotoProcesoResultadoDto
            {
                Manejado = true,
                IdSolicitud = id,
                TipoOperacion = "CREDITO",
                Estado = "PRE_EVALUACION",
                PasoActual = "PRECALIFICACION_IPS",
                Respuesta = respuestaInicio
            };
        }

        var idContado = await _repository.CrearContado(
            idConversacion,
            idModeloProducto,
            idPublicacion,
            idContacto);

        return new SolicitudMotoProcesoResultadoDto
        {
            Manejado = true,
            IdSolicitud = idContado,
            TipoOperacion = "CONTADO",
            Estado = "EN_PROCESO",
            PasoActual = "NOMBRE_COMPLETO",
            Respuesta =
                "Hola, soy Panambí, asistente de TuVendedor. Con gusto te voy a ayudar con la compra. Para comenzar, pasame tu nombre y apellido completo."
        };
    }

    public async Task CancelarActivaPorCambioDeProducto(
        SolicitudMotoProcesoDto solicitud,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _repository.Cancelar(
            solicitud.TipoOperacion,
            solicitud.IdSolicitud,
            "Cancelada automáticamente porque el cliente inició una consulta sobre otra publicación/modelo desde TuVendedor.");

        _logger.LogInformation(
            "Solicitud activa cancelada por cambio explícito de producto. Tipo={TipoOperacion}, IdSolicitud={IdSolicitud}, IdConversacion={IdConversacion}",
            solicitud.TipoOperacion,
            solicitud.IdSolicitud,
            solicitud.IdConversacion);
    }


    public async Task<SolicitudMotoProcesoResultadoDto> ProcesarActiva(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (EsCancelar(request.Mensaje))
        {
            await _repository.Cancelar(
                solicitud.TipoOperacion,
                solicitud.IdSolicitud,
                "Cancelado por solicitud del cliente.");

            return Resultado(
                solicitud,
                "Entendido. Cancelé esta solicitud y ya no voy a seguir pidiéndote datos para esta moto. Si más adelante querés consultar otra moto o cualquier otro producto de TuVendedor, escribime nomás.",
                "CANCELADA",
                "CANCELADA");
        }

        // Antes de intentar interpretar el mensaje como el dato del paso actual,
        // atendemos preguntas naturales sobre datos que ya están registrados.
        // Ejemplos: "¿cómo se llama el titular?", "¿qué nombre puse?",
        // "¿cuál es la CI registrada?". Esto evita que una pregunta del cliente
        // sea tratada como si fuera una autorización, una referencia, etc.
        if (EsConsultaDatoRegistrado(request.Mensaje))
        {
            return await ResponderDatoRegistrado(solicitud, request.Mensaje);
        }

        // Si el cliente vuelve después de un tiempo y pregunta dónde quedó,
        // no intentamos interpretar ese saludo/pregunta como el dato del paso actual.
        // En su lugar consultamos lo que realmente está guardado y le indicamos
        // qué ya tenemos, qué falta y cuál es el siguiente dato a enviar.
        if (EsConsultaEstadoSolicitud(request.Mensaje))
        {
            return await ResponderEstadoSolicitud(solicitud);
        }

        return solicitud.TipoOperacion.ToUpperInvariant() switch
        {
            "CREDITO" => await ProcesarCredito(solicitud, request),
            "CONTADO" => await ProcesarContado(solicitud, request),
            _ => Resultado(solicitud, "No pude identificar el tipo de solicitud. Te paso con un asesor.")
        };
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarCredito(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        var pasoActual =
            (solicitud.PasoActual ?? string.Empty)
            .Trim()
            .ToUpperInvariant();

        // Compatibilidad con solicitudes que quedaron en pasos del flujo anterior.
        // El nuevo flujo comienza por IPS y no vuelve a pedir edad/antigüedad antes
        // de registrar al cliente.
        if (pasoActual is "PRECALIFICACION_EDAD" or "PRECALIFICACION_ANTIGUEDAD")
        {
            await _repository.ActualizarPaso(
                "CREDITO",
                solicitud.IdSolicitud,
                "PRECALIFICACION_IPS");

            return Resultado(
                solicitud,
                "Para continuar con tu solicitud, primero necesito confirmar algo sencillo: ¿actualmente aportás a IPS?",
                "PRE_EVALUACION",
                "PRECALIFICACION_IPS");
        }

        // Reparación de solicitudes antiguas que quedaron esperando una referencia
        // comercial antes de registrar al titular.
        var resultadoPreEvaluacion =
            (solicitud.ResultadoPreEvaluacion ?? string.Empty)
            .Trim()
            .ToUpperInvariant();

        var referenciaPendiente =
            resultadoPreEvaluacion == "PENDIENTE_REFERENCIA_COMERCIAL"
            || resultadoPreEvaluacion == "PENDIENTE_REFERENCIAS_COMERCIALES";

        var yaEstaEnFlujoReferenciaOGarante =
            pasoActual.StartsWith("REF_COMERCIAL_")
            || pasoActual.StartsWith("GARANTE_")
            || pasoActual == "PRECALIFICACION_GARANTE"
            || pasoActual == "PENDIENTE_GARANTE_NOMBRE"
            || pasoActual == "ESPERANDO_GARANTE"
            || pasoActual == "PENDIENTE_CREDITOS_NOMBRE"
            || pasoActual == "PENDIENTE_CREDITOS";

        if (referenciaPendiente && !yaEstaEnFlujoReferenciaOGarante)
        {
            await _repository.ActualizarPaso(
                "CREDITO",
                solicitud.IdSolicitud,
                "REF_COMERCIAL_EXISTE");

            return Resultado(
                solicitud,
                await ObtenerMensajeFlujo(
                    "CREDITO_PREGUNTA_REFERENCIA_COMERCIAL",
                    "Como no contamos con IPS suficiente, podemos revisar otra opción. ¿Tenés alguna referencia comercial, por ejemplo una compra a cuotas que estés pagando o hayas pagado anteriormente?"),
                "PRE_EVALUACION",
                "REF_COMERCIAL_EXISTE");
        }

        return pasoActual switch
        {
            "PRECALIFICACION_IPS" => await ProcesarAportaIps(solicitud, request),
            "PRECALIFICACION_APORTES" => await ProcesarCantidadAportesIps(solicitud, request),
            "REF_COMERCIAL_PRECALIFICACION" => await ProcesarReferenciaComercialExiste(solicitud, request, false),
            "REF_COMERCIAL_EXISTE" => await ProcesarReferenciaComercialExiste(solicitud, request, false),
            "REF_COMERCIAL_NOMBRE" => await ProcesarReferenciaComercialNombre(solicitud, request, false),
            "REF_COMERCIAL_ANTIGUEDAD" => await ProcesarReferenciaComercialAntiguedad(solicitud, request, false),
            "REF_COMERCIAL_CUOTA" => await ProcesarReferenciaComercialCuota(solicitud, request, false),
            "PRECALIFICACION_GARANTE" => await ProcesarGarante(solicitud, request),
            "PENDIENTE_GARANTE_NOMBRE" => await ProcesarNombrePendienteGarante(solicitud, request),
            "ESPERANDO_GARANTE" => await ProcesarEsperaGarante(solicitud, request),
            "GARANTE_IPS" => await ProcesarGaranteIps(solicitud, request),
            "GARANTE_APORTES" => await ProcesarGaranteAportes(solicitud, request),
            "GARANTE_REF_EXISTE" => await ProcesarReferenciaComercialExiste(solicitud, request, true),
            "GARANTE_REF_NOMBRE" => await ProcesarReferenciaComercialNombre(solicitud, request, true),
            "GARANTE_REF_ANTIGUEDAD" => await ProcesarReferenciaComercialAntiguedad(solicitud, request, true),
            "GARANTE_REF_CUOTA" => await ProcesarReferenciaComercialCuota(solicitud, request, true),
            "PENDIENTE_CREDITOS_NOMBRE" => await ProcesarNombrePendienteCreditos(solicitud, request),
            "PENDIENTE_CREDITOS" => await ContinuarDatosTitularConRevision(
                solicitud,
                solicitud.MotivoPreEvaluacion ?? "La evaluación inicial requiere revisión del equipo de Créditos."),
            "NOMBRE_COMPLETO" => await ProcesarNombreCompleto(solicitud, request),
            "CORREGIR_NOMBRE_TITULAR" => await ProcesarCorreccionNombreTitular(solicitud, request),
            "CEDULA_NUMERO" => await ProcesarNumeroCedula(solicitud, request),
            "DOMICILIO_CIUDAD" => await ProcesarCiudad(solicitud, request),
            "DOMICILIO_BARRIO" => await ProcesarBarrio(solicitud, request),
            "DOMICILIO_DIRECCION" => await ProcesarDireccionDomicilio(solicitud, request),
            "LABORAL_EMPRESA" => await ProcesarEmpresa(solicitud, request),
            "LABORAL_DIRECCION" => await SaltarPasoLaboralAntiguo(solicitud),
            "LABORAL_TELEFONO" => await ProcesarTelefonoEmpresa(solicitud, request),
            "LABORAL_TELEFONO_ES_MOVIL" => await SaltarPasoLaboralAntiguo(solicitud),
            "LABORAL_JEFE_ENCARGADO" => await SaltarPasoLaboralAntiguo(solicitud),
            "REF_FAMILIAR_1" => await ProcesarReferenciaPersonal(
                solicitud,
                request,
                "FAMILIAR",
                "REF_FAMILIAR_2",
                "Gracias. Ahora pasame otra referencia familiar: nombre, teléfono y parentesco."),
            "REF_FAMILIAR_2" => await ProcesarReferenciaPersonal(
                solicitud,
                request,
                "FAMILIAR",
                "REF_AMIGO",
                "Gracias. Ahora pasame una referencia de amistad: nombre y teléfono."),
            "REF_AMIGO" => await ProcesarReferenciaPersonal(
                solicitud,
                request,
                "AMIGO",
                "ESTADO_CEDULA",
                "Gracias. Ya tengo las referencias personales. Antes de pedirte las fotos, confirmame si tu cédula está vigente y no vencida."),
            "REF_COMERCIAL_CONTROL" => await IrADocumentos(solicitud),
            "REF_COMERCIAL_DATOS" => await IrADocumentos(solicitud),
            "REF_COMERCIAL_MAS" => await IrADocumentos(solicitud),
            "ESTADO_CEDULA" => await ProcesarEstadoCedula(solicitud, request),
            "CEDULA_FRENTE" => await ProcesarDocumento(
                solicitud,
                request,
                "CEDULA_FRENTE",
                "Gracias. Ya recibí el frente de tu cédula. Ahora enviame una foto clara del dorso.",
                "CEDULA_DORSO"),
            "CEDULA_DORSO" => await ProcesarDorsoCedula(solicitud, request),
            "AUTORIZACION" => await ProcesarAutorizacion(solicitud, request),
            "LISTA_REVISION" => Resultado(
                solicitud,
                "Tu solicitud ya está registrada para revisión. Si necesitamos completar algún dato, te vamos a contactar."),
            _ => Resultado(
                solicitud,
                "Seguimos con tu solicitud desde donde quedó guardada. Si querés, decime qué dato necesitás completar.")
        };
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarContado(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        return solicitud.PasoActual.ToUpperInvariant() switch
        {
            "NOMBRE_COMPLETO" => await ProcesarNombreCompleto(solicitud, request),
            "CORREGIR_NOMBRE_TITULAR" => await ProcesarCorreccionNombreTitular(solicitud, request),
            "CEDULA_NUMERO" => await ProcesarNumeroCedula(solicitud, request),
            "ESTADO_CEDULA" => await ProcesarEstadoCedula(solicitud, request),
            "CEDULA_FRENTE" => await ProcesarDocumento(
                solicitud,
                request,
                "CEDULA_FRENTE",
                "Gracias, ya recibí el frente de tu cédula. Ahora enviame una foto clara del dorso.",
                "CEDULA_DORSO"),
            "CEDULA_DORSO" => await ProcesarDorsoCedula(solicitud, request),
            _ => Resultado(
                solicitud,
                "Sigamos con la documentación de tu compra al contado.")
        };
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarEdad(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        if (!TryParseFechaNacimiento(request.Mensaje, out var fechaNacimiento))
        {
            var msg = await ObtenerMensajeFlujo(
                "CREDITO_EDAD_INVALIDA",
                "No llegué a interpretar la fecha  Enviame tu fecha de nacimiento en formato DD/MM/AAAA.");

            return Resultado(
                solicitud,
                msg);
        }

        var regla = await _repository.ObtenerReglaCreditoActiva();
        var edad = CalcularEdad(fechaNacimiento);

        await _repository.GuardarFechaNacimiento(
            solicitud.IdContacto,
            fechaNacimiento);

        if (edad < regla.EdadMinima)
        {
            var motivo =
                $"Edad {edad}. El mínimo requerido es {regla.EdadMinima} años cumplidos.";

            await _repository.GuardarResultadoPreEvaluacion(
                solicitud.IdSolicitud,
                "NO_VIABLE",
                motivo,
                null,
                "NO_VIABLE");

            return Resultado(
                solicitud,
                $"Gracias por la información. Para solicitar el crédito el titular debe tener al menos {regla.EdadMinima} años cumplidos. Con la fecha indicada, la solicitud no puede continuar por ahora.",
                "NO_VIABLE",
                "NO_VIABLE");
        }

        await _repository.ActualizarPaso(
            "CREDITO",
            solicitud.IdSolicitud,
            "PRECALIFICACION_ANTIGUEDAD");

        var mensajeAntiguedad = await ObtenerMensajeFlujo(
            "CREDITO_PREGUNTA_ANTIGUEDAD",
            "Edad validada . ¿Cuánto tiempo de antigüedad tenés en tu trabajo actual? Podés responder, por ejemplo: 9 meses, 1 año o 3 años.");

        return Resultado(
            solicitud,
            mensajeAntiguedad,
            "PRE_EVALUACION",
            "PRECALIFICACION_ANTIGUEDAD");
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarAntiguedadLaboral(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        if (!TryParseDuracionMeses(request.Mensaje, out var antiguedadMeses) || antiguedadMeses < 0)
        {
            var msg = await ObtenerMensajeFlujo(
                "CREDITO_ANTIGUEDAD_INVALIDA",
                "No llegué a interpretar el tiempo  Podés responder, por ejemplo: 9 meses, 1 año, 1 año y 6 meses o 3 años.");

            return Resultado(solicitud, msg);
        }

        var regla = await _repository.ObtenerReglaCreditoActiva();

        await _repository.GuardarDatosLaboralesParciales(
            solicitud.IdSolicitud,
            antiguedadMeses: antiguedadMeses);

        if (antiguedadMeses < regla.AntiguedadLaboralMinMeses)
        {
            var motivo =
                $"Antigüedad laboral {antiguedadMeses} meses. Mínimo requerido {regla.AntiguedadLaboralMinMeses} meses.";

            if (EsViaGarante(solicitud.ViaEvaluacion))
            {
                return await PasarAPendienteCreditos(
                    solicitud,
                    motivo);
            }

            await _repository.GuardarResultadoPreEvaluacion(
                solicitud.IdSolicitud,
                "REQUIERE_GARANTE",
                motivo,
                solicitud.ViaEvaluacion,
                "PRECALIFICACION_GARANTE");

            var msgGarante = await ObtenerMensajeFlujo(
                "CREDITO_PREGUNTA_GARANTE",
                "Con estos datos todavía no calificamos para avanzar a sola firma. Si tenés un garante, podemos hacer una evaluación rápida del garante. ¿Tenés una persona que pueda salirte de garante? Respondeme SI o NO.");

            return Resultado(
                solicitud,
                msgGarante,
                "PRE_EVALUACION",
                "PRECALIFICACION_GARANTE");
        }

        // Las solicitudes nuevas ya llegan a este punto con una vía de evaluación
        // definida (IPS, referencia comercial o garante). Para solicitudes antiguas
        // que quedaron a mitad del flujo anterior, conservamos compatibilidad y
        // seguimos preguntando IPS si todavía no existe ViaEvaluacion.
        if (string.IsNullOrWhiteSpace(solicitud.ViaEvaluacion))
        {
            await _repository.ActualizarPaso(
                "CREDITO",
                solicitud.IdSolicitud,
                "PRECALIFICACION_IPS");

            return Resultado(
                solicitud,
                await ObtenerMensajeFlujo(
                    "CREDITO_INICIO_IPS",
                    "¿Actualmente aportás a IPS? "),
                "PRE_EVALUACION",
                "PRECALIFICACION_IPS");
        }

        await _repository.GuardarResultadoPreEvaluacion(
            solicitud.IdSolicitud,
            "VIABLE",
            null,
            solicitud.ViaEvaluacion,
            "NOMBRE_COMPLETO");

        await _repository.ActualizarPaso(
            "CREDITO",
            solicitud.IdSolicitud,
            "NOMBRE_COMPLETO");

        var mensajeDatos = await ObtenerMensajeFlujo(
            "CREDITO_PRECALIFICACION_OK_NOMBRE",
            "Perfecto  La evaluación inicial está bien. Ahora sí vamos a completar tus datos. ¿Cuál es tu nombre y apellido?");

        return Resultado(
            solicitud,
            mensajeDatos,
            "DOCUMENTACION",
            "NOMBRE_COMPLETO");
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarAportaIps(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        var texto = NormalizarTexto(request.Mensaje);

        if (!EsSi(texto) && !EsNo(texto))
        {
            return Resultado(
                solicitud,
                await ObtenerMensajeFlujo(
                    "CREDITO_IPS_NO_ENTENDIDO",
                    "No hay problema. Solo necesito confirmar si actualmente aportás a IPS. ¿Sí o no?"));
        }

        var aporta = EsSi(texto);

        await _repository.GuardarDatosLaboralesParciales(
            solicitud.IdSolicitud,
            aportaIps: aporta,
            cantidadAportesIps: aporta ? null : 0);

        if (aporta)
        {
            await _repository.ActualizarPaso(
                "CREDITO",
                solicitud.IdSolicitud,
                "PRECALIFICACION_APORTES");

            return Resultado(
                solicitud,
                await ObtenerMensajeFlujo(
                    "CREDITO_PREGUNTA_APORTES_IPS",
                    "Bien. ¿Cuántos aportes de IPS tenés aproximadamente?"),
                "PRE_EVALUACION",
                "PRECALIFICACION_APORTES");
        }

        await _repository.GuardarResultadoPreEvaluacion(
            solicitud.IdSolicitud,
            "PENDIENTE_REFERENCIA_COMERCIAL",
            "No aporta IPS.",
            "REFERENCIA_COMERCIAL",
            "REF_COMERCIAL_EXISTE");

        return Resultado(
            solicitud,
            await ObtenerMensajeFlujo(
                "CREDITO_PREGUNTA_REFERENCIA_COMERCIAL",
                "Entiendo. Podemos continuar por otra vía. ¿Tenés alguna referencia comercial, por ejemplo una compra a cuotas que estés pagando o hayas pagado anteriormente?"),
            "PRE_EVALUACION",
            "REF_COMERCIAL_EXISTE");
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarCantidadAportesIps(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        if (!TryParseEntero(request.Mensaje, out var aportes) || aportes < 0)
        {
            return Resultado(
                solicitud,
                await ObtenerMensajeFlujo(
                    "CREDITO_APORTES_INVALIDOS",
                    "No llegué a identificar la cantidad. ¿Cuántos aportes de IPS tenés aproximadamente?"));
        }

        var regla = await _repository.ObtenerReglaCreditoActiva();

        await _repository.GuardarDatosLaboralesParciales(
            solicitud.IdSolicitud,
            aportaIps: true,
            cantidadAportesIps: aportes);

        if (aportes >= regla.AportesIPSMinimos)
        {
            await _repository.GuardarResultadoPreEvaluacion(
                solicitud.IdSolicitud,
                "PREEVALUACION_INICIAL_OK",
                null,
                "IPS",
                "NOMBRE_COMPLETO");

            await _repository.ActualizarPaso(
                "CREDITO",
                solicitud.IdSolicitud,
                "NOMBRE_COMPLETO");

            return Resultado(
                solicitud,
                await ObtenerMensajeFlujo(
                    "CREDITO_PRECALIFICACION_OK_NOMBRE",
                    "Bien, podemos seguir. Pasame por favor tu nombre y apellido completo y tu número de cédula. Si preferís, podés enviarlos en mensajes separados."),
                "DOCUMENTACION",
                "NOMBRE_COMPLETO");
        }

        await _repository.GuardarResultadoPreEvaluacion(
            solicitud.IdSolicitud,
            "PENDIENTE_REFERENCIA_COMERCIAL",
            $"Cantidad de aportes IPS: {aportes}. Mínimo parametrizado: {regla.AportesIPSMinimos}.",
            "REFERENCIA_COMERCIAL",
            "REF_COMERCIAL_EXISTE");

        return Resultado(
            solicitud,
            await ObtenerMensajeFlujo(
                "CREDITO_PREGUNTA_REFERENCIA_COMERCIAL",
                "Gracias. Podemos revisar otra opción. ¿Tenés alguna referencia comercial, por ejemplo una compra a cuotas que estés pagando o hayas pagado anteriormente?"),
            "PRE_EVALUACION",
            "REF_COMERCIAL_EXISTE");
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarReferenciaComercialExiste(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request,
        bool esGarante)
    {
        var texto = NormalizarTexto(request.Mensaje);

        if (EsDatoNoDisponible(texto))
        {
            return esGarante
                ? await ContinuarDatosTitularConRevision(
                    solicitud,
                    "El garante no pudo completar ahora la información de referencia comercial.")
                : await ContinuarDatosTitularConRevision(
                    solicitud,
                    "El titular no tiene o no puede facilitar ahora una referencia comercial.");
        }

        if (!EsSi(texto) && !EsNo(texto))
        {
            var codigo = esGarante
                ? "CREDITO_GREF_RESP_INVALIDA"
                : "CREDITO_REF_RESP_INVALIDA";

            var fallback = esGarante
                ? "¿El garante tiene alguna referencia comercial donde haya pagado cuotas? Si no la tiene a mano, decime y seguimos."
                : "¿Tenés alguna referencia comercial donde hayas pagado cuotas? Si no la tenés a mano, decime y seguimos.";

            return Resultado(
                solicitud,
                await ObtenerMensajeFlujo(codigo, fallback));
        }

        if (EsNo(texto))
        {
            if (esGarante)
            {
                return await ContinuarDatosTitularConRevision(
                    solicitud,
                    "El garante informado no cuenta con IPS suficiente ni referencia comercial verificable.");
            }

            await _repository.GuardarDatosGarantePrecalificacion(
                solicitud.IdSolicitud,
                requiereGarante: true,
                estado: "PENDIENTE");

            await _repository.ActualizarPaso(
                "CREDITO",
                solicitud.IdSolicitud,
                "PRECALIFICACION_GARANTE");

            return Resultado(
                solicitud,
                await ObtenerMensajeFlujo(
                    "CREDITO_PREGUNTA_GARANTE",
                    "Entiendo. ¿Tenés alguna persona que pueda salirte de garante? Si todavía no tenés, igualmente vamos a dejar registrada tu solicitud."),
                "PRE_EVALUACION",
                "PRECALIFICACION_GARANTE");
        }

        var siguiente = esGarante
            ? "GARANTE_REF_NOMBRE"
            : "REF_COMERCIAL_NOMBRE";

        await _repository.ActualizarPaso(
            "CREDITO",
            solicitud.IdSolicitud,
            siguiente);

        return Resultado(
            solicitud,
            await ObtenerMensajeFlujo(
                esGarante
                    ? "CREDITO_PREGUNTA_GARANTE_REFERENCIA_NOMBRE"
                    : "CREDITO_PREGUNTA_REFERENCIA_NOMBRE",
                esGarante
                    ? "¿Cuál es el nombre del negocio o casa comercial donde el garante tiene esa referencia?"
                    : "¿Cuál es el nombre del negocio o casa comercial donde tenés esa referencia?"),
            "PRE_EVALUACION",
            siguiente);
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarReferenciaComercialNombre(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request,
        bool esGarante)
    {
        var nombre = (request.Mensaje ?? string.Empty).Trim();

        if (EsDatoNoDisponible(nombre))
        {
            return await ContinuarDatosTitularConRevision(
                solicitud,
                esGarante
                    ? "No se pudo completar ahora el nombre de la referencia comercial del garante."
                    : "No se pudo completar ahora el nombre de la referencia comercial del titular.");
        }

        if (nombre.Length < 2 || EsSi(nombre) || EsNo(nombre))
        {
            return Resultado(
                solicitud,
                await ObtenerMensajeFlujo(
                    esGarante ? "CREDITO_GREF_NOMBRE_INVALIDO" : "CREDITO_REF_NOMBRE_INVALIDO",
                    esGarante
                        ? "Pasame el nombre del negocio o casa comercial de la referencia del garante. Si no lo tenés a mano, decime y seguimos."
                        : "Pasame el nombre del negocio o casa comercial de tu referencia. Si no lo tenés a mano, decime y seguimos."));
        }

        await _repository.GuardarReferenciaComercialPrecalificacion(
            solicitud.IdSolicitud,
            esGarante,
            nombre: nombre);

        var siguiente = esGarante
            ? "GARANTE_REF_ANTIGUEDAD"
            : "REF_COMERCIAL_ANTIGUEDAD";

        await _repository.ActualizarPaso(
            "CREDITO",
            solicitud.IdSolicitud,
            siguiente);

        return Resultado(
            solicitud,
            await ObtenerMensajeFlujo(
                esGarante
                    ? "CREDITO_PREGUNTA_GARANTE_REFERENCIA_ANTIGUEDAD"
                    : "CREDITO_PREGUNTA_REFERENCIA_ANTIGUEDAD",
                esGarante
                    ? "¿Durante cuánto tiempo el garante pagó o viene pagando esa referencia? Si no lo recuerda, decime y seguimos."
                    : "¿Durante cuánto tiempo pagaste o venís pagando esa referencia? Si no lo recordás, decime y seguimos."),
            "PRE_EVALUACION",
            siguiente);
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarReferenciaComercialAntiguedad(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request,
        bool esGarante)
    {
        if (EsDatoNoDisponible(request.Mensaje))
        {
            return await ContinuarDatosTitularConRevision(
                solicitud,
                esGarante
                    ? "No se pudo precisar la antigüedad de la referencia comercial del garante."
                    : "No se pudo precisar la antigüedad de la referencia comercial del titular.");
        }

        if (!TryParseDuracionMeses(request.Mensaje, out var meses) || meses < 0)
        {
            return Resultado(
                solicitud,
                await ObtenerMensajeFlujo(
                    esGarante ? "CREDITO_GREF_TIEMPO_INVALIDO" : "CREDITO_REF_TIEMPO_INVALIDO",
                    "No llegué a interpretar el tiempo. Podés decirme, por ejemplo: 12 meses, 1 año o 2 años. Si no lo recordás, decime y seguimos."));
        }

        await _repository.GuardarReferenciaComercialPrecalificacion(
            solicitud.IdSolicitud,
            esGarante,
            antiguedadMeses: meses);

        var siguiente = esGarante
            ? "GARANTE_REF_CUOTA"
            : "REF_COMERCIAL_CUOTA";

        await _repository.ActualizarPaso(
            "CREDITO",
            solicitud.IdSolicitud,
            siguiente);

        return Resultado(
            solicitud,
            await ObtenerMensajeFlujo(
                esGarante
                    ? "CREDITO_PREGUNTA_GARANTE_REFERENCIA_CUOTA"
                    : "CREDITO_PREGUNTA_REFERENCIA_CUOTA",
                esGarante
                    ? "¿De cuánto era aproximadamente la cuota mensual que pagaba el garante? Si no lo recuerda, decime y seguimos."
                    : "¿De cuánto era aproximadamente la cuota mensual que pagabas? Si no lo recordás, decime y seguimos."),
            "PRE_EVALUACION",
            siguiente);
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarReferenciaComercialCuota(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request,
        bool esGarante)
    {
        if (EsDatoNoDisponible(request.Mensaje))
        {
            return await ContinuarDatosTitularConRevision(
                solicitud,
                esGarante
                    ? "No se pudo precisar el monto de la cuota de la referencia comercial del garante."
                    : "No se pudo precisar el monto de la cuota de la referencia comercial del titular.");
        }

        if (!TryParseMontoGuaranies(request.Mensaje, out var monto) || monto <= 0)
        {
            return Resultado(
                solicitud,
                await ObtenerMensajeFlujo(
                    esGarante ? "CREDITO_GREF_CUOTA_INVALIDA" : "CREDITO_REF_CUOTA_INVALIDA",
                    "No llegué a interpretar el monto. Decime aproximadamente cuánto era la cuota. Si no lo recordás, decime y seguimos."));
        }

        await _repository.GuardarReferenciaComercialPrecalificacion(
            solicitud.IdSolicitud,
            esGarante,
            montoCuota: monto);

        var actual = await _repository.ObtenerActivaPorConversacion(solicitud.IdConversacion)
                     ?? solicitud;

        var regla = await _repository.ObtenerReglaCreditoActiva();
        var cuotaMoto = actual.IdModeloProducto.HasValue
            ? await _repository.ObtenerCuotaReferenciaModelo(actual.IdModeloProducto.Value)
            : null;

        var meses = esGarante
            ? actual.GaranteRefComercialAntiguedadMeses
            : actual.RefComercialAntiguedadMeses;

        var comercio = esGarante
            ? actual.GaranteRefComercialNombre
            : actual.RefComercialNombre;

        if (!meses.HasValue || string.IsNullOrWhiteSpace(comercio) || !cuotaMoto.HasValue || cuotaMoto.Value <= 0)
        {
            return await ContinuarDatosTitularConRevision(
                solicitud,
                "La referencia comercial requiere verificación manual del equipo de Créditos.");
        }

        var montoMinimo = cuotaMoto.Value * (regla.ReferenciaComercialCuotaMinPorcentaje / 100m);
        var cumpleTiempo = meses.Value >= regla.ReferenciaComercialMinMeses;
        var cumpleMonto = monto >= montoMinimo;

        if (cumpleTiempo && cumpleMonto)
        {
            await _repository.AgregarReferencia(
                solicitud.IdSolicitud,
                esGarante ? "GARANTE_COMERCIAL" : "COMERCIAL",
                comercio,
                string.Empty,
                null,
                $"Referencia de preevaluación: {meses.Value} meses; cuota aprox. Gs. {monto:N0}; cuota moto de referencia Gs. {cuotaMoto.Value:N0}.");

            var via = esGarante
                ? "GARANTE_REFERENCIA_COMERCIAL"
                : "REFERENCIA_COMERCIAL";

            if (esGarante)
            {
                await _repository.GuardarDatosGarantePrecalificacion(
                    solicitud.IdSolicitud,
                    requiereGarante: true,
                    estado: "VIABLE");
            }

            await _repository.GuardarResultadoPreEvaluacion(
                solicitud.IdSolicitud,
                "PREEVALUACION_INICIAL_OK",
                null,
                via,
                "NOMBRE_COMPLETO");

            await _repository.ActualizarPaso(
                "CREDITO",
                solicitud.IdSolicitud,
                "NOMBRE_COMPLETO");

            return Resultado(
                solicitud,
                "Bien, podemos seguir con la solicitud. Pasame tu nombre y apellido completo y tu número de cédula. Si preferís, podés enviarlos en mensajes separados.",
                "DOCUMENTACION",
                "NOMBRE_COMPLETO");
        }

        if (esGarante)
        {
            return await ContinuarDatosTitularConRevision(
                solicitud,
                "La referencia comercial del garante necesita revisión manual.");
        }

        await _repository.GuardarResultadoPreEvaluacion(
            solicitud.IdSolicitud,
            "REQUIERE_GARANTE",
            "La referencia comercial informada no alcanza los parámetros automáticos de preevaluación.",
            "REFERENCIA_COMERCIAL",
            "PRECALIFICACION_GARANTE");

        await _repository.GuardarDatosGarantePrecalificacion(
            solicitud.IdSolicitud,
            requiereGarante: true,
            estado: "PENDIENTE");

        return Resultado(
            solicitud,
            await ObtenerMensajeFlujo(
                "CREDITO_PREGUNTA_GARANTE",
                "Gracias. Para reforzar la solicitud, ¿tenés alguna persona que pueda salirte de garante? Si no tenés, igualmente vamos a continuar registrando tus datos."),
            "PRE_EVALUACION",
            "PRECALIFICACION_GARANTE");
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarGarante(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        var texto = NormalizarTexto(request.Mensaje);

        if (EsNo(texto) || EsMensajeEsperaGarante(texto) || EsDatoNoDisponible(texto))
        {
            return await ContinuarDatosTitularConRevision(
                solicitud,
                "El titular no cuenta con garante confirmado en este momento.");
        }

        if (!EsSi(texto))
        {
            return Resultado(
                solicitud,
                await ObtenerMensajeFlujo(
                    "CREDITO_GARANTE_RESP_INVALIDA",
                    "¿Tenés alguna persona que pueda salirte de garante? Si todavía no tenés, decime y continuamos igualmente con el registro de tu solicitud."));
        }

        await _repository.GuardarDatosGarantePrecalificacion(
            solicitud.IdSolicitud,
            requiereGarante: true,
            estado: "EVALUANDO");

        await _repository.GuardarResultadoPreEvaluacion(
            solicitud.IdSolicitud,
            "EVALUANDO_GARANTE",
            solicitud.MotivoPreEvaluacion,
            "GARANTE",
            "GARANTE_IPS");

        return Resultado(
            solicitud,
            await ObtenerMensajeFlujo(
                "CREDITO_PREGUNTA_GARANTE_IPS",
                "Bien. ¿El garante aporta actualmente a IPS?"),
            "PRE_EVALUACION",
            "GARANTE_IPS");
    }


    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarEsperaGarante(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        var texto = NormalizarTexto(request.Mensaje);

        if (EsGaranteConfirmado(texto))
        {
            await _repository.GuardarDatosGarantePrecalificacion(
                solicitud.IdSolicitud,
                requiereGarante: true,
                estado: "EVALUANDO");

            await _repository.GuardarResultadoPreEvaluacion(
                solicitud.IdSolicitud,
                "EVALUANDO_GARANTE",
                solicitud.MotivoPreEvaluacion,
                "GARANTE",
                "GARANTE_IPS");

            return Resultado(
                solicitud,
                "Bien. ¿El garante aporta actualmente a IPS?",
                "PRE_EVALUACION",
                "GARANTE_IPS");
        }

        return await ContinuarDatosTitularConRevision(
            solicitud,
            "La solicitud quedó pendiente de confirmación de garante, pero continuamos registrando los datos del titular.");
    }


    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarGaranteIps(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        var texto = NormalizarTexto(request.Mensaje);

        if (EsDatoNoDisponible(texto))
        {
            return await ContinuarDatosTitularConRevision(
                solicitud,
                "No se pudo confirmar ahora la situación de IPS del garante.");
        }

        if (!EsSi(texto) && !EsNo(texto))
        {
            return Resultado(
                solicitud,
                await ObtenerMensajeFlujo(
                    "CREDITO_GARANTE_IPS_INVALIDO",
                    "Solo necesito confirmar si el garante aporta actualmente a IPS. Si no lo sabés ahora, decime y seguimos."));
        }

        var aporta = EsSi(texto);

        await _repository.GuardarDatosGarantePrecalificacion(
            solicitud.IdSolicitud,
            requiereGarante: true,
            aportaIps: aporta,
            cantidadAportesIps: aporta ? null : 0,
            estado: "EVALUANDO");

        if (aporta)
        {
            await _repository.ActualizarPaso(
                "CREDITO",
                solicitud.IdSolicitud,
                "GARANTE_APORTES");

            return Resultado(
                solicitud,
                await ObtenerMensajeFlujo(
                    "CREDITO_PREGUNTA_GARANTE_APORTES",
                    "¿Cuántos aportes de IPS tiene aproximadamente el garante?"),
                "PRE_EVALUACION",
                "GARANTE_APORTES");
        }

        await _repository.ActualizarPaso(
            "CREDITO",
            solicitud.IdSolicitud,
            "GARANTE_REF_EXISTE");

        return Resultado(
            solicitud,
            await ObtenerMensajeFlujo(
                "CREDITO_PREGUNTA_GARANTE_REFERENCIA",
                "Si el garante no tiene IPS, podemos considerar una referencia comercial. ¿Tiene alguna compra a cuotas o referencia comercial verificable?"),
            "PRE_EVALUACION",
            "GARANTE_REF_EXISTE");
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarGaranteAportes(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        if (EsDatoNoDisponible(request.Mensaje))
        {
            return await ContinuarDatosTitularConRevision(
                solicitud,
                "No se pudo confirmar ahora la cantidad de aportes del garante.");
        }

        if (!TryParseEntero(request.Mensaje, out var aportes) || aportes < 0)
        {
            return Resultado(
                solicitud,
                await ObtenerMensajeFlujo(
                    "CREDITO_GARANTE_APORTES_INVALIDOS",
                    "No llegué a identificar la cantidad de aportes del garante. Si no la sabés ahora, decime y seguimos."));
        }

        var regla = await _repository.ObtenerReglaCreditoActiva();

        await _repository.GuardarDatosGarantePrecalificacion(
            solicitud.IdSolicitud,
            requiereGarante: true,
            aportaIps: true,
            cantidadAportesIps: aportes,
            estado: aportes >= regla.AportesIPSMinimos ? "VIABLE" : "EVALUANDO");

        if (aportes >= regla.AportesIPSMinimos)
        {
            await _repository.GuardarResultadoPreEvaluacion(
                solicitud.IdSolicitud,
                "PREEVALUACION_INICIAL_OK",
                null,
                "GARANTE_IPS",
                "NOMBRE_COMPLETO");

            await _repository.ActualizarPaso(
                "CREDITO",
                solicitud.IdSolicitud,
                "NOMBRE_COMPLETO");

            return Resultado(
                solicitud,
                "Bien, podemos seguir con la solicitud. Pasame tu nombre y apellido completo y tu número de cédula. Si preferís, podés enviarlos en mensajes separados.",
                "DOCUMENTACION",
                "NOMBRE_COMPLETO");
        }

        await _repository.ActualizarPaso(
            "CREDITO",
            solicitud.IdSolicitud,
            "GARANTE_REF_EXISTE");

        return Resultado(
            solicitud,
            await ObtenerMensajeFlujo(
                "CREDITO_PREGUNTA_GARANTE_REFERENCIA",
                "Podemos revisar una referencia comercial del garante. ¿Tiene alguna compra a cuotas o referencia comercial verificable?"),
            "PRE_EVALUACION",
            "GARANTE_REF_EXISTE");
    }

    private async Task<SolicitudMotoProcesoResultadoDto> PrepararPendienteGarante(
        SolicitudMotoProcesoDto solicitud,
        string motivo,
        bool garanteEvaluadoNoViable = false)
    {
        await _repository.GuardarDatosGarantePrecalificacion(
            solicitud.IdSolicitud,
            requiereGarante: true,
            estado: "PENDIENTE_REVISION");

        return await ContinuarDatosTitularConRevision(
            solicitud,
            motivo);
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarNombrePendienteGarante(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        await _repository.ActualizarPaso(
            "CREDITO",
            solicitud.IdSolicitud,
            "NOMBRE_COMPLETO");

        return await ProcesarNombreCompleto(solicitud, request);
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarPendienteCreditos(
        SolicitudMotoProcesoDto solicitud)
    {
        return await ContinuarDatosTitularConRevision(
            solicitud,
            solicitud.MotivoPreEvaluacion ?? "El caso requiere revisión manual del equipo de Créditos.");
    }

    private async Task<SolicitudMotoProcesoResultadoDto> PasarAPendienteCreditos(
        SolicitudMotoProcesoDto solicitud,
        string motivo)
    {
        return await ContinuarDatosTitularConRevision(
            solicitud,
            motivo);
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarNombrePendienteCreditos(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        await _repository.ActualizarPaso(
            "CREDITO",
            solicitud.IdSolicitud,
            "NOMBRE_COMPLETO");

        return await ProcesarNombreCompleto(solicitud, request);
    }


    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarNombreCompleto(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        var mensaje = (request.Mensaje ?? string.Empty).Trim();

        if (TryParseNombreYCedulaFlexible(mensaje, out var nombreConCedula, out var cedula))
        {
            await _repository.GuardarDatosContactoParciales(
                solicitud.IdContacto,
                nombreCompleto: nombreConCedula,
                numeroCedula: cedula);

            await _repository.ActualizarPaso(
                solicitud.TipoOperacion,
                solicitud.IdSolicitud,
                solicitud.TipoOperacion.Equals("CREDITO", StringComparison.OrdinalIgnoreCase)
                    ? "DOMICILIO_CIUDAD"
                    : "ESTADO_CEDULA");

            return solicitud.TipoOperacion.Equals("CREDITO", StringComparison.OrdinalIgnoreCase)
                ? Resultado(
                    solicitud,
                    "Gracias. Ahora necesito tu domicilio. Primero, ¿en qué ciudad vivís?",
                    "DOCUMENTACION",
                    "DOMICILIO_CIUDAD")
                : Resultado(
                    solicitud,
                    "Gracias. Antes de las fotos, confirmame si tu cédula está vigente y no vencida.",
                    solicitud.Estado,
                    "ESTADO_CEDULA");
        }

        if (!EsNombrePersonaValido(mensaje))
        {
            return Resultado(
                solicitud,
                "Pasame tu nombre y apellido completo. Si querés, podés incluir también tu número de cédula en el mismo mensaje.");
        }

        await _repository.GuardarDatosContactoParciales(
            solicitud.IdContacto,
            nombreCompleto: mensaje);

        await _repository.ActualizarPaso(
            solicitud.TipoOperacion,
            solicitud.IdSolicitud,
            "CEDULA_NUMERO");

        return Resultado(
            solicitud,
            "Gracias. Ahora pasame tu número de cédula.",
            solicitud.Estado,
            "CEDULA_NUMERO");
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarCorreccionNombreTitular(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        var nombre = (request.Mensaje ?? string.Empty).Trim();

        if (!EsNombrePersonaValido(nombre))
        {
            return Resultado(
                solicitud,
                "Pasame solamente tu nombre y apellido completo para corregir el dato.",
                solicitud.Estado,
                "CORREGIR_NOMBRE_TITULAR");
        }

        await _repository.GuardarDatosContactoParciales(
            solicitud.IdContacto,
            nombreCompleto: nombre);

        var siguiente = solicitud.TipoOperacion.Equals("CREDITO", StringComparison.OrdinalIgnoreCase)
            ? "AUTORIZACION"
            : "CEDULA_NUMERO";

        await _repository.ActualizarPaso(
            solicitud.TipoOperacion,
            solicitud.IdSolicitud,
            siguiente);

        if (siguiente == "AUTORIZACION")
        {
            return await PrepararAutorizacion(
                solicitud,
                "Gracias, ya corregí el nombre del titular.");
        }

        return Resultado(
            solicitud,
            "Gracias, ya corregí el nombre. Ahora pasame tu número de cédula.",
            solicitud.Estado,
            siguiente);
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarNumeroCedula(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        var cedula = SoloDigitos(request.Mensaje);

        if (cedula.Length < 5 || cedula.Length > 10)
        {
            return Resultado(
                solicitud,
                "No llegué a identificar el número de cédula. Podés escribirlo con o sin puntos.");
        }

        await _repository.GuardarDatosContactoParciales(
            solicitud.IdContacto,
            numeroCedula: cedula);

        var esCredito = solicitud.TipoOperacion.Equals("CREDITO", StringComparison.OrdinalIgnoreCase);
        var siguiente = esCredito ? "DOMICILIO_CIUDAD" : "ESTADO_CEDULA";

        await _repository.ActualizarPaso(
            solicitud.TipoOperacion,
            solicitud.IdSolicitud,
            siguiente);

        return esCredito
            ? Resultado(
                solicitud,
                "Gracias. Ahora necesito tu domicilio. Primero, ¿en qué ciudad vivís?",
                "DOCUMENTACION",
                siguiente)
            : Resultado(
                solicitud,
                "Gracias. ¿Tu cédula está vigente y no vencida?",
                solicitud.Estado,
                siguiente);
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarDorsoCedula(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        if (EsDatoNoDisponible(request.Mensaje) && !TieneMediaValida(request))
        {
            if (solicitud.TipoOperacion.Equals("CREDITO", StringComparison.OrdinalIgnoreCase))
            {
                return await ContinuarDespuesDocumentosCredito(
                    solicitud,
                    "No hay problema. Dejamos pendiente la foto del dorso para que el equipo pueda solicitarla si hace falta.");
            }

            return Resultado(
                solicitud,
                "No hay problema. Dejamos pendiente la foto del dorso para completar después.");
        }

        if (!TieneMediaValida(request))
        {
            return Resultado(
                solicitud,
                "Enviame una foto clara del dorso de tu cédula. Si no la tenés a mano ahora, decime y seguimos.");
        }

        var archivo = await GuardarArchivoPrivado(
            solicitud,
            "CEDULA_DORSO",
            request);

        await _repository.GuardarDocumento(
            solicitud.TipoOperacion,
            solicitud.IdSolicitud,
            solicitud.IdConversacion,
            solicitud.IdModeloProducto,
            "CEDULA_DORSO",
            archivo.NombreArchivo,
            archivo.MimeType,
            archivo.RutaPrivada,
            archivo.HashSha256);

        if (solicitud.TipoOperacion.Equals("CONTADO", StringComparison.OrdinalIgnoreCase))
        {
            await _repository.MarcarListaRevision("CONTADO", solicitud.IdSolicitud);

            return Resultado(
                solicitud,
                "Gracias. Ya recibimos la documentación y la compra queda registrada para revisión con el asesor.",
                "LISTA_REVISION",
                "LISTA_REVISION");
        }

        return await ContinuarDespuesDocumentosCredito(
            solicitud,
            "Gracias. Ya recibí las fotos de tu cédula.");
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarCiudad(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        var valor = (request.Mensaje ?? string.Empty).Trim();

        if (!EsDatoNoDisponible(valor) && valor.Length < 2)
        {
            return Resultado(
                solicitud,
                "¿En qué ciudad vivís? Si no tenés el dato a mano ahora, decime y seguimos.");
        }

        if (!EsDatoNoDisponible(valor))
        {
            await _repository.GuardarDatosContactoParciales(
                solicitud.IdContacto,
                ciudad: valor);
        }

        await _repository.ActualizarPaso("CREDITO", solicitud.IdSolicitud, "DOMICILIO_BARRIO");

        return Resultado(
            solicitud,
            "¿Y en qué barrio vivís? Si no lo tenés a mano ahora, no hay problema.",
            "DOCUMENTACION",
            "DOMICILIO_BARRIO");
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarBarrio(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        var valor = (request.Mensaje ?? string.Empty).Trim();

        if (!EsDatoNoDisponible(valor) && valor.Length < 2)
        {
            return Resultado(
                solicitud,
                "¿Cuál es tu barrio? Si no lo tenés a mano ahora, decime y seguimos.");
        }

        if (!EsDatoNoDisponible(valor))
        {
            await _repository.GuardarDatosContactoParciales(
                solicitud.IdContacto,
                barrio: valor);
        }

        await _repository.ActualizarPaso("CREDITO", solicitud.IdSolicitud, "DOMICILIO_DIRECCION");

        return Resultado(
            solicitud,
            "Ahora pasame tu dirección particular o alguna referencia para ubicar tu domicilio. Si no la tenés a mano, seguimos igual.",
            "DOCUMENTACION",
            "DOMICILIO_DIRECCION");
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarDireccionDomicilio(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        var valor = (request.Mensaje ?? string.Empty).Trim();

        if (!EsDatoNoDisponible(valor) && valor.Length < 3)
        {
            return Resultado(
                solicitud,
                "Pasame tu dirección particular o alguna referencia para ubicar tu domicilio. Si no la tenés a mano, decime y seguimos.");
        }

        if (!EsDatoNoDisponible(valor))
        {
            await _repository.GuardarDatosContactoParciales(
                solicitud.IdContacto,
                direccion: valor);
        }

        await _repository.ActualizarPaso("CREDITO", solicitud.IdSolicitud, "LABORAL_EMPRESA");

        return Resultado(
            solicitud,
            "Gracias. Ahora seguimos con tus datos laborales. ¿Dónde trabajás actualmente? Pasame el nombre de la empresa o lugar de trabajo.",
            "DOCUMENTACION",
            "LABORAL_EMPRESA");
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarEmpresa(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        var valor = (request.Mensaje ?? string.Empty).Trim();

        if (EsDatoNoDisponible(valor))
        {
            await _repository.GuardarResultadoPreEvaluacion(
                solicitud.IdSolicitud,
                "PENDIENTE_REVISION",
                "El titular no pudo indicar ahora el lugar de trabajo.",
                solicitud.ViaEvaluacion,
                "LABORAL_TELEFONO");
        }
        else if (valor.Length >= 2)
        {
            await _repository.GuardarDatosLaboralesParciales(
                solicitud.IdSolicitud,
                empresa: valor);
        }
        else
        {
            return Resultado(
                solicitud,
                "¿Dónde trabajás actualmente? Si no tenés el dato a mano, decime y seguimos.");
        }

        await _repository.ActualizarPaso("CREDITO", solicitud.IdSolicitud, "LABORAL_TELEFONO");

        return Resultado(
            solicitud,
            "Si tenés a mano, pasame también un teléfono de la empresa o lugar de trabajo. Si no lo tenés ahora, no hay problema y seguimos.",
            "DOCUMENTACION",
            "LABORAL_TELEFONO");
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarDireccionEmpresa(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        // Compatibilidad con solicitudes iniciadas con el flujo anterior.
        // La dirección laboral ya no es bloqueante ni se exige por WhatsApp.
        if (!EsDatoNoDisponible(request.Mensaje))
        {
            var valor = (request.Mensaje ?? string.Empty).Trim();
            if (valor.Length >= 4)
            {
                await _repository.GuardarDatosLaboralesParciales(
                    solicitud.IdSolicitud,
                    direccionEmpresa: valor);
            }
        }

        return await IrAReferenciasPersonales(solicitud);
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarTelefonoEmpresa(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        var mensaje = (request.Mensaje ?? string.Empty).Trim();
        var telefono = SoloDigitos(mensaje);

        if (!EsDatoNoDisponible(mensaje) && telefono.Length >= 6)
        {
            await _repository.GuardarDatosLaboralesParciales(
                solicitud.IdSolicitud,
                telefonoEmpresa: telefono);
        }
        else if (!EsDatoNoDisponible(mensaje) && telefono.Length > 0)
        {
            return Resultado(
                solicitud,
                "No llegué a identificar bien el teléfono. Podés volver a enviarlo o decirme que no lo tenés a mano y seguimos.");
        }

        return await IrAReferenciasPersonales(solicitud);
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarTelefonoEmpresaEsMovil(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        return await IrAReferenciasPersonales(solicitud);
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarJefeEncargado(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        return await IrAReferenciasPersonales(solicitud);
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarIdentidad(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        if (!TryParseIdentidad(request.Mensaje, out var nombre, out var cedula))
        {
            return Resultado(
                solicitud,
                "Enviame esos datos así : Nombre completo | N.º de cédula. Ejemplo: Juan Pérez González | 4.123.456");
        }

        await _repository.GuardarIdentidadContacto(
            solicitud.IdContacto,
            nombre,
            cedula);

        await _repository.ActualizarPaso(
            solicitud.TipoOperacion,
            solicitud.IdSolicitud,
            "CEDULA_FRENTE");

        return Resultado(
            solicitud,
            "Gracias  Ahora enviame una foto clara del FRENTE de tu cédula.",
            solicitud.Estado,
            "CEDULA_FRENTE");
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarDocumento(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request,
        string tipoDocumento,
        string respuestaOk,
        string siguientePaso)
    {
        if (!TieneMediaValida(request))
        {
            if (EsDatoNoDisponible(request.Mensaje))
            {
                await _repository.ActualizarPaso(
                    solicitud.TipoOperacion,
                    solicitud.IdSolicitud,
                    siguientePaso);

                return Resultado(
                    solicitud,
                    tipoDocumento == "CEDULA_FRENTE"
                        ? "No hay problema. Dejamos pendiente la foto del frente. Si podés, enviame ahora el dorso; si tampoco lo tenés a mano, decime y seguimos."
                        : "No hay problema. Dejamos pendiente esa foto y seguimos.",
                    solicitud.Estado,
                    siguientePaso);
            }

            return Resultado(
                solicitud,
                tipoDocumento == "CEDULA_FRENTE"
                    ? "Enviame una foto clara del frente de tu cédula. Si no la tenés a mano ahora, decime y seguimos."
                    : "Enviame una foto clara del dorso de tu cédula. Si no la tenés a mano ahora, decime y seguimos.");
        }

        (string NombreArchivo, string MimeType, string RutaPrivada, string HashSha256) archivo;

        try
        {
            archivo = await GuardarArchivoPrivado(
                solicitud,
                tipoDocumento,
                request);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error guardando archivo privado. Solicitud={IdSolicitud}, Tipo={TipoDocumento}, CI={Cedula}",
                solicitud.IdSolicitud,
                tipoDocumento,
                solicitud.Cedula);

            return Resultado(
                solicitud,
                "No pude guardar la imagen en este momento. Podemos intentarlo otra vez o dejarla pendiente y seguir.");
        }

        try
        {
            await _repository.GuardarDocumento(
                solicitud.TipoOperacion,
                solicitud.IdSolicitud,
                solicitud.IdConversacion,
                solicitud.IdModeloProducto,
                tipoDocumento,
                archivo.NombreArchivo,
                archivo.MimeType,
                archivo.RutaPrivada,
                archivo.HashSha256);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Archivo guardado pero falló el registro en BBDD. Solicitud={IdSolicitud}, Tipo={TipoDocumento}, Ruta={Ruta}",
                solicitud.IdSolicitud,
                tipoDocumento,
                archivo.RutaPrivada);

            return Resultado(
                solicitud,
                "Recibí la imagen, pero no pude registrarla correctamente. Podemos volver a intentarlo sin perder los demás datos.");
        }

        await _repository.ActualizarPaso(
            solicitud.TipoOperacion,
            solicitud.IdSolicitud,
            siguientePaso);

        return Resultado(
            solicitud,
            respuestaOk,
            solicitud.Estado,
            siguientePaso);
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarEstadoCedula(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        var texto = NormalizarTexto(request.Mensaje);

        var indicaVigente =
            texto.Contains("VIGENTE")
            || texto.Contains("NO VENCIDA")
            || texto.Contains("NO VENCIDO")
            || EsSi(texto);

        var indicaNoVigente =
            texto.Contains("VENCIDA")
            || texto.Contains("VENCIDO")
            || texto.Contains("RENOV")
            || EsNo(texto);

        if (!indicaVigente && !indicaNoVigente && EsDatoNoDisponible(texto))
        {
            await _repository.GuardarEstadoCedula(
                solicitud.TipoOperacion,
                solicitud.IdSolicitud,
                "PENDIENTE");

            await _repository.ActualizarPaso(
                solicitud.TipoOperacion,
                solicitud.IdSolicitud,
                "CEDULA_FRENTE");

            return Resultado(
                solicitud,
                "No hay problema. Dejamos pendiente la verificación de vigencia. Si tenés a mano, enviame una foto clara del frente de tu cédula.",
                solicitud.Estado,
                "CEDULA_FRENTE");
        }

        if (!indicaVigente && !indicaNoVigente)
        {
            return Resultado(
                solicitud,
                "Antes de las fotos necesito confirmar si tu cédula está vigente y no vencida. Si no estás seguro ahora, decime y seguimos dejando ese dato pendiente.");
        }

        await _repository.GuardarEstadoCedula(
            solicitud.TipoOperacion,
            solicitud.IdSolicitud,
            indicaVigente ? "VIGENTE" : "VENCIDA");

        await _repository.ActualizarPaso(
            solicitud.TipoOperacion,
            solicitud.IdSolicitud,
            "CEDULA_FRENTE");

        return Resultado(
            solicitud,
            indicaVigente
                ? "Gracias. Ya estamos terminando. Enviame una foto clara del frente de tu cédula."
                : "Gracias por avisarme. Voy a dejar registrado que la cédula necesita revisión. Igual podemos avanzar con la carga: enviame una foto del frente si la tenés a mano.",
            solicitud.Estado,
            "CEDULA_FRENTE");
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarComprobanteRenovacion(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        if (!TieneMediaValida(request))
        {
            return Resultado(
                solicitud,
                "Necesito el comprobante de renovación para continuar . Podés enviarlo como foto o PDF.");
        }

        var archivo = await GuardarArchivoPrivado(
            solicitud,
            "COMPROBANTE_RENOVACION",
            request);

        await _repository.GuardarDocumento(
            solicitud.TipoOperacion,
            solicitud.IdSolicitud,
            solicitud.IdConversacion,
            solicitud.IdModeloProducto,
            "COMPROBANTE_RENOVACION",
            archivo.NombreArchivo,
            archivo.MimeType,
            archivo.RutaPrivada,
            archivo.HashSha256);

        return await ContinuarDespuesCedula(solicitud, "RENOVACION");
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ContinuarDespuesCedula(
        SolicitudMotoProcesoDto solicitud,
        string estadoCedula)
    {
        var frente = await _repository.TieneDocumento(
            solicitud.TipoOperacion,
            solicitud.IdSolicitud,
            "CEDULA_FRENTE");

        var dorso = await _repository.TieneDocumento(
            solicitud.TipoOperacion,
            solicitud.IdSolicitud,
            "CEDULA_DORSO");

        if (!frente || !dorso)
        {
            var paso = !frente ? "CEDULA_FRENTE" : "CEDULA_DORSO";
            await _repository.ActualizarPaso(solicitud.TipoOperacion, solicitud.IdSolicitud, paso);

            return Resultado(
                solicitud,
                !frente
                    ? "Antes de seguir me falta la foto del FRENTE de tu cédula ."
                    : "Antes de seguir me falta la foto del DORSO de tu cédula .",
                solicitud.Estado,
                paso);
        }

        if (estadoCedula == "RENOVACION")
        {
            var comprobante = await _repository.TieneDocumento(
                solicitud.TipoOperacion,
                solicitud.IdSolicitud,
                "COMPROBANTE_RENOVACION");

            if (!comprobante)
            {
                await _repository.ActualizarPaso(
                    solicitud.TipoOperacion,
                    solicitud.IdSolicitud,
                    "COMPROBANTE_RENOVACION");

                return Resultado(
                    solicitud,
                    "Me falta el comprobante de renovación de la cédula .",
                    solicitud.Estado,
                    "COMPROBANTE_RENOVACION");
            }
        }

        if (solicitud.TipoOperacion == "CONTADO")
        {
            await _repository.MarcarListaRevision("CONTADO", solicitud.IdSolicitud);

            return Resultado(
                solicitud,
                "Perfecto  Ya recibimos la documentación necesaria para la compra al contado. Queda lista para revisión y cierre con el asesor.",
                "LISTA_REVISION",
                "LISTA_REVISION");
        }

        await _repository.ActualizarPaso(
            "CREDITO",
            solicitud.IdSolicitud,
            "DOMICILIO");

        return Resultado(
            solicitud,
            "Cédula recibida . Ahora necesito tu domicilio particular: Ciudad | Barrio | Calle o dirección donde vivís.",
            "DOCUMENTACION",
            "DOMICILIO");
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarDomicilio(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        if (!TryParseTresCampos(
                request.Mensaje,
                out var ciudad,
                out var barrio,
                out var direccion))
        {
            return Resultado(
                solicitud,
                "Enviame tu domicilio así: Ciudad | Barrio | Calle o dirección donde vivís.");
        }

        await _repository.GuardarDomicilioContacto(
            solicitud.IdContacto,
            ciudad,
            barrio,
            direccion);

        await _repository.ActualizarPaso(
            "CREDITO",
            solicitud.IdSolicitud,
            "LABORAL_COMPLETO");

        return Resultado(
            solicitud,
            "Domicilio guardado . Ahora completamos los datos laborales:\n" +
            "Dirección de la empresa | Teléfono de la empresa | ¿El teléfono es celular? SI/NO | Nombre del jefe/encargado si es celular.\n\n" +
            "Si el teléfono no es celular, podés dejar el último dato vacío.",
            "DOCUMENTACION",
            "LABORAL_COMPLETO");
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarLaboralCompleto(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        if (!TryParseLaboralCompleto(
                request.Mensaje,
                out var direccion,
                out var telefono,
                out var esMovil,
                out var encargado))
        {
            return Resultado(
                solicitud,
                "Enviame: Dirección empresa | Teléfono empresa | SI/NO es celular | Nombre del jefe/encargado si es celular.");
        }

        if (esMovil && string.IsNullOrWhiteSpace(encargado))
        {
            return Resultado(
                solicitud,
                "Como el número de la empresa es celular, necesito el nombre del jefe o encargado al que pertenece ese número.");
        }

        await _repository.GuardarDatosLaboralesCompletos(
            solicitud.IdSolicitud,
            direccion,
            telefono,
            esMovil,
            encargado);

        await _repository.ActualizarPaso(
            "CREDITO",
            solicitud.IdSolicitud,
            "REF_FAMILIAR_1");

        return Resultado(
            solicitud,
            "Datos laborales guardados . Ahora vamos con las referencias personales. Necesitamos solamente 3 personas: 2 familiares/parientes y 1 amistad.\n\n" +
            "Empecemos con la primera referencia familiar. Podés escribirlo de forma natural. Ejemplo: Cesar Leite, 0982 33 44 55, hno.",
            "DOCUMENTACION",
            "REF_FAMILIAR_1");
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarReferenciaPersonal(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request,
        string tipoEsperado,
        string siguientePaso,
        string respuestaOk)
    {
        if (EsDatoNoDisponible(request.Mensaje))
        {
            await _repository.GuardarResultadoPreEvaluacion(
                solicitud.IdSolicitud,
                "PENDIENTE_REVISION",
                "El titular no pudo completar todas las referencias personales durante la conversación.",
                solicitud.ViaEvaluacion,
                "ESTADO_CEDULA");

            return await IrADocumentos(solicitud);
        }

        if (!TryParseReferenciaPersonal(
                request.Mensaje,
                tipoEsperado,
                out var tipoDetectado,
                out var nombre,
                out var telefono,
                out var relacion))
        {
            return Resultado(
                solicitud,
                "No llegué a identificar bien la referencia. Pasame nombre, teléfono y la relación que tiene con vos. Si no tenés el dato a mano ahora, decime y seguimos.");
        }

        var referenciasExistentes =
            await _repository.ObtenerReferencias(solicitud.IdSolicitud);

        var telefonoNormalizado = SoloDigitos(telefono);

        if (referenciasExistentes.Any(
                x => SoloDigitos(x.Telefono) == telefonoNormalizado))
        {
            return Resultado(
                solicitud,
                "Ese teléfono ya está registrado como referencia. Pasame otra persona con un número diferente.");
        }

        var regla = await _repository.ObtenerReglaCreditoActiva();

        await _repository.AgregarReferencia(
            solicitud.IdSolicitud,
            tipoDetectado,
            nombre,
            telefonoNormalizado,
            EsTipo(tipoDetectado, "FAMILIAR") ? relacion : "AMIGO",
            null);

        var referenciasActualizadas =
            await _repository.ObtenerReferencias(solicitud.IdSolicitud);

        var familiares = referenciasActualizadas.Count(x => EsTipo(x.Tipo, "FAMILIAR"));
        var amigos = referenciasActualizadas.Count(x => EsTipo(x.Tipo, "AMIGO"));

        if (familiares < regla.ReferenciasFamiliaresMinimas)
        {
            var paso = familiares == 0 ? "REF_FAMILIAR_1" : "REF_FAMILIAR_2";

            await _repository.ActualizarPaso(
                "CREDITO",
                solicitud.IdSolicitud,
                paso);

            return Resultado(
                solicitud,
                "Gracias. Pasame otra referencia familiar: nombre, teléfono y parentesco.",
                "DOCUMENTACION",
                paso);
        }

        if (amigos < regla.ReferenciasAmigosMinimas)
        {
            await _repository.ActualizarPaso(
                "CREDITO",
                solicitud.IdSolicitud,
                "REF_AMIGO");

            return Resultado(
                solicitud,
                "Gracias. Ahora pasame una referencia de amistad: nombre y teléfono.",
                "DOCUMENTACION",
                "REF_AMIGO");
        }

        return await IrADocumentos(solicitud);
    }

    private async Task<SolicitudMotoProcesoResultadoDto> PrepararReferenciasComerciales(
        SolicitudMotoProcesoDto solicitud,
        string? mensajeAnterior = null)
    {
        return await IrADocumentos(solicitud, mensajeAnterior);
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarReferenciaComercialDatos(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        // IMPORTANTE: no tener referencia comercial NO bloquea el crédito.
        // Aceptamos respuestas naturales como: "no tengo", "ninguna",
        // "no cuento con referencias comerciales", etc. y avanzamos.
        if (EsSinReferenciaComercial(request.Mensaje))
        {
            return await PrepararAutorizacion(
                solicitud,
                "Está bien  No tener referencia comercial no bloquea tu solicitud. Continuamos con la autorización final.");
        }

        var comercio = (request.Mensaje ?? string.Empty).Trim();

        if (comercio.Length < 2 || EsSi(comercio))
        {
            return Resultado(
                solicitud,
                "Si tenés una referencia comercial, indicame el nombre del negocio o casa comercial. Si no tenés ninguna, escribime NO TENGO y continuamos.");
        }

        var referencias = await _repository.ObtenerReferencias(solicitud.IdSolicitud);

        if (referencias.Any(x =>
            EsTipo(x.Tipo, "COMERCIAL") &&
            string.Equals(
                NormalizarTexto(x.Nombre),
                NormalizarTexto(comercio),
                StringComparison.OrdinalIgnoreCase)))
        {
            return Resultado(
                solicitud,
                "Ese negocio ya fue registrado como referencia comercial. Indicame otro nombre si tenés una referencia adicional.");
        }

        await _repository.AgregarReferencia(
            solicitud.IdSolicitud,
            "COMERCIAL",
            comercio,
            string.Empty,
            null,
            "Referencia comercial verificable informada por el cliente.");

        await _repository.ActualizarPaso(
            "CREDITO",
            solicitud.IdSolicitud,
            "REF_COMERCIAL_MAS");

        return Resultado(
            solicitud,
            "Referencia comercial guardada . Si tenés otra referencia comercial, también la podemos registrar. ¿Querés agregar otra? Respondé SI o NO.",
            "DOCUMENTACION",
            "REF_COMERCIAL_MAS");
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarReferenciaComercialMas(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        var texto = NormalizarTexto(request.Mensaje);

        if (EsSi(texto))
        {
            await _repository.ActualizarPaso(
                "CREDITO",
                solicitud.IdSolicitud,
                "REF_COMERCIAL_DATOS");

            return Resultado(
                solicitud,
                "Perfecto  Indicame el nombre del otro negocio o casa comercial donde tenés referencia.",
                "DOCUMENTACION",
                "REF_COMERCIAL_DATOS");
        }

        if (EsNo(texto))
        {
            return await PrepararAutorizacion(solicitud);
        }

        return Resultado(
            solicitud,
            "Si tenés otra referencia comercial respondé SI. Si ya no tenés más, respondé NO y pasamos a la autorización.");
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarReferenciaComercialOpcional(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        var texto = NormalizarTexto(request.Mensaje);

        if (EsSi(texto))
        {
            await _repository.ActualizarPaso(
                "CREDITO",
                solicitud.IdSolicitud,
                "REF_COMERCIAL_OPCIONAL_DATOS");

            return Resultado(
                solicitud,
                "Buenísimo  Indicame el nombre del negocio o casa comercial donde tenés la referencia.",
                "DOCUMENTACION",
                "REF_COMERCIAL_OPCIONAL_DATOS");
        }

        if (EsNo(texto))
        {
            return await PrepararAutorizacion(solicitud);
        }

        return Resultado(
            solicitud,
            "¿Querés agregar una referencia comercial adicional? Respondé SI o NO.");
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarReferenciaComercialOpcionalDatos(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        var comercio = (request.Mensaje ?? string.Empty).Trim();

        if (comercio.Length < 2 || EsSi(comercio) || EsNo(comercio))
        {
            return Resultado(
                solicitud,
                "Indicame el nombre del negocio o casa comercial donde tenés la referencia comercial.");
        }

        await _repository.AgregarReferencia(
            solicitud.IdSolicitud,
            "COMERCIAL",
            comercio,
            string.Empty,
            null,
            "Referencia comercial adicional.");

        return await PrepararAutorizacion(solicitud);
    }

    private async Task<SolicitudMotoProcesoResultadoDto> PrepararAutorizacion(
        SolicitudMotoProcesoDto solicitud,
        string? mensajeAnterior = null)
    {
        var texto = await _repository.ObtenerTextoAutorizacion();

        if (string.IsNullOrWhiteSpace(texto))
        {
            throw new ReglasdeNegocioException(
                "No está configurado el texto de autorización de crédito.");
        }

        await _repository.ActualizarPaso(
            "CREDITO",
            solicitud.IdSolicitud,
            "AUTORIZACION");

        var prefijo = string.IsNullOrWhiteSpace(mensajeAnterior)
            ? string.Empty
            : mensajeAnterior.Trim() + "\n\n";

        return Resultado(
            solicitud,
            prefijo +
            "Para terminar necesito tu autorización para utilizar los datos que nos proporcionaste únicamente para la gestión y evaluación de esta solicitud de crédito.\n\n" +
            texto +
            "\n\n¿Me autorizás a continuar?",
            "DOCUMENTACION",
            "AUTORIZACION");
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarAutorizacion(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        var textoRespuesta = NormalizarTexto(request.Mensaje);

        if (EsNo(textoRespuesta))
        {
            return Resultado(
                solicitud,
                "Entiendo. No voy a cerrar la autorización sin tu consentimiento. La solicitud queda guardada y podemos retomarla cuando quieras.",
                solicitud.Estado,
                "AUTORIZACION");
        }

        if (!EsAutorizacionNatural(textoRespuesta))
        {
            return Resultado(
                solicitud,
                "Para terminar necesito saber si me autorizás a continuar con la gestión y evaluación de la solicitud. Podés responder, por ejemplo: Sí, autorizo.",
                solicitud.Estado,
                "AUTORIZACION");
        }

        var actual = await _repository.ObtenerActivaPorConversacion(solicitud.IdConversacion)
                     ?? solicitud;

        var nombre = $"{actual.Nombre} {actual.Apellido}".Trim();
        var cedula = SoloDigitos(actual.Cedula ?? string.Empty);

        if (!EsNombrePersonaValido(nombre))
        {
            await _repository.ActualizarPaso(
                "CREDITO",
                solicitud.IdSolicitud,
                "CORREGIR_NOMBRE_TITULAR");

            return Resultado(
                solicitud,
                "Antes de cerrar la autorización necesito corregir el nombre del titular. Pasame tu nombre y apellido completo.",
                solicitud.Estado,
                "CORREGIR_NOMBRE_TITULAR");
        }

        if (string.IsNullOrWhiteSpace(cedula))
        {
            await _repository.ActualizarPaso(
                "CREDITO",
                solicitud.IdSolicitud,
                "CEDULA_NUMERO");

            return Resultado(
                solicitud,
                "Antes de cerrar la autorización necesito tu número de cédula.",
                solicitud.Estado,
                "CEDULA_NUMERO");
        }

        var textoAutorizacion = await _repository.ObtenerTextoAutorizacion();
        if (string.IsNullOrWhiteSpace(textoAutorizacion))
        {
            throw new ReglasdeNegocioException(
                "No está configurado el texto de autorización de crédito.");
        }

        await _repository.GuardarAutorizacion(
            solicitud.IdSolicitud,
            VERSION_AUTORIZACION,
            textoAutorizacion,
            request.Mensaje,
            nombre,
            cedula);

        return await ValidarChecklistFinal(solicitud);
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ValidarChecklistFinal(
        SolicitudMotoProcesoDto solicitud)
    {
        var actual = await _repository.ObtenerActivaPorConversacion(solicitud.IdConversacion)
                     ?? solicitud;

        var regla = await _repository.ObtenerReglaCreditoActiva();
        var referencias = await _repository.ObtenerReferencias(solicitud.IdSolicitud);
        var familiares = referencias.Count(x => EsTipo(x.Tipo, "FAMILIAR"));
        var amigos = referencias.Count(x => EsTipo(x.Tipo, "AMIGO"));

        var frente = await _repository.TieneDocumento(
            "CREDITO",
            solicitud.IdSolicitud,
            "CEDULA_FRENTE");

        var dorso = await _repository.TieneDocumento(
            "CREDITO",
            solicitud.IdSolicitud,
            "CEDULA_DORSO");

        var observaciones = new List<string>();

        if (string.IsNullOrWhiteSpace(actual.Ciudad))
            observaciones.Add("ciudad pendiente");

        if (string.IsNullOrWhiteSpace(actual.Barrio))
            observaciones.Add("barrio pendiente");

        if (string.IsNullOrWhiteSpace(actual.Direccion))
            observaciones.Add("dirección particular pendiente");

        if (string.IsNullOrWhiteSpace(actual.Empresa))
            observaciones.Add("lugar de trabajo pendiente");

        if (string.IsNullOrWhiteSpace(actual.TelefonoEmpresa))
            observaciones.Add("teléfono laboral pendiente");

        if (familiares < regla.ReferenciasFamiliaresMinimas)
            observaciones.Add("referencias familiares pendientes");

        if (amigos < regla.ReferenciasAmigosMinimas)
            observaciones.Add("referencia de amistad pendiente");

        if (!frente)
            observaciones.Add("foto del frente de la CI pendiente");

        if (!dorso)
            observaciones.Add("foto del dorso de la CI pendiente");

        if (!string.Equals(actual.EstadoCedula, "VIGENTE", StringComparison.OrdinalIgnoreCase))
            observaciones.Add("vigencia de CI pendiente de verificación");

        if (observaciones.Count > 0)
        {
            await _repository.GuardarResultadoPreEvaluacion(
                solicitud.IdSolicitud,
                "PENDIENTE_REVISION",
                string.Join("; ", observaciones),
                actual.ViaEvaluacion,
                "LISTA_REVISION");
        }

        await _repository.MarcarListaRevision(
            "CREDITO",
            solicitud.IdSolicitud);

        return Resultado(
            solicitud,
            "Gracias. Tu solicitud quedó registrada para revisión con el equipo de Créditos. Si necesitamos completar algún dato, te vamos a contactar.",
            "LISTA_REVISION",
            "LISTA_REVISION");
    }

    private async Task<(string NombreArchivo, string MimeType, string RutaPrivada, string HashSha256)> GuardarArchivoPrivado(
        SolicitudMotoProcesoDto solicitud,
        string tipoDocumento,
        MotoConversacionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.MediaBase64))
        {
            throw new ReglasdeNegocioException("No se recibió el archivo.");
        }

        var base64 = request.MediaBase64.Trim();
        var indiceComa = base64.IndexOf(',');
        if (base64.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && indiceComa >= 0)
        {
            base64 = base64[(indiceComa + 1)..];
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Base64 inválido en documento de solicitud {IdSolicitud}.", solicitud.IdSolicitud);
            throw new ReglasdeNegocioException("El archivo recibido no es válido.");
        }

        var maxBytes = _configuration.GetValue<long?>("IA:MaxPrivateDocumentBytes") ?? 8L * 1024 * 1024;
        if (bytes.LongLength > maxBytes)
        {
            throw new ReglasdeNegocioException("El archivo supera el tamaño máximo permitido.");
        }

        var cedula = SoloDigitos(solicitud.Cedula ?? string.Empty);
        if (string.IsNullOrWhiteSpace(cedula))
        {
            cedula = $"SIN_CI_{solicitud.IdSolicitud}";
        }

        var basePathConfigurado = _configuration["IA:PrivateDocumentsPath"];
        var basePath = string.IsNullOrWhiteSpace(basePathConfigurado)
            ? Path.Combine(AppContext.BaseDirectory, "private-documents")
            : basePathConfigurado;

        try
        {
            Directory.CreateDirectory(basePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "No se pudo usar PrivateDocumentsPath={BasePath}. Se usará una carpeta local de respaldo.",
                basePath);

            basePath = Path.Combine(AppContext.BaseDirectory, "private-documents");
            Directory.CreateDirectory(basePath);
        }

        // Organización final:
        // private-documents / credito / CI / solicitud_ID / CEDULA_FRENTE.jpg
        var carpeta = Path.Combine(
            basePath,
            solicitud.TipoOperacion.ToLowerInvariant(),
            cedula,
            $"solicitud_{solicitud.IdSolicitud}");

        Directory.CreateDirectory(carpeta);

        var extension = ObtenerExtension(request.MediaMimeType, request.MediaNombre);
        var nombreSeguro = $"{tipoDocumento}{extension}";
        var ruta = Path.Combine(carpeta, nombreSeguro);

        await File.WriteAllBytesAsync(ruta, bytes);

        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        _logger.LogInformation(
            "Documento guardado correctamente. Solicitud={IdSolicitud}, Tipo={TipoDocumento}, Ruta={Ruta}",
            solicitud.IdSolicitud,
            tipoDocumento,
            ruta);

        return (
            nombreSeguro,
            request.MediaMimeType ?? "application/octet-stream",
            ruta,
            hash);
    }

    private static bool TieneMediaValida(MotoConversacionRequest request)
    {
        var tipo = (request.TipoMensaje ?? string.Empty).Trim().ToUpperInvariant();
        return (tipo == "IMAGEN" || tipo == "DOCUMENTO")
               && !string.IsNullOrWhiteSpace(request.MediaBase64)
               && !string.IsNullOrWhiteSpace(request.MediaMimeType);
    }

    private static string ObtenerExtension(string? mimeType, string? nombreOriginal)
    {
        if (!string.IsNullOrWhiteSpace(nombreOriginal))
        {
            var ext = Path.GetExtension(nombreOriginal);
            if (!string.IsNullOrWhiteSpace(ext) && ext.Length <= 10)
            {
                return ext;
            }
        }

        return (mimeType ?? string.Empty).ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            "application/pdf" => ".pdf",
            _ => ".bin"
        };
    }

    private static bool TryParseFechaNacimiento(string texto, out DateTime fecha)
    {
        var formatos = new[] { "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy", "yyyy-MM-dd" };
        return DateTime.TryParseExact(
            texto.Trim(),
            formatos,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out fecha)
            && fecha.Date <= DateTime.Today;
    }

    private static int CalcularEdad(DateTime fechaNacimiento)
    {
        var hoy = DateTime.Today;
        var edad = hoy.Year - fechaNacimiento.Year;
        if (fechaNacimiento.Date > hoy.AddYears(-edad)) edad--;
        return edad;
    }

    private static bool TryParsePrecalificacionLaboral(
        string texto,
        out string empresa,
        out int antiguedadMeses,
        out bool aportaIps,
        out int cantidadAportes)
    {
        empresa = string.Empty;
        antiguedadMeses = 0;
        aportaIps = false;
        cantidadAportes = 0;

        var partes = SepararCampos(texto);
        if (partes.Length < 4) return false;

        empresa = partes[0];
        if (string.IsNullOrWhiteSpace(empresa)) return false;

        if (!TryParseEntero(partes[1], out antiguedadMeses) || antiguedadMeses < 0) return false;

        var ips = NormalizarTexto(partes[2]);
        if (EsSi(ips)) aportaIps = true;
        else if (EsNo(ips)) aportaIps = false;
        else return false;

        if (!TryParseEntero(partes[3], out cantidadAportes) || cantidadAportes < 0) return false;

        if (!aportaIps) cantidadAportes = 0;
        return true;
    }

    private static bool TryParseLaboralCompleto(
        string texto,
        out string direccion,
        out string telefono,
        out bool esMovil,
        out string? encargado)
    {
        direccion = string.Empty;
        telefono = string.Empty;
        esMovil = false;
        encargado = null;

        var partes = SepararCampos(texto);
        if (partes.Length < 3) return false;

        direccion = partes[0];
        telefono = partes[1];

        var movil = NormalizarTexto(partes[2]);
        if (EsSi(movil)) esMovil = true;
        else if (EsNo(movil)) esMovil = false;
        else return false;

        if (partes.Length >= 4)
        {
            encargado = partes[3].Trim();
            if (string.IsNullOrWhiteSpace(encargado)) encargado = null;
        }

        return !string.IsNullOrWhiteSpace(direccion)
               && SoloDigitos(telefono).Length >= 6;
    }

    private static bool TryParseIdentidad(
        string texto,
        out string nombre,
        out string cedula)
    {
        nombre = string.Empty;
        cedula = string.Empty;

        var partes = SepararCampos(texto);
        if (partes.Length < 2) return false;

        nombre = partes[0].Trim();
        cedula = partes[1].Trim();

        return nombre.Length >= 4 && SoloDigitos(cedula).Length >= 5;
    }

    private static bool TryParseReferenciaPersonal(
        string? texto,
        string tipoEsperado,
        out string tipoDetectado,
        out string nombre,
        out string telefono,
        out string relacion)
    {
        tipoDetectado = string.Empty;
        nombre = string.Empty;
        telefono = string.Empty;
        relacion = string.Empty;

        if (string.IsNullOrWhiteSpace(texto))
            return false;

        var contenido = texto.Trim();

        // 1) Intento normal: 0981 123456 / 0981-123456 / +595 981 123456.
        var matchTelefono = Regex.Match(
            contenido,
            @"(?:\+?595[\s\-.]*)?(?:0?9\d{2})(?:[\s\-.]*\d){6}",
            RegexOptions.IgnoreCase);

        var indiceTelefono = -1;
        var numero = string.Empty;

        if (matchTelefono.Success)
        {
            indiceTelefono = matchTelefono.Index;
            numero = NormalizarTelefonoParaguayo(matchTelefono.Value);
        }

        // 2) Modo tolerante para errores de tipeo pequeños dentro del campo teléfono.
        // Ej.: "0971c 000000". Si entre separadores hay exactamente un número
        // paraguayo reconocible al quitar caracteres accidentales, lo aceptamos.
        if (string.IsNullOrWhiteSpace(numero))
        {
            var candidatos = Regex.Split(contenido, @"[,;|]")
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();

            foreach (var candidato in candidatos.Skip(1))
            {
                var normalizado = NormalizarTelefonoParaguayo(candidato);
                if (string.IsNullOrWhiteSpace(normalizado))
                    continue;

                var idx = contenido.IndexOf(candidato, StringComparison.OrdinalIgnoreCase);
                if (idx < 0)
                    continue;

                indiceTelefono = idx;
                numero = normalizado;
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(numero) || indiceTelefono < 0)
            return false;

        var parteNombre = contenido
            .Substring(0, indiceTelefono)
            .Trim()
            .Trim(',', ';', '|', '-', ':');

        parteNombre = Regex.Replace(
                parteNombre,
                @"^\s*(NOMBRE(?:\s+Y\s+APELLIDO)?|REFERENCIA)\s*[:\-]?\s*",
                string.Empty,
                RegexOptions.IgnoreCase)
            .Trim()
            .Trim(',', ';', '|', '-', ':');

        var palabrasNombre = parteNombre.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parteNombre.Length < 4
            || palabrasNombre.Length < 2
            || parteNombre.Any(char.IsDigit))
        {
            return false;
        }

        nombre = parteNombre;
        telefono = numero;

        var parentesco = DetectarParentescoReferencia(contenido);
        var esAmistad = DetectarAmistadReferencia(contenido);

        // Si el cliente escribió explícitamente amigo/amiga/amistad, respetamos eso
        // aunque el bot estuviera esperando una referencia familiar.
        if (esAmistad)
        {
            tipoDetectado = "AMIGO";
            relacion = "AMIGO";
            return true;
        }

        // Si escribió un parentesco, se guarda como familiar aunque el paso actual
        // estuviera esperando la amistad.
        if (!string.IsNullOrWhiteSpace(parentesco))
        {
            tipoDetectado = "FAMILIAR";
            relacion = parentesco;
            return true;
        }

        // Cuando estamos específicamente en el paso de amistad permitimos nombre +
        // teléfono sin obligar a escribir la palabra "amigo".
        if (string.Equals(tipoEsperado, "AMIGO", StringComparison.OrdinalIgnoreCase))
        {
            tipoDetectado = "AMIGO";
            relacion = "AMIGO";
            return true;
        }

        // Para una referencia familiar sí necesitamos conocer el parentesco.
        return false;
    }

    private static string NormalizarTelefonoParaguayo(string texto)
    {
        var numero = SoloDigitos(texto);

        if (numero.StartsWith("595") && numero.Length == 12)
            numero = "0" + numero.Substring(3);

        return numero.Length == 10 && numero.StartsWith("09")
            ? numero
            : string.Empty;
    }

    private static bool DetectarAmistadReferencia(string texto)
    {
        var normalizado = NormalizarTexto(texto);

        return Regex.IsMatch(
            normalizado,
            @"\b(AMIGO|AMIGA|AMISTAD|AMIGO/A|AMIGA/O)\b",
            RegexOptions.IgnoreCase);
    }

    private static string? DetectarParentescoReferencia(
        string texto)
    {
        var normalizado =
            NormalizarTexto(
                texto);

        var opciones =
            new (string Patron, string Valor)[]
            {
                (@"\b(HNO|HERMANO)\b", "HERMANO"),
                (@"\b(HNA|HERMANA)\b", "HERMANA"),
                (@"\b(PRIMO)\b", "PRIMO"),
                (@"\b(PRIMA)\b", "PRIMA"),
                (@"\b(PADRE|PAPA)\b", "PADRE"),
                (@"\b(MADRE|MAMA)\b", "MADRE"),
                (@"\b(TIO)\b", "TIO"),
                (@"\b(TIA)\b", "TIA"),
                (@"\b(ABUELO)\b", "ABUELO"),
                (@"\b(ABUELA)\b", "ABUELA"),
                (@"\b(HIJO)\b", "HIJO"),
                (@"\b(HIJA)\b", "HIJA"),
                (@"\b(SOBRINO)\b", "SOBRINO"),
                (@"\b(SOBRINA)\b", "SOBRINA"),
                (@"\b(NIETO)\b", "NIETO"),
                (@"\b(NIETA)\b", "NIETA"),
                (@"\b(CUNADO)\b", "CUNADO"),
                (@"\b(CUNADA)\b", "CUNADA"),
                (@"\b(SUEGRO)\b", "SUEGRO"),
                (@"\b(SUEGRA)\b", "SUEGRA"),
                (@"\b(ESPOSO)\b", "ESPOSO"),
                (@"\b(ESPOSA)\b", "ESPOSA"),
                (@"\b(FAMILIAR|PARIENTE)\b", "FAMILIAR")
            };

        foreach (
            var opcion
            in opciones)
        {
            if (
                Regex.IsMatch(
                    normalizado,
                    opcion.Patron,
                    RegexOptions.IgnoreCase)
            )
            {
                return opcion.Valor;
            }
        }

        return null;
    }

    private static bool TryParseTresCampos(
        string texto,
        out string campo1,
        out string campo2,
        out string campo3)
    {
        campo1 = campo2 = campo3 = string.Empty;
        var partes = SepararCampos(texto);
        if (partes.Length < 3) return false;

        campo1 = partes[0].Trim();
        campo2 = partes[1].Trim();
        campo3 = partes[2].Trim();

        return campo1.Length > 0 && campo2.Length > 0 && campo3.Length > 0;
    }

    private static bool TryParseDosCampos(
        string texto,
        out string campo1,
        out string campo2)
    {
        campo1 = campo2 = string.Empty;
        var partes = SepararCampos(texto);
        if (partes.Length < 2) return false;

        campo1 = partes[0].Trim();
        campo2 = partes[1].Trim();
        return campo1.Length > 0 && SoloDigitos(campo2).Length >= 6;
    }

    private static string[] SepararCampos(string texto)
        => texto
            .Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static bool TryParseEntero(
        string texto,
        out int valor)
    {
        valor = 0;

        var match = Regex.Match(
            texto ?? string.Empty,
            @"\d+");

        if (!match.Success)
            return false;

        return int.TryParse(match.Value, out valor);
    }

    private static bool TryParseDuracionMeses(
        string? texto,
        out int meses)
    {
        meses = 0;

        if (string.IsNullOrWhiteSpace(texto))
            return false;

        var t = NormalizarTexto(texto);
        const string numero =
            @"(?:\d+|UN|UNA|UNO|DOS|TRES|CUATRO|CINCO|SEIS|SIETE|OCHO|NUEVE|DIEZ|ONCE|DOCE)";

        var anos = 0;
        var mesesAdicionales = 0;
        var encontroUnidad = false;

        var matchAnos = Regex.Match(
            t,
            $@"(?<n>{numero})\s*(ANO|ANOS)",
            RegexOptions.IgnoreCase);

        if (matchAnos.Success
            && TryParseNumeroDuracion(matchAnos.Groups["n"].Value, out var nAnos))
        {
            anos = nAnos;
            encontroUnidad = true;
        }

        var matchMeses = Regex.Match(
            t,
            $@"(?<n>{numero})\s*(MES|MESES)",
            RegexOptions.IgnoreCase);

        if (matchMeses.Success
            && TryParseNumeroDuracion(matchMeses.Groups["n"].Value, out var nMeses))
        {
            mesesAdicionales = nMeses;
            encontroUnidad = true;
        }

        // Expresiones naturales como "un año y medio" o "año y medio".
        if (Regex.IsMatch(t, @"(ANO|ANOS)\s+Y\s+MEDIO\b", RegexOptions.IgnoreCase))
        {
            if (anos == 0)
                anos = 1;

            mesesAdicionales += 6;
            encontroUnidad = true;
        }
        else if (Regex.IsMatch(t, @"\bMEDIO\s+(ANO|AÑO)\b", RegexOptions.IgnoreCase))
        {
            mesesAdicionales += 6;
            encontroUnidad = true;
        }

        if (encontroUnidad)
        {
            meses = checked(anos * 12 + mesesAdicionales);
            return meses >= 0;
        }

        // Compatibilidad: si manda solo "9", en un paso de antigüedad
        // se interpreta como 9 meses.
        return TryParseEntero(t, out meses);
    }

    private static bool TryParseNumeroDuracion(
        string texto,
        out int valor)
    {
        valor = 0;

        if (int.TryParse(texto, out valor))
            return true;

        valor = NormalizarTexto(texto) switch
        {
            "UN" or "UNA" or "UNO" => 1,
            "DOS" => 2,
            "TRES" => 3,
            "CUATRO" => 4,
            "CINCO" => 5,
            "SEIS" => 6,
            "SIETE" => 7,
            "OCHO" => 8,
            "NUEVE" => 9,
            "DIEZ" => 10,
            "ONCE" => 11,
            "DOCE" => 12,
            _ => -1
        };

        return valor >= 0;
    }

    private static bool TryParseMontoGuaranies(
        string? texto,
        out decimal monto)
    {
        monto = 0m;

        if (string.IsNullOrWhiteSpace(texto))
            return false;

        var t = NormalizarTexto(texto)
            .Replace("GS.", string.Empty)
            .Replace("GS", string.Empty)
            .Replace("G.", string.Empty)
            .Trim();

        var esMil = Regex.IsMatch(t, @"MIL\b", RegexOptions.IgnoreCase);
        var match = Regex.Match(t, @"\d[\d\.\,\s]*");
        if (!match.Success)
            return false;

        var digitos = new string(match.Value.Where(char.IsDigit).ToArray());
        if (!decimal.TryParse(digitos, NumberStyles.None, CultureInfo.InvariantCulture, out monto))
            return false;

        if (esMil && monto < 10000m)
            monto *= 1000m;

        return monto > 0;
    }

    private static bool EsViaGarante(string? via)
    {
        var t = NormalizarTexto(via);
        return t.StartsWith("GARANTE");
    }

    private static bool EsGaranteAunNoDisponible(string? texto)
    {
        var t = NormalizarTexto(texto);

        return t.Contains("TODAVIA NO")
               || t.Contains("AUN NO")
               || t.Contains("NO CONSEGUI")
               || t.Contains("NO HE CONSEGUIDO")
               || t.Contains("NO TENGO GARANTE")
               || t.Contains("NO ENCONTRE")
               || t.Contains("SIGO BUSCANDO");
    }

    private static bool EsGaranteConfirmado(string? texto)
    {
        var t = NormalizarTexto(texto);

        if (EsGaranteAunNoDisponible(t))
            return false;

        return t == "SI"
               || t.StartsWith("SI ")
               || t.Contains("YA TENGO GARANTE")
               || t.Contains("CONSEGUI GARANTE")
               || t.Contains("YA CONSEGUI GARANTE")
               || t.Contains("ME SALE DE GARANTE")
               || t.Contains("YA HABLE")
               || t.Contains("ME CONFIRMO")
               || t.Contains("ACEPTO SER GARANTE")
               || (t.Contains("GARANTE") && t.Contains("ACEPTO"));
    }

    private static bool EsMensajeEsperaGarante(string? texto)
    {
        var t = NormalizarTexto(texto);
        if (!t.Contains("GARANTE"))
            return false;

        return t.Contains("HABLAR")
               || t.Contains("HABLARE")
               || t.Contains("VOY A HABLAR")
               || t.Contains("CONSULTAR")
               || t.Contains("VOY A CONSULTAR")
               || t.Contains("PREGUNTAR")
               || t.Contains("LE VOY A PREGUNTAR")
               || t.Contains("VER SI")
               || t.Contains("VOY A VER")
               || t.Contains("VER CON")
               || t.Contains("CONSULTO Y TE AVISO")
               || t.Contains("DESPUES TE AVISO")
               || t.Contains("TE AVISO")
               || t.Contains("TE CONFIRMO");
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ContinuarDespuesDocumentosCredito(
        SolicitudMotoProcesoDto solicitud,
        string? mensajeAnterior = null)
    {
        var actual = await _repository.ObtenerActivaPorConversacion(solicitud.IdConversacion)
                     ?? solicitud;

        var prefijo = string.IsNullOrWhiteSpace(mensajeAnterior)
            ? string.Empty
            : mensajeAnterior.Trim() + "\n\n";

        if (!EsNombrePersonaValido($"{actual.Nombre} {actual.Apellido}".Trim()))
        {
            await _repository.ActualizarPaso("CREDITO", actual.IdSolicitud, "NOMBRE_COMPLETO");
            return Resultado(
                actual,
                prefijo + "Antes de terminar necesito tu nombre y apellido completo.",
                "DOCUMENTACION",
                "NOMBRE_COMPLETO");
        }

        if (string.IsNullOrWhiteSpace(actual.Cedula))
        {
            await _repository.ActualizarPaso("CREDITO", actual.IdSolicitud, "CEDULA_NUMERO");
            return Resultado(
                actual,
                prefijo + "Antes de terminar necesito tu número de cédula.",
                "DOCUMENTACION",
                "CEDULA_NUMERO");
        }

        return await PrepararAutorizacion(actual, mensajeAnterior);
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ContinuarDatosTitularConRevision(
        SolicitudMotoProcesoDto solicitud,
        string motivo)
    {
        await _repository.GuardarResultadoPreEvaluacion(
            solicitud.IdSolicitud,
            "PENDIENTE_REVISION",
            motivo,
            solicitud.ViaEvaluacion,
            "NOMBRE_COMPLETO");

        await _repository.ActualizarPaso(
            "CREDITO",
            solicitud.IdSolicitud,
            "NOMBRE_COMPLETO");

        return Resultado(
            solicitud,
            "No hay problema. Voy a dejar esa parte pendiente para que la revise el equipo de Créditos y seguimos completando tu solicitud. Pasame tu nombre y apellido completo y tu número de cédula. Si preferís, podés enviarlos en mensajes separados.",
            "DOCUMENTACION",
            "NOMBRE_COMPLETO");
    }

    private async Task<SolicitudMotoProcesoResultadoDto> IrAReferenciasPersonales(
        SolicitudMotoProcesoDto solicitud)
    {
        await _repository.ActualizarPaso(
            "CREDITO",
            solicitud.IdSolicitud,
            "REF_FAMILIAR_1");

        return Resultado(
            solicitud,
            "Gracias. Ahora necesito algunas referencias personales. Pasame la primera referencia familiar: nombre, teléfono y parentesco.",
            "DOCUMENTACION",
            "REF_FAMILIAR_1");
    }

    private async Task<SolicitudMotoProcesoResultadoDto> IrADocumentos(
        SolicitudMotoProcesoDto solicitud,
        string? mensajeAnterior = null)
    {
        await _repository.ActualizarPaso(
            "CREDITO",
            solicitud.IdSolicitud,
            "ESTADO_CEDULA");

        var prefijo = string.IsNullOrWhiteSpace(mensajeAnterior)
            ? string.Empty
            : mensajeAnterior.Trim() + "\n\n";

        return Resultado(
            solicitud,
            prefijo + "Ya estamos terminando. Antes de pedirte las fotos, confirmame si tu cédula está vigente y no vencida.",
            "DOCUMENTACION",
            "ESTADO_CEDULA");
    }

    private async Task<SolicitudMotoProcesoResultadoDto> SaltarPasoLaboralAntiguo(
        SolicitudMotoProcesoDto solicitud)
    {
        return await IrAReferenciasPersonales(solicitud);
    }

    private static bool EsDatoNoDisponible(string? valor)
    {
        var t = NormalizarTexto(valor);

        if (string.IsNullOrWhiteSpace(t))
            return false;

        return t == "NO"
               || t.Contains("NO TENGO")
               || t.Contains("NO SE")
               || t.Contains("NO RECUERDO")
               || t.Contains("NO ME ACUERDO")
               || t.Contains("NO TENGO A MANO")
               || t.Contains("NO TENGO AHORA")
               || t.Contains("NO PUEDO AHORA")
               || t.Contains("DESPUES")
               || t.Contains("MÁS TARDE")
               || t.Contains("MAS TARDE")
               || t.Contains("NINGUNA")
               || t.Contains("NINGUNO");
    }

    private static bool EsAutorizacionNatural(string? valor)
    {
        var t = NormalizarTexto(valor);

        if (string.IsNullOrWhiteSpace(t))
            return false;

        return EsSi(t)
               || t.Contains("AUTORIZO")
               || t.Contains("DE ACUERDO")
               || t.Contains("ACEPTO")
               || t == "OK"
               || t == "DALE"
               || t.Contains("PODES CONTINUAR")
               || t.Contains("PUEDE CONTINUAR")
               || t.Contains("CONTINUA");
    }

    private static bool TryParseNombreYCedulaFlexible(
        string? texto,
        out string nombre,
        out string cedula)
    {
        nombre = string.Empty;
        cedula = string.Empty;

        if (string.IsNullOrWhiteSpace(texto))
            return false;

        var contenido = Regex.Replace(texto.Trim(), @"\s+", " ");

        // Primero conservamos compatibilidad con el formato Nombre | CI.
        if (TryParseIdentidad(contenido, out var nombreSeparado, out var cedulaSeparada)
            && EsNombrePersonaValido(nombreSeparado))
        {
            nombre = nombreSeparado.Trim();
            cedula = SoloDigitos(cedulaSeparada);
            return cedula.Length is >= 5 and <= 10;
        }

        // También aceptamos formas naturales como:
        // "Juan Pérez 4.123.456" o "Juan Pérez, CI 4.123.456".
        var matches = Regex.Matches(
            contenido,
            @"(?<!\d)(?:\d[\.\-\s]?){5,10}(?!\d)");

        foreach (Match match in matches.Cast<Match>().Reverse())
        {
            var ci = SoloDigitos(match.Value);
            if (ci.Length is < 5 or > 10)
                continue;

            var posibleNombre = contenido
                .Remove(match.Index, match.Length)
                .Replace("CI", "", StringComparison.OrdinalIgnoreCase)
                .Replace("CÉDULA", "", StringComparison.OrdinalIgnoreCase)
                .Replace("CEDULA", "", StringComparison.OrdinalIgnoreCase)
                .Replace(":", " ")
                .Replace(",", " ")
                .Replace("|", " ")
                .Trim();

            posibleNombre = Regex.Replace(posibleNombre, @"\s+", " ");

            if (!EsNombrePersonaValido(posibleNombre))
                continue;

            nombre = posibleNombre;
            cedula = ci;
            return true;
        }

        return false;
    }

    private async Task<string> ObtenerMensajeFlujo(
        string codigo,
        string fallback)
    {
        try
        {
            var mensaje = await _repository.ObtenerPromptFlujo(codigo);
            var seleccionado = string.IsNullOrWhiteSpace(mensaje)
                ? fallback
                : mensaje.Trim();

            return QuitarEmojisFlujo(seleccionado);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "No se pudo obtener mensaje configurable de flujo. Codigo={Codigo}",
                codigo);

            return fallback;
        }
    }

    private static string QuitarEmojisFlujo(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return texto;

        return texto
            .Replace("\U0001F60A", string.Empty)
            .Replace("\u2705", string.Empty)
            .Replace("\U0001F449", string.Empty)
            .Replace("\u23F3", string.Empty)
            .Replace("\U0001F4F7", string.Empty)
            .Replace("\U0001F4C4", string.Empty)
            .Replace("\U0001F44B", string.Empty)
            .Replace("\U0001F3CD\uFE0F", string.Empty)
            .Replace("\U0001F3CD", string.Empty)
            .Replace("\U0001F525", string.Empty)
            .Replace("\U0001F44D", string.Empty)
            .Replace("\U0001F609", string.Empty)
            .Replace("\U0001F642", string.Empty)
            .Replace("\U0001F600", string.Empty)
            .Replace("  ", " ")
            .Trim();
    }

    private static bool TryParseAutorizacion(
        string texto,
        out string nombre,
        out string cedula)
    {
        nombre = string.Empty;
        cedula = string.Empty;

        var normalizado = NormalizarTexto(texto);
        if (!normalizado.Contains("OK AUTORIZO")) return false;

        var matchNombre = Regex.Match(
            texto,
            @"Nombre\s*y\s*Apellido\s*:\s*(.+)",
            RegexOptions.IgnoreCase);

        var matchCedula = Regex.Match(
            texto,
            @"(?:N\.?\s*[°ºo]?\s*de\s*C[eé]dula|C[eé]dula|CI)\s*:\s*([0-9\.\-\s]+)",
            RegexOptions.IgnoreCase);

        if (!matchNombre.Success || !matchCedula.Success) return false;

        nombre = matchNombre.Groups[1].Value.Trim();
        cedula = matchCedula.Groups[1].Value.Trim();

        return nombre.Length >= 4 && SoloDigitos(cedula).Length >= 5;
    }

    private static bool MismoNombre(string registrado, string recibido)
        => NormalizarTexto(registrado) == NormalizarTexto(recibido);

    private async Task<SolicitudMotoProcesoResultadoDto> ResponderEstadoSolicitud(
        SolicitudMotoProcesoDto solicitud)
    {
        var actual = await _repository.ObtenerActivaPorConversacion(solicitud.IdConversacion)
                     ?? solicitud;

        if (string.Equals(actual.Estado, "LISTA_REVISION", StringComparison.OrdinalIgnoreCase)
            || string.Equals(actual.PasoActual, "LISTA_REVISION", StringComparison.OrdinalIgnoreCase))
        {
            return Resultado(
                actual,
                "Tu solicitud ya está registrada para revisión con el equipo de Créditos. Si necesitamos completar algún dato, te vamos a contactar.",
                "LISTA_REVISION",
                "LISTA_REVISION");
        }

        // No mostramos checklists largos. El cliente solamente necesita saber
        // qué sigue ahora; el detalle completo queda para el CRM interno.
        return Resultado(
            actual,
            "Sí, tengo tu solicitud guardada. Seguimos desde donde quedamos. " +
            ObtenerIndicacionPasoActual(actual.PasoActual),
            actual.Estado,
            actual.PasoActual);
    }

    private static string ObtenerPasoCorrectoSegunReferencias(
        string pasoActual,
        int familiares,
        int amigos,
        int comerciales,
        bool autorizacion,
        ReglaCreditoMotoDto regla)
    {
        var paso = (pasoActual ?? string.Empty).Trim().ToUpperInvariant();

        var esZonaReferencias =
            paso == "REF_FAMILIAR_1"
            || paso == "REF_FAMILIAR_2"
            || paso == "REF_AMIGO"
            || paso == "REF_COMERCIAL_CONTROL"
            || paso == "REF_COMERCIAL_DATOS"
            || paso == "REF_COMERCIAL_MAS"
            || paso == "AUTORIZACION";

        if (!esZonaReferencias)
            return pasoActual;

        if (familiares < regla.ReferenciasFamiliaresMinimas)
            return familiares == 0 ? "REF_FAMILIAR_1" : "REF_FAMILIAR_2";

        if (amigos < regla.ReferenciasAmigosMinimas)
            return "REF_AMIGO";

        if (!autorizacion)
        {
            // La referencia comercial ya forma parte de la preevaluación inicial
            // cuando corresponde. Después de completar las referencias personales
            // pasamos directamente a la autorización y no preguntamos dos veces.
            return "AUTORIZACION";
        }

        return pasoActual;
    }

    private static string ObtenerIndicacionPasoActual(string? pasoActual)
    {
        return (pasoActual ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "PRECALIFICACION_IPS" =>
                "Necesito confirmar si actualmente aportás a IPS.",
            "PRECALIFICACION_APORTES" =>
                "Decime aproximadamente cuántos aportes de IPS tenés.",
            "REF_COMERCIAL_EXISTE" or "REF_COMERCIAL_PRECALIFICACION" =>
                "Necesito saber si tenés alguna referencia comercial o compra a cuotas anterior.",
            "REF_COMERCIAL_NOMBRE" =>
                "Pasame el nombre del negocio o casa comercial de tu referencia.",
            "REF_COMERCIAL_ANTIGUEDAD" =>
                "Decime aproximadamente cuánto tiempo pagaste esa referencia.",
            "REF_COMERCIAL_CUOTA" =>
                "Decime aproximadamente de cuánto era la cuota de esa referencia.",
            "PRECALIFICACION_GARANTE" =>
                "Necesito saber si contás con una persona que pueda salirte de garante.",
            "GARANTE_IPS" =>
                "Necesito confirmar si el garante aporta a IPS.",
            "GARANTE_APORTES" =>
                "Decime aproximadamente cuántos aportes de IPS tiene el garante.",
            "GARANTE_REF_EXISTE" =>
                "Necesito saber si el garante tiene alguna referencia comercial.",
            "GARANTE_REF_NOMBRE" =>
                "Pasame el nombre del negocio o casa comercial de la referencia del garante.",
            "GARANTE_REF_ANTIGUEDAD" =>
                "Decime aproximadamente cuánto tiempo tiene esa referencia del garante.",
            "GARANTE_REF_CUOTA" =>
                "Decime aproximadamente de cuánto era la cuota de esa referencia del garante.",
            "NOMBRE_COMPLETO" =>
                "Pasame tu nombre y apellido completo y tu número de cédula; si preferís, podés enviarlos por separado.",
            "CORREGIR_NOMBRE_TITULAR" =>
                "Pasame tu nombre y apellido completo para corregir el dato.",
            "CEDULA_NUMERO" =>
                "Pasame tu número de cédula.",
            "DOMICILIO_CIUDAD" =>
                "Decime en qué ciudad vivís.",
            "DOMICILIO_BARRIO" =>
                "Decime en qué barrio vivís.",
            "DOMICILIO_DIRECCION" =>
                "Pasame tu dirección particular o una referencia de tu domicilio.",
            "LABORAL_EMPRESA" =>
                "Decime dónde trabajás actualmente.",
            "LABORAL_TELEFONO" =>
                "Si tenés a mano, pasame un teléfono de la empresa. Si no lo tenés, seguimos igual.",
            "REF_FAMILIAR_1" or "REF_FAMILIAR_2" =>
                "Pasame una referencia familiar: nombre, teléfono y parentesco.",
            "REF_AMIGO" =>
                "Pasame una referencia de amistad: nombre y teléfono.",
            "ESTADO_CEDULA" =>
                "Necesito confirmar si tu cédula está vigente y no vencida.",
            "CEDULA_FRENTE" =>
                "Enviame una foto clara del frente de tu cédula.",
            "CEDULA_DORSO" =>
                "Enviame una foto clara del dorso de tu cédula.",
            "AUTORIZACION" =>
                "Estamos en la autorización final. Podés responder de forma natural si autorizás o no.",
            "LISTA_REVISION" =>
                "La solicitud ya está registrada para revisión.",
            _ =>
                "Seguimos con el próximo dato de tu solicitud."
        };
    }

    private static bool EsNombrePersonaValido(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return false;

        var original = Regex.Replace(valor.Trim(), @"\s+", " ");
        var normalizado = NormalizarTexto(original);

        if (original.Length < 5 || original.Length > 80)
            return false;

        if (SoloDigitos(original).Length > 0)
            return false;

        if (original.Contains('?') || original.Contains('¿')
            || original.Contains('!') || original.Contains('¡')
            || original.Contains(':') || original.Contains(';'))
            return false;

        var partes = original
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Pedimos por lo menos nombre + apellido y evitamos que una frase larga
        // de conversación termine guardada como identidad del titular.
        if (partes.Length < 2 || partes.Length > 6)
            return false;

        // Cada palabra debe parecer parte de un nombre. Permitimos tildes, ñ,
        // apóstrofe y guion para no restringir nombres reales.
        if (partes.Any(p => !Regex.IsMatch(
                p,
                @"^[\p{L}][\p{L}'’\-]*$",
                RegexOptions.CultureInvariant)))
            return false;

        var frasesNoNombre = new[]
        {
            "HOLA", "BUEN DIA", "BUENAS", "BUENAS TARDES", "BUENAS NOCHES",
            "COMO SE LLAMA", "CUAL ES", "QUIEN ES", "DONDE QUEDAMOS",
            "EN QUE PARTE", "QUE NECESITAS", "QUE NECESITO", "QUE ME FALTA",
            "TE PASO", "TE ENVIO", "CONTINUAMOS", "SIGAMOS", "REFERENCIAS PERSONALES",
            "TENGO QUE", "TENEMOS QUE", "ESTA MAL", "NO ES UN NOMBRE"
        };

        return !frasesNoNombre.Any(x => normalizado.Contains(x));
    }

    private static bool EsConsultaDatoRegistrado(string? mensaje)
    {
        var texto = NormalizarTexto(mensaje);

        if (string.IsNullOrWhiteSpace(texto))
            return false;

        var consultaNombreTitular =
            texto.Contains("COMO SE LLAMA EL TITULAR")
            || texto.Contains("CUAL ES EL NOMBRE DEL TITULAR")
            || texto.Contains("CUAL ES EL TITULAR")
            || texto.Contains("QUIEN ES EL TITULAR")
            || texto.Contains("QUE NOMBRE PUSE")
            || texto.Contains("QUE NOMBRE DI")
            || texto.Contains("CUAL ES MI NOMBRE REGISTRADO")
            || texto.Contains("NOMBRE REGISTRADO")
            || texto == "NOMBRE DEL TITULAR";

        var consultaCedula =
            texto.Contains("CUAL ES LA CEDULA REGISTRADA")
            || texto.Contains("CUAL ES MI CEDULA")
            || texto.Contains("QUE CEDULA PUSE")
            || texto.Contains("QUE CEDULA DI")
            || texto.Contains("NUMERO DE CEDULA REGISTRADO")
            || texto.Contains("CI REGISTRADA")
            || texto.Contains("CUAL ES LA CI")
            || texto.Contains("CUAL ES MI CI");

        return consultaNombreTitular || consultaCedula;
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ResponderDatoRegistrado(
        SolicitudMotoProcesoDto solicitud,
        string? mensaje)
    {
        var actual = await _repository.ObtenerActivaPorConversacion(solicitud.IdConversacion)
                     ?? solicitud;

        var texto = NormalizarTexto(mensaje);
        var nombreCompleto = $"{actual.Nombre} {actual.Apellido}".Trim();
        var cedula = SoloDigitos(actual.Cedula ?? string.Empty);

        var preguntaNombre =
            texto.Contains("TITULAR")
            || texto.Contains("NOMBRE PUSE")
            || texto.Contains("NOMBRE DI")
            || texto.Contains("NOMBRE REGISTRADO");

        var preguntaCedula =
            texto.Contains("CEDULA")
            || texto.Contains("CI REGISTRADA")
            || texto.Contains("CUAL ES LA CI")
            || texto.Contains("CUAL ES MI CI");

        string respuesta;

        if (preguntaNombre)
        {
            if (!EsNombrePersonaValido(nombreCompleto))
            {
                await _repository.ActualizarPaso(
                    actual.TipoOperacion,
                    actual.IdSolicitud,
                    "CORREGIR_NOMBRE_TITULAR");

                return Resultado(
                    actual,
                    "Detecté que el nombre del titular quedó mal guardado en una prueba anterior. No voy a tomar ese texto como nombre. Enviame ahora solamente tu nombre y apellido completo y lo corrijo sin perder los demás datos de la solicitud.",
                    actual.Estado,
                    "CORREGIR_NOMBRE_TITULAR");
            }

            respuesta = $"El titular registrado en tu solicitud es: {nombreCompleto}.";
        }
        else if (preguntaCedula)
        {
            respuesta = string.IsNullOrWhiteSpace(cedula)
                ? "Todavía no tengo registrada la Cédula de Identidad del titular."
                : $"La Cédula de Identidad registrada en tu solicitud es: {cedula}.";
        }
        else
        {
            respuesta = "Decime qué dato de tu solicitud querés consultar y te indico lo que ya está registrado.";
        }

        if (string.Equals(actual.PasoActual, "AUTORIZACION", StringComparison.OrdinalIgnoreCase))
        {
            respuesta +=
                "\n\nSeguimos en la autorización final. Si estás de acuerdo, podés responder de forma natural, por ejemplo: Sí, autorizo.";
        }

        return Resultado(
            actual,
            respuesta,
            actual.Estado,
            actual.PasoActual);
    }

    private static bool EsConsultaEstadoSolicitud(string? mensaje)
    {
        var texto = NormalizarTexto(mensaje);

        if (string.IsNullOrWhiteSpace(texto))
            return false;

        // Si ya existe una solicitud activa, un saludo o una pregunta de estado
        // NO debe procesarse como si fuera el dato esperado del paso actual.
        // El cliente puede escribir de forma natural: "hola que tal", "qué me falta",
        // "cuántas referencias tenés", "dónde quedamos", etc.
        var esSaludo =
            texto == "HOLA"
            || texto.StartsWith("HOLA ")
            || texto == "BUEN DIA"
            || texto.StartsWith("BUEN DIA ")
            || texto == "BUENAS"
            || texto.StartsWith("BUENAS ")
            || texto == "BUENAS TARDES"
            || texto == "BUENAS NOCHES";

        if (esSaludo)
            return true;

        var consultaRetoma =
            texto.Contains("DONDE QUEDAMOS")
            || texto.Contains("EN QUE PARTE QUEDAMOS")
            || texto.Contains("POR DONDE QUEDAMOS")
            || texto.Contains("DONDE ESTAMOS")
            || texto.Contains("EN QUE PARTE ESTAMOS")
            || texto.Contains("RETOMEMOS")
            || texto.Contains("CONTINUEMOS")
            || texto == "CONTINUAR"
            || texto == "SIGAMOS";

        var consultaFaltantes =
            texto.Contains("QUE ME FALTA")
            || texto.Contains("QUE TE FALTA")
            || texto.Contains("QUE FALTA")
            || texto.Contains("QUE FALTAN")
            || texto.Contains("QUE DATOS FALTAN")
            || texto.Contains("CUANTO FALTA")
            || texto.Contains("CUANTOS FALTAN")
            || texto.Contains("CUANTAS FALTAN");

        var consultaGuardado =
            texto.Contains("QUE YA TENES")
            || texto.Contains("QUE YA TENEMOS")
            || texto.Contains("QUE TENES MIO")
            || texto.Contains("CUANTO YA TENES")
            || texto.Contains("CUANTOS YA TENES")
            || texto.Contains("CUANTAS YA TENES")
            || texto.Contains("CUANTOS TENES")
            || texto.Contains("CUANTAS TENES")
            || texto.Contains("CUANTAS REFERENCIAS")
            || texto.Contains("CUANTOS DATOS")
            || texto.Contains("QUE REFERENCIAS TENES");

        return consultaRetoma || consultaFaltantes || consultaGuardado;
    }

    private static bool EsCancelar(string mensaje)
    {
        var t = NormalizarTexto(mensaje);

        if (string.IsNullOrWhiteSpace(t))
            return false;

        if (
            t == "CANCELAR"
            || t.Contains("CANCELAR SOLICITUD")
            || t.Contains("CANCELA LA SOLICITUD")
            || t.Contains("CANCELA MI SOLICITUD")
            || t.Contains("CANCELAME LA SOLICITUD")
            || t.Contains("ANULAR SOLICITUD")
            || t.Contains("ANULA LA SOLICITUD")
        )
        {
            return true;
        }

        /*
         * El cliente puede desistir con lenguaje natural y no necesariamente
         * diciendo "cancelar solicitud". Estas frases deben cortar la máquina
         * de estados antes de que el texto sea interpretado como el dato que
         * Panambí está esperando (domicilio, empresa, referencia, etc.).
         */
        var desistimientosDirectos = new[]
        {
            "YA NO QUIERO",
            "NO QUIERO MAS",
            "NO QUIERO LA MOTO",
            "NO QUIERO MOTO",
            "NO QUIERO MOTOS",
            "NO QUIERO ESA MOTO",
            "YA NO QUIERO LA MOTO",
            "YA NO QUIERO MOTO",
            "YA NO QUIERO MOTOS",
            "NO ME INTERESA LA MOTO",
            "YA NO ME INTERESA LA MOTO",
            "NO ME INTERESA MAS",
            "YA NO ME INTERESA",
            "NO VOY A COMPRAR",
            "YA NO VOY A COMPRAR",
            "DEJEMOS NOMAS",
            "DEJA NOMAS",
            "DEJALO NOMAS",
            "NO QUIERO SEGUIR CON LA SOLICITUD",
            "NO QUIERO CONTINUAR CON LA SOLICITUD",
            "NO QUIERO SEGUIR CON EL CREDITO",
            "NO QUIERO CONTINUAR CON EL CREDITO",
            "NO QUIERO EL CREDITO",
            "YA NO QUIERO EL CREDITO"
        };

        return desistimientosDirectos.Any(
            frase =>
                t == frase
                || t.StartsWith(frase + " ")
                || t.Contains(" " + frase + " ")
                || t.EndsWith(" " + frase));
    }

    private static bool EsTipo(string actual, string esperado)
        => string.Equals(actual, esperado, StringComparison.OrdinalIgnoreCase);

    private static bool EsSinReferenciaComercial(string? texto)
    {
        var t = NormalizarTexto(texto);

        if (string.IsNullOrWhiteSpace(t))
            return false;

        return t == "NO"
               || t.StartsWith("NO TENGO")
               || t.Contains("NO TENGO REFERENCIA")
               || t.Contains("NO TENGO REFERENCIAS")
               || t.Contains("NO TENGO COMERCIAL")
               || t.Contains("NO TENGO COMERCIALES")
               || t.Contains("NO CUENTO CON REFERENCIA")
               || t.Contains("NO CUENTO CON REFERENCIAS")
               || t.Contains("NO POSEO REFERENCIA")
               || t.Contains("NO POSEO REFERENCIAS")
               || t == "NINGUNA"
               || t == "NINGUNO"
               || t.StartsWith("NINGUNA ")
               || t.StartsWith("NINGUNO ")
               || t.Contains("NO DISPONGO DE REFERENCIA")
               || t.Contains("NO DISPONGO DE REFERENCIAS");
    }

    private static bool EsSi(string texto)
    {
        var t = NormalizarTexto(texto);

        if (string.IsNullOrWhiteSpace(t) || EsNo(t))
            return false;

        return t == "SI"
               || t.StartsWith("SI ")
               || t.Contains("CLARO")
               || t.Contains("CORRECTO")
               || t.Contains("ASI ES")
               || t.Contains("EFECTIVAMENTE")
               || t.Contains("APORTO")
               || t.Contains("TENGO IPS")
               || t.Contains("TENGO SEGURO")
               || t.Contains("CUENTO CON IPS")
               || t.Contains("POSEO IPS")
               || (t.Contains("TENGO") && !t.Contains("NO TENGO"));
    }

    private static bool EsNo(string texto)
    {
        var t = NormalizarTexto(texto);

        return t == "NO"
               || t.StartsWith("NO ")
               || t.Contains("NO TENGO")
               || t.Contains("NO APORTO")
               || t.Contains("NO CUENTO")
               || t.Contains("NO POSEO")
               || t.Contains("SIN IPS")
               || t.Contains("NO DISPONGO");
    }

    private static string SoloDigitos(string texto)
        => new((texto ?? string.Empty).Where(char.IsDigit).ToArray());

    private static string NormalizarTexto(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return string.Empty;

        var formD = texto.Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();

        foreach (var c in formD)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }

        return Regex.Replace(sb.ToString().Normalize(NormalizationForm.FormC), @"\s+", " ").Trim();
    }

    private static SolicitudMotoProcesoResultadoDto Resultado(
        SolicitudMotoProcesoDto solicitud,
        string respuesta,
        string? estado = null,
        string? paso = null)
        => new()
        {
            Manejado = true,
            IdSolicitud = solicitud.IdSolicitud,
            TipoOperacion = solicitud.TipoOperacion,
            Respuesta = respuesta,
            Estado = estado ?? solicitud.Estado,
            PasoActual = paso ?? solicitud.PasoActual
        };
}
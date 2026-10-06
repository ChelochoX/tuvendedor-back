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
    private const string VERSION_AUTORIZACION = "2026-09";

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
                "Ya tenemos una solicitud en proceso 😊. Sigamos desde donde quedamos.");
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
                "¿Actualmente aportás a IPS? 😊");

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
                "Perfecto 😊 Para la compra al contado necesitamos una Cédula de Identidad paraguaya (CI) vigente, es decir, no vencida. Te voy guiando paso a paso.\n\n¿Cuál es tu nombre y apellido?"
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
                "Listo 😊 cancelé esta solicitud. Cuando quieras podemos comenzar una nueva.",
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
        return solicitud.PasoActual.ToUpperInvariant() switch
        {
            "PRECALIFICACION_IPS" => await ProcesarAportaIps(solicitud, request),
            "PRECALIFICACION_APORTES" => await ProcesarCantidadAportesIps(solicitud, request),
            "REF_COMERCIAL_PRECALIFICACION" => await ProcesarReferenciaComercialExiste(solicitud, request, false),
            "REF_COMERCIAL_EXISTE" => await ProcesarReferenciaComercialExiste(solicitud, request, false),
            "REF_COMERCIAL_NOMBRE" => await ProcesarReferenciaComercialNombre(solicitud, request, false),
            "REF_COMERCIAL_ANTIGUEDAD" => await ProcesarReferenciaComercialAntiguedad(solicitud, request, false),
            "REF_COMERCIAL_CUOTA" => await ProcesarReferenciaComercialCuota(solicitud, request, false),
            "PRECALIFICACION_GARANTE" => await ProcesarGarante(solicitud, request),
            "ESPERANDO_GARANTE" => await ProcesarEsperaGarante(solicitud, request),
            "GARANTE_IPS" => await ProcesarGaranteIps(solicitud, request),
            "GARANTE_APORTES" => await ProcesarGaranteAportes(solicitud, request),
            "GARANTE_REF_EXISTE" => await ProcesarReferenciaComercialExiste(solicitud, request, true),
            "GARANTE_REF_NOMBRE" => await ProcesarReferenciaComercialNombre(solicitud, request, true),
            "GARANTE_REF_ANTIGUEDAD" => await ProcesarReferenciaComercialAntiguedad(solicitud, request, true),
            "GARANTE_REF_CUOTA" => await ProcesarReferenciaComercialCuota(solicitud, request, true),
            "PENDIENTE_CREDITOS" => await ProcesarPendienteCreditos(solicitud),
            "PRECALIFICACION_EDAD" => await ProcesarEdad(solicitud, request),
            "PRECALIFICACION_ANTIGUEDAD" => await ProcesarAntiguedadLaboral(solicitud, request),
            "NOMBRE_COMPLETO" => await ProcesarNombreCompleto(solicitud, request),
            "CORREGIR_NOMBRE_TITULAR" => await ProcesarCorreccionNombreTitular(solicitud, request),
            "CEDULA_NUMERO" => await ProcesarNumeroCedula(solicitud, request),
            "ESTADO_CEDULA" => await ProcesarEstadoCedula(solicitud, request),
            "CEDULA_FRENTE" => await ProcesarDocumento(
                solicitud,
                request,
                "CEDULA_FRENTE",
                "Perfecto, ya recibí el frente de tu CI ✅. Ahora enviame una foto clara del DORSO de tu Cédula de Identidad paraguaya.",
                "CEDULA_DORSO"),
            "CEDULA_DORSO" => await ProcesarDorsoCedula(solicitud, request),
            "DOMICILIO_CIUDAD" => await ProcesarCiudad(solicitud, request),
            "DOMICILIO_BARRIO" => await ProcesarBarrio(solicitud, request),
            "DOMICILIO_DIRECCION" => await ProcesarDireccionDomicilio(solicitud, request),
            "LABORAL_EMPRESA" => await ProcesarEmpresa(solicitud, request),
            "LABORAL_DIRECCION" => await ProcesarDireccionEmpresa(solicitud, request),
            "LABORAL_TELEFONO" => await ProcesarTelefonoEmpresa(solicitud, request),
            "LABORAL_TELEFONO_ES_MOVIL" => await ProcesarTelefonoEmpresaEsMovil(solicitud, request),
            "LABORAL_JEFE_ENCARGADO" => await ProcesarJefeEncargado(solicitud, request),
            "REF_FAMILIAR_1" => await ProcesarReferenciaPersonal(
                solicitud,
                request,
                "FAMILIAR",
                "REF_FAMILIAR_2",
                "Primera referencia familiar guardada ✅. Ahora pasame la segunda referencia familiar. Ejemplo: Cesar Leite, 0982 33 44 55, hno."),
            "REF_FAMILIAR_2" => await ProcesarReferenciaPersonal(
                solicitud,
                request,
                "FAMILIAR",
                "REF_AMIGO",
                "Segunda referencia familiar guardada ✅. Ahora pasame la referencia de amistad. Ejemplo: Juan Perez, 0981 11 22 33."),
            "REF_AMIGO" => await ProcesarReferenciaPersonal(
                solicitud,
                request,
                "AMIGO",
                "REF_COMERCIAL_CONTROL",
                "Perfecto ✅ Ya tenemos las 3 referencias personales requeridas."),
            // Compatibilidad con solicitudes que quedaron en pasos antiguos:
            // la referencia comercial ya se valida al inicio de la preevaluación,
            // por lo tanto no la volvemos a pedir al final.
            "REF_COMERCIAL_CONTROL" => await PrepararReferenciasComerciales(solicitud),
            "REF_COMERCIAL_DATOS" => await PrepararReferenciasComerciales(solicitud),
            "REF_COMERCIAL_MAS" => await PrepararReferenciasComerciales(solicitud),
            "AUTORIZACION" => await ProcesarAutorizacion(solicitud, request),
            _ => Resultado(
                solicitud,
                "Sigamos con tu solicitud 😊. Si querés cancelar, escribí 'cancelar solicitud'.")
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
                "Perfecto, ya recibí el frente de tu CI ✅. Ahora enviame una foto clara del DORSO de tu Cédula de Identidad paraguaya.",
                "CEDULA_DORSO"),
            "CEDULA_DORSO" => await ProcesarDorsoCedula(solicitud, request),
            _ => Resultado(
                solicitud,
                "Sigamos con la documentación de tu compra al contado 😊.")
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
                "No llegué a interpretar la fecha 😊 Enviame tu fecha de nacimiento en formato DD/MM/AAAA.");

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
            "Edad validada ✅. ¿Cuánto tiempo de antigüedad tenés en tu trabajo actual? Podés responder, por ejemplo: 9 meses, 1 año o 3 años.");

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
                "No llegué a interpretar el tiempo 😊 Podés responder, por ejemplo: 9 meses, 1 año, 1 año y 6 meses o 3 años.");

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
                await _repository.GuardarResultadoPreEvaluacion(
                    solicitud.IdSolicitud,
                    "PENDIENTE_CREDITOS",
                    motivo,
                    solicitud.ViaEvaluacion,
                    "PENDIENTE_CREDITOS");

                var pendiente = await ObtenerMensajeFlujo(
                    "CREDITO_PENDIENTE_CREDITOS",
                    "Gracias por los datos 😊. Voy a dejar tu caso para consultar con el equipo de Créditos y te daremos un retorno en la brevedad para confirmar si podemos avanzar.");

                return Resultado(
                    solicitud,
                    pendiente,
                    "PRE_EVALUACION",
                    "PENDIENTE_CREDITOS");
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
                    "¿Actualmente aportás a IPS? 😊"),
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
            "Perfecto ✅ La evaluación inicial está bien. Ahora sí vamos a completar tus datos. ¿Cuál es tu nombre y apellido?");

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
            var msg = await ObtenerMensajeFlujo(
                "CREDITO_IPS_NO_ENTENDIDO",
                "No llegué a entenderte 😊 ¿Actualmente aportás a IPS?");

            return Resultado(solicitud, msg);
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

            var msg = await ObtenerMensajeFlujo(
                "CREDITO_PREGUNTA_APORTES_IPS",
                "Perfecto 😊 ¿Cuántos aportes de IPS tenés actualmente? Podés responder, por ejemplo: 8 aportes.");

            return Resultado(
                solicitud,
                msg,
                "PRE_EVALUACION",
                "PRECALIFICACION_APORTES");
        }

        await _repository.GuardarResultadoPreEvaluacion(
            solicitud.IdSolicitud,
            "PENDIENTE_REFERENCIA_COMERCIAL",
            "No aporta IPS.",
            "REFERENCIA_COMERCIAL",
            "REF_COMERCIAL_EXISTE");

        var respuesta = await ObtenerMensajeFlujo(
            "CREDITO_PREGUNTA_REFERENCIA_COMERCIAL",
            "Está bien 😊 Si no tenés IPS, podemos hacer una evaluación rápida con una referencia comercial verificable. ¿Tenés alguna casa comercial o negocio donde hayas pagado cuotas? Respondeme SI o NO.");

        return Resultado(
            solicitud,
            respuesta,
            "PRE_EVALUACION",
            "REF_COMERCIAL_EXISTE");
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarCantidadAportesIps(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        if (!TryParseEntero(request.Mensaje, out var aportes) || aportes < 0)
        {
            var msg = await ObtenerMensajeFlujo(
                "CREDITO_APORTES_INVALIDOS",
                "No llegué a identificar la cantidad 😊 ¿Cuántos aportes de IPS tenés actualmente?");

            return Resultado(solicitud, msg);
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
                "PRECALIFICACION_EDAD");

            var msgEdad = await ObtenerMensajeFlujo(
                "CREDITO_PREGUNTA_EDAD",
                "Perfecto ✅ Por IPS podemos continuar con la evaluación. ¿Cuál es tu fecha de nacimiento? Enviamela en formato DD/MM/AAAA.");

            return Resultado(
                solicitud,
                msgEdad,
                "PRE_EVALUACION",
                "PRECALIFICACION_EDAD");
        }

        await _repository.GuardarResultadoPreEvaluacion(
            solicitud.IdSolicitud,
            "PENDIENTE_REFERENCIA_COMERCIAL",
            $"Cantidad de aportes IPS: {aportes}. Mínimo requerido: {regla.AportesIPSMinimos}.",
            "REFERENCIA_COMERCIAL",
            "REF_COMERCIAL_EXISTE");

        var respuesta = await ObtenerMensajeFlujo(
            "CREDITO_PREGUNTA_REFERENCIA_COMERCIAL",
            "Todavía no alcanzás el mínimo de aportes IPS, pero podemos evaluar una referencia comercial verificable. ¿Tenés alguna casa comercial o negocio donde hayas pagado cuotas? Respondeme SI o NO.");

        return Resultado(
            solicitud,
            respuesta,
            "PRE_EVALUACION",
            "REF_COMERCIAL_EXISTE");
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarPrecalificacionLaboral(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        if (!TryParsePrecalificacionLaboral(
                request.Mensaje,
                out var empresa,
                out var antiguedad,
                out var aportaIps,
                out var aportesIps))
        {
            return Resultado(
                solicitud,
                "Necesito esos datos así 😊: Empresa | Antigüedad en meses | SI/NO aporta IPS | Cantidad de aportes. Ejemplo: Empresa ABC | 8 | SI | 5");
        }

        var regla = await _repository.ObtenerReglaCreditoActiva();

        await _repository.GuardarPrecalificacionLaboral(
            solicitud.IdSolicitud,
            empresa,
            antiguedad,
            aportaIps,
            aportesIps);

        if (antiguedad < regla.AntiguedadLaboralMinMeses)
        {
            var motivo =
                $"Antigüedad laboral {antiguedad} meses. Mínimo requerido {regla.AntiguedadLaboralMinMeses} meses.";

            await _repository.GuardarResultadoPreEvaluacion(
                solicitud.IdSolicitud,
                "NO_VIABLE",
                motivo,
                null,
                "NO_VIABLE");

            return Resultado(
                solicitud,
                $"Gracias. Para que la solicitud pueda avanzar necesitás al menos {regla.AntiguedadLaboralMinMeses} meses de antigüedad laboral. Con los datos actuales no puede continuar por ahora.",
                "NO_VIABLE",
                "NO_VIABLE");
        }

        if (aportaIps && aportesIps >= regla.AportesIPSMinimos)
        {
            await _repository.GuardarResultadoPreEvaluacion(
                solicitud.IdSolicitud,
                "VIABLE",
                null,
                "IPS",
                "IDENTIDAD");

            return Resultado(
                solicitud,
                $"La preevaluación puede continuar ✅. Cumplís la antigüedad laboral y el mínimo de {regla.AportesIPSMinimos} aportes IPS.\n\n" +
                "Ahora enviame: Nombre completo | N.º de cédula.",
                "DOCUMENTACION",
                "IDENTIDAD");
        }

        await _repository.GuardarResultadoPreEvaluacion(
            solicitud.IdSolicitud,
            "PENDIENTE_REFERENCIAS_COMERCIALES",
            "No aporta IPS o no alcanza el mínimo requerido.",
            "REFERENCIAS_COMERCIALES",
            "NOMBRE_COMPLETO");

        return Resultado(
            solicitud,
            $"La antigüedad laboral está bien ✅. Como no contás con al menos {regla.AportesIPSMinimos} aportes IPS, igualmente podemos continuar. Más adelante te voy a consultar si tenés alguna referencia comercial; si no tenés, no bloquea la solicitud. Ahora necesito registrar tus datos como titular de la solicitud. ¿Cuál es tu nombre y apellido?",
            "DOCUMENTACION",
            "NOMBRE_COMPLETO");
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarReferenciaComercialExiste(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request,
        bool esGarante)
    {
        var texto = NormalizarTexto(request.Mensaje);

        if (!EsSi(texto) && !EsNo(texto))
        {
            var codigo = esGarante
                ? "CREDITO_GREF_RESP_INVALIDA"
                : "CREDITO_REF_RESP_INVALIDA";

            var fallback = esGarante
                ? "No llegué a entenderte 😊 ¿El garante tiene alguna referencia comercial verificable?"
                : "No llegué a entenderte 😊 ¿Tenés alguna referencia comercial verificable?";

            return Resultado(
                solicitud,
                await ObtenerMensajeFlujo(codigo, fallback));
        }

        if (EsNo(texto))
        {
            if (esGarante)
            {
                return await PasarAPendienteCreditos(
                    solicitud,
                    "El garante no tiene IPS suficiente ni referencia comercial verificable.");
            }

            await _repository.GuardarDatosGarantePrecalificacion(
                solicitud.IdSolicitud,
                requiereGarante: true,
                estado: "PENDIENTE");

            await _repository.ActualizarPaso(
                "CREDITO",
                solicitud.IdSolicitud,
                "PRECALIFICACION_GARANTE");

            var msg = await ObtenerMensajeFlujo(
                "CREDITO_PREGUNTA_GARANTE",
                "Con estos datos todavía no calificamos para avanzar a sola firma. Si tenés un garante, podemos evaluarlo. ¿Tenés una persona que pueda salirte de garante? Respondeme SI o NO.");

            return Resultado(
                solicitud,
                msg,
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

        var codigoNombre = esGarante
            ? "CREDITO_PREGUNTA_GARANTE_REFERENCIA_NOMBRE"
            : "CREDITO_PREGUNTA_REFERENCIA_NOMBRE";

        var fallbackNombre = esGarante
            ? "Perfecto. ¿Cuál es el nombre del negocio o casa comercial donde el garante tiene esa referencia?"
            : "Perfecto. ¿Cuál es el nombre del negocio o casa comercial donde tenés esa referencia?";

        return Resultado(
            solicitud,
            await ObtenerMensajeFlujo(codigoNombre, fallbackNombre),
            "PRE_EVALUACION",
            siguiente);
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarReferenciaComercialNombre(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request,
        bool esGarante)
    {
        var nombre = (request.Mensaje ?? string.Empty).Trim();

        if (nombre.Length < 2 || EsSi(nombre) || EsNo(nombre))
        {
            var codigoNombreInvalido = esGarante
                ? "CREDITO_GREF_NOMBRE_INVALIDO"
                : "CREDITO_REF_NOMBRE_INVALIDO";

            var fallbackNombreInvalido = esGarante
                ? "¿Cuál es el nombre del negocio o casa comercial donde el garante tiene la referencia?"
                : "¿Cuál es el nombre del negocio o casa comercial donde tenés la referencia?";

            return Resultado(
                solicitud,
                await ObtenerMensajeFlujo(
                    codigoNombreInvalido,
                    fallbackNombreInvalido));
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

        var codigoPreguntaAntiguedad = esGarante
            ? "CREDITO_PREGUNTA_GARANTE_REFERENCIA_ANTIGUEDAD"
            : "CREDITO_PREGUNTA_REFERENCIA_ANTIGUEDAD";

        var fallbackPreguntaAntiguedad = esGarante
            ? "¿Durante cuánto tiempo el garante pagó o viene pagando esa referencia? Podés responder: 1 año, 18 meses, 3 años, etc."
            : "¿Durante cuánto tiempo pagaste o venís pagando esa referencia? Podés responder: 1 año, 18 meses, 3 años, etc.";

        return Resultado(
            solicitud,
            await ObtenerMensajeFlujo(
                codigoPreguntaAntiguedad,
                fallbackPreguntaAntiguedad),
            "PRE_EVALUACION",
            siguiente);
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarReferenciaComercialAntiguedad(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request,
        bool esGarante)
    {
        if (!TryParseDuracionMeses(request.Mensaje, out var meses) || meses < 0)
        {
            var codigoTiempoInvalido = esGarante
                ? "CREDITO_GREF_TIEMPO_INVALIDO"
                : "CREDITO_REF_TIEMPO_INVALIDO";

            var fallbackTiempoInvalido =
                "No llegué a interpretar el tiempo 😊 Podés responder: 12 meses, 1 año, 1 año y 6 meses o 3 años.";

            return Resultado(
                solicitud,
                await ObtenerMensajeFlujo(
                    codigoTiempoInvalido,
                    fallbackTiempoInvalido));
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

        var codigoPreguntaCuota = esGarante
            ? "CREDITO_PREGUNTA_GARANTE_REFERENCIA_CUOTA"
            : "CREDITO_PREGUNTA_REFERENCIA_CUOTA";

        var fallbackPreguntaCuota = esGarante
            ? "¿De cuánto era aproximadamente la cuota mensual que pagaba el garante en esa referencia comercial? Podés responder, por ejemplo: 450.000."
            : "¿De cuánto era aproximadamente la cuota mensual que pagabas en esa referencia comercial? Podés responder, por ejemplo: 450.000.";

        return Resultado(
            solicitud,
            await ObtenerMensajeFlujo(
                codigoPreguntaCuota,
                fallbackPreguntaCuota),
            "PRE_EVALUACION",
            siguiente);
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarReferenciaComercialCuota(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request,
        bool esGarante)
    {
        if (!TryParseMontoGuaranies(request.Mensaje, out var monto) || monto <= 0)
        {
            var codigoCuotaInvalida = esGarante
                ? "CREDITO_GREF_CUOTA_INVALIDA"
                : "CREDITO_REF_CUOTA_INVALIDA";

            var fallbackCuotaInvalida =
                "No llegué a interpretar el monto 😊 Decime aproximadamente cuánto era la cuota, por ejemplo: 450.000.";

            return Resultado(
                solicitud,
                await ObtenerMensajeFlujo(
                    codigoCuotaInvalida,
                    fallbackCuotaInvalida));
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

        if (!meses.HasValue || string.IsNullOrWhiteSpace(comercio))
        {
            return await PasarAPendienteCreditos(
                solicitud,
                "No se pudo reconstruir la referencia comercial informada.");
        }

        if (!cuotaMoto.HasValue || cuotaMoto.Value <= 0)
        {
            return await PasarAPendienteCreditos(
                solicitud,
                "No existe una cuota activa del modelo para comparar la referencia comercial.");
        }

        var montoMinimo = cuotaMoto.Value * (regla.ReferenciaComercialCuotaMinPorcentaje / 100m);
        var cumpleTiempo = meses.Value >= regla.ReferenciaComercialMinMeses;
        var cumpleMonto = monto >= montoMinimo;

        if (cumpleTiempo && cumpleMonto)
        {
            await _repository.AgregarReferencia(
                solicitud.IdSolicitud,
                esGarante ? "GARANTE_COMERCIAL" : "COMERCIAL",
                comercio!,
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
                "PRECALIFICACION_EDAD");

            var msgEdad = await ObtenerMensajeFlujo(
                "CREDITO_PREGUNTA_EDAD",
                "Perfecto ✅ Con esos datos podemos continuar con la evaluación. ¿Cuál es tu fecha de nacimiento? Enviamela en formato DD/MM/AAAA.");

            return Resultado(
                solicitud,
                msgEdad,
                "PRE_EVALUACION",
                "PRECALIFICACION_EDAD");
        }

        var motivo =
            $"Referencia comercial no alcanza la preevaluación. Tiempo={meses.Value} meses (mínimo {regla.ReferenciaComercialMinMeses}); " +
            $"Cuota referencia={monto:N0}; cuota moto={cuotaMoto.Value:N0}; porcentaje mínimo={regla.ReferenciaComercialCuotaMinPorcentaje:N0}%.";

        if (esGarante)
        {
            return await PasarAPendienteCreditos(solicitud, motivo);
        }

        await _repository.GuardarResultadoPreEvaluacion(
            solicitud.IdSolicitud,
            "REQUIERE_GARANTE",
            motivo,
            "REFERENCIA_COMERCIAL",
            "PRECALIFICACION_GARANTE");

        await _repository.GuardarDatosGarantePrecalificacion(
            solicitud.IdSolicitud,
            requiereGarante: true,
            estado: "PENDIENTE");

        var msg = await ObtenerMensajeFlujo(
            "CREDITO_PREGUNTA_GARANTE",
            "Con la referencia informada todavía no alcanzamos la condición para avanzar a sola firma. Si tenés un garante, podemos evaluarlo. ¿Tenés una persona que pueda salirte de garante? Respondeme SI o NO.");

        return Resultado(
            solicitud,
            msg,
            "PRE_EVALUACION",
            "PRECALIFICACION_GARANTE");
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarGarante(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        var texto = NormalizarTexto(request.Mensaje);

        if (EsMensajeEsperaGarante(texto))
        {
            await _repository.GuardarDatosGarantePrecalificacion(
                solicitud.IdSolicitud,
                requiereGarante: true,
                estado: "ESPERANDO_CLIENTE");

            await _repository.ActualizarPaso(
                "CREDITO",
                solicitud.IdSolicitud,
                "ESPERANDO_GARANTE");

            var espera = await ObtenerMensajeFlujo(
                "CREDITO_ESPERANDO_GARANTE",
                "Perfecto 😊 Hablá tranquilo con tu posible garante. Quedamos pendientes de tu retorno y cuando me confirmes seguimos desde acá, sin empezar de nuevo.");

            return Resultado(
                solicitud,
                espera,
                "PRE_EVALUACION",
                "ESPERANDO_GARANTE");
        }

        if (EsNo(texto))
        {
            return await PasarAPendienteCreditos(
                solicitud,
                "El titular no califica a sola firma y no cuenta con garante por ahora.");
        }

        if (!EsSi(texto))
        {
            var msg = await ObtenerMensajeFlujo(
                "CREDITO_GARANTE_RESP_INVALIDA",
                "No llegué a entenderte 😊 ¿Contás con un garante? Si primero necesitás hablar con alguien, decímelo.");

            return Resultado(solicitud, msg);
        }

        await _repository.GuardarDatosGarantePrecalificacion(
            solicitud.IdSolicitud,
            requiereGarante: true,
            estado: "EVALUANDO");

        await _repository.ActualizarPaso(
            "CREDITO",
            solicitud.IdSolicitud,
            "GARANTE_IPS");

        var preguntaIps = await ObtenerMensajeFlujo(
            "CREDITO_PREGUNTA_GARANTE_IPS",
            "Perfecto 😊 Hagamos una evaluación rápida del garante. ¿El garante aporta actualmente a IPS? Respondeme SI o NO.");

        return Resultado(
            solicitud,
            preguntaIps,
            "PRE_EVALUACION",
            "GARANTE_IPS");
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarEsperaGarante(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        var texto = NormalizarTexto(request.Mensaje);

        if (EsMensajeEsperaGarante(texto))
        {
            return Resultado(
                solicitud,
                await ObtenerMensajeFlujo(
                    "CREDITO_ESPERANDO_GARANTE",
                    "Perfecto 😊 Quedamos pendientes de tu retorno. Cuando tengas la confirmación del garante, escribime y seguimos desde acá."));
        }

        if (texto.Contains("YA TENGO")
            || texto.Contains("CONSEGUI")
            || texto.Contains("YA CONSEGUI")
            || texto.Contains("ME SALE")
            || texto.Contains("YA HABLE")
            || texto.Contains("ME CONFIRMO")
            || texto.Contains("ACEPTO")
            || EsSi(texto))
        {
            await _repository.GuardarDatosGarantePrecalificacion(
                solicitud.IdSolicitud,
                requiereGarante: true,
                estado: "EVALUANDO");

            await _repository.ActualizarPaso(
                "CREDITO",
                solicitud.IdSolicitud,
                "GARANTE_IPS");

            return Resultado(
                solicitud,
                await ObtenerMensajeFlujo(
                    "CREDITO_PREGUNTA_GARANTE_IPS",
                    "Buenísimo 😊 ¿El garante aporta actualmente a IPS? Respondeme SI o NO."),
                "PRE_EVALUACION",
                "GARANTE_IPS");
        }

        if (EsNo(texto))
        {
            return await PasarAPendienteCreditos(
                solicitud,
                "El cliente volvió sin un garante disponible.");
        }

        return Resultado(
            solicitud,
            await ObtenerMensajeFlujo(
                "CREDITO_ESPERANDO_GARANTE",
                "Seguimos pendientes del garante 😊. Cuando tengas su confirmación, avisame y retomamos desde acá."));
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarGaranteIps(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        var texto = NormalizarTexto(request.Mensaje);

        if (!EsSi(texto) && !EsNo(texto))
        {
            return Resultado(
                solicitud,
                await ObtenerMensajeFlujo(
                    "CREDITO_GARANTE_IPS_INVALIDO",
                    "No llegué a entenderte 😊 ¿El garante aporta actualmente a IPS?"));
        }

        var aporta = EsSi(texto);

        await _repository.GuardarDatosGarantePrecalificacion(
            solicitud.IdSolicitud,
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
                    "¿Cuántos aportes de IPS tiene actualmente el garante? Podés responder, por ejemplo: 8 aportes."),
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
                "Si el garante no tiene IPS, podemos evaluarlo por referencia comercial. ¿Tiene alguna referencia comercial verificable donde haya pagado cuotas? Respondeme SI o NO."),
            "PRE_EVALUACION",
            "GARANTE_REF_EXISTE");
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarGaranteAportes(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        if (!TryParseEntero(request.Mensaje, out var aportes) || aportes < 0)
        {
            return Resultado(
                solicitud,
                await ObtenerMensajeFlujo(
                    "CREDITO_GARANTE_APORTES_INVALIDOS",
                    "No llegué a identificar la cantidad 😊 ¿Cuántos aportes de IPS tiene el garante?"));
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
                "PRECALIFICACION_EDAD");

            return Resultado(
                solicitud,
                await ObtenerMensajeFlujo(
                    "CREDITO_PREGUNTA_EDAD",
                    "Perfecto ✅ El garante cumple la validación inicial por IPS. Ahora seguimos con los datos del titular. ¿Cuál es tu fecha de nacimiento? Enviamela DD/MM/AAAA."),
                "PRE_EVALUACION",
                "PRECALIFICACION_EDAD");
        }

        await _repository.ActualizarPaso(
            "CREDITO",
            solicitud.IdSolicitud,
            "GARANTE_REF_EXISTE");

        return Resultado(
            solicitud,
            await ObtenerMensajeFlujo(
                "CREDITO_PREGUNTA_GARANTE_REFERENCIA",
                "El garante todavía no alcanza el mínimo de aportes IPS. Podemos evaluarlo por referencia comercial. ¿Tiene alguna referencia comercial verificable donde haya pagado cuotas? Respondeme SI o NO."),
            "PRE_EVALUACION",
            "GARANTE_REF_EXISTE");
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarPendienteCreditos(
        SolicitudMotoProcesoDto solicitud)
    {
        return Resultado(
            solicitud,
            await ObtenerMensajeFlujo(
                "CREDITO_PENDIENTE_CREDITOS",
                "Gracias por los datos 😊. Voy a dejar tu caso para consultar con el equipo de Créditos y te daremos un retorno en la brevedad para confirmar si podemos avanzar."),
            "PRE_EVALUACION",
            "PENDIENTE_CREDITOS");
    }

    private async Task<SolicitudMotoProcesoResultadoDto> PasarAPendienteCreditos(
        SolicitudMotoProcesoDto solicitud,
        string motivo)
    {
        await _repository.GuardarResultadoPreEvaluacion(
            solicitud.IdSolicitud,
            "PENDIENTE_CREDITOS",
            motivo,
            solicitud.ViaEvaluacion,
            "PENDIENTE_CREDITOS");

        return await ProcesarPendienteCreditos(solicitud);
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarNombreCompleto(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        var nombre = (request.Mensaje ?? string.Empty).Trim();

        // Nunca guardamos saludos, preguntas o frases de conversación como si
        // fueran el nombre del titular. Debe parecer realmente un nombre completo.
        if (!EsNombrePersonaValido(nombre))
        {
            return Resultado(
                solicitud,
                "Necesito solamente tu nombre y apellido completo, sin preguntas ni otros datos. Ejemplo: Juan Pérez González.");
        }

        await _repository.GuardarDatosContactoParciales(
            solicitud.IdContacto,
            nombreCompleto: nombre);

        await _repository.ActualizarPaso(
            solicitud.TipoOperacion,
            solicitud.IdSolicitud,
            "CEDULA_NUMERO");

        return Resultado(
            solicitud,
            "Gracias 😊 Ahora decime tu número de Cédula de Identidad paraguaya (CI).",
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
                "Necesito solamente el nombre y apellido completo del titular. Ejemplo: Juan Pérez González.",
                solicitud.Estado,
                "CORREGIR_NOMBRE_TITULAR");
        }

        await _repository.GuardarDatosContactoParciales(
            solicitud.IdContacto,
            nombreCompleto: nombre);

        if (solicitud.TipoOperacion.Equals("CREDITO", StringComparison.OrdinalIgnoreCase))
        {
            await _repository.ActualizarPaso(
                "CREDITO",
                solicitud.IdSolicitud,
                "AUTORIZACION");

            var cedula = SoloDigitos(solicitud.Cedula ?? string.Empty);

            return Resultado(
                solicitud,
                "Perfecto ✅ Corregí el nombre del titular. No se perdió ningún otro dato de la solicitud.\n\n" +
                "Seguimos exactamente donde estábamos: la autorización final. Enviame:\n" +
                "OK AUTORIZO\n" +
                $"Nombre y Apellido: {nombre}\n" +
                $"N.° de Cédula: {cedula}",
                solicitud.Estado,
                "AUTORIZACION");
        }

        // Este paso de corrección se utiliza principalmente durante el crédito.
        // Para contado, si llegara a utilizarse, continuamos con la CI sin borrar
        // ningún dato ya guardado.
        await _repository.ActualizarPaso(
            solicitud.TipoOperacion,
            solicitud.IdSolicitud,
            "CEDULA_NUMERO");

        return Resultado(
            solicitud,
            "Perfecto ✅ Corregí el nombre del titular. Ahora decime tu número de Cédula de Identidad paraguaya (CI).",
            solicitud.Estado,
            "CEDULA_NUMERO");
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
                "Necesito el número de tu Cédula de Identidad paraguaya (CI). Podés escribirlo con o sin puntos. Ejemplo: 4.123.456.");
        }

        await _repository.GuardarDatosContactoParciales(
            solicitud.IdContacto,
            numeroCedula: cedula);

        await _repository.ActualizarPaso(
            solicitud.TipoOperacion,
            solicitud.IdSolicitud,
            "ESTADO_CEDULA");

        return Resultado(
            solicitud,
            "¿Tu Cédula de Identidad paraguaya está vigente, es decir, NO está vencida? Respondeme SI o NO.",
            solicitud.Estado,
            "ESTADO_CEDULA");
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarDorsoCedula(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        if (!TieneMediaValida(request))
        {
            return Resultado(
                solicitud,
                "Necesito una foto clara del DORSO de tu Cédula de Identidad paraguaya 📷.");
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
                "Perfecto 😊 Ya recibimos tu CI vigente, frente y dorso. La compra al contado queda lista para revisión y cierre con el asesor.",
                "LISTA_REVISION",
                "LISTA_REVISION");
        }

        await _repository.ActualizarPaso(
            "CREDITO",
            solicitud.IdSolicitud,
            "DOMICILIO_CIUDAD");

        return Resultado(
            solicitud,
            "CI recibida ✅. Ahora seguimos con tu domicilio. ¿En qué ciudad vivís?",
            "DOCUMENTACION",
            "DOMICILIO_CIUDAD");
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarCiudad(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        var valor = (request.Mensaje ?? string.Empty).Trim();
        if (valor.Length < 2)
        {
            return Resultado(solicitud, "¿En qué ciudad vivís?");
        }

        await _repository.GuardarDatosContactoParciales(
            solicitud.IdContacto,
            ciudad: valor);

        await _repository.ActualizarPaso("CREDITO", solicitud.IdSolicitud, "DOMICILIO_BARRIO");

        return Resultado(
            solicitud,
            "Gracias 😊 ¿En qué barrio vivís?",
            "DOCUMENTACION",
            "DOMICILIO_BARRIO");
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarBarrio(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        var valor = (request.Mensaje ?? string.Empty).Trim();
        if (valor.Length < 2)
        {
            return Resultado(solicitud, "¿Cuál es tu barrio?");
        }

        await _repository.GuardarDatosContactoParciales(
            solicitud.IdContacto,
            barrio: valor);

        await _repository.ActualizarPaso("CREDITO", solicitud.IdSolicitud, "DOMICILIO_DIRECCION");

        return Resultado(
            solicitud,
            "Perfecto. ¿Cuál es tu calle o dirección donde vivís?",
            "DOCUMENTACION",
            "DOMICILIO_DIRECCION");
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarDireccionDomicilio(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        var valor = (request.Mensaje ?? string.Empty).Trim();
        if (valor.Length < 4)
        {
            return Resultado(solicitud, "Decime tu calle o dirección donde vivís, por favor.");
        }

        await _repository.GuardarDatosContactoParciales(
            solicitud.IdContacto,
            direccion: valor);

        await _repository.ActualizarPaso("CREDITO", solicitud.IdSolicitud, "LABORAL_EMPRESA");

        return Resultado(
            solicitud,
            "Domicilio guardado ✅. Ahora seguimos con tus datos laborales. ¿Cuál es el nombre de la empresa donde trabajás?",
            "DOCUMENTACION",
            "LABORAL_EMPRESA");
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarEmpresa(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        var valor = (request.Mensaje ?? string.Empty).Trim();
        if (valor.Length < 2)
        {
            return Resultado(solicitud, "¿Cuál es el nombre de la empresa donde trabajás?");
        }

        await _repository.GuardarDatosLaboralesParciales(
            solicitud.IdSolicitud,
            empresa: valor);

        await _repository.ActualizarPaso("CREDITO", solicitud.IdSolicitud, "LABORAL_DIRECCION");

        return Resultado(
            solicitud,
            "Gracias 😊 ¿Cuál es la dirección de la empresa?",
            "DOCUMENTACION",
            "LABORAL_DIRECCION");
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarDireccionEmpresa(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        var valor = (request.Mensaje ?? string.Empty).Trim();
        if (valor.Length < 4)
        {
            return Resultado(solicitud, "¿Cuál es la dirección de la empresa donde trabajás?");
        }

        await _repository.GuardarDatosLaboralesParciales(
            solicitud.IdSolicitud,
            direccionEmpresa: valor);

        await _repository.ActualizarPaso("CREDITO", solicitud.IdSolicitud, "LABORAL_TELEFONO");

        return Resultado(
            solicitud,
            "Perfecto. ¿Cuál es el teléfono de la empresa?",
            "DOCUMENTACION",
            "LABORAL_TELEFONO");
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarTelefonoEmpresa(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        var telefono = SoloDigitos(request.Mensaje);
        if (telefono.Length < 6)
        {
            return Resultado(solicitud, "Necesito un teléfono válido de la empresa.");
        }

        await _repository.GuardarDatosLaboralesParciales(
            solicitud.IdSolicitud,
            telefonoEmpresa: telefono);

        await _repository.ActualizarPaso("CREDITO", solicitud.IdSolicitud, "LABORAL_TELEFONO_ES_MOVIL");

        return Resultado(
            solicitud,
            "¿Ese teléfono de la empresa es un número celular? Respondeme SI o NO.",
            "DOCUMENTACION",
            "LABORAL_TELEFONO_ES_MOVIL");
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarTelefonoEmpresaEsMovil(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        var texto = NormalizarTexto(request.Mensaje);
        if (!EsSi(texto) && !EsNo(texto))
        {
            return Resultado(solicitud, "¿El teléfono de la empresa es celular? Respondeme SI o NO.");
        }

        var esMovil = EsSi(texto);

        await _repository.GuardarDatosLaboralesParciales(
            solicitud.IdSolicitud,
            telefonoEsMovil: esMovil);

        if (esMovil)
        {
            await _repository.ActualizarPaso("CREDITO", solicitud.IdSolicitud, "LABORAL_JEFE_ENCARGADO");

            return Resultado(
                solicitud,
                "Como es un número celular, necesito el nombre del jefe o encargado al que corresponde ese número.",
                "DOCUMENTACION",
                "LABORAL_JEFE_ENCARGADO");
        }

        await _repository.ActualizarPaso("CREDITO", solicitud.IdSolicitud, "REF_FAMILIAR_1");

        return Resultado(
            solicitud,
            "Datos laborales guardados ✅. Ahora vamos con las referencias personales. Necesitamos datos de 3 personas: 2 familiares/parientes y 1 amistad.\n\nEmpecemos con la primera persona que será tu referencia familiar. Pasame: Nombre y apellido | Teléfono | Parentesco.",
            "DOCUMENTACION",
            "REF_FAMILIAR_1");
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarJefeEncargado(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        var nombre = (request.Mensaje ?? string.Empty).Trim();
        if (nombre.Length < 3)
        {
            return Resultado(solicitud, "Decime el nombre del jefe o encargado al que corresponde ese número celular.");
        }

        await _repository.GuardarDatosLaboralesParciales(
            solicitud.IdSolicitud,
            nombreJefeEncargado: nombre);

        await _repository.ActualizarPaso("CREDITO", solicitud.IdSolicitud, "REF_FAMILIAR_1");

        return Resultado(
            solicitud,
            "Datos laborales guardados ✅. Ahora vamos con las referencias personales. Necesitamos datos de 3 personas: 2 familiares/parientes y 1 amistad.\n\nEmpecemos con la primera persona que será tu referencia familiar. Pasame: Nombre y apellido | Teléfono | Parentesco.",
            "DOCUMENTACION",
            "REF_FAMILIAR_1");
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarIdentidad(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        if (!TryParseIdentidad(request.Mensaje, out var nombre, out var cedula))
        {
            return Resultado(
                solicitud,
                "Enviame esos datos así 😊: Nombre completo | N.º de cédula. Ejemplo: Juan Pérez González | 4.123.456");
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
            "Gracias 😊 Ahora enviame una foto clara del FRENTE de tu cédula.",
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
            return Resultado(
                solicitud,
                tipoDocumento == "CEDULA_FRENTE"
                    ? "Necesito la foto del FRENTE de tu cédula 📷. Enviamela como imagen."
                    : "Necesito la foto del DORSO de tu cédula 📷. Enviamela como imagen.");
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
                "No pude guardar la imagen en este momento. Volvé a enviarla como foto y seguimos desde este mismo paso.");
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
                "Recibí la imagen, pero no pude registrarla correctamente. Volvé a enviarla y continuamos desde este mismo paso.");
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

        if (indicaVigente && !texto.Contains("RENOV"))
        {
            await _repository.GuardarEstadoCedula(
                solicitud.TipoOperacion,
                solicitud.IdSolicitud,
                "VIGENTE");

            await _repository.ActualizarPaso(
                solicitud.TipoOperacion,
                solicitud.IdSolicitud,
                "CEDULA_FRENTE");

            return Resultado(
                solicitud,
                "Perfecto ✅ Ahora enviame una foto clara del FRENTE de tu Cédula de Identidad paraguaya (CI).",
                solicitud.Estado,
                "CEDULA_FRENTE");
        }

        if (indicaNoVigente)
        {
            await _repository.GuardarEstadoCedula(
                solicitud.TipoOperacion,
                solicitud.IdSolicitud,
                "VENCIDA");

            return Resultado(
                solicitud,
                "Para continuar necesitamos una Cédula de Identidad paraguaya (CI) vigente, es decir, no vencida. Cuando tengas tu CI vigente, respondeme SI y continuamos desde este mismo paso.",
                solicitud.Estado,
                "ESTADO_CEDULA");
        }

        return Resultado(
            solicitud,
            "Necesito confirmar si tu Cédula de Identidad paraguaya está vigente y no vencida. Respondeme SI o NO.");
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarComprobanteRenovacion(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        if (!TieneMediaValida(request))
        {
            return Resultado(
                solicitud,
                "Necesito el comprobante de renovación para continuar 📄. Podés enviarlo como foto o PDF.");
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
                    ? "Antes de seguir me falta la foto del FRENTE de tu cédula 📷."
                    : "Antes de seguir me falta la foto del DORSO de tu cédula 📷.",
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
                    "Me falta el comprobante de renovación de la cédula 📄.",
                    solicitud.Estado,
                    "COMPROBANTE_RENOVACION");
            }
        }

        if (solicitud.TipoOperacion == "CONTADO")
        {
            await _repository.MarcarListaRevision("CONTADO", solicitud.IdSolicitud);

            return Resultado(
                solicitud,
                "Perfecto 😊 Ya recibimos la documentación necesaria para la compra al contado. Queda lista para revisión y cierre con el asesor.",
                "LISTA_REVISION",
                "LISTA_REVISION");
        }

        await _repository.ActualizarPaso(
            "CREDITO",
            solicitud.IdSolicitud,
            "DOMICILIO");

        return Resultado(
            solicitud,
            "Cédula recibida ✅. Ahora necesito tu domicilio particular: Ciudad | Barrio | Calle o dirección donde vivís.",
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
            "Domicilio guardado ✅. Ahora completamos los datos laborales:\n" +
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
            "Datos laborales guardados ✅. Ahora vamos con las referencias personales. Necesitamos solamente 3 personas: 2 familiares/parientes y 1 amistad.\n\n" +
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
        // Las referencias personales se aceptan en cualquier orden.
        // Ejemplo: aunque estemos pidiendo un familiar, si el cliente manda
        // "Carlos Gonzalez, 0981 123456, amigo", guardamos la amistad y
        // continuamos pidiendo solamente lo que todavía falta.
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
                "No pude identificar bien la referencia. Enviamela en una sola línea así: Nombre y apellido, teléfono, relación. " +
                "Ejemplos: Cesar Leite, 0982 33 44 55, hno. | Carlos Gonzalez, 0981 123456, amigo. " +
                "Podés pasar familiares y amistad en cualquier orden; yo voy guardando cada una y te digo cuáles faltan.");
        }

        var referenciasExistentes =
            await _repository.ObtenerReferencias(solicitud.IdSolicitud);

        var telefonoNormalizado = SoloDigitos(telefono);

        if (referenciasExistentes.Any(
                x => SoloDigitos(x.Telefono) == telefonoNormalizado))
        {
            return Resultado(
                solicitud,
                "Ese teléfono ya fue utilizado en otra referencia. Pasame otra persona con un número diferente.");
        }

        var regla = await _repository.ObtenerReglaCreditoActiva();

        var familiaresAntes = referenciasExistentes.Count(x => EsTipo(x.Tipo, "FAMILIAR"));
        var amigosAntes = referenciasExistentes.Count(x => EsTipo(x.Tipo, "AMIGO"));

        // Si ya completó una categoría, no seguimos agregando referencias de ese
        // mismo tipo como obligatorias. Le explicamos exactamente qué falta.
        if (EsTipo(tipoDetectado, "FAMILIAR")
            && familiaresAntes >= regla.ReferenciasFamiliaresMinimas)
        {
            return Resultado(
                solicitud,
                $"Ya tengo las {regla.ReferenciasFamiliaresMinimas} referencias familiares requeridas ✅. " +
                "Ahora me falta la referencia de amistad. Pasame: Nombre y apellido, teléfono, amigo/a.");
        }

        if (EsTipo(tipoDetectado, "AMIGO")
            && amigosAntes >= regla.ReferenciasAmigosMinimas)
        {
            return Resultado(
                solicitud,
                $"Ya tengo la referencia de amistad requerida ✅. " +
                $"Todavía me faltan {Math.Max(0, regla.ReferenciasFamiliaresMinimas - familiaresAntes)} referencia(s) familiar(es). " +
                "Pasame: Nombre y apellido, teléfono, parentesco.");
        }

        await _repository.AgregarReferencia(
            solicitud.IdSolicitud,
            tipoDetectado,
            nombre,
            telefonoNormalizado,
            EsTipo(tipoDetectado, "FAMILIAR") ? relacion : "AMIGO",
            null);

        // Volvemos a leer la BBDD: ella es la fuente de verdad para saber qué falta.
        var referenciasActualizadas =
            await _repository.ObtenerReferencias(solicitud.IdSolicitud);

        var familiares = referenciasActualizadas.Count(x => EsTipo(x.Tipo, "FAMILIAR"));
        var amigos = referenciasActualizadas.Count(x => EsTipo(x.Tipo, "AMIGO"));

        var guardadaComo = EsTipo(tipoDetectado, "FAMILIAR")
            ? $"referencia familiar ({relacion.ToLowerInvariant()})"
            : "referencia de amistad";

        var prefijo = $"Perfecto ✅ Guardé a {nombre} como {guardadaComo}.";

        if (familiares < regla.ReferenciasFamiliaresMinimas)
        {
            var faltanFamiliares = regla.ReferenciasFamiliaresMinimas - familiares;
            var paso = familiares == 0 ? "REF_FAMILIAR_1" : "REF_FAMILIAR_2";

            await _repository.ActualizarPaso(
                "CREDITO",
                solicitud.IdSolicitud,
                paso);

            return Resultado(
                solicitud,
                prefijo + "\n\n" +
                $"Hasta ahora tengo {familiares} familiar(es) y {amigos} amistad. " +
                $"Me falta{(faltanFamiliares == 1 ? "" : "n")} {faltanFamiliares} referencia(s) familiar(es).\n" +
                "Pasame la siguiente así: Nombre y apellido, teléfono, parentesco. Ejemplo: Esteban Martinez, 0971 000000, primo.",
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
                prefijo + "\n\n" +
                $"Ya tengo las {familiares} referencias familiares ✅. Ahora me falta 1 referencia de amistad.\n" +
                "Pasame: Nombre y apellido, teléfono, amigo/a. Ejemplo: Carlos Gonzalez, 0981 123456, amigo.",
                "DOCUMENTACION",
                "REF_AMIGO");
        }

        // Ya están las 3 referencias personales. Continuamos automáticamente.
        return await PrepararReferenciasComerciales(
            solicitud,
            prefijo + "\n\nYa están completas las 3 referencias personales requeridas: 2 familiares/parientes y 1 amistad ✅.");
    }

    private async Task<SolicitudMotoProcesoResultadoDto> PrepararReferenciasComerciales(
        SolicitudMotoProcesoDto solicitud,
        string? mensajeAnterior = null)
    {
        // La referencia comercial ya se utiliza solamente en la PRECALIFICACION
        // cuando no alcanza/no existe IPS. No la volvemos a pedir al final porque
        // eso genera preguntas repetidas y fricción innecesaria.
        return await PrepararAutorizacion(
            solicitud,
            mensajeAnterior);
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
                "Está bien 😊 No tener referencia comercial no bloquea tu solicitud. Continuamos con la autorización final.");
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
            "Referencia comercial guardada ✅. Si tenés otra referencia comercial, también la podemos registrar. ¿Querés agregar otra? Respondé SI o NO.",
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
                "Perfecto 😊 Indicame el nombre del otro negocio o casa comercial donde tenés referencia.",
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
                "Buenísimo 😊 Indicame el nombre del negocio o casa comercial donde tenés la referencia.",
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

        var prefijoAutorizacion = string.IsNullOrWhiteSpace(mensajeAnterior)
            ? string.Empty
            : mensajeAnterior.Trim() + "\n\n";

        return Resultado(
            solicitud,
            prefijoAutorizacion +
            "Ya estamos en el último paso ✅. Leé la siguiente autorización:\n\n" +
            texto +
            "\n\nPara aceptarla, respondé exactamente con este formato:\n" +
            "OK AUTORIZO\nNombre y Apellido: TU NOMBRE COMPLETO\nN.° de Cédula: TU CÉDULA",
            "DOCUMENTACION",
            "AUTORIZACION");
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarAutorizacion(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        if (!TryParseAutorizacion(
                request.Mensaje,
                out var nombre,
                out var cedula))
        {
            return Resultado(
                solicitud,
                "Para registrar la autorización necesito el formato completo:\nOK AUTORIZO\nNombre y Apellido: TU NOMBRE COMPLETO\nN.° de Cédula: TU CÉDULA");
        }

        var nombreRegistrado =
            $"{solicitud.Nombre} {solicitud.Apellido}".Trim();

        if (!EsNombrePersonaValido(nombre))
        {
            return Resultado(
                solicitud,
                "El nombre enviado en la autorización no parece un nombre y apellido válido. Revisalo y enviame nuevamente la autorización completa.",
                solicitud.Estado,
                "AUTORIZACION");
        }

        var cedulaRegistrada = SoloDigitos(solicitud.Cedula ?? string.Empty);
        var cedulaRecibida = SoloDigitos(cedula);

        // AUTORREPARACIÓN: versiones anteriores podían guardar por error un saludo
        // o una pregunta como nombre del titular. Si detectamos ese dato corrupto
        // y la autorización trae un nombre válido junto con la misma CI ya registrada,
        // corregimos el contacto automáticamente y continuamos.
        if (!EsNombrePersonaValido(nombreRegistrado))
        {
            if (!string.IsNullOrWhiteSpace(cedulaRegistrada)
                && cedulaRegistrada == cedulaRecibida)
            {
                await _repository.GuardarDatosContactoParciales(
                    solicitud.IdContacto,
                    nombreCompleto: nombre);

                nombreRegistrado = nombre;
            }
            else
            {
                await _repository.ActualizarPaso(
                    "CREDITO",
                    solicitud.IdSolicitud,
                    "CORREGIR_NOMBRE_TITULAR");

                return Resultado(
                    solicitud,
                    "Detecté que el nombre del titular quedó mal guardado en una prueba anterior. No vamos a perder los demás datos. Enviame ahora solamente tu nombre y apellido completo para corregirlo y volver a la autorización.",
                    solicitud.Estado,
                    "CORREGIR_NOMBRE_TITULAR");
            }
        }

        if (!MismoNombre(nombreRegistrado, nombre))
        {
            return Resultado(
                solicitud,
                $"El nombre de la autorización no coincide con el registrado en la solicitud. " +
                $"El titular registrado es: {nombreRegistrado}.\n\n" +
                "Volvé a enviarme la autorización completa usando exactamente ese nombre:\n" +
                "OK AUTORIZO\n" +
                $"Nombre y Apellido: {nombreRegistrado}\n" +
                $"N.° de Cédula: {cedulaRegistrada}",
                solicitud.Estado,
                "AUTORIZACION");
        }

        if (cedulaRegistrada != cedulaRecibida)
        {
            return Resultado(
                solicitud,
                "El número de cédula de la autorización no coincide con el registrado en la solicitud.\n\n" +
                $"La CI registrada es: {cedulaRegistrada}.\n\n" +
                "Volvé a enviarme la autorización completa con esos datos:\n" +
                "OK AUTORIZO\n" +
                $"Nombre y Apellido: {nombreRegistrado}\n" +
                $"N.° de Cédula: {cedulaRegistrada}",
                solicitud.Estado,
                "AUTORIZACION");
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
        var refs = await _repository.ObtenerReferencias(solicitud.IdSolicitud);

        var familiares = refs.Count(x => EsTipo(x.Tipo, "FAMILIAR"));
        var amigos = refs.Count(x => EsTipo(x.Tipo, "AMIGO"));
        var comerciales = refs.Count(x => EsTipo(x.Tipo, "COMERCIAL"));

        var frente = await _repository.TieneDocumento("CREDITO", solicitud.IdSolicitud, "CEDULA_FRENTE");
        var dorso = await _repository.TieneDocumento("CREDITO", solicitud.IdSolicitud, "CEDULA_DORSO");
        var autorizacion = await _repository.TieneAutorizacion(solicitud.IdSolicitud);

        var cedulaVigente = string.Equals(
            actual.EstadoCedula,
            "VIGENTE",
            StringComparison.OrdinalIgnoreCase);

        var edadOk = actual.FechaNacimiento.HasValue
                     && CalcularEdad(actual.FechaNacimiento.Value) >= regla.EdadMinima;
        var antiguedadOk = (actual.AntiguedadMeses ?? 0) >= regla.AntiguedadLaboralMinMeses;
        // IPS y referencias comerciales aportan información para la evaluación,
        // pero la ausencia de referencia comercial NO bloquea el envío a revisión.
        // El requisito laboral bloqueante sigue siendo la antigüedad mínima.
        var viableLaboral = antiguedadOk;

        var identidadOk = !string.IsNullOrWhiteSpace(actual.Nombre)
                          && !string.IsNullOrWhiteSpace(actual.Cedula);
        var domicilioOk = !string.IsNullOrWhiteSpace(actual.Ciudad)
                          && !string.IsNullOrWhiteSpace(actual.Barrio)
                          && !string.IsNullOrWhiteSpace(actual.Direccion);
        var laboralOk = !string.IsNullOrWhiteSpace(actual.Empresa)
                        && !string.IsNullOrWhiteSpace(actual.DireccionEmpresa)
                        && !string.IsNullOrWhiteSpace(actual.TelefonoEmpresa)
                        && (actual.TelefonoEmpresaEsMovil != true
                            || !string.IsNullOrWhiteSpace(actual.NombreJefeEncargado));

        if (!edadOk || !viableLaboral || !identidadOk || !cedulaVigente || !domicilioOk || !laboralOk || familiares < regla.ReferenciasFamiliaresMinimas || amigos < regla.ReferenciasAmigosMinimas || !frente || !dorso || !autorizacion)
        {
            var faltantes = new List<string>();

            if (!edadOk) faltantes.Add("edad mínima");
            if (!antiguedadOk) faltantes.Add("antigüedad laboral");
            if (!identidadOk) faltantes.Add("nombre y número de CI");
            if (!cedulaVigente) faltantes.Add("CI paraguaya vigente/no vencida");
            if (!domicilioOk) faltantes.Add("datos de domicilio");
            if (!laboralOk) faltantes.Add("datos laborales");
            if (familiares < regla.ReferenciasFamiliaresMinimas) faltantes.Add("referencias familiares");
            if (amigos < regla.ReferenciasAmigosMinimas) faltantes.Add("referencia de amigo/a");
            if (!frente) faltantes.Add("frente de CI");
            if (!dorso) faltantes.Add("dorso de CI");
            if (!autorizacion) faltantes.Add("autorización");

            return Resultado(
                solicitud,
                "Todavía falta completar o validar: " + string.Join(", ", faltantes) + ". Vamos a completar eso antes de enviar la solicitud a revisión.");
        }

        await _repository.MarcarListaRevision("CREDITO", solicitud.IdSolicitud);

        return Resultado(
            solicitud,
            "Perfecto ✅ La solicitud ya tiene los datos y documentos requeridos para pasar a revisión. Esto no significa aprobación automática; queda sujeta a verificación de los datos y evaluación de crédito.",
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

    private async Task<string> ObtenerMensajeFlujo(
        string codigo,
        string fallback)
    {
        try
        {
            var mensaje = await _repository.ObtenerPromptFlujo(codigo);
            return string.IsNullOrWhiteSpace(mensaje)
                ? fallback
                : mensaje.Trim();
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

        var frente = await _repository.TieneDocumento(
            actual.TipoOperacion,
            actual.IdSolicitud,
            "CEDULA_FRENTE");

        var dorso = await _repository.TieneDocumento(
            actual.TipoOperacion,
            actual.IdSolicitud,
            "CEDULA_DORSO");

        if (actual.TipoOperacion.Equals("CONTADO", StringComparison.OrdinalIgnoreCase))
        {
            var yaTengoContado = new List<string>();
            var faltaContado = new List<string>();

            var nombreActualContado = $"{actual.Nombre} {actual.Apellido}".Trim();
            if (EsNombrePersonaValido(nombreActualContado))
                yaTengoContado.Add("nombre del titular");
            else
                faltaContado.Add("nombre y apellido válido del titular");

            if (!string.IsNullOrWhiteSpace(actual.Cedula))
                yaTengoContado.Add("número de CI");
            else
                faltaContado.Add("número de CI");

            if (string.Equals(actual.EstadoCedula, "VIGENTE", StringComparison.OrdinalIgnoreCase))
                yaTengoContado.Add("confirmación de CI vigente");
            else
                faltaContado.Add("confirmar que la CI esté vigente/no vencida");

            if (frente)
                yaTengoContado.Add("foto del frente de la CI");
            else
                faltaContado.Add("foto del frente de la CI");

            if (dorso)
                yaTengoContado.Add("foto del dorso de la CI");
            else
                faltaContado.Add("foto del dorso de la CI");

            var respuestaContado = new StringBuilder();
            respuestaContado.AppendLine("Sí 😊 retomamos tu compra al contado desde donde quedó guardada.");

            if (yaTengoContado.Count > 0)
                respuestaContado.AppendLine($"\n✅ Ya tengo: {string.Join(", ", yaTengoContado)}.");

            if (faltaContado.Count > 0)
                respuestaContado.AppendLine($"\n⏳ Falta: {string.Join(", ", faltaContado)}.");

            respuestaContado.Append("\n👉 ");
            respuestaContado.Append(ObtenerIndicacionPasoActual(actual.PasoActual));

            return Resultado(
                actual,
                respuestaContado.ToString(),
                actual.Estado,
                actual.PasoActual);
        }

        var regla = await _repository.ObtenerReglaCreditoActiva();
        var referencias = await _repository.ObtenerReferencias(actual.IdSolicitud);
        var autorizacion = await _repository.TieneAutorizacion(actual.IdSolicitud);

        var familiares = referencias.Count(x => EsTipo(x.Tipo, "FAMILIAR"));
        var amigos = referencias.Count(x => EsTipo(x.Tipo, "AMIGO"));
        var comerciales = referencias.Count(x => EsTipo(x.Tipo, "COMERCIAL"));
        var nombreActual = $"{actual.Nombre} {actual.Apellido}".Trim();

        // Si una versión anterior guardó por error una frase/saludo como nombre y
        // ya estamos después de la etapa de identificación, priorizamos reparar ese
        // dato antes de continuar. No se borra nada de lo ya cargado.
        string pasoCorregido;
        if (!EsNombrePersonaValido(nombreActual)
            && !string.IsNullOrWhiteSpace(actual.Cedula))
        {
            pasoCorregido = "CORREGIR_NOMBRE_TITULAR";
        }
        else
        {
            // Si por una prueba anterior el PasoActual quedó desfasado respecto a las
            // referencias realmente guardadas, lo corregimos usando la BBDD como fuente.
            pasoCorregido = ObtenerPasoCorrectoSegunReferencias(
                actual.PasoActual,
                familiares,
                amigos,
                comerciales,
                autorizacion,
                regla);
        }

        if (!string.Equals(pasoCorregido, actual.PasoActual, StringComparison.OrdinalIgnoreCase))
        {
            await _repository.ActualizarPaso(
                "CREDITO",
                actual.IdSolicitud,
                pasoCorregido);

            actual.PasoActual = pasoCorregido;
        }

        var yaTengo = new List<string>();
        var faltan = new List<string>();

        if (actual.FechaNacimiento.HasValue)
            yaTengo.Add("fecha de nacimiento");
        else
            faltan.Add("fecha de nacimiento");

        if (actual.AntiguedadMeses.HasValue)
            yaTengo.Add("antigüedad laboral");
        else
            faltan.Add("antigüedad laboral");

        if (actual.AportaIPS.HasValue)
        {
            yaTengo.Add(actual.AportaIPS == true
                ? $"datos de IPS ({actual.CantidadAportesIPS ?? 0} aporte(s))"
                : "confirmación de que no aporta a IPS");
        }
        else
        {
            faltan.Add("confirmar si aporta a IPS");
        }

        if (EsNombrePersonaValido(nombreActual))
            yaTengo.Add("nombre y apellido del titular");
        else
            faltan.Add("corregir nombre y apellido del titular");

        if (!string.IsNullOrWhiteSpace(actual.Cedula))
            yaTengo.Add("número de CI");
        else
            faltan.Add("número de CI");

        if (string.Equals(actual.EstadoCedula, "VIGENTE", StringComparison.OrdinalIgnoreCase))
            yaTengo.Add("confirmación de CI vigente/no vencida");
        else
            faltan.Add("confirmar que la CI paraguaya esté vigente/no vencida");

        if (frente)
            yaTengo.Add("foto del frente de la CI");
        else
            faltan.Add("foto del frente de la CI");

        if (dorso)
            yaTengo.Add("foto del dorso de la CI");
        else
            faltan.Add("foto del dorso de la CI");

        var domicilioCompleto =
            !string.IsNullOrWhiteSpace(actual.Ciudad)
            && !string.IsNullOrWhiteSpace(actual.Barrio)
            && !string.IsNullOrWhiteSpace(actual.Direccion);

        if (domicilioCompleto)
            yaTengo.Add("domicilio particular");
        else
            faltan.Add("domicilio particular: ciudad, barrio y dirección");

        var laboralCompleto =
            !string.IsNullOrWhiteSpace(actual.Empresa)
            && !string.IsNullOrWhiteSpace(actual.DireccionEmpresa)
            && !string.IsNullOrWhiteSpace(actual.TelefonoEmpresa)
            && (actual.TelefonoEmpresaEsMovil != true
                || !string.IsNullOrWhiteSpace(actual.NombreJefeEncargado));

        if (laboralCompleto)
            yaTengo.Add("datos laborales");
        else
            faltan.Add("completar los datos laborales");

        if (familiares > 0 || amigos > 0)
            yaTengo.Add($"referencias personales: {familiares} familiar(es) y {amigos} amistad");

        var familiaresFaltantes = Math.Max(0, regla.ReferenciasFamiliaresMinimas - familiares);
        var amigosFaltantes = Math.Max(0, regla.ReferenciasAmigosMinimas - amigos);

        if (familiaresFaltantes > 0)
            faltan.Add($"{familiaresFaltantes} referencia(s) familiar(es)");

        if (amigosFaltantes > 0)
            faltan.Add($"{amigosFaltantes} referencia(s) de amistad");

        if (comerciales > 0)
            yaTengo.Add($"{comerciales} referencia(s) comercial(es)");
        // Si no tiene referencias comerciales, no se agrega como faltante:
        // es información opcional y no bloquea la solicitud.

        if (autorizacion)
            yaTengo.Add("autorización de evaluación de crédito");
        else
            faltan.Add("autorización final de evaluación de crédito");

        var respuesta = new StringBuilder();
        respuesta.AppendLine("Sí 😊 retomamos tu solicitud de crédito desde donde quedó guardada.");

        if (yaTengo.Count > 0)
            respuesta.AppendLine($"\n✅ Ya tengo: {string.Join(", ", yaTengo)}.");

        if (faltan.Count > 0)
            respuesta.AppendLine($"\n⏳ Falta: {string.Join(", ", faltan)}.");
        else
            respuesta.AppendLine("\n✅ Ya están completos los datos requeridos.");

        respuesta.Append("\n👉 Ahora estamos en este punto: ");
        respuesta.Append(ObtenerIndicacionPasoActual(actual.PasoActual));

        return Resultado(
            actual,
            respuesta.ToString(),
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
                "necesito confirmar si actualmente aportás a IPS. Respondeme SI o NO.",
            "PRECALIFICACION_APORTES" =>
                "necesito saber cuántos aportes de IPS tenés actualmente.",
            "REF_COMERCIAL_PRECALIFICACION" or "REF_COMERCIAL_EXISTE" =>
                "necesito confirmar si tenés una referencia comercial verificable donde hayas pagado cuotas. Respondeme SI o NO.",
            "REF_COMERCIAL_NOMBRE" =>
                "necesito el nombre del negocio o casa comercial de tu referencia.",
            "REF_COMERCIAL_ANTIGUEDAD" =>
                "necesito saber durante cuánto tiempo pagaste esa referencia. Podés responder en meses o años.",
            "REF_COMERCIAL_CUOTA" =>
                "necesito saber de cuánto era aproximadamente la cuota mensual de esa referencia.",
            "PRECALIFICACION_GARANTE" =>
                "necesito saber si contás con una persona que pueda salirte de garante. Si todavía tenés que hablar con alguien, decímelo y esperamos tu retorno.",
            "ESPERANDO_GARANTE" =>
                "quedamos esperando tu confirmación del garante. Cuando tengas respuesta, escribime y seguimos desde acá.",
            "GARANTE_IPS" =>
                "necesito confirmar si el garante aporta actualmente a IPS.",
            "GARANTE_APORTES" =>
                "necesito saber cuántos aportes de IPS tiene actualmente el garante.",
            "GARANTE_REF_EXISTE" =>
                "necesito confirmar si el garante tiene una referencia comercial verificable donde haya pagado cuotas.",
            "GARANTE_REF_NOMBRE" =>
                "necesito el nombre del negocio o casa comercial de la referencia del garante.",
            "GARANTE_REF_ANTIGUEDAD" =>
                "necesito saber durante cuánto tiempo el garante pagó esa referencia. Podés responder en meses o años.",
            "GARANTE_REF_CUOTA" =>
                "necesito saber de cuánto era aproximadamente la cuota mensual de la referencia del garante.",
            "PENDIENTE_CREDITOS" =>
                "tu caso quedó pendiente de consulta con el equipo de Créditos. Te daremos retorno cuando tengamos una respuesta.",
            "PRECALIFICACION_EDAD" =>
                "necesito tu fecha de nacimiento. Enviamela en formato DD/MM/AAAA.",
            "PRECALIFICACION_ANTIGUEDAD" =>
                "necesito saber cuánto tiempo de antigüedad tenés en tu trabajo actual. Podés responder en meses o años.",
            "NOMBRE_COMPLETO" =>
                "necesito tu nombre y apellido.",
            "CORREGIR_NOMBRE_TITULAR" =>
                "detecté que el nombre del titular quedó mal registrado. Enviame solamente tu nombre y apellido completo para corregirlo.",
            "CEDULA_NUMERO" =>
                "necesito tu número de Cédula de Identidad paraguaya (CI).",
            "ESTADO_CEDULA" =>
                "necesito confirmar si tu CI paraguaya está vigente y no vencida. Respondeme SI o NO.",
            "CEDULA_FRENTE" =>
                "necesito la foto del FRENTE de tu Cédula de Identidad paraguaya.",
            "CEDULA_DORSO" =>
                "necesito la foto del DORSO de tu Cédula de Identidad paraguaya.",
            "DOMICILIO_CIUDAD" =>
                "necesito saber en qué ciudad vivís.",
            "DOMICILIO_BARRIO" =>
                "necesito saber en qué barrio vivís.",
            "DOMICILIO_DIRECCION" =>
                "necesito tu calle o dirección particular.",
            "LABORAL_EMPRESA" =>
                "necesito el nombre de la empresa donde trabajás.",
            "LABORAL_DIRECCION" =>
                "necesito la dirección de la empresa donde trabajás.",
            "LABORAL_TELEFONO" =>
                "necesito el teléfono de la empresa.",
            "LABORAL_TELEFONO_ES_MOVIL" =>
                "necesito confirmar si el teléfono de la empresa es celular. Respondeme SI o NO.",
            "LABORAL_JEFE_ENCARGADO" =>
                "necesito el nombre del jefe o encargado al que corresponde el número celular de la empresa.",
            "REF_FAMILIAR_1" =>
                "necesito la primera referencia familiar. Pasame Nombre y apellido, teléfono y parentesco. Ejemplo: Cesar Leite, 0982 33 44 55, hno.",
            "REF_FAMILIAR_2" =>
                "necesito la segunda referencia familiar. Pasame Nombre y apellido, teléfono y parentesco. Ejemplo: Carlos Gonzales, 0981 11 22 33, primo.",
            "REF_AMIGO" =>
                "necesito la referencia de amistad. Pasame Nombre y apellido y teléfono. Ejemplo: Juan Perez, 0971 22 33 44.",
            "REF_COMERCIAL_CONTROL" =>
                "vamos a revisar las referencias comerciales.",
            "REF_COMERCIAL_DATOS" =>
                "si tenés alguna referencia comercial, pasame el nombre del negocio o casa comercial. Si no tenés ninguna, escribime NO TENGO y continuamos; no bloquea la solicitud.",
            "REF_COMERCIAL_MAS" =>
                "ya tenemos al menos una referencia comercial. Si tenés otra respondé SI; si no tenés más, respondé NO y pasamos a la autorización.",
            "AUTORIZACION" =>
                "estamos en la autorización final de evaluación de crédito.",
            "LISTA_REVISION" =>
                "la solicitud ya está lista para revisión.",
            _ =>
                "seguimos desde el último paso guardado de tu solicitud."
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
                "\n\nSeguimos en la autorización final. Para aceptarla, enviame el formato completo con los mismos datos registrados.";
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
        return t.Contains("CANCELAR SOLICITUD") || t == "CANCELAR";
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
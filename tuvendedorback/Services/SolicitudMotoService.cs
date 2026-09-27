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
            var regla = await _repository.ObtenerReglaCreditoActiva();

            var id = await _repository.CrearCredito(
                idConversacion,
                idModeloProducto,
                idPublicacion,
                idContacto);

            return new SolicitudMotoProcesoResultadoDto
            {
                Manejado = true,
                IdSolicitud = id,
                TipoOperacion = "CREDITO",
                Estado = "PRE_EVALUACION",
                PasoActual = "PRECALIFICACION_EDAD",
                Respuesta =
                    "¡Claro! 😊 Para solicitar la moto a crédito voy a necesitar algunos datos y documentos.\n\n" +
                    "Vamos a completar todo paso a paso:\n" +
                    $"• Tener al menos {regla.EdadMinima} años cumplidos.\n" +
                    "• Cédula de Identidad paraguaya (CI) vigente, no vencida, con foto o copia clara del frente y dorso.\n" +
                    "• Dirección particular del titular: dirección, barrio y ciudad donde vive.\n" +
                    $"• Datos laborales del titular: lugar de trabajo, al menos {regla.AntiguedadLaboralMinMeses} meses de antigüedad, teléfono laboral y dirección de la empresa.\n" +
                    $"• Aportes de IPS: si aporta, verificamos la cantidad de aportes; si no aporta o no alcanza {regla.AportesIPSMinimos}, la evaluación continúa con referencia comercial.\n" +
                    $"• 3 referencias personales de otras personas: {regla.ReferenciasFamiliaresMinimas} familiares/parientes y {regla.ReferenciasAmigosMinimas} amistad. De cada persona te pediré nombre, teléfono y parentesco cuando corresponda.\n" +
                    "• Al menos 1 referencia comercial verificable: tenés que indicarme el nombre del negocio o casa comercial donde tenés la referencia. Con 1 ya cumplís; si tenés más, también las registramos.\n" +
                    "• Al final te voy a enviar la autorización de evaluación de crédito completa para que la leas y la aceptes.\n\n" +
                    "Te voy a pedir un dato por vez para hacerlo sencillo 😊\n\n" +
                    "Empecemos: ¿cuál es tu fecha de nacimiento? Enviamela en formato DD/MM/AAAA."
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
            "PRECALIFICACION_EDAD" => await ProcesarEdad(solicitud, request),
            "PRECALIFICACION_ANTIGUEDAD" => await ProcesarAntiguedadLaboral(solicitud, request),
            "PRECALIFICACION_IPS" => await ProcesarAportaIps(solicitud, request),
            "PRECALIFICACION_APORTES" => await ProcesarCantidadAportesIps(solicitud, request),
            "REF_COMERCIAL_PRECALIFICACION" => await ProcesarReferenciaComercialPrecalificacion(solicitud, request),
            "NOMBRE_COMPLETO" => await ProcesarNombreCompleto(solicitud, request),
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
            "REF_COMERCIAL_CONTROL" => await PrepararReferenciasComerciales(solicitud),
            "REF_COMERCIAL_DATOS" => await ProcesarReferenciaComercialDatos(solicitud, request),
            "REF_COMERCIAL_MAS" => await ProcesarReferenciaComercialMas(solicitud, request),
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
            return Resultado(
                solicitud,
                "Necesito tu fecha de nacimiento para validar la edad. Enviamela así: DD/MM/AAAA. Ejemplo: 15/08/1998.");
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

        return Resultado(
            solicitud,
            $"Edad validada ✅. Ahora decime cuántos meses de antigüedad tenés en tu trabajo actual. El mínimo requerido es {regla.AntiguedadLaboralMinMeses} meses. Podés responder, por ejemplo: 8 meses.",
            "PRE_EVALUACION",
            "PRECALIFICACION_ANTIGUEDAD");
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarAntiguedadLaboral(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        if (!TryParseEntero(request.Mensaje, out var antiguedadMeses) || antiguedadMeses < 0)
        {
            return Resultado(
                solicitud,
                "Decime tu antigüedad laboral en meses. Por ejemplo: 8 meses.");
        }

        var regla = await _repository.ObtenerReglaCreditoActiva();

        await _repository.GuardarDatosLaboralesParciales(
            solicitud.IdSolicitud,
            antiguedadMeses: antiguedadMeses);

        if (antiguedadMeses < regla.AntiguedadLaboralMinMeses)
        {
            var motivo =
                $"Antigüedad laboral {antiguedadMeses} meses. Mínimo requerido {regla.AntiguedadLaboralMinMeses} meses.";

            await _repository.GuardarResultadoPreEvaluacion(
                solicitud.IdSolicitud,
                "NO_VIABLE",
                motivo,
                null,
                "NO_VIABLE");

            return Resultado(
                solicitud,
                $"Gracias. Para que la solicitud pueda continuar necesitás al menos {regla.AntiguedadLaboralMinMeses} meses de antigüedad laboral. Con los datos actuales no puede continuar por ahora.",
                "NO_VIABLE",
                "NO_VIABLE");
        }

        await _repository.ActualizarPaso(
            "CREDITO",
            solicitud.IdSolicitud,
            "PRECALIFICACION_IPS");

        return Resultado(
            solicitud,
            "Antigüedad laboral validada ✅. ¿Actualmente aportás a IPS? Respondeme SI o NO.",
            "PRE_EVALUACION",
            "PRECALIFICACION_IPS");
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
                "¿Actualmente aportás a IPS? Respondeme SI o NO.");
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
                "Perfecto 😊 ¿Cuántos aportes de IPS tenés actualmente? Respondeme solo la cantidad, por ejemplo: 5.",
                "PRE_EVALUACION",
                "PRECALIFICACION_APORTES");
        }

        await _repository.GuardarResultadoPreEvaluacion(
            solicitud.IdSolicitud,
            "PENDIENTE_REFERENCIAS_COMERCIALES",
            "No aporta IPS.",
            "REFERENCIAS_COMERCIALES",
            "NOMBRE_COMPLETO");

        return Resultado(
            solicitud,
            "Está bien 😊 Podemos continuar con la solicitud. Más adelante te voy a pedir al menos 1 referencia comercial verificable. Ahora necesito registrar tus datos como titular de la solicitud. ¿Cuál es tu nombre y apellido?",
            "DOCUMENTACION",
            "NOMBRE_COMPLETO");
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarCantidadAportesIps(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        if (!TryParseEntero(request.Mensaje, out var aportes) || aportes < 0)
        {
            return Resultado(
                solicitud,
                "Decime cuántos aportes de IPS tenés. Por ejemplo: 5.");
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
                "VIABLE",
                null,
                "IPS",
                "NOMBRE_COMPLETO");

            return Resultado(
                solicitud,
                "Perfecto ✅ Cumplís los requisitos básicos para continuar. Ahora necesito registrar tus datos como titular de la solicitud. ¿Cuál es tu nombre y apellido?",
                "DOCUMENTACION",
                "NOMBRE_COMPLETO");
        }

        await _repository.GuardarResultadoPreEvaluacion(
            solicitud.IdSolicitud,
            "PENDIENTE_REFERENCIAS_COMERCIALES",
            $"Cantidad de aportes IPS: {aportes}. Mínimo requerido: {regla.AportesIPSMinimos}.",
            "REFERENCIAS_COMERCIALES",
            "NOMBRE_COMPLETO");

        return Resultado(
            solicitud,
            $"La antigüedad laboral está bien ✅. Como todavía no llegás al mínimo de {regla.AportesIPSMinimos} aportes de IPS, continuamos y más adelante te voy a pedir al menos 1 referencia comercial verificable. Ahora necesito registrar tus datos como titular de la solicitud. ¿Cuál es tu nombre y apellido?",
            "DOCUMENTACION",
            "NOMBRE_COMPLETO");
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
            $"La antigüedad laboral está bien ✅. Como no contás con al menos {regla.AportesIPSMinimos} aportes IPS, continuamos y más adelante te voy a pedir al menos 1 referencia comercial verificable. Ahora necesito registrar tus datos como titular de la solicitud. ¿Cuál es tu nombre y apellido?",
            "DOCUMENTACION",
            "NOMBRE_COMPLETO");
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarReferenciaComercialPrecalificacion(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        if (EsNo(request.Mensaje))
        {
            return Resultado(
                solicitud,
                "Para continuar necesitamos por lo menos 1 referencia comercial verificable. Indicame el nombre del negocio o casa comercial donde tenés la referencia.",
                "PRE_EVALUACION",
                "REF_COMERCIAL_PRECALIFICACION");
        }

        var comercio = (request.Mensaje ?? string.Empty).Trim();
        if (comercio.Length < 2 || EsSi(comercio))
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
            "Referencia comercial informada por el cliente.");

        await _repository.GuardarResultadoPreEvaluacion(
            solicitud.IdSolicitud,
            "VIABLE",
            null,
            "REFERENCIAS_COMERCIALES",
            "NOMBRE_COMPLETO");

        return Resultado(
            solicitud,
            "Perfecto ✅ Ya tenemos una referencia comercial. Ahora necesito registrar tus datos como titular de la solicitud. ¿Cuál es tu nombre y apellido?",
            "DOCUMENTACION",
            "NOMBRE_COMPLETO");
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarNombreCompleto(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        var nombre = (request.Mensaje ?? string.Empty).Trim();

        if (nombre.Length < 4 || SoloDigitos(nombre).Length > 0)
        {
            return Resultado(
                solicitud,
                "Decime tu nombre y apellido, por favor. Ejemplo: Juan Pérez González.");
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
        if (!TryParseReferenciaPersonal(
                request.Mensaje,
                tipoEsperado,
                out var nombre,
                out var telefono,
                out var relacion))
        {
            return Resultado(
                solicitud,
                tipoEsperado == "FAMILIAR"
                    ? "No pude identificar todos los datos de la referencia familiar. Podés escribirlo así, por ejemplo: Cesar Leite, 0982 33 44 55, hno. También acepto: Carlos Gonzales | 0981 11 22 33 | primo."
                    : "No pude identificar nombre y teléfono de la referencia de amistad. Podés escribirlo así, por ejemplo: Juan Perez, 0981 11 22 33.");
        }

        var referenciasExistentes =
            await _repository.ObtenerReferencias(
                solicitud.IdSolicitud);

        var telefonoNormalizado =
            SoloDigitos(telefono);

        if (
            referenciasExistentes.Any(
                x =>
                    SoloDigitos(x.Telefono)
                    ==
                    telefonoNormalizado)
        )
        {
            return Resultado(
                solicitud,
                "Ese teléfono ya fue utilizado en otra referencia. Necesito una persona diferente con otro número.");
        }

        await _repository.AgregarReferencia(
            solicitud.IdSolicitud,
            tipoEsperado,
            nombre,
            telefonoNormalizado,
            tipoEsperado == "FAMILIAR"
                ? relacion
                : "AMIGO",
            null);

        if (
            string.Equals(
                siguientePaso,
                "AUTORIZACION",
                StringComparison.OrdinalIgnoreCase)
        )
        {
            return await PrepararAutorizacion(
                solicitud);
        }

        if (
            string.Equals(
                siguientePaso,
                "REF_COMERCIAL_CONTROL",
                StringComparison.OrdinalIgnoreCase)
        )
        {
            return await PrepararReferenciasComerciales(
                solicitud,
                respuestaOk);
        }

        if (
            siguientePaso == "REF_COMERCIAL_OPCIONAL"
            &&
            string.Equals(
                solicitud.ViaEvaluacion,
                "REFERENCIAS_COMERCIALES",
                StringComparison.OrdinalIgnoreCase)
        )
        {
            return await PrepararAutorizacion(
                solicitud);
        }

        await _repository.ActualizarPaso(
            "CREDITO",
            solicitud.IdSolicitud,
            siguientePaso);

        if (siguientePaso == "REF_COMERCIAL_OPCIONAL")
        {
            return Resultado(
                solicitud,
                respuestaOk +
                "\\n\\nAhora necesitamos por lo menos 1 referencia comercial verificable. Indicame el nombre del negocio o casa comercial donde tenés la referencia.",
                "DOCUMENTACION",
                siguientePaso);
        }

        return Resultado(
            solicitud,
            respuestaOk,
            "DOCUMENTACION",
            siguientePaso);
    }

    private async Task<SolicitudMotoProcesoResultadoDto> PrepararReferenciasComerciales(
        SolicitudMotoProcesoDto solicitud,
        string? mensajeAnterior = null)
    {
        var referencias = await _repository.ObtenerReferencias(solicitud.IdSolicitud);
        var comerciales = referencias.Count(x => EsTipo(x.Tipo, "COMERCIAL"));

        if (comerciales < 1)
        {
            await _repository.ActualizarPaso(
                "CREDITO",
                solicitud.IdSolicitud,
                "REF_COMERCIAL_DATOS");

            var prefijo = string.IsNullOrWhiteSpace(mensajeAnterior)
                ? string.Empty
                : mensajeAnterior.Trim() + "\n\n";

            return Resultado(
                solicitud,
                prefijo +
                "Ahora necesito por lo menos 1 referencia comercial verificable. Indicame el nombre del negocio o casa comercial donde tenés la referencia. Con una ya cumplís el requisito.",
                "DOCUMENTACION",
                "REF_COMERCIAL_DATOS");
        }

        await _repository.ActualizarPaso(
            "CREDITO",
            solicitud.IdSolicitud,
            "REF_COMERCIAL_MAS");

        var prefijoExistente = string.IsNullOrWhiteSpace(mensajeAnterior)
            ? string.Empty
            : mensajeAnterior.Trim() + "\n\n";

        return Resultado(
            solicitud,
            prefijoExistente +
            $"Ya tenemos {comerciales} referencia(s) comercial(es) registrada(s) ✅. Con una ya cumplís el requisito. Si tenés otra, también la podemos registrar. ¿Querés agregar otra referencia comercial? Respondé SI o NO.",
            "DOCUMENTACION",
            "REF_COMERCIAL_MAS");
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarReferenciaComercialDatos(
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
            "Referencia comercial guardada ✅. Con una ya cumplís este requisito. Si tenés otra referencia comercial, también la podemos registrar. ¿Querés agregar otra? Respondé SI o NO.",
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
        SolicitudMotoProcesoDto solicitud)
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

        return Resultado(
            solicitud,
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

        if (!MismoNombre(nombreRegistrado, nombre))
        {
            return Resultado(
                solicitud,
                "El nombre de la autorización no coincide con el nombre registrado en la solicitud. Revisalo y enviame nuevamente la autorización completa.");
        }

        if (SoloDigitos(solicitud.Cedula ?? string.Empty) != SoloDigitos(cedula))
        {
            return Resultado(
                solicitud,
                "El número de cédula de la autorización no coincide con el registrado en la solicitud. Revisalo y enviame nuevamente la autorización completa.");
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
        var ipsOk = actual.AportaIPS == true
                    && (actual.CantidadAportesIPS ?? 0) >= regla.AportesIPSMinimos;
        var minimoComerciales = Math.Max(1, regla.ReferenciasComercialesMinimasSinIps);
        var comercialOk = comerciales >= minimoComerciales;

        var viableLaboral = antiguedadOk && (ipsOk || comercialOk);

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

        if (!edadOk || !viableLaboral || !identidadOk || !cedulaVigente || !domicilioOk || !laboralOk || familiares < regla.ReferenciasFamiliaresMinimas || amigos < regla.ReferenciasAmigosMinimas || !comercialOk || !frente || !dorso || !autorizacion)
        {
            var faltantes = new List<string>();

            if (!edadOk) faltantes.Add("edad mínima");
            if (!antiguedadOk) faltantes.Add("antigüedad laboral");
            if (!ipsOk && !comercialOk) faltantes.Add("IPS o referencia comercial para la evaluación");
            if (!comercialOk) faltantes.Add("al menos 1 referencia comercial verificable");
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
        out string nombre,
        out string telefono,
        out string relacion)
    {
        nombre = string.Empty;
        telefono = string.Empty;
        relacion = string.Empty;

        if (string.IsNullOrWhiteSpace(texto))
        {
            return false;
        }

        var contenido =
            texto.Trim();

        /*
         * Acepta ejemplos como:
         *
         * Cesar Leite, 0982 33 44 55 hno
         * Cesar Leite, 0982 33 44 55, hno
         * Cesar Leite | 0982 33 44 55 | hermano
         * Carlos Gonzales, 0981 112233 es Primo
         * Juan Perez, 0981 11 22 33
         *
         * También acepta +595:
         * Juan Perez, +595 981 112233
         */
        var matchTelefono =
            Regex.Match(
                contenido,
                @"(?:\+?595[\s\-.]*)?(?:0?9\d{2})(?:[\s\-.]*\d){6}",
                RegexOptions.IgnoreCase);

        if (!matchTelefono.Success)
        {
            return false;
        }

        var numero =
            SoloDigitos(
                matchTelefono.Value);

        if (
            numero.StartsWith("595")
            &&
            numero.Length == 12
        )
        {
            numero =
                "0" + numero.Substring(3);
        }

        if (
            numero.Length != 10
            ||
            !numero.StartsWith("09")
        )
        {
            return false;
        }

        var parteNombre =
            contenido
                .Substring(
                    0,
                    matchTelefono.Index)
                .Trim()
                .Trim(
                    ',',
                    ';',
                    '|',
                    '-',
                    ':');

        parteNombre =
            Regex.Replace(
                    parteNombre,
                    @"^\s*(NOMBRE(?:\s+Y\s+APELLIDO)?|REFERENCIA)\s*[:\-]?\s*",
                    string.Empty,
                    RegexOptions.IgnoreCase)
                .Trim()
                .Trim(
                    ',',
                    ';',
                    '|',
                    '-',
                    ':');

        var palabrasNombre =
            parteNombre.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries
                |
                StringSplitOptions.TrimEntries);

        if (
            parteNombre.Length < 4
            ||
            palabrasNombre.Length < 2
            ||
            parteNombre.Any(char.IsDigit)
        )
        {
            return false;
        }

        nombre =
            parteNombre;

        telefono =
            numero;

        if (
            string.Equals(
                tipoEsperado,
                "AMIGO",
                StringComparison.OrdinalIgnoreCase)
        )
        {
            relacion =
                "AMIGO";

            return true;
        }

        var parentesco =
            DetectarParentescoReferencia(
                contenido);

        if (string.IsNullOrWhiteSpace(parentesco))
        {
            return false;
        }

        relacion =
            parentesco;

        return true;
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

        var match =
            Regex.Match(
                texto ?? string.Empty,
                @"\d+");

        if (!match.Success)
        {
            return false;
        }

        return int.TryParse(
            match.Value,
            out valor);
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

    private static bool EsCancelar(string mensaje)
    {
        var t = NormalizarTexto(mensaje);
        return t.Contains("CANCELAR SOLICITUD") || t == "CANCELAR";
    }

    private static bool EsTipo(string actual, string esperado)
        => string.Equals(actual, esperado, StringComparison.OrdinalIgnoreCase);

    private static bool EsSi(string texto)
    {
        var t = NormalizarTexto(texto);
        return t == "SI" || t.StartsWith("SI ") || t.Contains("CLARO") || t.Contains("TENGO");
    }

    private static bool EsNo(string texto)
    {
        var t = NormalizarTexto(texto);
        return t == "NO" || t.StartsWith("NO ") || t.Contains("NO TENGO");
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
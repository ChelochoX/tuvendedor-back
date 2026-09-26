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
                    $"Perfecto 😊 Vamos a verificar primero si la solicitud puede avanzar. " +
                    $"Para crédito, el titular debe tener al menos {regla.EdadMinima} años cumplidos y {regla.AntiguedadLaboralMinMeses} meses de antigüedad laboral. " +
                    $"Si aporta IPS, necesitamos al menos {regla.AportesIPSMinimos} aportes; si no aporta o no alcanza ese mínimo, revisamos referencias comerciales.\n\n" +
                    "Empecemos por tu fecha de nacimiento. Enviamela en formato DD/MM/AAAA."
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
            PasoActual = "IDENTIDAD",
            Respuesta =
                "Perfecto 😊 Para la compra al contado solamente necesitamos validar tu cédula. " +
                "Enviame primero: Nombre completo | N.º de cédula."
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
            "PRECALIFICACION_LABORAL" => await ProcesarPrecalificacionLaboral(solicitud, request),
            "REF_COMERCIAL_PRECALIFICACION" => await ProcesarReferenciaComercialPrecalificacion(solicitud, request),
            "IDENTIDAD" => await ProcesarIdentidad(solicitud, request),
            "CEDULA_FRENTE" => await ProcesarDocumento(
                solicitud,
                request,
                "CEDULA_FRENTE",
                "Perfecto, ya recibí el frente ✅. Ahora enviame la foto del DORSO de tu cédula.",
                "CEDULA_DORSO"),
            "CEDULA_DORSO" => await ProcesarDocumento(
                solicitud,
                request,
                "CEDULA_DORSO",
                "Gracias 😊 Ya tengo frente y dorso. Ahora decime si tu cédula está VIGENTE o EN RENOVACIÓN.",
                "ESTADO_CEDULA"),
            "ESTADO_CEDULA" => await ProcesarEstadoCedula(solicitud, request),
            "COMPROBANTE_RENOVACION" => await ProcesarComprobanteRenovacion(solicitud, request),
            "DOMICILIO" => await ProcesarDomicilio(solicitud, request),
            "LABORAL_COMPLETO" => await ProcesarLaboralCompleto(solicitud, request),
            "REF_FAMILIAR_1" => await ProcesarReferenciaPersonal(
                solicitud,
                request,
                "FAMILIAR",
                "REF_FAMILIAR_2",
                "Primera referencia familiar guardada ✅. Ahora pasame una SEGUNDA referencia familiar: Nombre y apellido | Teléfono | Parentesco."),
            "REF_FAMILIAR_2" => await ProcesarReferenciaPersonal(
                solicitud,
                request,
                "FAMILIAR",
                "REF_AMIGO",
                "Segunda referencia familiar guardada ✅. Ahora necesito una referencia de un AMIGO/A: Nombre y apellido | Teléfono | AMIGO."),
            "REF_AMIGO" => await ProcesarReferenciaPersonal(
                solicitud,
                request,
                "AMIGO",
                "REF_COMERCIAL_OPCIONAL",
                "Perfecto ✅ Ya tenemos las 3 referencias personales requeridas."),
            "REF_COMERCIAL_OPCIONAL" => await ProcesarReferenciaComercialOpcional(solicitud, request),
            "REF_COMERCIAL_OPCIONAL_DATOS" => await ProcesarReferenciaComercialOpcionalDatos(solicitud, request),
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
            "IDENTIDAD" => await ProcesarIdentidad(solicitud, request),
            "CEDULA_FRENTE" => await ProcesarDocumento(
                solicitud,
                request,
                "CEDULA_FRENTE",
                "Perfecto, ya recibí el frente ✅. Ahora enviame la foto del DORSO de tu cédula.",
                "CEDULA_DORSO"),
            "CEDULA_DORSO" => await ProcesarDocumento(
                solicitud,
                request,
                "CEDULA_DORSO",
                "Gracias 😊 Ya tengo frente y dorso. Ahora decime si tu cédula está VIGENTE o EN RENOVACIÓN.",
                "ESTADO_CEDULA"),
            "ESTADO_CEDULA" => await ProcesarEstadoCedula(solicitud, request),
            "COMPROBANTE_RENOVACION" => await ProcesarComprobanteRenovacion(solicitud, request),
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
                $"Gracias por la información. Para solicitar el crédito el titular debe tener al menos {regla.EdadMinima} años cumplidos. Con la fecha indicada, la solicitud no puede avanzar por ahora.",
                "NO_VIABLE",
                "NO_VIABLE");
        }

        await _repository.ActualizarPaso(
            "CREDITO",
            solicitud.IdSolicitud,
            "PRECALIFICACION_LABORAL");

        return Resultado(
            solicitud,
            "Edad validada ✅. Ahora necesito estos datos laborales en una sola línea:\n" +
            "Empresa | Antigüedad en meses | ¿Aporta IPS? SI/NO | Cantidad de aportes IPS\n\n" +
            "Ejemplo: Empresa ABC | 8 | SI | 5\n" +
            "Si no aportás IPS: Empresa ABC | 8 | NO | 0",
            "PRE_EVALUACION",
            "PRECALIFICACION_LABORAL");
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
            "REF_COMERCIAL_PRECALIFICACION");

        return Resultado(
            solicitud,
            $"La antigüedad laboral está bien ✅. Como no contás con al menos {regla.AportesIPSMinimos} aportes IPS, podemos continuar revisando referencias comerciales.\n\n" +
            $"Necesito al menos {regla.ReferenciasComercialesMinimasSinIps} referencia comercial verificable. Enviame: Comercio/Empresa | Teléfono.",
            "PRE_EVALUACION",
            "REF_COMERCIAL_PRECALIFICACION");
    }

    private async Task<SolicitudMotoProcesoResultadoDto> ProcesarReferenciaComercialPrecalificacion(
        SolicitudMotoProcesoDto solicitud,
        MotoConversacionRequest request)
    {
        if (EsNo(request.Mensaje))
        {
            var reglaNoRef = await _repository.ObtenerReglaCreditoActiva();
            var motivo =
                $"No alcanza {reglaNoRef.AportesIPSMinimos} aportes IPS y no cuenta con referencia comercial para la vía alternativa.";

            await _repository.GuardarResultadoPreEvaluacion(
                solicitud.IdSolicitud,
                "NO_VIABLE",
                motivo,
                "REFERENCIAS_COMERCIALES",
                "NO_VIABLE");

            return Resultado(
                solicitud,
                "Entiendo. Como no alcanzás el mínimo de aportes IPS y tampoco contás con una referencia comercial verificable, la solicitud no puede avanzar por esta vía en este momento.",
                "NO_VIABLE",
                "NO_VIABLE");
        }

        if (!TryParseDosCampos(request.Mensaje, out var comercio, out var telefono))
        {
            return Resultado(
                solicitud,
                "Enviame la referencia comercial así: Nombre del comercio o empresa | Teléfono. Si no tenés ninguna, respondé NO.");
        }

        await _repository.AgregarReferencia(
            solicitud.IdSolicitud,
            "COMERCIAL",
            comercio,
            telefono,
            null,
            "Referencia comercial para preevaluación por IPS insuficiente/no aportante.");

        var regla = await _repository.ObtenerReglaCreditoActiva();
        var referencias = await _repository.ObtenerReferencias(solicitud.IdSolicitud);
        var comerciales = referencias.Count(x => EsTipo(x.Tipo, "COMERCIAL"));

        if (comerciales < regla.ReferenciasComercialesMinimasSinIps)
        {
            return Resultado(
                solicitud,
                $"Gracias ✅. Necesito {regla.ReferenciasComercialesMinimasSinIps - comerciales} referencia(s) comercial(es) más para completar esta preevaluación. Enviame: Comercio/Empresa | Teléfono.");
        }

        await _repository.GuardarResultadoPreEvaluacion(
            solicitud.IdSolicitud,
            "VIABLE",
            null,
            "REFERENCIAS_COMERCIALES",
            "IDENTIDAD");

        return Resultado(
            solicitud,
            "Perfecto ✅ Ya tenemos la referencia comercial necesaria para continuar la preevaluación. Ahora enviame: Nombre completo | N.º de cédula.",
            "DOCUMENTACION",
            "IDENTIDAD");
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

        var archivo = await GuardarArchivoPrivado(
            solicitud,
            tipoDocumento,
            request);

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

        if (texto.Contains("RENOV"))
        {
            await _repository.GuardarEstadoCedula(
                solicitud.TipoOperacion,
                solicitud.IdSolicitud,
                "RENOVACION");

            await _repository.ActualizarPaso(
                solicitud.TipoOperacion,
                solicitud.IdSolicitud,
                "COMPROBANTE_RENOVACION");

            return Resultado(
                solicitud,
                "Perfecto. Enviame una foto o PDF del comprobante/recibo de renovación de la cédula 📄.",
                solicitud.Estado,
                "COMPROBANTE_RENOVACION");
        }

        if (texto.Contains("VIGENTE"))
        {
            await _repository.GuardarEstadoCedula(
                solicitud.TipoOperacion,
                solicitud.IdSolicitud,
                "VIGENTE");

            return await ContinuarDespuesCedula(solicitud, "VIGENTE");
        }

        return Resultado(
            solicitud,
            "Necesito confirmar el estado de tu cédula. Respondeme: VIGENTE o EN RENOVACIÓN.");
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
            "Datos laborales guardados ✅. Ahora necesito 3 referencias personales: 2 familiares y 1 amigo/a.\n\n" +
            "Primera referencia FAMILIAR: Nombre y apellido | Teléfono | Parentesco.",
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
        if (!TryParseTresCampos(
                request.Mensaje,
                out var nombre,
                out var telefono,
                out var relacion))
        {
            return Resultado(
                solicitud,
                tipoEsperado == "FAMILIAR"
                    ? "Enviame: Nombre y apellido | Teléfono | Parentesco."
                    : "Enviame: Nombre y apellido | Teléfono | AMIGO.");
        }

        var relacionNormalizada = NormalizarTexto(relacion);

        if (tipoEsperado == "AMIGO" && !relacionNormalizada.Contains("AMIG"))
        {
            return Resultado(
                solicitud,
                "Esta tercera referencia debe ser de un amigo o amiga. Enviame: Nombre y apellido | Teléfono | AMIGO.");
        }

        if (tipoEsperado == "FAMILIAR" && relacionNormalizada.Contains("AMIG"))
        {
            return Resultado(
                solicitud,
                "Esta referencia debe ser de un familiar. Indicame también el parentesco, por ejemplo: madre, padre, hermano/a, tío/a, primo/a.");
        }

        var referenciasExistentes = await _repository.ObtenerReferencias(solicitud.IdSolicitud);
        var telefonoNormalizado = SoloDigitos(telefono);

        if (referenciasExistentes.Any(x => SoloDigitos(x.Telefono) == telefonoNormalizado))
        {
            return Resultado(
                solicitud,
                "Ese teléfono ya fue utilizado en otra referencia. Necesito una persona diferente con otro número.");
        }

        await _repository.AgregarReferencia(
            solicitud.IdSolicitud,
            tipoEsperado,
            nombre,
            telefono,
            tipoEsperado == "FAMILIAR" ? relacion : "AMIGO",
            null);

        if (
            siguientePaso == "REF_COMERCIAL_OPCIONAL"
            && string.Equals(
                solicitud.ViaEvaluacion,
                "REFERENCIAS_COMERCIALES",
                StringComparison.OrdinalIgnoreCase)
        )
        {
            return await PrepararAutorizacion(solicitud);
        }

        await _repository.ActualizarPaso(
            "CREDITO",
            solicitud.IdSolicitud,
            siguientePaso);

        if (siguientePaso == "REF_COMERCIAL_OPCIONAL")
        {
            return Resultado(
                solicitud,
                respuestaOk + "\n\nSi además tenés alguna referencia comercial, puede ayudar en la evaluación. ¿Querés agregar una? Respondé SI o NO.",
                "DOCUMENTACION",
                siguientePaso);
        }

        return Resultado(
            solicitud,
            respuestaOk,
            "DOCUMENTACION",
            siguientePaso);
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
                "Buenísimo 😊 Enviame: Comercio/Empresa | Teléfono.",
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
        if (!TryParseDosCampos(request.Mensaje, out var comercio, out var telefono))
        {
            return Resultado(
                solicitud,
                "Enviame la referencia comercial así: Comercio/Empresa | Teléfono.");
        }

        await _repository.AgregarReferencia(
            solicitud.IdSolicitud,
            "COMERCIAL",
            comercio,
            telefono,
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

        var comprobanteOk = true;
        if (string.Equals(actual.EstadoCedula, "RENOVACION", StringComparison.OrdinalIgnoreCase))
        {
            comprobanteOk = await _repository.TieneDocumento(
                "CREDITO",
                solicitud.IdSolicitud,
                "COMPROBANTE_RENOVACION");
        }

        var edadOk = actual.FechaNacimiento.HasValue
                     && CalcularEdad(actual.FechaNacimiento.Value) >= regla.EdadMinima;
        var antiguedadOk = (actual.AntiguedadMeses ?? 0) >= regla.AntiguedadLaboralMinMeses;
        var ipsOk = actual.AportaIPS == true
                    && (actual.CantidadAportesIPS ?? 0) >= regla.AportesIPSMinimos;
        var comercialOk = comerciales >= regla.ReferenciasComercialesMinimasSinIps;

        var viableLaboral = antiguedadOk && (ipsOk || comercialOk);

        if (!edadOk || !viableLaboral || familiares < regla.ReferenciasFamiliaresMinimas || amigos < regla.ReferenciasAmigosMinimas || !frente || !dorso || !comprobanteOk || !autorizacion)
        {
            var faltantes = new List<string>();

            if (!edadOk) faltantes.Add("edad mínima");
            if (!antiguedadOk) faltantes.Add("antigüedad laboral");
            if (!ipsOk && !comercialOk) faltantes.Add("IPS o referencias comerciales");
            if (familiares < regla.ReferenciasFamiliaresMinimas) faltantes.Add("2 referencias familiares");
            if (amigos < regla.ReferenciasAmigosMinimas) faltantes.Add("1 referencia de amigo/a");
            if (!frente) faltantes.Add("frente de cédula");
            if (!dorso) faltantes.Add("dorso de cédula");
            if (!comprobanteOk) faltantes.Add("comprobante de renovación");
            if (!autorizacion) faltantes.Add("autorización");

            return Resultado(
                solicitud,
                "Todavía falta completar o validar: " + string.Join(", ", faltantes) + ". Vamos a completar eso antes de enviar la solicitud a revisión.");
        }

        await _repository.MarcarListaRevision("CREDITO", solicitud.IdSolicitud);

        return Resultado(
            solicitud,
            "Perfecto ✅ La solicitud ya tiene los datos y documentos requeridos para pasar a revisión. Esto no significa aprobación automática; la evaluación final debe verificar la información comercial y crediticia correspondiente.",
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

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(request.MediaBase64);
        }
        catch
        {
            throw new ReglasdeNegocioException("El archivo recibido no es válido.");
        }

        var maxBytes = _configuration.GetValue<long?>("IA:MaxPrivateDocumentBytes") ?? 8L * 1024 * 1024;
        if (bytes.LongLength > maxBytes)
        {
            throw new ReglasdeNegocioException("El archivo supera el tamaño máximo permitido.");
        }

        var basePath = _configuration["IA:PrivateDocumentsPath"];
        if (string.IsNullOrWhiteSpace(basePath))
        {
            basePath = Path.Combine(AppContext.BaseDirectory, "private-documents");
        }

        var carpeta = Path.Combine(
            basePath,
            solicitud.TipoOperacion.ToLowerInvariant(),
            solicitud.IdSolicitud.ToString(CultureInfo.InvariantCulture));

        Directory.CreateDirectory(carpeta);

        var extension = ObtenerExtension(request.MediaMimeType, request.MediaNombre);
        var nombreSeguro = $"{tipoDocumento}_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}{extension}";
        var ruta = Path.Combine(carpeta, nombreSeguro);

        await File.WriteAllBytesAsync(ruta, bytes);

        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        return (
            request.MediaNombre ?? nombreSeguro,
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

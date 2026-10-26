using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using tuvendedorback.DTOs;
using tuvendedorback.Exceptions;
using tuvendedorback.Helpers;
using tuvendedorback.Repositories.Interfaces;
using tuvendedorback.Repositories;
using tuvendedorback.Request;
using tuvendedorback.Services.Interfaces;

namespace tuvendedorback.Services;

public class MotoConversacionService
    : IMotoConversacionService
{
    private const string CODIGO_PROMPT_BASE =
        "MOTO_VENTAS_BASE";

    private const int MINUTOS_MODO_HUMANO =
        15;

    private const double CONFIANZA_MINIMA_IMAGEN_MODELO =
        0.72;

    private const string MARCADOR_DERIVACION_OTRO_PRODUCTO =
        "PRODUCTO DISTINTO DE MOTO";

    private const string MARCADOR_ROUTER_OTRO_PRODUCTO =
        "[[DERIVAR_OTRO_PRODUCTO]]";

    private const string MARCADOR_ROUTER_MOTO_NO_RECONOCIDA =
        "[[MOTO_NO_RECONOCIDA]]";


    private readonly IIAConversacionRepository
        _repository;

    private readonly IPublicacionRepository
        _publicacionRepository;

    private readonly IMotoOfertaService
        _motoOfertaService;

    private readonly IOllamaService
        _ollamaService;

    private readonly PanambiPromptsEditorRepository _editorPrompts;
    private readonly PanambiEnlacePendienteRepository _enlaces;
    private IReadOnlyDictionary<string, string> _textosPublicados =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private string Texto(string codigo, params (string Campo, string Valor)[] variables)
    {
        if (!_textosPublicados.TryGetValue(codigo, out var contenido) ||
            string.IsNullOrWhiteSpace(contenido))
            throw new InvalidOperationException($"La plantilla {codigo} no tiene versión publicada en SQL.");
        foreach (var (campo, valor) in variables)
            contenido = contenido.Replace("{" + campo + "}", valor ?? string.Empty, StringComparison.Ordinal);
        return contenido;
    }

    private readonly ISolicitudMotoService
        _solicitudMotoService;

    private readonly IClientesService
        _clientesService;

    private readonly IClientesRepository
        _clientesRepository;

    private readonly ILogger<MotoConversacionService>
        _logger;

    private readonly int
        _contextoMinutos;


    public MotoConversacionService(
        IIAConversacionRepository repository,
        IPublicacionRepository publicacionRepository,
        IMotoOfertaService motoOfertaService,
        IOllamaService ollamaService,
        PanambiPromptsEditorRepository editorPrompts,
        PanambiEnlacePendienteRepository enlaces,
        ISolicitudMotoService solicitudMotoService,
        IClientesService clientesService,
        IClientesRepository clientesRepository,
        IConfiguration configuration,
        ILogger<MotoConversacionService> logger)
    {
        _repository =
            repository;

        _publicacionRepository =
            publicacionRepository;

        _motoOfertaService =
            motoOfertaService;

        _ollamaService =
            ollamaService;
        _editorPrompts = editorPrompts;
        _enlaces = enlaces;

        _solicitudMotoService =
            solicitudMotoService;

        _clientesService =
            clientesService;

        _clientesRepository = clientesRepository;

        _logger =
            logger;

        _contextoMinutos =
            Math.Max(
                5,
                configuration.GetValue<int?>(
                    "IA:ContextMinutes")
                ??
                30);
    }


    // =========================================================
    // PROCESAR MENSAJE
    // =========================================================

    public async Task<MotoConversacionResponseDto>
        ProcesarMensaje(
            MotoConversacionRequest request,
            CancellationToken cancellationToken = default)
    {
        // =====================================================
        // 1. IDENTIFICAR CONVERSACION
        // =====================================================

        var identificador =
            NormalizarIdentificador(
                request.Telefono);

        if (
            string.IsNullOrWhiteSpace(
                identificador)
        )
        {
            throw new ReglasdeNegocioException(
                "No se pudo identificar el contacto.");
        }

        // Multicanal: dos números receptores nunca deben compartir el mismo
        // contexto, historial ni modo HUMANO. El bridge actual sin CanalWhatsapp
        // conserva literalmente su identificador histórico y no se altera.
        // Los dígitos del teléfono siguen disponibles en NumeroWhatsapp.
        if (!string.IsNullOrWhiteSpace(request.CanalWhatsapp))
        {
            var canal = request.CanalWhatsapp.Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(canal, @"^[a-zA-Z0-9_-]{1,40}$"))
                throw new ReglasdeNegocioException("Canal WhatsApp no válido.");
            identificador = $"WA:{canal}:{identificador}";
        }

        var idConversacion =
            await _repository
                .ObtenerOCrearConversacion(
                    identificador);

        // Snapshot por conversación: los constructores síncronos de respuestas
        // usan textos publicados en SQL, sin fallback textual en C#.
        // La siguiente entrada carga los cambios publicados desde la pantalla.
        _textosPublicados = (await _editorPrompts.ListarPublicados())
            .ToDictionary(x => x.Codigo, x => x.Contenido, StringComparer.OrdinalIgnoreCase);

        // Respetar una baja global aunque el mismo cliente escriba desde
        // otro canal. El mensaje queda en el historial, sin nuevas ofertas.
        // Se verifica solo si el bridge resolvió el teléfono real.
        if (!string.IsNullOrWhiteSpace(request.NumeroWhatsapp) &&
            await _clientesRepository.ExisteBajaGlobalWhatsapp(request.NumeroWhatsapp))
        {
            await _repository.RegistrarMensaje(idConversacion, "CLIENTE", MensajeParaHistorial(request));
            return new MotoConversacionResponseDto
            {
                IdConversacion = idConversacion,
                Respuesta = string.Empty,
                RequierePublicacion = false
            };
        }

        var idPublicacionExplicita =
            request.IdPublicacion
            ??
            ExtraerIdPublicacion(
                request.Mensaje);

        bool? esPublicacionMotoExplicita =
            null;

        if (idPublicacionExplicita.HasValue)
        {
            esPublicacionMotoExplicita =
                await _repository
                    .EsPublicacionMoto(
                        idPublicacionExplicita.Value);
        }

        // Desde el primer mensaje ya conservamos al contacto como
        // oportunidad comercial. Si luego consulta un modelo concreto,
        // actualizamos esta misma ficha en vez de crear duplicados.
        var idInteresadoRegistrado = await RegistrarInteresadoSeguro(
            new InteresadoWhatsAppEventoRequest
            {
                IdConversacion =
                    idConversacion,

                IdentificadorExterno =
                    identificador,

                NumeroWhatsapp =
                    request.NumeroWhatsapp,

                NombreContacto =
                    request.NombreContacto,

                IdPublicacion =
                    idPublicacionExplicita,

                TipoConsulta =
                    DetectarTipoConsultaInteres(
                        request.Mensaje),

                EstadoConsulta =
                    "CONSULTANDO",

                MensajeCliente =
                    MensajeParaHistorial(
                        request),

                EsEntradaCliente =
                    true
            });

        // MODO HUMANO PERSISTENTE: solo el operador puede devolver el chat
        // a Panambí desde el CRM. No existe vencimiento por tiempo, ni el
        // cliente lo puede reactivar sin autorización del vendedor.
        var modoConversacion = await _repository.ObtenerModoConversacion(idConversacion);
        if (string.Equals(modoConversacion, "HUMANO", StringComparison.OrdinalIgnoreCase))
        {
            // Guardar la entrada para que el vendedor pueda verla en el CRM.
            await _repository.RegistrarMensaje(idConversacion, "CLIENTE", MensajeParaHistorial(request));
            return new MotoConversacionResponseDto
            {
                IdConversacion = idConversacion,
                IdPublicacion = null,
                Marca = null,
                Modelo = null,
                Respuesta = string.Empty,
                RequierePublicacion = false
            };
        }

        // =====================================================
        // 3. REGISTRAR MENSAJE DEL CLIENTE
        // =====================================================

        await _repository
            .RegistrarMensaje(
                idConversacion,
                "CLIENTE",
                MensajeParaHistorial(request));

        // Una baja de WhatsApp tiene prioridad absoluta, incluso si
        // había un formulario de crédito o una derivación en curso.
        if (PoliticaSeguimientoCliente.Evaluar(request.Mensaje).NoContactar)
        {
            var respuestaBaja = await _editorPrompts.ObtenerObligatorio("BAJA_WHATSAPP");
            await RegistrarCierreSeguro(idConversacion, respuestaBaja,
                "NO_CONTACTAR_SOLICITADO", idInteresadoRegistrado);
            return RespuestaCierre(idConversacion, respuestaBaja);
        }

        // =====================================================
        // 4. HUMANO SOLO SI EL CLIENTE LO PIDE EXPLICITAMENTE
        // =====================================================

        if (
            SolicitaAtencionHumana(
                request.Mensaje)
        )
        {
            await _repository
                .CambiarModoConversacion(
                    idConversacion,
                    "HUMANO");

            var respuestaHumano = await _editorPrompts.ObtenerObligatorio("DERIVACION_HUMANA");

            await _repository
                .RegistrarMensaje(
                    idConversacion,
                    "IA",
                    respuestaHumano);

            await RegistrarInteresadoSeguro(
                new InteresadoWhatsAppEventoRequest
                {
                    IdConversacion =
                        idConversacion,

                    EstadoConsulta =
                        "DERIVADO_HUMANO",

                    Respuesta =
                        respuestaHumano,

                    MotivoSeguimiento =
                        "El cliente solicitó atención de una persona.",

                    EsEntradaCliente =
                        false
                });

            return new MotoConversacionResponseDto
            {
                IdConversacion = idConversacion,
                IdPublicacion = null,
                Marca = null,
                Modelo = null,
                Respuesta = respuestaHumano,
                RequierePublicacion = false
            };
        }

        // =====================================================
        // 5. SOLICITUD DE COMPRA/CREDITO ACTIVA
        //
        // Si ya estamos recopilando datos o documentos,
        // esa máquina de estados tiene prioridad sobre la venta.
        // =====================================================

        var solicitudActiva =
            await _solicitudMotoService
                .ObtenerActiva(
                    idConversacion);

        if (solicitudActiva is not null)
        {
            var proceso =
                await _solicitudMotoService
                    .ProcesarActiva(
                        solicitudActiva,
                        request,
                        cancellationToken);

            await _repository
                .RegistrarMensaje(
                    idConversacion,
                    "IA",
                    proceso.Respuesta);

            var solicitudCancelada =
                string.Equals(
                    proceso.Estado,
                    "CANCELADA",
                    StringComparison.OrdinalIgnoreCase)
                ||
                string.Equals(
                    proceso.PasoActual,
                    "CANCELADA",
                    StringComparison.OrdinalIgnoreCase);

            await RegistrarInteresadoSeguro(
                new InteresadoWhatsAppEventoRequest
                {
                    IdConversacion =
                        idConversacion,

                    IdModeloProducto =
                        solicitudActiva.IdModeloProducto,

                    IdPublicacion =
                        solicitudActiva.IdPublicacion,

                    TipoConsulta =
                        solicitudActiva.TipoOperacion,

                    EstadoConsulta =
                        solicitudCancelada
                            ? "CERRADO"
                            : EsCreditoOperacion(
                                solicitudActiva.TipoOperacion)
                                ? "CREDITO_EN_PROCESO"
                                : "CONTADO_EN_PROCESO",

                    TipoOperacion =
                        solicitudActiva.TipoOperacion,

                    IdSolicitudOperacion =
                        solicitudActiva.IdSolicitud,

                    PasoOperacion =
                        proceso.PasoActual
                        ??
                        solicitudActiva.PasoActual,

                    Respuesta =
                        proceso.Respuesta,

                    MotivoSeguimiento =
                        solicitudCancelada
                            ? "Cliente desistió de la solicitud de moto."
                            : "Solicitud en proceso. Revisar avance si el cliente deja de responder.",

                    EsEntradaCliente =
                        false
                });

            return new MotoConversacionResponseDto
            {
                IdConversacion = idConversacion,
                IdPublicacion = solicitudActiva.IdPublicacion,
                Marca = null,
                Modelo = null,
                Respuesta = proceso.Respuesta,
                RequierePublicacion = false
            };
        }

        // =====================================================
        // 5A. RESPETO A LA DECISION COMERCIAL DEL CLIENTE
        //
        // Se ejecuta DESPUES de las solicitudes activas para que un
        // «no» a una pregunta de IPS no cierre una solicitud de credito.
        // Se ejecuta ANTES de cualquier busqueda de Marketplace/Ollama.
        // =====================================================

        var decisionComercial = PoliticaSeguimientoCliente.Evaluar(request.Mensaje);

        // La solicitud explícita de desistimiento se reconoce incluso
        // sin un seguimiento previo. No se envía a la IA general.
        if (decisionComercial.Desiste)
        {
            var respuestaDesistimiento = await _editorPrompts.ObtenerObligatorio("DESISTIMIENTO_COMERCIAL");

            await RegistrarCierreSeguro(
                idConversacion, respuestaDesistimiento, "RECHAZO_PRODUCTO_EXPLICITO",
                idInteresadoRegistrado);
            return RespuestaCierre(idConversacion, respuestaDesistimiento);
        }

        // Si esa oportunidad ya fue cerrada, no responder a «gracias»,
        // «hola», «no» ni a mensajes sin intención nueva de compra.
        // Una publicación nueva o interés explícito sí puede abrir otra consulta.
        if (idInteresadoRegistrado.HasValue &&
            !decisionComercial.RetomaConsulta &&
            !idPublicacionExplicita.HasValue)
        {
            var interesadoCerrado = await EsOportunidadRechazada(
                idInteresadoRegistrado.Value);
            if (interesadoCerrado)
                return RespuestaCierre(idConversacion, string.Empty);
        }

        // Si el cliente pidió esperar, no redirigirlo a otros productos.
        // La pausa ya fue registrada por RegistrarInteraccionWhatsApp.
        if (decisionComercial.PausaIndefinida ||
            decisionComercial.PausaValor.HasValue)
        {
            var respuestaEspera = await _editorPrompts.ObtenerObligatorio("PAUSA_COMERCIAL");
            await _repository.RegistrarMensaje(idConversacion, "IA", respuestaEspera);
            return RespuestaCierre(idConversacion, respuestaEspera);
        }

        // Negativas breves: «No», «Ahora no, gracias», «Ya no puedo gracias».
        // Solamente cierran si existe contexto comercial real; nunca se
        // interpretan de esta forma dentro de una solicitud de crédito activa.
        // Importante: esto sucede ANTES del router de Marketplace y Ollama.
        if (EsNegativaComercialCorta(request.Mensaje) &&
            (await UltimaRespuestaFueOfertaComercial(idConversacion) ||
             (EsNegativaConAgradecimiento(request.Mensaje) &&
              await TieneContextoComercialSeleccionado(idConversacion))))
        {
            var respuestaNegativa = await _editorPrompts.ObtenerObligatorio("DESISTIMIENTO_COMERCIAL");

            await RegistrarCierreSeguro(
                idConversacion, respuestaNegativa, "RECHAZO_PRODUCTO_EXPLICITO",
                idInteresadoRegistrado);
            return RespuestaCierre(idConversacion, respuestaNegativa);
        }

        // =====================================================
        // 5A. ENLACES EXTERNOS SIN CONTENIDO VERIFICABLE
        //
        // Un link externo (Facebook, TikTok, Instagram, etc.) NO permite
        // saber qué muestra. Evitamos derivarlo al catálogo, a la IA o
        // a un modelo de una conversación previa. El cliente debe
        // identificar el producto en un mensaje propio sin el enlace.
        // Las publicaciones nativas de TuVendedor con ID reconocido
        // mantienen su flujo original.
        // =====================================================

        if (!idPublicacionExplicita.HasValue &&
            EsEnlaceExternoNoVerificable(request.Mensaje) &&
            !EsTipoMensaje(request, "IMAGEN") &&
            !EsTipoMensaje(request, "DOCUMENTO"))
        {
            await _repository.LimpiarProductoContexto(idConversacion);
            var respuestaEnlace = await _editorPrompts.ObtenerObligatorio("ENLACE_SIN_ACCESO");
            await _enlaces.MarcarPendiente(idConversacion);

            await _repository.RegistrarMensaje(idConversacion, "IA", respuestaEnlace);
            _logger.LogInformation(
                "Panambí solicitó aclaración por enlace externo. Conversación {IdConversacion}",
                idConversacion);

            return new MotoConversacionResponseDto
            {
                IdConversacion = idConversacion,
                IdPublicacion = null,
                Marca = null,
                Modelo = null,
                Respuesta = respuestaEnlace,
                RequierePublicacion = false
            };
        }

        // Si el cliente responde «ese», «el del video» o «¿cuánto cuesta?»
        // después de la aclaración, no debemos interpretar un modelo
        // antiguo de la conversación como si fuera el del enlace.
        if (!idPublicacionExplicita.HasValue &&
            EsReferenciaAmbiguaAlEnlace(request.Mensaje))
        {
            if (await _enlaces.EstaPendiente(idConversacion))
            {
                var respuestaAclaracion = await _editorPrompts.ObtenerObligatorio("ENLACE_ACLARACION");
                await _repository.RegistrarMensaje(idConversacion, "IA", respuestaAclaracion);
                return new MotoConversacionResponseDto
                {
                    IdConversacion = idConversacion,
                    Respuesta = respuestaAclaracion,
                    RequierePublicacion = false
                };
            }
        }

        // El cliente identificó el producto: la aclaración del enlace deja de estar pendiente.
        // Un enlace nuevo vuelve a activar el estado antes de este punto.
        if (await _enlaces.EstaPendiente(idConversacion) &&
            !EsEnlaceExternoNoVerificable(request.Mensaje) &&
            !EsReferenciaAmbiguaAlEnlace(request.Mensaje))
        {
            await _enlaces.LimpiarPendiente(idConversacion);
        }

        // =====================================================
        // 5B. SALUDO CASUAL: PRIORIDAD ANTES DEL MARKETPLACE
        //
        // Si el cliente sólo saluda, NO se ejecuta la búsqueda de
        // publicaciones y NO se reutiliza un inmueble anterior.
        // La solicitud activa de crédito, el modo HUMANO y los cierres
        // comerciales ya se comprobaron antes de llegar aquí.
        // Un enlace/ID de publicación explícito no es un saludo casual.
        // =====================================================

        // Intenciones sociales editables desde la pantalla de prompts.
        // La clasificación solo puede interrumpir la búsqueda cuando NO hay
        // una petición comercial explícita ni una publicación enlazada.
        if (!idPublicacionExplicita.HasValue &&
            !ContieneSolicitudComercialExplicita(request.Mensaje))
        {
            var diccionario = await _editorPrompts.ObtenerPublicado("DICCIONARIO_SOCIAL");
            var intencion = DetectarIntencionSocial(request.Mensaje, diccionario);
            // Contingencia conservadora para charla no reconocida: no buscar
            // inmuebles por una pregunta social o mensaje ambiguo.
            if (intencion is null && EsMensajeSocialAmbiguo(request.Mensaje))
                intencion = "ACLARACION";
            if (intencion is not null)
            {
                var codigoRespuesta = intencion switch
                {
                    "SALUDO" => "SALUDO_CASUAL",
                    "RESPONSE" => "RESPUESTA_SOCIAL",
                    "RESPUESTA_SOCIAL" => "RESPUESTA_SOCIAL",
                    "AGRADECIMIENTO" => "RESPUESTA_AGRADECIMIENTO",
                    "DESPEDIDA" => "RESPUESTA_DESPEDIDA",
                    "ACLARACION" => "RESPUESTA_ACLARACION",
                    _ => "SALUDO_CASUAL"
                };
                var respuestaSocial = await _editorPrompts.ObtenerObligatorio(codigoRespuesta);
                // Las restricciones de seguridad siguen siendo técnicas, no editoriales.
                if (respuestaSocial.Length > 500 ||
                    Regex.IsMatch(respuestaSocial, @"https?://|\bTV_PRODUCTO\b|/share/producto/", RegexOptions.IgnoreCase))
                {
                    throw new InvalidOperationException($"Plantilla social no válida: {codigoRespuesta}");
                }
                _logger.LogInformation("Panambi intención social {Intencion} conversación {Conversacion}", intencion, idConversacion);
                await _repository.RegistrarMensaje(idConversacion, "IA", respuestaSocial);
                return new MotoConversacionResponseDto
                {
                    IdConversacion = idConversacion,
                    IdPublicacion = null,
                    Marca = null,
                    Modelo = null,
                    Respuesta = respuestaSocial,
                    RequierePublicacion = false
                };
            }
        }

        // =====================================================
        // 5B. PUBLICACION DE PRODUCTO QUE NO ES MOTO
        // =====================================================

        if (
            idPublicacionExplicita.HasValue
            &&
            esPublicacionMotoExplicita
            ==
            false
            &&
            !SolicitaAtencionHumana(
                request.Mensaje)
        )
        {
            return await ProcesarPublicacionMarketplace(
                idConversacion,
                idPublicacionExplicita.Value,
                request.Mensaje);
        }

        // =====================================================
        // 6. PUBLICACION EXPLICITA
        // =====================================================

        var idPublicacion =
            idPublicacionExplicita;

        if (idPublicacion.HasValue)
        {
            var tipoOperacionPublicacion =
                DetectarInicioOperacion(
                    request.Mensaje);

            if (
                !string.IsNullOrWhiteSpace(
                    tipoOperacionPublicacion)
            )
            {
                var ofertaPublicacion =
                    await _motoOfertaService
                        .ObtenerOfertaPorPublicacion(
                            idPublicacion.Value);

                await _repository
                    .ActualizarContexto(
                        idConversacion,
                        idPublicacion.Value,
                        ofertaPublicacion.Modelo.Id,
                        null);

                var procesoPublicacion =
                    await _solicitudMotoService
                        .Iniciar(
                            idConversacion,
                            ofertaPublicacion.Modelo.Id,
                            idPublicacion.Value,
                            identificador,
                            tipoOperacionPublicacion,
                            cancellationToken);

                await _repository
                    .RegistrarMensaje(
                        idConversacion,
                        "IA",
                        procesoPublicacion.Respuesta);

                await RegistrarInteresadoSeguro(
                    new InteresadoWhatsAppEventoRequest
                    {
                        IdConversacion =
                            idConversacion,

                        IdModeloProducto =
                            ofertaPublicacion.Modelo.Id,

                        IdPublicacion =
                            idPublicacion.Value,

                        TipoConsulta =
                            tipoOperacionPublicacion,

                        EstadoConsulta =
                            EsCreditoOperacion(
                                tipoOperacionPublicacion)
                                ? "CREDITO_EN_PROCESO"
                                : "CONTADO_EN_PROCESO",

                        TipoOperacion =
                            tipoOperacionPublicacion,

                        IdSolicitudOperacion =
                            procesoPublicacion.IdSolicitud,

                        PasoOperacion =
                            procesoPublicacion.PasoActual,

                        MensajeCliente =
                            request.Mensaje,

                        Respuesta =
                            procesoPublicacion.Respuesta,

                        MotivoSeguimiento =
                            "Cliente inició una operación desde una publicación.",

                        EsEntradaCliente =
                            false
                    });

                return new MotoConversacionResponseDto
                {
                    IdConversacion = idConversacion,
                    IdPublicacion = idPublicacion.Value,
                    Marca = ofertaPublicacion.Modelo.Marca,
                    Modelo = $"{ofertaPublicacion.Modelo.Marca} {ofertaPublicacion.Modelo.Nombre}",
                    Respuesta = procesoPublicacion.Respuesta,
                    RequierePublicacion = false
                };
            }

            return await ProcesarPorPublicacion(
                idConversacion,
                idPublicacion.Value,
                request.Mensaje,
                cancellationToken);
        }

        // =====================================================
        // 6A. IMAGEN FUERA DE UNA SOLICITUD ACTIVA
        // =====================================================

        if (
            EsTipoMensaje(
                request,
                "IMAGEN")
        )
        {
            return await ProcesarImagenFueraDeSolicitud(
                idConversacion,
                request,
                cancellationToken);
        }

        // =====================================================
        // 7. CATALOGO REAL DESDE BBDD
        // =====================================================

        var modelos =
            await _repository
                .ObtenerModelosMotoActivos();

        var contextoActual =
            await _repository
                .ObtenerContextoActual(
                    idConversacion);

        var contextoVigente =
            EsContextoProductoVigente(
                contextoActual);

        // =====================================================
        // 7. PRIMERO BUSCAR SI MENCIONO UN MODELO REAL
        //
        // IMPORTANTE:
        // esto va ANTES de detectar "catalogo".
        // Asi "que precio tiene el modelo Viva 110"
        // reconoce VIVA 110 y no muestra todo el catalogo.
        // =====================================================

        var coincidencias =
            ResolverModelosDesdeTexto(
                request.Mensaje,
                modelos);

        // =====================================================
        // 7A. MARKETPLACE GENERAL (NO MOTO)
        //
        // La IA interpreta el lenguaje; la BBDD decide qué existe.
        // Si no reconocimos una moto real, primero intentamos resolver
        // la consulta contra Publicaciones activas de MARKETPLACE.
        // Esto evita que "me interesan lotes" termine en KENTON.
        // =====================================================

        if (coincidencias.Count == 0)
        {
            var respuestaMarketplace =
                await ProcesarMarketplaceSiCorresponde(
                    idConversacion,
                    request.Mensaje,
                    contextoActual,
                    contextoVigente,
                    cancellationToken);

            if (respuestaMarketplace is not null)
            {
                return respuestaMarketplace;
            }
        }

        var consultaPromociones =
            EsConsultaPromociones(
                request.Mensaje);

        // =====================================================
        // 7A. CONSULTA GENERAL DE PROMOCIONES
        //
        // IMPORTANTE:
        // - Si el cliente menciona UN modelo concreto, dejamos
        //   que el flujo normal responda por ese modelo.
        // - Si pregunta genericamente "que hay en promo?",
        //   respondemos SOLO con modelos que realmente tienen
        //   promo vigente en BBDD.
        // - Si menciona una marca, filtramos por esa marca.
        // - NO usamos Ollama para esto.
        // =====================================================

        int? idModeloContextoParaPromo =
            null;

        if (
            consultaPromociones
            &&
            coincidencias.Count != 1
            &&
            contextoVigente
        )
        {
            idModeloContextoParaPromo =
                contextoActual
                    ?.IdModeloProductoActual;
        }

        var esCatalogoPromociones =
            consultaPromociones
            &&
            (
                EsConsultaCatalogoPromociones(
                    request.Mensaje)
                ||
                !idModeloContextoParaPromo.HasValue
            );

        if (
            esCatalogoPromociones
            &&
            coincidencias.Count != 1
        )
        {
            var marcaPromo =
                ResolverMarcaDesdeTexto(
                    request.Mensaje,
                    modelos);

            var modelosPromo =
                await _motoOfertaService
                    .ObtenerModelosConPromoVigente();

            if (
                !string.IsNullOrWhiteSpace(
                    marcaPromo)
            )
            {
                modelosPromo =
                    modelosPromo
                        .Where(
                            x =>
                                string.Equals(
                                    x.Marca,
                                    marcaPromo,
                                    StringComparison.OrdinalIgnoreCase))
                        .ToList();
            }

            /*
             * Si el texto ya acotó una familia de modelos
             * (por ejemplo "SHARK en promo"), mostramos solo
             * las coincidencias de esa familia que realmente
             * tienen promo vigente.
             */
            if (
                coincidencias.Count > 1
                &&
                (
                    string.IsNullOrWhiteSpace(
                        marcaPromo)
                    ||
                    TieneReferenciaModeloAdicional(
                        request.Mensaje,
                        marcaPromo)
                )
            )
            {
                var idsCoincidentes =
                    coincidencias
                        .Select(
                            x => x.IdModeloProducto)
                        .ToHashSet();

                modelosPromo =
                    modelosPromo
                        .Where(
                            x =>
                                idsCoincidentes.Contains(
                                    x.IdModeloProducto))
                        .ToList();
            }

            await _repository
                .LimpiarProductoContexto(
                    idConversacion);

            var respuestaPromo =
                ConstruirRespuestaPromociones(
                    modelosPromo,
                    marcaPromo);

            await _repository
                .RegistrarMensaje(
                    idConversacion,
                    "IA",
                    respuestaPromo);

            return new MotoConversacionResponseDto
            {
                IdConversacion = idConversacion,
                IdPublicacion = null,
                Marca = marcaPromo,
                Modelo = null,
                Respuesta = respuestaPromo,
                RequierePublicacion = false
            };
        }

        // Los saludos exclusivamente sociales se resolvieron en 5B.
        // Consultas reales de catálogo continúan por este router.

        if (
            coincidencias.Count == 0
            &&
            EsConsultaGenericaMarcas(
                request.Mensaje)
        )
        {
            await _repository
                .LimpiarProductoContexto(
                    idConversacion);

            var respuestaGeneral =
                ConstruirRespuestaMarcasDisponibles(
                    modelos,
                    await _editorPrompts.ObtenerObligatorio("MOTO_CONSULTA_MARCA_INTRO"));

            await _repository
                .RegistrarMensaje(
                    idConversacion,
                    "IA",
                    respuestaGeneral);

            return new MotoConversacionResponseDto
            {
                IdConversacion = idConversacion,
                IdPublicacion = null,
                Marca = null,
                Modelo = null,
                Respuesta = respuestaGeneral,
                RequierePublicacion = false
            };
        }

        // =====================================================
        // 7B.1 CONSULTA EXPLICITA POR MARCA
        //
        // Toda afirmacion sobre marcas disponibles sale SOLO de BBDD.
        // Qwen no participa en este flujo.
        //
        // Ejemplos:
        // "la marca Leopard no tienen?"
        // "tienen Yamaha?"
        // "trabajan con Honda?"
        // "venden Kenton?"
        // =====================================================

        var consultaMarcaCatalogo =
            ResolverConsultaMarcaCatalogo(
                request.Mensaje,
                modelos,
                permitirMarcaDesconocida:
                    coincidencias.Count == 0);

        if (
            consultaMarcaCatalogo.EsConsulta
        )
        {
            await _repository
                .LimpiarProductoContexto(
                    idConversacion);

            var respuestaMarcaConsultada =
                !string.IsNullOrWhiteSpace(
                    consultaMarcaCatalogo.MarcaCatalogo)
                    ? ConstruirRespuestaModelosMarca(
                        modelos,
                        consultaMarcaCatalogo.MarcaCatalogo!)
                    : ConstruirRespuestaMarcaNoDisponible(
                        modelos,
                        consultaMarcaCatalogo.MarcaSolicitada);

            await _repository
                .RegistrarMensaje(
                    idConversacion,
                    "IA",
                    respuestaMarcaConsultada);

            return new MotoConversacionResponseDto
            {
                IdConversacion = idConversacion,
                IdPublicacion = null,
                Marca = consultaMarcaCatalogo.MarcaCatalogo,
                Modelo = null,
                Respuesta = respuestaMarcaConsultada,
                RequierePublicacion = false
            };
        }

        /*
         * Las consultas de otros productos ya fueron atendidas arriba
         * por ProcesarMarketplaceSiCorresponde(). Si llegamos hasta acá,
         * seguimos con el router general/moto sin derivar automáticamente.
         */

        /*
         * Si el texto parece nombrar un producto nuevo que no pudimos
         * resolver contra el catálogo de motos, el router semántico
         * decide si es una moto desconocida u otro tipo de producto.
         * Esto evita que una frase como "precio de una aspiradora"
         * termine usando la última moto del contexto.
         */
        if (
            coincidencias.Count == 0
            &&
            PareceConsultaProductoNuevoNoResuelto(
                request.Mensaje)
        )
        {
            return await ProcesarConversacionAbierta(
                idConversacion,
                request.Mensaje,
                modelos,
                cancellationToken);
        }

        /*
         * Si no reconocimos una moto real del catálogo, el router general
         * decide si se trata de otro producto, una moto no identificada
         * o una conversación general.
         *
         * La única excepción es un follow-up comercial válido del modelo
         * actual y con contexto todavía vigente.
         */
        if (
            coincidencias.Count == 0
            &&
            !(
                contextoVigente
                &&
                EsSeguimientoDelModeloActual(
                    request.Mensaje)
            )
            &&
            !EsConsultaOtrosModelos(
                request.Mensaje)
            &&
            !PareceCambioDeModelo(
                request.Mensaje)
        )
        {
            return await ProcesarConversacionAbierta(
                idConversacion,
                request.Mensaje,
                modelos,
                cancellationToken);
        }

        var tipoOperacionSolicitada =
            DetectarInicioOperacion(
                request.Mensaje);

        if (
            !string.IsNullOrWhiteSpace(
                tipoOperacionSolicitada)
        )
        {
            MotoModeloCandidatoDto? modeloSolicitud =
                coincidencias.Count == 1
                    ? coincidencias[0]
                    : null;

            if (
                modeloSolicitud is null
                &&
                contextoVigente
                &&
                contextoActual
                    ?.IdModeloProductoActual
                    is int idModeloContexto
                &&
                EsSeguimientoDelModeloActual(
                    request.Mensaje)
            )
            {
                modeloSolicitud =
                    modelos.FirstOrDefault(
                        x =>
                            x.IdModeloProducto
                            ==
                            idModeloContexto);
            }

            if (modeloSolicitud is null)
            {
                var respuestaElegirModelo =
                    ConstruirRespuestaGuiada(
                        modelos,
                        (await _editorPrompts.ObtenerObligatorio("MOTO_ELEGIR_MODELO_OPERACION"))
                            .Replace("{OPERACION}", tipoOperacionSolicitada.ToLowerInvariant(), StringComparison.Ordinal));

                await _repository
                    .RegistrarMensaje(
                        idConversacion,
                        "IA",
                        respuestaElegirModelo);

                return new MotoConversacionResponseDto
                {
                    IdConversacion = idConversacion,
                    IdPublicacion = null,
                    Marca = null,
                    Modelo = null,
                    Respuesta = respuestaElegirModelo,
                    RequierePublicacion = false
                };
            }

            await _repository
                .ActualizarContexto(
                    idConversacion,
                    modeloSolicitud.IdPublicacion,
                    modeloSolicitud.IdModeloProducto,
                    null);

            var proceso =
                await _solicitudMotoService
                    .Iniciar(
                        idConversacion,
                        modeloSolicitud.IdModeloProducto,
                        modeloSolicitud.IdPublicacion,
                        identificador,
                        tipoOperacionSolicitada,
                        cancellationToken);

            await _repository
                .RegistrarMensaje(
                    idConversacion,
                    "IA",
                    proceso.Respuesta);

            await RegistrarInteresadoSeguro(
                new InteresadoWhatsAppEventoRequest
                {
                    IdConversacion =
                        idConversacion,

                    IdModeloProducto =
                        modeloSolicitud.IdModeloProducto,

                    IdPublicacion =
                        modeloSolicitud.IdPublicacion,

                    TipoConsulta =
                        tipoOperacionSolicitada,

                    EstadoConsulta =
                        EsCreditoOperacion(
                            tipoOperacionSolicitada)
                            ? "CREDITO_EN_PROCESO"
                            : "CONTADO_EN_PROCESO",

                    TipoOperacion =
                        tipoOperacionSolicitada,

                    IdSolicitudOperacion =
                        proceso.IdSolicitud,

                    PasoOperacion =
                        proceso.PasoActual,

                    MensajeCliente =
                        request.Mensaje,

                    Respuesta =
                        proceso.Respuesta,

                    MotivoSeguimiento =
                        "Cliente inició una operación sobre un modelo del catálogo.",

                    EsEntradaCliente =
                        false
                });

            return new MotoConversacionResponseDto
            {
                IdConversacion = idConversacion,
                IdPublicacion = modeloSolicitud.IdPublicacion,
                Marca = modeloSolicitud.Marca,
                Modelo = $"{modeloSolicitud.Marca} {modeloSolicitud.Modelo}",
                Respuesta = proceso.Respuesta,
                RequierePublicacion = false
            };
        }

        if (
            coincidencias.Count == 1
        )
        {
            return await ProcesarPorModelo(
                idConversacion,
                coincidencias[0],
                request.Mensaje,
                cancellationToken);
        }

        if (
            coincidencias.Count > 1
        )
        {
            // Si el cliente solamente menciono una marca
            // (ej.: "Kenton"), mostramos los modelos reales
            // de esa marca en vez de decir que no entendimos.
            var primeraMarca =
                coincidencias[0].Marca;

            var mismaMarca =
                coincidencias.All(
                    x =>
                        string.Equals(
                            x.Marca,
                            primeraMarca,
                            StringComparison.OrdinalIgnoreCase));

            if (
                mismaMarca
                &&
                EsSeleccionSoloMarca(
                    request.Mensaje,
                    primeraMarca)
            )
            {
                await _repository
                    .LimpiarProductoContexto(
                        idConversacion);

                var respuestaMarca =
                    ConstruirRespuestaModelosMarca(
                        modelos,
                        primeraMarca);

                await _repository
                    .RegistrarMensaje(
                        idConversacion,
                        "IA",
                        respuestaMarca);

                return new MotoConversacionResponseDto
                {
                    IdConversacion = idConversacion,
                    IdPublicacion = null,
                    Marca = primeraMarca,
                    Modelo = null,
                    Respuesta = respuestaMarca,
                    RequierePublicacion = false
                };
            }

            await _repository
                .LimpiarProductoContexto(
                    idConversacion);

            var respuestaAmbigua =
                ConstruirPreguntaAmbigua(
                    coincidencias);

            await _repository
                .RegistrarMensaje(
                    idConversacion,
                    "IA",
                    respuestaAmbigua);

            return new MotoConversacionResponseDto
            {
                IdConversacion = idConversacion,
                IdPublicacion = null,
                Marca = null,
                Modelo = null,
                Respuesta = respuestaAmbigua,
                RequierePublicacion = false
            };
        }

        // =====================================================
        // 8. CONSULTA GENERAL DE MODELOS / CATALOGO
        // =====================================================

        if (
            EsConsultaOtrosModelos(
                request.Mensaje)
        )
        {
            /*
             * CONSULTA DE CATALOGO:
             *
             * SIEMPRE mostramos TODOS los modelos activos
             * recuperados desde BBDD.
             *
             * No filtramos por el modelo que quedó en contexto.
             * No excluimos el modelo actual.
             * No dejamos que Qwen invente opciones.
             */

            var respuestaCatalogo =
                ConstruirRespuestaOtrosModelos(
                    modelos,
                    null);

            await _repository
                .RegistrarMensaje(
                    idConversacion,
                    "IA",
                    respuestaCatalogo);

            return new MotoConversacionResponseDto
            {
                IdConversacion = idConversacion,
                IdPublicacion = null,
                Marca = null,
                Modelo = null,
                Respuesta = respuestaCatalogo,
                RequierePublicacion = false
            };
        }

        // =====================================================
        // 9. DETECTAR QUE QUIERE CAMBIAR DE MODELO
        // =====================================================

        if (
            PareceCambioDeModelo(
                request.Mensaje)
        )
        {
            await _repository
                .LimpiarProductoContexto(
                    idConversacion);

            var respuestaCambio =
                ConstruirRespuestaGuiada(
                    modelos,
                    await _editorPrompts.ObtenerObligatorio("MOTO_ELEGIR_CAMBIO"));

            await _repository
                .RegistrarMensaje(
                    idConversacion,
                    "IA",
                    respuestaCambio);

            return new MotoConversacionResponseDto
            {
                IdConversacion = idConversacion,
                IdPublicacion = null,
                Marca = null,
                Modelo = null,
                Respuesta = respuestaCambio,
                RequierePublicacion = false
            };
        }

        // =====================================================
        // 10. RECUPERAR MODELO DEL CONTEXTO SOLO EN FOLLOW-UP
        // =====================================================

        if (
            contextoVigente
            &&
            contextoActual
                ?.IdModeloProductoActual
                is int idModeloActual
            &&
            EsSeguimientoDelModeloActual(
                request.Mensaje)
        )
        {
            var modeloActual =
                modelos.FirstOrDefault(
                    x =>
                        x.IdModeloProducto
                        ==
                        idModeloActual);

            if (modeloActual is not null)
            {
                return await ProcesarPorModelo(
                    idConversacion,
                    modeloActual,
                    request.Mensaje,
                    cancellationToken);
            }
        }

        // =====================================================
        // 11. CONTEXTO DE PUBLICACION SOLO EN FOLLOW-UP
        // =====================================================

        if (
            contextoVigente
            &&
            contextoActual
                ?.IdPublicacion
                is int idPublicacionContexto
            &&
            EsSeguimientoDelModeloActual(
                request.Mensaje)
        )
        {
            return await ProcesarPorPublicacion(
                idConversacion,
                idPublicacionContexto,
                request.Mensaje,
                cancellationToken);
        }

        // =====================================================
        // 12. PREGUNTA COMERCIAL SIN MODELO ELEGIDO
        //
        // Ejemplo:
        // "me pasas sus precios?"
        // "que precio tienen?"
        // "que cuotas tienen?"
        //
        // NO PASAMOS A HUMANO.
        // Pedimos elegir un modelo usando datos de BBDD.
        // =====================================================

        if (
            EsConsultaPrecioOCuotas(
                request.Mensaje)
        )
        {
            var respuestaSeleccion =
                ConstruirRespuestaGuiada(
                    modelos,
                    await _editorPrompts.ObtenerObligatorio("MOTO_SOLICITAR_MODELO_PRECIO"));

            await _repository
                .RegistrarMensaje(
                    idConversacion,
                    "IA",
                    respuestaSeleccion);

            await RegistrarInteresadoSeguro(
                new InteresadoWhatsAppEventoRequest
                {
                    IdConversacion =
                        idConversacion,

                    TipoConsulta =
                        "PRECIO_CUOTAS",

                    EstadoConsulta =
                        "ESPERANDO_MODELO",

                    Respuesta =
                        respuestaSeleccion,

                    MotivoSeguimiento =
                        "Consultó precio/cuotas pero todavía no definió modelo.",

                    EsEntradaCliente =
                        false
                });

            return new MotoConversacionResponseDto
            {
                IdConversacion = idConversacion,
                IdPublicacion = null,
                Marca = null,
                Modelo = null,
                Respuesta = respuestaSeleccion,
                RequierePublicacion = false
            };
        }

        // El saludo casual ya se resolvió al comienzo (etapa 5B).

        // =====================================================
        // 14. CONVERSACION ABIERTA / ROUTER GENERAL
        // =====================================================

        return await ProcesarConversacionAbierta(
            idConversacion,
            request.Mensaje,
            modelos,
            cancellationToken);
    }


    // =========================================================
    // PROCESAR IMAGEN FUERA DE SOLICITUD
    // =========================================================

    private async Task<MotoConversacionResponseDto>
        ProcesarImagenFueraDeSolicitud(
            int idConversacion,
            MotoConversacionRequest request,
            CancellationToken cancellationToken)
    {
        var modelos =
            await _repository
                .ObtenerModelosMotoActivos();

        if (
            !string.IsNullOrWhiteSpace(
                request.Mensaje)
        )
        {
            var porTexto =
                ResolverModelosDesdeTexto(
                    request.Mensaje,
                    modelos);

            if (porTexto.Count == 1)
            {
                return await ProcesarPorModelo(
                    idConversacion,
                    porTexto[0],
                    request.Mensaje,
                    cancellationToken);
            }

            if (porTexto.Count > 1)
            {
                await _repository
                    .LimpiarProductoContexto(
                        idConversacion);

                var respuestaTextoAmbiguo =
                    ConstruirPreguntaAmbigua(
                        porTexto);

                await _repository
                    .RegistrarMensaje(
                        idConversacion,
                        "IA",
                        respuestaTextoAmbiguo);

                return new MotoConversacionResponseDto
                {
                    IdConversacion = idConversacion,
                    IdPublicacion = null,
                    Marca = null,
                    Modelo = null,
                    Respuesta = respuestaTextoAmbiguo,
                    RequierePublicacion = false
                };
            }
        }

        var analisis =
            await _ollamaService
                .AnalizarImagenMoto(
                    request.MediaBase64 ?? string.Empty,
                    request.MediaMimeType,
                    request.Mensaje,
                    cancellationToken);

        if (analisis is null)
        {
            await _repository
                .LimpiarProductoContexto(
                    idConversacion);

            var respuestaSinVision = await _editorPrompts.ObtenerObligatorio("IMAGEN_NO_ANALIZABLE");

            await _repository
                .RegistrarMensaje(
                    idConversacion,
                    "IA",
                    respuestaSinVision);

            return new MotoConversacionResponseDto
            {
                IdConversacion = idConversacion,
                IdPublicacion = null,
                Marca = null,
                Modelo = null,
                Respuesta = respuestaSinVision,
                RequierePublicacion = false
            };
        }

        if (
            string.Equals(
                analisis.TipoContenido,
                "OTRO_PRODUCTO",
                StringComparison.OrdinalIgnoreCase)
        )
        {
            var textoMarketplaceDetectado =
                string.Join(
                    " ",
                    new[]
                    {
                        request.Mensaje,
                        analisis.TextoVisible,
                        analisis.DescripcionBreve
                    }
                    .Where(x => !string.IsNullOrWhiteSpace(x)));

            var respuestaMarketplaceImagen =
                await ProcesarMarketplaceSiCorresponde(
                    idConversacion,
                    textoMarketplaceDetectado,
                    null,
                    false,
                    cancellationToken);

            if (respuestaMarketplaceImagen is not null)
            {
                return respuestaMarketplaceImagen;
            }

            await _repository
                .ActualizarContextoMarketplace(
                    idConversacion,
                    null);

            var respuestaOtroProductoImagen = await _editorPrompts.ObtenerObligatorio("IMAGEN_OTRO_PRODUCTO");

            await _repository
                .RegistrarMensaje(
                    idConversacion,
                    "IA",
                    respuestaOtroProductoImagen);

            return new MotoConversacionResponseDto
            {
                IdConversacion = idConversacion,
                IdPublicacion = null,
                Marca = null,
                Modelo = null,
                Respuesta = respuestaOtroProductoImagen,
                RequierePublicacion = false
            };
        }

        if (!analisis.EsMoto)
        {
            await _repository
                .LimpiarProductoContexto(
                    idConversacion);

            var respuestaNoMoto = await _editorPrompts.ObtenerObligatorio("IMAGEN_SIN_MOTO");

            await _repository
                .RegistrarMensaje(
                    idConversacion,
                    "IA",
                    respuestaNoMoto);

            return new MotoConversacionResponseDto
            {
                IdConversacion = idConversacion,
                IdPublicacion = null,
                Marca = null,
                Modelo = null,
                Respuesta = respuestaNoMoto,
                RequierePublicacion = false
            };
        }

        var textoDetectado =
            string.Join(
                " ",
                new[]
                {
                    analisis.Marca,
                    analisis.Modelo,
                    analisis.TextoVisible
                }
                .Where(
                    x => !string.IsNullOrWhiteSpace(x)));

        var coincidencias =
            ResolverModelosDesdeTexto(
                textoDetectado,
                modelos);

        if (
            coincidencias.Count == 1
            &&
            analisis.Confianza
            >=
            CONFIANZA_MINIMA_IMAGEN_MODELO
        )
        {
            var modelo =
                coincidencias[0];

            /*
             * La imagen ya identificó con suficiente confianza
             * un modelo REAL del catálogo.
             *
             * No volvemos a preguntar si quiere precio o cuotas:
             * reutilizamos el flujo comercial determinístico del modelo,
             * que consulta la BBDD y devuelve directamente el precio
             * al contado y/o los planes vigentes disponibles.
             *
             * request.Mensaje normalmente viene vacío para una imagen.
             * En ese caso DebeResponderOfertaSinIA(...) devuelve true
             * y ConstruirRespuestaDirectaModelo(...) arma la cotización
             * sin depender de Ollama.
             */
            return await ProcesarPorModelo(
                idConversacion,
                modelo,
                request.Mensaje,
                cancellationToken);
        }

        await _repository
            .LimpiarProductoContexto(
                idConversacion);

        if (coincidencias.Count > 1)
        {
            var respuestaAmbigua =
                ConstruirPreguntaAmbigua(
                    coincidencias);

            await _repository
                .RegistrarMensaje(
                    idConversacion,
                    "IA",
                    respuestaAmbigua);

            return new MotoConversacionResponseDto
            {
                IdConversacion = idConversacion,
                IdPublicacion = null,
                Marca = analisis.Marca,
                Modelo = null,
                Respuesta = respuestaAmbigua,
                RequierePublicacion = false
            };
        }

        var posibleNombre =
            string.Join(
                " ",
                new[]
                {
                    analisis.Marca,
                    analisis.Modelo
                }
                .Where(
                    x => !string.IsNullOrWhiteSpace(x)));

        var respuestaNoCatalogo =
            string.IsNullOrWhiteSpace(
                posibleNombre)
                ? Texto("IMAGEN_MOTO_SIN_MARCA")
                : Texto("IMAGEN_MOTO_NO_CATALOGO", ("POSIBLE_MODELO", posibleNombre));

        await _repository
            .RegistrarMensaje(
                idConversacion,
                "IA",
                respuestaNoCatalogo);

        return new MotoConversacionResponseDto
        {
            IdConversacion = idConversacion,
            IdPublicacion = null,
            Marca = analisis.Marca,
            Modelo = analisis.Modelo,
            Respuesta = respuestaNoCatalogo,
            RequierePublicacion = false
        };
    }



    // =========================================================
    // MARKETPLACE GENERAL (PRODUCTOS NO MOTO)
    // =========================================================

    private async Task<MotoConversacionResponseDto?>
        ProcesarMarketplaceSiCorresponde(
            int idConversacion,
            string? mensaje,
            MotoConversacionContextoDto? contextoActual,
            bool contextoVigente,
            CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(mensaje))
        {
            return null;
        }

        // "el primero", "el segundo", etc. se resuelve contra la última
        // lista real que Panambí envió en esta conversación.
        var idSeleccionHistorial =
            await ResolverSeleccionMarketplaceDesdeHistorial(
                idConversacion,
                mensaje);

        if (idSeleccionHistorial.HasValue)
        {
            return await ProcesarPublicacionMarketplace(
                idConversacion,
                idSeleccionHistorial.Value,
                mensaje);
        }

        // Distinguir una búsqueda NUEVA de una consulta a la ficha anterior.
        // "Me podrías dar opciones de terrenos en Areguá" debe buscar en
        // el catálogo real, incluso si el contexto guardado es la publicación 37.
        var solicitaBusquedaNueva = SolicitaNuevaBusquedaMarketplace(mensaje);

        // Si ya tenemos una publicación no-moto en contexto, permitimos
        // seguimientos naturales: "cuánto cuesta", "dónde queda", "pasame",
        // "me interesa", etc., pero NO cuando solicita nuevas opciones.
        if (
            contextoActual?.IdPublicacion
                is int idPublicacionContexto
            &&
            !contextoActual.IdModeloProductoActual.HasValue
            &&
            !solicitaBusquedaNueva
        )
        {
            var esMotoContexto =
                await _repository
                    .EsPublicacionMoto(
                        idPublicacionContexto);

            if (esMotoContexto == false)
            {
                return await ProcesarPublicacionMarketplace(
                    idConversacion,
                    idPublicacionContexto,
                    mensaje);
            }
        }

        // Preguntas generales como "qué venden" o "qué productos tienen"
        // se responden con categorías que realmente poseen publicaciones
        // activas en MARKETPLACE.
        if (EsConsultaGeneralMarketplace(mensaje))
        {
            var categorias =
                await _publicacionRepository
                    .ObtenerCategoriasMarketplaceActivasParaIA();

            await _repository
                .ActualizarContextoMarketplace(
                    idConversacion,
                    null);

            var respuestaCategorias =
                ConstruirRespuestaCategoriasMarketplace(
                    categorias);

            await _repository
                .RegistrarMensaje(
                    idConversacion,
                    "IA",
                    respuestaCategorias);

            return new MotoConversacionResponseDto
            {
                IdConversacion = idConversacion,
                IdPublicacion = null,
                Marca = null,
                Modelo = null,
                Respuesta = respuestaCategorias,
                RequierePublicacion = false
            };
        }

        var terminos =
            ExtraerTerminosBusquedaMarketplace(
                mensaje);

        if (terminos.Count == 0)
        {
            return null;
        }

        // La intención y la ubicación son condiciones OBLIGATORIAS cuando
        // el cliente las mencionó; jamás ofrecer anuncios por compartir la ciudad.
        var rubroSolicitado = InferirRubroBusquedaMarketplace(mensaje);
        var zonaSolicitada = ExtraerZonaBusquedaMarketplace(mensaje);
        var publicaciones =
            await _publicacionRepository
                .BuscarPublicacionesMarketplaceParaIA(
                    terminos,
                    5,
                    rubroSolicitado,
                    zonaSolicitada);

        if (publicaciones.Count > 0)
        {
            var idContexto =
                publicaciones.Count == 1
                    ? publicaciones[0].Id
                    : (int?)null;

            await _repository
                .ActualizarContextoMarketplace(
                    idConversacion,
                    idContexto);

            var respuesta =
                await ConstruirRespuestaPublicacionesMarketplace(
                    publicaciones);

            await _repository
                .RegistrarMensaje(
                    idConversacion,
                    "IA",
                    respuesta);

            return new MotoConversacionResponseDto
            {
                IdConversacion = idConversacion,
                IdPublicacion = idContexto,
                Marca = null,
                Modelo = null,
                Respuesta = respuesta,
                RequierePublicacion = false
            };
        }

        // Si no encontramos nada, solamente afirmamos "no encontré" cuando
        // el mensaje es claramente de otro producto o menciona una categoría
        // real del Marketplace. Así no confundimos una marca de moto desconocida
        // con un producto no-moto.
        var categoriasDisponibles =
            await _publicacionRepository
                .ObtenerCategoriasMarketplaceActivasParaIA();

        var esConsultaOtroProducto =
            // Si pidió opciones de un rubro específico y la búsqueda devolvió
            // 0 resultados, el sistema DEBE informarlo. No pasar a Ollama,
            // ni reciclar una publicación anterior por no hallar coincidencias.
            (solicitaBusquedaNueva && !string.IsNullOrWhiteSpace(rubroSolicitado))
            || EsConsultaComercialEvidenteOtroProducto(mensaje)
            || MensajeMencionaCategoriaMarketplace(mensaje, categoriasDisponibles);

        if (!esConsultaOtroProducto)
        {
            return null;
        }

        await _repository
            .ActualizarContextoMarketplace(
                idConversacion,
                null);

        var introduccionSinResultados = await _editorPrompts.ObtenerObligatorio("MARKETPLACE_SIN_RESULTADOS");
        var descripcionBuscada = rubroSolicitado switch
        {
            "TERRENO" => "terrenos",
            "CASA" => "casas",
            "DEPARTAMENTO" => "departamentos",
            "INMUEBLE" => "inmuebles",
            _ => "productos"
        };
        var busquedaSolicitada = descripcionBuscada
            + (string.IsNullOrWhiteSpace(zonaSolicitada) ? "" : $" en {zonaSolicitada.ToLowerInvariant()}");
        var respuestaSinResultados = introduccionSinResultados.Trim()
            + $"\nNo encontré publicaciones activas de {busquedaSolicitada} que coincidan con lo que pediste."
            + "\nPodés ver el catálogo en https://tuvendedor.com.py o decirme otra zona para buscar.";

        await _repository
            .RegistrarMensaje(
                idConversacion,
                "IA",
                respuestaSinResultados);

        return new MotoConversacionResponseDto
        {
            IdConversacion = idConversacion,
            IdPublicacion = null,
            Marca = null,
            Modelo = null,
            Respuesta = respuestaSinResultados,
            RequierePublicacion = false
        };
    }


    private async Task<MotoConversacionResponseDto>
        ProcesarPublicacionMarketplace(
            int idConversacion,
            int idPublicacion,
            string? mensajeCliente)
    {
        var publicacion =
            await _publicacionRepository
                .ObtenerPublicacionMarketplaceParaIA(
                    idPublicacion);

        if (publicacion is null)
        {
            await _repository
                .ActualizarContextoMarketplace(
                    idConversacion,
                    null);

            var respuestaNoDisponible = await _editorPrompts.ObtenerObligatorio("MARKETPLACE_PUBLICACION_INACTIVA");

            await _repository
                .RegistrarMensaje(
                    idConversacion,
                    "IA",
                    respuestaNoDisponible);

            return new MotoConversacionResponseDto
            {
                IdConversacion = idConversacion,
                IdPublicacion = null,
                Marca = null,
                Modelo = null,
                Respuesta = respuestaNoDisponible,
                RequierePublicacion = false
            };
        }

        await _repository
            .ActualizarContextoMarketplace(
                idConversacion,
                publicacion.Id);

        var respuesta = ConstruirRespuestaDetalleMarketplace(publicacion, mensajeCliente);

        // Escrituras y títulos requieren documentación: no afirmamos que
        // un inmueble está listo para transferir sin constancia en la ficha.
        var consultaNormalizada = NormalizarTexto(mensajeCliente ?? "");
        var pideDocumentacion = EsConsultaDocumentacionInmueble(consultaNormalizada);
        var pideVisita = EsSolicitudVisitaInmueble(consultaNormalizada);
        if (pideDocumentacion || pideVisita)
        {
            // Nunca confirmar documentación ni una visita de forma automática.
            // Queda en HUMANO para que el vendedor continúe desde WhatsApp.
            await _repository.CambiarModoConversacion(idConversacion, "HUMANO");
            respuesta += "\n\nDejo esta consulta con un asesor de TuVendedor para continuar personalmente.";
        }

        await _repository
            .RegistrarMensaje(
                idConversacion,
                "IA",
                respuesta);

        await RegistrarInteresadoSeguro(
            new InteresadoWhatsAppEventoRequest
            {
                IdConversacion = idConversacion,
                IdPublicacion = publicacion.Id,
                TipoConsulta = "MARKETPLACE",
                EstadoConsulta = (pideDocumentacion || pideVisita) ? "DERIVADO_HUMANO" : "CONSULTANDO",
                MensajeCliente = mensajeCliente,
                Respuesta = respuesta,
                MotivoSeguimiento = pideDocumentacion
                    ? "Revisar documentación inmobiliaria en atención humana."
                    : pideVisita
                        ? "Cliente solicita coordinar una visita inmobiliaria. Atención humana."
                        : "Cliente consultó una publicación no-moto del Marketplace.",
                EsEntradaCliente = false
            });

        return new MotoConversacionResponseDto
        {
            IdConversacion = idConversacion,
            IdPublicacion = publicacion.Id,
            Marca = null,
            Modelo = null,
            Respuesta = respuesta,
            RequierePublicacion = false
        };
    }


    private async Task<int?>
        ResolverSeleccionMarketplaceDesdeHistorial(
            int idConversacion,
            string mensaje)
    {
        var indice =
            ObtenerIndiceSeleccionMarketplace(
                mensaje);

        if (!indice.HasValue)
        {
            return null;
        }

        var historial =
            await _repository
                .ObtenerUltimosMensajes(
                    idConversacion,
                    12);

        var ultimoListado =
            historial
                .Where(
                    x =>
                        string.Equals(
                            x.Emisor,
                            "IA",
                            StringComparison.OrdinalIgnoreCase)
                        &&
                        x.Mensaje.Contains(
                            "/share/producto/",
                            StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(
                    x => x.Fecha)
                .FirstOrDefault();

        if (ultimoListado is null)
        {
            return null;
        }

        var ids =
            Regex.Matches(
                    ultimoListado.Mensaje,
                    @"/share/producto/(?<id>\d+)",
                    RegexOptions.IgnoreCase)
                .Select(
                    m =>
                        int.TryParse(
                            m.Groups["id"].Value,
                            out var id)
                            ? id
                            : 0)
                .Where(id => id > 0)
                .Distinct()
                .ToList();

        var posicion =
            indice.Value - 1;

        return posicion >= 0
               && posicion < ids.Count
            ? ids[posicion]
            : null;
    }


    private static int? ObtenerIndiceSeleccionMarketplace(
        string mensaje)
    {
        var texto =
            NormalizarTexto(
                mensaje);

        if (string.IsNullOrWhiteSpace(texto))
        {
            return null;
        }

        if (
            ContieneFrase(texto, "EL PRIMERO")
            || ContieneFrase(texto, "LA PRIMERA")
            || texto == "PRIMERO"
            || texto == "1"
        )
        {
            return 1;
        }

        if (
            ContieneFrase(texto, "EL SEGUNDO")
            || ContieneFrase(texto, "LA SEGUNDA")
            || texto == "SEGUNDO"
            || texto == "2"
        )
        {
            return 2;
        }

        if (
            ContieneFrase(texto, "EL TERCERO")
            || ContieneFrase(texto, "LA TERCERA")
            || texto == "TERCERO"
            || texto == "3"
        )
        {
            return 3;
        }

        if (
            ContieneFrase(texto, "EL CUARTO")
            || ContieneFrase(texto, "LA CUARTA")
            || texto == "CUARTO"
            || texto == "4"
        )
        {
            return 4;
        }

        if (
            ContieneFrase(texto, "EL QUINTO")
            || ContieneFrase(texto, "LA QUINTA")
            || texto == "QUINTO"
            || texto == "5"
        )
        {
            return 5;
        }

        return null;
    }


    // Solo liberar el producto fijado cuando se pide explícitamente otro.
    // Preguntas sobre precio, ubicación, superficie, título, pagos y
    // disponibilidad continúan sobre la publicación seleccionada.
    private static string? InferirRubroBusquedaMarketplace(string mensaje)
    {
        var t = NormalizarTexto(mensaje);
        if (Regex.IsMatch(t, @"\b(TERRENO|TERRENOS|LOTE|LOTES)\b")) return "TERRENO";
        if (Regex.IsMatch(t, @"\b(CASA|CASAS|DUPLEX)\b")) return "CASA";
        if (Regex.IsMatch(t, @"\b(DEPARTAMENTO|DEPARTAMENTOS)\b")) return "DEPARTAMENTO";
        if (Regex.IsMatch(t, @"\b(INMUEBLE|INMUEBLES|PROPIEDAD|PROPIEDADES)\b")) return "INMUEBLE";
        return null;
    }

    private static string? ExtraerZonaBusquedaMarketplace(string mensaje)
    {
        var t = NormalizarTexto(mensaje);
        // "terrenos por Areguá", "casas en Luque": zona explícita, no inferida.
        var match = Regex.Match(t, @"\b(?:POR|EN|CERCA DE|ZONA DE)\s+([A-Z]{3,25})\b");
        if (!match.Success) return null;
        var zona = match.Groups[1].Value;
        return zona is "EL" or "LA" or "LOS" or "LAS" or "UN" or "UNA" or "VENTA" or "OFERTA" or "CUOTAS" or "CREDITO" ? null : zona;
    }

    private static bool SolicitaNuevaBusquedaMarketplace(string mensaje)
    {
        var texto = NormalizarTexto(mensaje);

        if (string.IsNullOrWhiteSpace(texto))
            return false;

        // Prioridad comercial: una solicitud de OPCIONES/ALTERNATIVAS de un
        // rubro inicia una búsqueda nueva, aunque exista una publicación vieja
        // seleccionada. Ejemplo real: "Me podrías dar opciones de terrenos en
        // Areguá". La versión anterior no reconocía "me podrías dar" y
        // volvía a mostrar la publicación antigua (#37).
        const string rubros =
            @"(?:TERRENOS?|LOTES?|CASAS?|DUPLEX|DEPARTAMENTOS?|INMUEBLES?|PROPIEDADES?|CHACRAS?|PARCELAS?)";

        var solicitaOpciones = Regex.IsMatch(
            texto,
            @"\b(?:OPCIONES?|ALTERNATIVAS?|LISTADO|CATALOGO)\b.{0,70}\b" + rubros + @"\b")
            || Regex.IsMatch(
                texto,
                @"\b(?:PODRIAS|PODES|PUEDE|PUEDEN|PUEDO|ME MOSTRAS|ME MOSTRARIAS)\b.{0,75}\b" + rubros + @"\b")
            || Regex.IsMatch(
                texto,
                @"\b" + rubros + @"\s+(?:EN|POR|CERCA DE|ZONA DE)\b")
            || Regex.IsMatch(
                texto,
                @"\b(?:BUSCO|BUSCABA|BUSCANDO|NECESITO|NECESITARIA)\b.{0,75}\b" + rubros + @"\b");

        return solicitaOpciones
            || EsConsultaGeneralMarketplace(mensaje)
            || Regex.IsMatch(texto, @"\b(?:QUE|CUALES|MOSTRAME|MOSTRAR|TENES|TIENEN|QUIERO)\b.{0,70}\b(?:OPCIONES|TERRENOS|CASAS|LOTES|INMUEBLES|PROPIEDADES)\b")
            || texto.StartsWith("BUSCO ", StringComparison.Ordinal)
            || texto.StartsWith("ESTOY BUSCANDO ", StringComparison.Ordinal)
            || texto.StartsWith("BUSCANDO ", StringComparison.Ordinal)
            || ContieneFrase(texto, "TENES OTRA")
            || ContieneFrase(texto, "TIENEN OTRA")
            || ContieneFrase(texto, "HAY OTROS")
            || ContieneFrase(texto, "OTRO TERRENO")
            || ContieneFrase(texto, "OTRA CASA")
            || ContieneFrase(texto, "OTRA PROPIEDAD")
            || ContieneFrase(texto, "OTRA OPCION")
            || ContieneFrase(texto, "MAS OPCIONES")
            || ContieneFrase(texto, "OTRAS OPCIONES")
            || ContieneFrase(texto, "CAMBIAR DE PRODUCTO")
            || ContieneFrase(texto, "BUSCO OTRO")
            || ContieneFrase(texto, "MOSTRAME OTROS")
            || ContieneFrase(texto, "QUIERO VER OTROS");
    }

    private static bool EsSeguimientoMarketplace(
        string mensaje)
    {
        var texto =
            NormalizarTexto(
                mensaje);

        if (string.IsNullOrWhiteSpace(texto))
        {
            return false;
        }

        return
            texto == "SI"
            || texto == "DALE"
            || texto.Contains("PASAME")
            || texto.Contains("MANDAME")
            || texto.Contains("ENVIAME")
            || texto.Contains("ME INTERESA")
            || texto.Contains("PRECIO")
            || texto.Contains("CUANTO")
            || texto.Contains("DONDE")
            || texto.Contains("UBICACION")
            || texto.Contains("DIRECCION")
            || texto.Contains("DETALLE")
            || texto.Contains("INFO")
            || texto.Contains("INFORMACION")
            || texto.Contains("VENDEDOR")
            || texto.Contains("CONTACTO")
            || texto.Contains("PUBLICACION")
            || texto.Contains("QUIERO SABER")
            || texto.Contains("QUIERO VER");
    }


    private static bool EsConsultaGeneralMarketplace(
        string mensaje)
    {
        var texto =
            NormalizarTexto(
                mensaje);

        if (string.IsNullOrWhiteSpace(texto))
        {
            return false;
        }

        if (
            texto.Contains("MOTO")
            || texto.Contains("MARCA")
            || texto.Contains("MODELO")
        )
        {
            return false;
        }

        return
            texto == "QUE TIENEN"
            || texto == "QUE TENES"
            || texto == "QUE HAY"
            || texto.Contains("QUE PRODUCTOS TIENEN")
            || texto.Contains("QUE PRODUCTOS TENES")
            || texto.Contains("QUE VENDEN")
            || texto.Contains("QUE CATEGORIAS TIENEN")
            || texto.Contains("MOSTRAME EL CATALOGO")
            || texto.Contains("MOSTRAR EL CATALOGO")
            || texto.Contains("VER EL CATALOGO")
            || texto.Contains("PRODUCTOS DISPONIBLES");
    }


    private static IReadOnlyList<string>
        ExtraerTerminosBusquedaMarketplace(
            string mensaje)
    {
        var texto =
            NormalizarTexto(
                mensaje);

        if (string.IsNullOrWhiteSpace(texto))
        {
            return Array.Empty<string>();
        }

        var stopWords =
            new HashSet<string>(
                new[]
                {
                    "HOLA", "HOLAA", "HOLAAA", "BUENAS", "BUEN", "DIA",
                    "ME", "MI", "MIS", "TE", "SE", "LE", "LES",
                    "INTERESA", "INTERESAN", "INTERESADO", "INTERESADA",
                    "QUIERO", "QUISIERA", "BUSCO", "BUSCANDO", "NECESITO",
                    "ALGUN", "ALGUNA", "ALGUNOS", "ALGUNAS",
                    "UN", "UNA", "UNOS", "UNAS", "EL", "LA", "LOS", "LAS",
                    "DE", "DEL", "EN", "POR", "PARA", "CON", "SIN", "Y", "O",
                    "QUE", "CUAL", "CUALES", "COMO", "SI", "NO",
                    "TIENEN", "TIENES", "TENES", "VENDEN", "HAY",
                    "MOSTRAME", "MOSTRAR", "PASAME", "PASAR", "VER",
                    "PRECIO", "PRECIOS", "CUANTO", "CUESTA", "CUESTAN", "SALE",
                    "COMPRAR", "COMPRA", "VENDER", "VENTA", "DISPONIBLE", "DISPONIBLES",
                    "PRODUCTO", "PRODUCTOS", "PUBLICACION", "PUBLICACIONES",
                    "OFERTA", "OFERTAS", "INFO", "INFORMACION"
                },
                StringComparer.OrdinalIgnoreCase);

        var tokens =
            texto
                .Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries)
                .Where(
                    token =>
                        token.Length >= 3
                        &&
                        !stopWords.Contains(token)
                        &&
                        !EsTokenSoloNumerico(token))
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .Take(6)
                .ToList();

        var expandidos =
            new List<string>(tokens);

        if (tokens.Any(x => x is "LOTE" or "LOTES"))
        {
            expandidos.Add("LOTE");
            expandidos.Add("TERRENO");
        }

        if (tokens.Any(x => x is "TERRENO" or "TERRENOS"))
        {
            expandidos.Add("TERRENO");
            expandidos.Add("LOTE");
        }

        if (tokens.Any(x => x is "INMUEBLE" or "INMUEBLES"))
        {
            expandidos.Add("INMUEBLE");
            expandidos.Add("CASA");
            expandidos.Add("TERRENO");
            expandidos.Add("DEPARTAMENTO");
        }

        return expandidos
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
    }


    private static bool MensajeMencionaCategoriaMarketplace(
        string mensaje,
        IReadOnlyList<string> categorias)
    {
        var texto =
            NormalizarTexto(
                mensaje);

        if (string.IsNullOrWhiteSpace(texto))
        {
            return false;
        }

        return categorias.Any(
            categoria =>
            {
                var normalizada =
                    NormalizarTexto(
                        categoria);

                return
                    !string.IsNullOrWhiteSpace(normalizada)
                    &&
                    ContieneFrase(
                        texto,
                        normalizada);
            });
    }


    private async Task<string> ConstruirRespuestaPublicacionesMarketplace(
        IReadOnlyList<ProductoSharePreviewDto> publicaciones)
    {
        var sb = new StringBuilder();
        var saludo = await _editorPrompts.ObtenerObligatorio("MARKETPLACE_SALUDO");
        saludo = saludo.Replace("{opciones}", publicaciones.Count == 1 ? "esta opción" : "estas opciones", StringComparison.OrdinalIgnoreCase);
        sb.AppendLine(saludo.Trim());

        sb.AppendLine();

        for (var i = 0; i < publicaciones.Count; i++)
        {
            var item = publicaciones[i];

            sb.AppendLine(
                $"{i + 1}. *{item.Titulo.Trim()}*");

            sb.AppendLine(
                $"   {FormatearPrecioMarketplace(item.Precio, item.Moneda)}");

            if (!string.IsNullOrWhiteSpace(item.Ubicacion))
            {
                sb.AppendLine(
                    $"   Ubicación: {item.Ubicacion.Trim()}");
            }

            sb.AppendLine(
                $"   https://tuvendedor.com.py/share/producto/{item.Id}");

            if (i < publicaciones.Count - 1)
            {
                sb.AppendLine();
            }
        }

        sb.AppendLine();

        var cierre = await _editorPrompts.ObtenerObligatorio("MARKETPLACE_CIERRE");
        sb.Append(cierre.Trim());

        return sb.ToString().Trim();
    }


    private string ConstruirRespuestaDetalleMarketplace(
        ProductoSharePreviewDto publicacion,
        string? mensajeCliente)
    {
        // Respuestas deterministas ancladas en la ficha real; no se envía
        // a Ollama para improvisar importes, títulos, medidas ni condiciones.
        var pregunta = NormalizarTexto(mensajeCliente ?? string.Empty);
        var nombre = publicacion.Titulo?.Trim() ?? "la publicación";
        var url = $"https://www.tuvendedor.com.py/share/producto/{publicacion.Id}";
        var precio = FormatearPrecioMarketplace(publicacion.Precio, publicacion.Moneda);
        var documentacion = EsConsultaDocumentacionInmueble(pregunta);
        var visita = EsSolicitudVisitaInmueble(pregunta);
        var precioConsultado = ContieneAlgunaPalabra(pregunta, "PRECIO", "CUANTO CUESTA", "CUANTO SALE", "COSTO", "VALOR");
        var ubicacionConsultada = ContieneAlgunaPalabra(pregunta, "UBICACION", "DONDE QUEDA", "DIRECCION", "CALLE", "ZONA", "LUGAR");
        var superficieConsultada = ContieneAlgunaPalabra(pregunta, "SUPERFICIE", "MEDIDAS", "METROS", "DIMENSIONES", "TAMANO", "FRENTE", "FONDO", "M2", "M²");
        var pagoConsultado = ContieneAlgunaPalabra(pregunta, "CUOTA", "FINANCI", "ENTREGA", "CREDITO", "PAGOS", "CONTADO");
        var fotosConsultadas = ContieneAlgunaPalabra(pregunta, "FOTO", "IMAGEN", "VIDEO");
        var disponibleConsultado = ContieneAlgunaPalabra(pregunta, "DISPONIBLE", "SIGUE EN VENTA", "TODAVIA ESTA", "AUN ESTA");
        var detalleMedidas = ExtraerMedidasPublicadas(publicacion.Descripcion);
        var respuestas = new List<string>();

        // Una consulta puede hacer DOS preguntas: "metros y título".
        if (superficieConsultada)
            respuestas.Add(!string.IsNullOrWhiteSpace(detalleMedidas)
                ? Texto("INMUEBLE_METROS_CONFIRMADOS", ("NOMBRE", nombre), ("MEDIDAS", detalleMedidas))
                : Texto("INMUEBLE_METROS_SIN_DATO", ("NOMBRE", nombre)));
        if (precioConsultado)
            respuestas.Add(Texto("INMUEBLE_PRECIO", ("NOMBRE", nombre), ("PRECIO", precio)));
        if (ubicacionConsultada)
            respuestas.Add(!string.IsNullOrWhiteSpace(publicacion.Ubicacion)
                ? Texto("INMUEBLE_UBICACION", ("UBICACION", publicacion.Ubicacion.Trim()))
                : Texto("INMUEBLE_UBICACION_NO_CONSTA"));
        if (pagoConsultado)
            respuestas.Add(Texto("INMUEBLE_CONDICIONES_PAGO", ("PRECIO", precio)));
        if (fotosConsultadas)
            respuestas.Add(Texto("INMUEBLE_FOTOGRAFIAS", ("URL", url)));
        if (disponibleConsultado)
            respuestas.Add(Texto("INMUEBLE_DISPONIBILIDAD", ("NOMBRE", nombre)));
        if (documentacion)
            respuestas.Add(Texto("INMUEBLE_DOCUMENTACION"));
        if (visita)
            respuestas.Add(Texto("INMUEBLE_COORDINAR_VISITA"));

        if (respuestas.Count > 0)
            return string.Join("\n\n", respuestas.Distinct());

        // Si el usuario recién llegó desde el enlace, ofrecemos una ficha
        // BREVE, no un catálogo ni bloques repetidos de toda la descripción.
        var ficha = new List<string>
        {
            $"*{nombre}*",
            $"Precio publicado: *{precio}*"
        };
        if (!string.IsNullOrWhiteSpace(publicacion.Ubicacion))
            ficha.Add($"Ubicación: {publicacion.Ubicacion.Trim()}");
        if (!string.IsNullOrWhiteSpace(detalleMedidas))
            ficha.Add($"Dimensiones: {detalleMedidas}");
        ficha.Add($"Más información: {url}");
        ficha.Add(Texto("INMUEBLE_FICHA_CIERRE"));
        return string.Join("\n", ficha);
    }

    private static bool ContieneAlgunaPalabra(string texto, params string[] expresiones)
        => expresiones.Any(texto.Contains);

    private static bool EsConsultaDocumentacionInmueble(string texto) =>
        ContieneAlgunaPalabra(texto, "TITULO", "TRANSFERIR", "TRANSFERENCIA",
            "ESCRITURA", "CEDULA CATASTRAL", "DOCUMENTACION", "DOCUMENTOS", "IMPUESTOS");

    private static bool EsSolicitudVisitaInmueble(string texto) =>
        ContieneAlgunaPalabra(texto, "AGENDAR VISITA", "COORDINAR VISITA",
            "QUIERO VISITAR", "QUIERO VER EL TERRENO", "QUIERO VER LA CASA",
            "QUIERO CONOCER EL TERRENO", "PUEDO IR A VER", "PASAR A VER",
            "PASAR A MIRAR", "IR A MIRAR", "QUIERO IR A VER",
            "VER LA PROPIEDAD", "VISITAR EL INMUEBLE", "AGENDAMOS UNA VISITA");

    private static string ExtraerMedidasPublicadas(string? descripcion)
    {
        if (string.IsNullOrWhiteSpace(descripcion)) return string.Empty;
        // Sólo devolver frases de superficie o dimensiones realmente presentes.
        var lineas = Regex.Split(descripcion, @"[\r\n]+")
            .Select(x => x.Trim(' ', '-', '•', '*'))
            .Where(x => x.Length > 0 && Regex.IsMatch(x,
                @"(?i)\b(superficie|dimensiones|medidas|frente|fondo|laterales|metros cuadrados|m2|m²)\b"))
            .Select(x => TruncarTextoMarketplace(x, 200))
            .Take(3).ToList();
        if (lineas.Count > 0) return string.Join("; ", lineas);
        var coincidencias = Regex.Matches(descripcion,
            @"(?i)(?:superficie total|superficie|medidas aproximadas|dimensiones)\s*[:\-]?\s*[^.\r\n]{3,160}");
        return string.Join("; ", coincidencias.Cast<Match>()
            .Select(x => x.Value.Trim()).Take(2));
    }

    private string ConstruirRespuestaCategoriasMarketplace(
        IReadOnlyList<string> categorias)
    {
        if (categorias.Count == 0)
        {
            return
                Texto("MARKETPLACE_CATEGORIAS_VACIAS");
        }

        var lista =
            categorias
                .Take(12)
                .Select(x => $"• {x.Trim()}");

        return Texto("MARKETPLACE_CATEGORIAS_LISTADO", ("CATEGORIAS", string.Join("\n", lista)));
    }


    private string ConstruirRespuestaMarketplaceSinResultados(
        IReadOnlyList<string> terminos,
        IReadOnlyList<string> categorias)
    {
        var buscado =
            terminos.Count == 0
                ? "eso que buscás"
                : string.Join(
                    " ",
                    terminos.Take(3));

        var respuesta =
            Texto("MARKETPLACE_BUSQUEDA_VACIA", ("BUSQUEDA", buscado.ToLowerInvariant()));

        if (categorias.Count > 0)
        {
            respuesta +=
                Texto("MARKETPLACE_OTRAS_CATEGORIAS", ("CATEGORIAS", string.Join("\n", categorias.Take(8).Select(x => $"• {x.Trim()}"))));
        }

        respuesta +=
            Texto("MARKETPLACE_REINTENTAR");

        return respuesta;
    }


    private static string FormatearPrecioMarketplace(
        decimal precio,
        string? moneda)
    {
        if (precio <= 0)
        {
            return "Precio a consultar";
        }

        var valor =
            precio.ToString(
                "N0",
                CultureInfo.GetCultureInfo("es-PY"));

        var codigo =
            string.IsNullOrWhiteSpace(moneda)
                ? "PYG"
                : moneda.Trim().ToUpperInvariant();

        return codigo switch
        {
            "USD" => $"USD {valor}",
            "PYG" => $"Gs. {valor}",
            "GS" => $"Gs. {valor}",
            "GS." => $"Gs. {valor}",
            _ => $"{codigo} {valor}"
        };
    }


    private static string TruncarTextoMarketplace(
        string? texto,
        int limite)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return string.Empty;
        }

        var limpio =
            Regex.Replace(
                texto,
                @"\s+",
                " ")
                .Trim();

        return limpio.Length <= limite
            ? limpio
            : limpio[..limite].TrimEnd() + "…";
    }


    // =========================================================
    // DERIVAR PRODUCTO QUE NO ES MOTO
    // =========================================================

    private async Task<MotoConversacionResponseDto>
        DerivarOtroProducto(
            int idConversacion,
            int? idPublicacion,
            string? mensajeCliente)
    {
        await _repository
            .LimpiarProductoContexto(
                idConversacion);

        await _repository
            .CambiarModoConversacion(
                idConversacion,
                "HUMANO");

        var respuesta =
            await PrepararRespuestaCliente(
                idConversacion,
                await _editorPrompts.ObtenerObligatorio("OTRO_PRODUCTO_ASESOR"));

        await _repository
            .RegistrarMensaje(
                idConversacion,
                "IA",
                respuesta);

        await RegistrarInteresadoSeguro(
            new InteresadoWhatsAppEventoRequest
            {
                IdConversacion =
                    idConversacion,

                IdPublicacion =
                    idPublicacion,

                TipoConsulta =
                    "OTRO_PRODUCTO",

                EstadoConsulta =
                    "DERIVADO_HUMANO",

                MensajeCliente =
                    mensajeCliente,

                Respuesta =
                    respuesta,

                MotivoSeguimiento =
                    "Consulta comercial sobre un producto distinto de moto. Requiere atención manual.",

                EsEntradaCliente =
                    false
            });

        return new MotoConversacionResponseDto
        {
            IdConversacion = idConversacion,
            IdPublicacion = idPublicacion,
            Marca = null,
            Modelo = null,
            Respuesta = respuesta,
            RequierePublicacion = false
        };
    }


    // =========================================================
    // CONVERSACION ABIERTA / GENERAL
    // =========================================================

    private async Task<MotoConversacionResponseDto>
        ProcesarConversacionAbierta(
            int idConversacion,
            string mensaje,
            IReadOnlyList<MotoModeloCandidatoDto> modelos,
            CancellationToken cancellationToken)
    {
        await _repository
            .LimpiarProductoContexto(
                idConversacion);

        var historial =
            await _repository
                .ObtenerUltimosMensajes(
                    idConversacion,
                    8);

        var respuestaIA =
            await _ollamaService
                .GenerarRespuesta(
                    (await ConstruirPromptConversacionAbierta(modelos))
                    + "\n\n" + (await _editorPrompts.ObtenerObligatorio("CONVERSACION_GENERAL")),
                    historial,
                    cancellationToken);

        if (
            EsMarcadorExacto(
                respuestaIA,
                MARCADOR_ROUTER_MOTO_NO_RECONOCIDA)
        )
        {
            var respuestaMotoNoReconocida =
                ConstruirRespuestaMarcasDisponibles(
                    modelos,
                    await _editorPrompts.ObtenerObligatorio("MOTO_NO_IDENTIFICADA_INTRO"));

            await _repository
                .RegistrarMensaje(
                    idConversacion,
                    "IA",
                    respuestaMotoNoReconocida);

            return new MotoConversacionResponseDto
            {
                IdConversacion = idConversacion,
                IdPublicacion = null,
                Marca = null,
                Modelo = null,
                Respuesta = respuestaMotoNoReconocida,
                RequierePublicacion = false
            };
        }

        if (string.IsNullOrWhiteSpace(respuestaIA))
        {
            respuestaIA =
                await _editorPrompts.ObtenerObligatorio("GENERAL_SIN_RESPUESTA");
        }

        respuestaIA =
            LimpiarMarcadoresRouter(
                respuestaIA);

        if (string.IsNullOrWhiteSpace(respuestaIA))
        {
            respuestaIA =
                await _editorPrompts.ObtenerObligatorio("GENERAL_SIN_CONTENIDO");
        }

        if (
            !respuestaIA.Contains(
                "tuvendedor.com.py",
                StringComparison.OrdinalIgnoreCase)
        )
        {
            respuestaIA += await _editorPrompts.ObtenerObligatorio("GENERAL_ENLACE_MARKETPLACE");
        }

        respuestaIA =
            await PrepararRespuestaCliente(
                idConversacion,
                respuestaIA);

        await _repository
            .RegistrarMensaje(
                idConversacion,
                "IA",
                respuestaIA);

        return new MotoConversacionResponseDto
        {
            IdConversacion = idConversacion,
            IdPublicacion = null,
            Marca = null,
            Modelo = null,
            Respuesta = respuestaIA,
            RequierePublicacion = false
        };
    }


    private async Task<string> ConstruirPromptConversacionAbierta(
        IReadOnlyList<MotoModeloCandidatoDto> modelos)
    {
        var marcasCatalogo =
            modelos
                .Where(
                    x =>
                        !string.IsNullOrWhiteSpace(
                            x.Marca))
                .Select(
                    x => x.Marca.Trim())
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .OrderBy(
                    x => x)
                .ToList();

        var marcasPermitidas =
            marcasCatalogo.Count == 0
                ? "NINGUNA MARCA CARGADA"
                : string.Join(
                    ", ",
                    marcasCatalogo);

        var plantilla = await _editorPrompts.ObtenerObligatorio("CONVERSACION_ROUTER_BASE");
        return plantilla
            .Replace("{MARCAS_PERMITIDAS}", marcasPermitidas, StringComparison.Ordinal)
            .Replace("{MARCADOR_MOTO_NO_RECONOCIDA}", MARCADOR_ROUTER_MOTO_NO_RECONOCIDA, StringComparison.Ordinal);
    }


    private static bool EsMarcadorExacto(
        string? respuesta,
        string marcador)
    {
        return string.Equals(
            respuesta?.Trim(),
            marcador,
            StringComparison.Ordinal);
    }


    private static string LimpiarMarcadoresRouter(
        string respuesta)
    {
        return respuesta
            .Replace(
                MARCADOR_ROUTER_OTRO_PRODUCTO,
                string.Empty,
                StringComparison.Ordinal)
            .Replace(
                MARCADOR_ROUTER_MOTO_NO_RECONOCIDA,
                string.Empty,
                StringComparison.Ordinal)
            .Trim();
    }


    // =========================================================
    // PROCESAR POR PUBLICACION
    // =========================================================

    private async Task<MotoConversacionResponseDto>
        ProcesarPorPublicacion(
            int idConversacion,
            int idPublicacion,
            string mensaje,
            CancellationToken cancellationToken)
    {
        var oferta =
            await _motoOfertaService
                .ObtenerOfertaPorPublicacion(
                    idPublicacion);

        await _repository
            .ActualizarContexto(
                idConversacion,
                idPublicacion,
                oferta.Modelo.Id,
                null);

        if (
            DebeResponderOfertaSinIA(
                mensaje)
        )
        {
            var respuestaDirecta =
                await PrepararRespuestaCliente(
                    idConversacion,
                    ConstruirRespuestaDirectaModelo(
                        oferta,
                        mensaje));

            await _repository
                .RegistrarMensaje(
                    idConversacion,
                    "IA",
                    respuestaDirecta);

            await RegistrarInteresadoSeguro(
                new InteresadoWhatsAppEventoRequest
                {
                    IdConversacion =
                        idConversacion,

                    IdModeloProducto =
                        oferta.Modelo.Id,

                    IdPublicacion =
                        idPublicacion,

                    TipoConsulta =
                        DetectarTipoConsultaInteres(
                            mensaje),

                    EstadoConsulta =
                        "COTIZADO",

                    MensajeCliente =
                        mensaje,

                    Respuesta =
                        respuestaDirecta,

                    MotivoSeguimiento =
                        "Cliente recibió información comercial del modelo.",

                    EsEntradaCliente =
                        false
                });

            return new MotoConversacionResponseDto
            {
                IdConversacion = idConversacion,
                IdPublicacion = idPublicacion,
                Marca = oferta.Modelo.Marca,
                Modelo = $"{oferta.Modelo.Marca} {oferta.Modelo.Nombre}",
                Respuesta = respuestaDirecta,
                RequierePublicacion = false
            };
        }

        return await ProcesarOferta(
            idConversacion,
            idPublicacion,
            oferta.Modelo.Id,
            oferta,
            cancellationToken);
    }


    // =========================================================
    // PROCESAR POR MODELO
    // =========================================================

    private async Task<MotoConversacionResponseDto>
        ProcesarPorModelo(
            int idConversacion,
            MotoModeloCandidatoDto modelo,
            string mensaje,
            CancellationToken cancellationToken)
    {
        /*
         * IMPORTANTE:
         *
         * Para cotizar por WhatsApp NO exigimos una publicación.
         * El precio comercial pertenece al MODELO y se obtiene
         * directamente desde ListasPreciosProducto.
         *
         * La promo es opcional.
         * Si no existe promo vigente, MotoOfertaService usa
         * automáticamente la lista NORMAL.
         */
        MotoOfertaDto oferta;

        try
        {
            oferta =
                await _motoOfertaService
                    .ObtenerOfertaPorModelo(
                        modelo.IdModeloProducto);
        }
        catch (ReglasdeNegocioException)
        {
            /*
             * Este sí es un caso real para intervención humana:
             * el modelo existe pero no tiene precio NORMAL activo.
             *
             * No inventamos un monto.
             */
            await _repository
                .ActualizarContexto(
                    idConversacion,
                    modelo.IdPublicacion,
                    modelo.IdModeloProducto,
                    null);

            await _repository
                .CambiarModoConversacion(
                    idConversacion,
                    "HUMANO");

            var respuestaSinPrecio =
                await PrepararRespuestaCliente(
                    idConversacion,
                    Texto("MOTO_SIN_PRECIO_ASESOR", ("MARCA", modelo.Marca), ("MODELO", modelo.Modelo)));

            await _repository
                .RegistrarMensaje(
                    idConversacion,
                    "IA",
                    respuestaSinPrecio);

            await RegistrarInteresadoSeguro(
                new InteresadoWhatsAppEventoRequest
                {
                    IdConversacion =
                        idConversacion,

                    IdModeloProducto =
                        modelo.IdModeloProducto,

                    IdPublicacion =
                        modelo.IdPublicacion,

                    TipoConsulta =
                        DetectarTipoConsultaInteres(
                            mensaje),

                    EstadoConsulta =
                        "PENDIENTE_ASESOR",

                    MensajeCliente =
                        mensaje,

                    Respuesta =
                        respuestaSinPrecio,

                    MotivoSeguimiento =
                        "Modelo identificado pero requiere confirmación comercial humana.",

                    EsEntradaCliente =
                        false
                });

            return new MotoConversacionResponseDto
            {
                IdConversacion =
                    idConversacion,

                IdPublicacion =
                    modelo.IdPublicacion,

                Marca =
                    modelo.Marca,

                Modelo =
                    $"{modelo.Marca} {modelo.Modelo}",

                Respuesta =
                    respuestaSinPrecio,

                RequierePublicacion =
                    false
            };
        }

        await _repository
            .ActualizarContexto(
                idConversacion,
                oferta.PublicacionId,
                modelo.IdModeloProducto,
                null);

        if (
            DebeResponderOfertaSinIA(
                mensaje)
        )
        {
            var respuestaDirecta =
                await PrepararRespuestaCliente(
                    idConversacion,
                    ConstruirRespuestaDirectaModelo(
                        oferta,
                        mensaje));

            await _repository
                .RegistrarMensaje(
                    idConversacion,
                    "IA",
                    respuestaDirecta);

            await RegistrarInteresadoSeguro(
                new InteresadoWhatsAppEventoRequest
                {
                    IdConversacion =
                        idConversacion,

                    IdModeloProducto =
                        modelo.IdModeloProducto,

                    IdPublicacion =
                        oferta.PublicacionId,

                    TipoConsulta =
                        DetectarTipoConsultaInteres(
                            mensaje),

                    EstadoConsulta =
                        "COTIZADO",

                    MensajeCliente =
                        mensaje,

                    Respuesta =
                        respuestaDirecta,

                    MotivoSeguimiento =
                        "Cliente recibió precio/cuotas del modelo.",

                    EsEntradaCliente =
                        false
                });

            return new MotoConversacionResponseDto
            {
                IdConversacion = idConversacion,
                IdPublicacion = oferta.PublicacionId,
                Marca = oferta.Modelo.Marca,
                Modelo = $"{oferta.Modelo.Marca} {oferta.Modelo.Nombre}",
                Respuesta = respuestaDirecta,
                RequierePublicacion = false
            };
        }

        return await ProcesarOferta(
            idConversacion,
            oferta.PublicacionId,
            modelo.IdModeloProducto,
            oferta,
            cancellationToken);
    }


    // =========================================================
    // PROCESAR OFERTA + MARCA + QWEN
    // =========================================================

    private async Task<MotoConversacionResponseDto>
        ProcesarOferta(
            int idConversacion,
            int? idPublicacion,
            int idModeloProducto,
            MotoOfertaDto oferta,
            CancellationToken cancellationToken)
    {
        // =====================================================
        // PROMPT DE MARCA
        // =====================================================

        var codigoPromptMarca =
            ConstruirCodigoPromptMarca(
                oferta.Modelo.Marca);


        _logger.LogInformation(
            "Estrategia IA. Marca={Marca}, Modelo={Modelo}, Prompt={Prompt}",
            oferta.Modelo.Marca,
            oferta.Modelo.Nombre,
            codigoPromptMarca);


        // =====================================================
        // PROMPT BASE
        // =====================================================

        var promptBase = await _editorPrompts.ObtenerPublicado(CODIGO_PROMPT_BASE)
            ?? await _repository.ObtenerPromptActivo(CODIGO_PROMPT_BASE);


        if (
            string.IsNullOrWhiteSpace(
                promptBase)
        )
        {
            throw new ReglasdeNegocioException(
                $"No existe el prompt activo {CODIGO_PROMPT_BASE}.");
        }


        // =====================================================
        // PROMPT MARCA
        // =====================================================

        var promptMarca = await _editorPrompts.ObtenerPublicado(codigoPromptMarca)
            ?? await _repository.ObtenerPromptActivo(codigoPromptMarca);


        if (
            string.IsNullOrWhiteSpace(
                promptMarca)
        )
        {
            throw new ReglasdeNegocioException(
                $"La estrategia comercial IA para la marca {oferta.Modelo.Marca} todavía no está configurada.");
        }


        // =====================================================
        // GUARDAR CONTEXTO
        // =====================================================

        await _repository
            .ActualizarContexto(
                idConversacion,
                idPublicacion,
                idModeloProducto,
                codigoPromptMarca);


        // =====================================================
        // SYSTEM PROMPT
        // =====================================================

        var promptSistema =
            ConstruirPromptSistema(
                promptBase,
                promptMarca,
                oferta,
                await _editorPrompts.ObtenerObligatorio("MOTO_SISTEMA_INSTRUCCIONES"));


        // =====================================================
        // HISTORIAL
        // =====================================================

        var historial =
            await _repository
                .ObtenerUltimosMensajes(
                    idConversacion,
                    4);


        // =====================================================
        // QWEN
        // =====================================================

        string respuestaIA;

        try
        {
            respuestaIA =
                await _ollamaService
                    .GenerarRespuesta(
                        promptSistema,
                        historial,
                        cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Ollama no pudo responder a tiempo. Se utiliza respuesta comercial segura.");

            respuestaIA =
                string.Empty;
        }

        // =====================================================
        // SI QWEN NO DEVUELVE RESPUESTA, NO PASAMOS A HUMANO.
        // DAMOS UNA SALIDA COMERCIAL SEGURA CON DATOS YA CARGADOS.
        // =====================================================

        if (
            string.IsNullOrWhiteSpace(
                respuestaIA)
        )
        {
            respuestaIA =
                ConstruirRespuestaComercialSegura(
                    oferta);
        }


        respuestaIA =
            await PrepararRespuestaCliente(
                idConversacion,
                respuestaIA);

        // =====================================================
        // GUARDAR RESPUESTA
        // =====================================================

        await _repository
            .RegistrarMensaje(
                idConversacion,
                "IA",
                respuestaIA);

        await RegistrarInteresadoSeguro(
            new InteresadoWhatsAppEventoRequest
            {
                IdConversacion =
                    idConversacion,

                IdModeloProducto =
                    idModeloProducto,

                IdPublicacion =
                    idPublicacion,

                TipoConsulta =
                    "CONSULTA_MODELO",

                EstadoConsulta =
                    "COTIZADO",

                Respuesta =
                    respuestaIA,

                MotivoSeguimiento =
                    "Cliente recibió información comercial del modelo.",

                EsEntradaCliente =
                    false
            });


        return new MotoConversacionResponseDto
        {
            IdConversacion =
                idConversacion,

            IdPublicacion =
                idPublicacion,

            Marca =
                oferta.Modelo.Marca,

            Modelo =
                $"{oferta.Modelo.Marca} {oferta.Modelo.Nombre}",

            Respuesta =
                respuestaIA,

            RequierePublicacion =
                false
        };
    }


    private async Task<string> PrepararRespuestaCliente(
        int idConversacion,
        string respuesta)
    {
        var limpia = QuitarEmojisRespuesta(respuesta);

        try
        {
            var historial =
                await _repository.ObtenerUltimosMensajes(
                    idConversacion,
                    20);

            var yaRespondioPanambi =
                historial.Any(
                    x => string.Equals(
                        x.Emisor,
                        "IA",
                        StringComparison.OrdinalIgnoreCase));

            if (!yaRespondioPanambi)
            {
                var saludo = await _editorPrompts.ObtenerObligatorio("PANAMBI_PRESENTACION");
                return saludo.Replace("{RESPUESTA}", limpia, StringComparison.Ordinal);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "No se pudo verificar si Panambí ya se presentó. IdConversacion={IdConversacion}",
                idConversacion);
        }

        return limpia;
    }

    private static string QuitarEmojisRespuesta(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return string.Empty;

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

    // =========================================================
    // CRM / INTERESADOS
    // =========================================================

    private static MotoConversacionResponseDto RespuestaCierre(
        int idConversacion, string respuesta) => new()
        {
            IdConversacion = idConversacion,
            Respuesta = respuesta,
            RequierePublicacion = false
        };

    private async Task<bool> EsOportunidadRechazada(int idInteresado)
    {
        // Un fallo leyendo el CRM no debe permitir que un saludo posterior
        // vuelva a abrir una oportunidad. Se propaga para revisión.
        var detalle = await _clientesService.ObtenerDetalleInteresado(idInteresado);
        return string.Equals(detalle.Interesado.EstadoConsulta, "CERRADO",
                   StringComparison.OrdinalIgnoreCase)
            && (string.Equals(detalle.Interesado.MotivoSeguimiento,
                   "RECHAZO_MOTO_EXPLICITO", StringComparison.OrdinalIgnoreCase)
                || string.Equals(detalle.Interesado.MotivoSeguimiento,
                   "RECHAZO_PRODUCTO_EXPLICITO", StringComparison.OrdinalIgnoreCase));
    }

    private static bool EsNegativaComercialCorta(string? mensaje)
    {
        if (string.IsNullOrWhiteSpace(mensaje)) return false;
        var normalizado = mensaje.ToLowerInvariant()
            .Normalize(System.Text.NormalizationForm.FormD);
        var texto = new string(normalizado.Where(c =>
            System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) !=
            System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
        texto = Regex.Replace(texto, @"[^a-z0-9\s]", " ");
        texto = Regex.Replace(texto, @"\s+", " ").Trim();
        // No cerrar por "no" cuando representa otra cosa, salvo que la
        // última acción de Panambí fuera una oferta/seguimiento comercial.
        return Regex.IsMatch(texto,
            @"^(?:(?:buenos dias|buen dia|buenas tardes|buenas noches|hola)\s+)?(?:no|no gracias|no muchas gracias|no por ahora|por ahora no|ahora no|ahora mismo no|ahora no gracias|de momento no|en este momento no|no puedo|ya no puedo|yano puedo|ya no puedo gracias|ya no gracias|no quiero|no quiero gracias|no necesito|no necesito gracias|gracias pero no|muchas gracias pero no|no me interesa|no estoy interesad[oa]|mejor no|ya no puedo comprar)(?:\s+(?:desde ya gracias|gracias|muchas gracias|por su tiempo|por la informacion))?$",
            RegexOptions.CultureInvariant);
    }

    private static bool EsNegativaConAgradecimiento(string? mensaje)
    {
        var texto = NormalizarTexto(mensaje ?? "");
        return texto.Contains("NO GRACIAS") || texto.Contains("AHORA NO") ||
               texto.Contains("AHORA MISMO NO") || texto.Contains("POR AHORA NO") ||
               texto.Contains("DE MOMENTO NO") || texto.Contains("EN ESTE MOMENTO NO") ||
               texto.Contains("GRACIAS PERO NO") || texto.Contains("NO PUEDO") || texto.Contains("NO ME INTERESA") ||
               texto.Contains("NO ESTOY INTERESAD") || texto.Contains("NO QUIERO") ||
               texto.Contains("MEJOR NO") || texto.Contains("NO NECESITO") ||
               texto.Contains("YANO PUEDO");
    }

    private async Task<bool> TieneContextoComercialSeleccionado(int idConversacion)
    {
        var contexto = await _repository.ObtenerContextoActual(idConversacion);
        return contexto?.IdPublicacion is > 0 || contexto?.IdModeloProductoActual is > 0;
    }

    private async Task<bool> UltimaRespuestaFueOfertaComercial(int idConversacion)
    {
        var historial = await _repository.ObtenerUltimosMensajes(idConversacion, 12);
        var ultima = historial
            .Where(x => string.Equals(x.Emisor, "IA", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Fecha)
            .FirstOrDefault()?.Mensaje ?? string.Empty;
        var t = NormalizarTexto(ultima);
        // Un “no” solamente se interpreta como rechazo si fue respuesta
        // a una propuesta comercial; no a cualquier pregunta aislada.
        return t.Contains("SEGUIS INTERESAD") || t.Contains("SEGUIS BUSCANDO") ||
               t.Contains("TE ESCRIBO NUEVAMENTE") ||
               t.Contains("POR ULTIMA VEZ") ||
               t.Contains("AL CONTADO O A CREDITO") ||
               t.Contains("TE GUSTARIA ADQUIRIR") ||
               t.Contains("TE GUSTARIA PASAR A MIRAR") ||
               t.Contains("PODEMOS AGENDAR UNA VISITA") ||
               t.Contains("QUIERES AGENDAR UNA VISITA") ||
               t.Contains("QUERES AGENDAR UNA VISITA") ||
               t.Contains("TE INTERESA ESTA PROPIEDAD") ||
               t.Contains("SI QUERES COORDINAR") ||
               t.Contains("PRECIO PUBLICADO") ||
               t.Contains("VER PUBLICACION") ||
               t.Contains("/SHARE/PRODUCTO/");
    }

    private async Task RegistrarCierreSeguro(
        int idConversacion, string respuesta, string motivo,
        int? idInteresado)
    {
        // Cerrar también el historial del modelo, no solamente el CRM principal.
        int? idModeloProducto = null;
        if (idInteresado.HasValue)
            idModeloProducto = (await _clientesService
                .ObtenerDetalleInteresado(idInteresado.Value))
                .Interesado.IdModeloProducto;

        // En un cierre comercial NO se ignora el fallo del CRM: de otro
        // modo podríamos despedirnos pero dejar mensajes en la cola.
        await _clientesService.RegistrarInteraccionWhatsApp(
            new InteresadoWhatsAppEventoRequest
            {
                IdConversacion = idConversacion,
                IdModeloProducto = idModeloProducto,
                EstadoConsulta = "CERRADO",
                MotivoSeguimiento = motivo,
                Respuesta = respuesta,
                EsEntradaCliente = false
            });
        await _repository.RegistrarMensaje(idConversacion, "IA", respuesta);
    }

    private async Task<int?> RegistrarInteresadoSeguro(
        InteresadoWhatsAppEventoRequest evento)
    {
        try
        {
            return await _clientesService.RegistrarInteraccionWhatsApp(evento);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "No se pudo actualizar interesado desde WhatsApp. IdConversacion={IdConversacion}",
                evento.IdConversacion);
            return null;
        }
    }


    private static string DetectarTipoConsultaInteres(
        string? mensaje)
    {
        if (string.IsNullOrWhiteSpace(
            mensaje))
        {
            return "CONSULTA_GENERAL";
        }

        var operacion =
            DetectarInicioOperacion(
                mensaje);

        if (!string.IsNullOrWhiteSpace(
            operacion))
        {
            return operacion;
        }

        if (EsConsultaPromociones(
            mensaje))
        {
            return "PROMOCION";
        }

        if (EsConsultaPrecioOCuotas(
            mensaje))
        {
            return "PRECIO_CUOTAS";
        }

        if (ExtraerIdPublicacion(
            mensaje).HasValue)
        {
            return "PUBLICACION";
        }

        return "CONSULTA_GENERAL";
    }


    private static bool EsCreditoOperacion(
        string? tipoOperacion)
    {
        return string.Equals(
            tipoOperacion,
            "CREDITO",
            StringComparison.OrdinalIgnoreCase);
    }


    // =========================================================
    // RESOLVER MODELOS DESDE TEXTO
    // =========================================================

    private static List<MotoModeloCandidatoDto>
        ResolverModelosDesdeTexto(
            string mensaje,
            IReadOnlyList<MotoModeloCandidatoDto> modelos)
    {
        var texto =
            NormalizarTexto(
                mensaje);


        if (
            string.IsNullOrWhiteSpace(
                texto)
        )
        {
            return new List<MotoModeloCandidatoDto>();
        }


        var resultados =
            new List<
                (
                    MotoModeloCandidatoDto Modelo,
                    int Puntaje
                )>();


        foreach (
            var modelo
            in modelos)
        {
            var marca =
                NormalizarTexto(
                    modelo.Marca);


            var nombreModelo =
                NormalizarTexto(
                    modelo.Modelo);


            var codigo =
                NormalizarTexto(
                    modelo.CodigoReferencia);


            var tokensTexto =
                ObtenerTokens(
                    texto);


            var tokensModelo =
                ObtenerTokens(
                    nombreModelo);


            var marcaCoincide =
                !string.IsNullOrWhiteSpace(
                    marca)
                &&
                ContieneFrase(
                    texto,
                    marca);


            var modeloCompletoCoincide =
                !string.IsNullOrWhiteSpace(
                    nombreModelo)
                &&
                ContieneFrase(
                    texto,
                    nombreModelo);


            var codigoCoincide =
                !string.IsNullOrWhiteSpace(
                    codigo)
                &&
                ContieneFrase(
                    texto,
                    codigo);


            var cilindradaCoincide =
                modelo.Cilindrada.HasValue
                &&
                tokensTexto.Contains(
                    modelo.Cilindrada.Value.ToString());


            var tokensDistintivosModelo =
                tokensModelo
                    .Where(
                        token =>
                            token.Length >= 3
                            &&
                            !EsTokenSoloNumerico(
                                token))
                    .ToList();


            var cantidadTokensModeloCoincidentes =
                tokensDistintivosModelo
                    .Count(
                        token =>
                            tokensTexto.Contains(
                                token));

            /*
             * Toleramos errores de escritura pequeños en el nombre
             * del modelo. Ejemplo: "SHARCK" -> "SHARK".
             *
             * Esto solo se aplica a tokens distintivos de al menos
             * 4 caracteres para evitar falsos positivos.
             */
            var cantidadTokensModeloAproximados =
                tokensDistintivosModelo
                    .Where(
                        token =>
                            token.Length >= 4
                            &&
                            !tokensTexto.Contains(
                                token))
                    .Count(
                        tokenModelo =>
                            tokensTexto.Any(
                                tokenTexto =>
                                    CoincideTokenAproximado(
                                        tokenTexto,
                                        tokenModelo)));


            var tieneTokenDistintivo =
                (
                    cantidadTokensModeloCoincidentes
                    +
                    cantidadTokensModeloAproximados
                )
                >
                0;


            /*
             * Para ser candidato debe existir alguna
             * evidencia real en el mensaje.
             */
            var esCandidato =
                modeloCompletoCoincide

                ||

                codigoCoincide

                ||

                tieneTokenDistintivo

                ||

                (
                    marcaCoincide
                    &&
                    cilindradaCoincide
                )

                ||

                marcaCoincide;


            if (!esCandidato)
            {
                continue;
            }


            var puntaje =
                0;


            if (codigoCoincide)
            {
                puntaje += 150;
            }


            if (modeloCompletoCoincide)
            {
                puntaje += 120;
            }


            if (marcaCoincide)
            {
                puntaje += 20;
            }


            if (cilindradaCoincide)
            {
                puntaje += 20;
            }


            puntaje +=
                cantidadTokensModeloCoincidentes
                *
                35;

            puntaje +=
                cantidadTokensModeloAproximados
                *
                18;


            /*
             * Si todos los tokens distintivos
             * del modelo están presentes,
             * agregamos prioridad.
             */
            if (
                tokensDistintivosModelo.Count > 0
                &&
                cantidadTokensModeloCoincidentes
                ==
                tokensDistintivosModelo.Count
            )
            {
                puntaje += 50;
            }


            resultados.Add(
                (
                    modelo,
                    puntaje
                ));
        }


        if (
            resultados.Count == 0
        )
        {
            return new List<MotoModeloCandidatoDto>();
        }


        /*
         * Nos quedamos solamente con los modelos
         * que obtuvieron el MEJOR puntaje.
         *
         * Ejemplo:
         *
         * "Kenton Viva 110"
         *
         * VIVA gana claramente sobre otros Kenton 110.
         *
         * Pero:
         *
         * "Kenton 110"
         *
         * varios Kenton 110 pueden empatar.
         */
        var mejorPuntaje =
            resultados.Max(
                x => x.Puntaje);


        return resultados
            .Where(
                x =>
                    x.Puntaje
                    ==
                    mejorPuntaje)
            .Select(
                x => x.Modelo)
            .DistinctBy(
                x =>
                    x.IdModeloProducto)
            .Take(6)
            .ToList();
    }


    // =========================================================
    // COINCIDENCIA APROXIMADA DE TOKENS
    // =========================================================

    private static bool CoincideTokenAproximado(
        string tokenTexto,
        string tokenModelo)
    {
        if (
            string.IsNullOrWhiteSpace(
                tokenTexto)
            ||
            string.IsNullOrWhiteSpace(
                tokenModelo)
        )
        {
            return false;
        }

        if (
            tokenTexto.Length < 4
            ||
            tokenModelo.Length < 4
        )
        {
            return false;
        }

        if (
            Math.Abs(
                tokenTexto.Length
                -
                tokenModelo.Length)
            >
            1
        )
        {
            return false;
        }

        return
            DistanciaEdicionMaximaUno(
                tokenTexto,
                tokenModelo);
    }


    private static bool DistanciaEdicionMaximaUno(
        string a,
        string b)
    {
        if (
            string.Equals(
                a,
                b,
                StringComparison.OrdinalIgnoreCase)
        )
        {
            return true;
        }

        if (
            Math.Abs(
                a.Length
                -
                b.Length)
            >
            1
        )
        {
            return false;
        }

        var i = 0;
        var j = 0;
        var diferencias = 0;

        while (
            i < a.Length
            &&
            j < b.Length
        )
        {
            if (
                char.ToUpperInvariant(a[i])
                ==
                char.ToUpperInvariant(b[j])
            )
            {
                i++;
                j++;
                continue;
            }

            diferencias++;

            if (
                diferencias > 1
            )
            {
                return false;
            }

            if (
                a.Length > b.Length
            )
            {
                i++;
            }
            else if (
                b.Length > a.Length
            )
            {
                j++;
            }
            else
            {
                i++;
                j++;
            }
        }

        if (
            i < a.Length
            ||
            j < b.Length
        )
        {
            diferencias++;
        }

        return diferencias <= 1;
    }


    // =========================================================
    // RESOLVER MARCA DESDE TEXTO
    // =========================================================

    private static string? ResolverMarcaDesdeTexto(
        string mensaje,
        IReadOnlyList<MotoModeloCandidatoDto> modelos)
    {
        var texto =
            NormalizarTexto(
                mensaje);

        if (
            string.IsNullOrWhiteSpace(
                texto)
        )
        {
            return null;
        }

        var marcas =
            modelos
                .Where(
                    x =>
                        !string.IsNullOrWhiteSpace(
                            x.Marca))
                .Select(
                    x => x.Marca.Trim())
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(
                    x => x.Length)
                .ToList();

        return marcas
            .FirstOrDefault(
                marca =>
                    ContieneFrase(
                        texto,
                        NormalizarTexto(
                            marca)));
    }


    // =========================================================
    // DETECTAR SI ADEMAS DE LA MARCA MENCIONO UN MODELO/FAMILIA
    //
    // Evita que "Kenton en promo" se reduzca accidentalmente a
    // modelos cuyo nombre tambien contiene la palabra KENTON.
    // En cambio "Kenton Shark en promo" si conserva SHARK como
    // filtro adicional.
    // =========================================================

    private static bool TieneReferenciaModeloAdicional(
        string mensaje,
        string marca)
    {
        var tokens =
            ObtenerTokens(
                NormalizarTexto(
                    mensaje));

        var tokensMarca =
            ObtenerTokens(
                NormalizarTexto(
                    marca));

        var palabrasGenericas =
            new HashSet<string>(
                new[]
                {
                    "QUE", "CUAL", "CUALES", "TIENE", "TIENEN",
                    "TENES", "TENEMOS", "HAY", "EN", "DE", "DEL",
                    "LA", "LAS", "EL", "LOS", "UNA", "UN",
                    "PROMO", "PROMOS", "PROMOCION", "PROMOCIONES",
                    "OFERTA", "OFERTAS", "MES", "ESTE", "ESTAN",
                    "ESTA", "DISPONIBLE", "DISPONIBLES", "MODELO",
                    "MODELOS", "MOTO", "MOTOS", "MOSTRAME", "VER"
                },
                StringComparer.OrdinalIgnoreCase);

        return tokens.Any(
            token =>
                !tokensMarca.Contains(
                    token)
                &&
                !palabrasGenericas.Contains(
                    token)
                &&
                token.Length >= 2);
    }


    // =========================================================
    // PREGUNTA AMBIGUA
    // =========================================================

    private string ConstruirPreguntaAmbigua(
        IReadOnlyList<MotoModeloCandidatoDto> modelos)
    {
        var opciones =
            modelos
                .DistinctBy(
                    x => x.IdModeloProducto)
                .Take(6)
                .ToList();

        if (
            opciones.Count == 0
        )
        {
            return
                Texto("MOTO_AMBIGUA_VACIA");
        }

        if (
            opciones.Count == 1
        )
        {
            return
                Texto("MOTO_AMBIGUA_UNICA", ("MARCA", opciones[0].Marca), ("MODELO", opciones[0].Modelo));
        }

        var sb =
            new StringBuilder();

        sb.AppendLine(
            Texto("MOTO_AMBIGUA_LISTA_TITULO"));

        sb.AppendLine();

        foreach (
            var modelo
            in opciones)
        {
            sb.AppendLine(
                $"• *{modelo.Marca} {modelo.Modelo}*");
        }

        sb.AppendLine();
        sb.Append(
            Texto("MOTO_AMBIGUA_LISTA_CIERRE"));

        return sb.ToString();
    }


    // =========================================================
    // PARECE UNA SELECCION DE MODELO, PERO NO HUBO COINCIDENCIA
    // =========================================================

    private static bool PareceMencionModeloNoResuelta(
        string mensaje)
    {
        var texto =
            NormalizarTexto(
                mensaje);

        if (
            string.IsNullOrWhiteSpace(
                texto)
        )
        {
            return false;
        }

        return
            texto.Contains("ME INTERESA LA")
            ||
            texto.Contains("ME INTERESA EL")
            ||
            texto.Contains("ESTOY INTERESADO EN")
            ||
            texto.Contains("ESTOY INTERESADA EN")
            ||
            texto.Contains("QUIERO VER LA")
            ||
            texto.Contains("QUIERO VER EL")
            ||
            texto.Contains("QUIERO LA")
            ||
            texto.Contains("QUIERO EL")
            ||
            texto.Contains("BUSCO LA")
            ||
            texto.Contains("BUSCO EL");
    }


    // =========================================================
    // DETECTAR MENSAJE CLARAMENTE INCOMPRENSIBLE
    // =========================================================

    private static bool EsMensajeClaramenteIncomprensible(
        string mensaje)
    {
        var texto =
            NormalizarTexto(
                mensaje);

        if (
            string.IsNullOrWhiteSpace(
                texto)
        )
        {
            return false;
        }

        var letras =
            texto
                .Where(
                    char.IsLetter)
                .ToList();

        if (
            letras.Count < 8
        )
        {
            return false;
        }

        var cantidadVocales =
            letras.Count(
                c =>
                    "AEIOU".Contains(
                        char.ToUpperInvariant(c)));

        /*
         * Conservador: solo marcamos como incomprensible cuando
         * hay bastante texto y casi ninguna vocal. Evita tomar
         * mensajes normales como ruido.
         */
        return cantidadVocales <= 1;
    }


    // =========================================================
    // DETECTAR CAMBIO DE MODELO
    // =========================================================

    private static bool PareceCambioDeModelo(
        string mensaje)
    {
        var texto =
            NormalizarTexto(
                mensaje);


        if (
            string.IsNullOrWhiteSpace(
                texto)
        )
        {
            return false;
        }


        var frases =
            new[]
            {
                "OTRA MOTO",
                "OTRO MODELO",
                "OTRA OPCION",
                "QUIERO OTRA",
                "QUIERO OTRO",
                "CAMBIAR DE MOTO",
                "CAMBIAR MODELO",
                "VER OTRA",
                "VER OTRO"
            };


        return frases.Any(
            frase =>
                ContieneFrase(
                    texto,
                    frase));
    }


    // =========================================================
    // CODIGO PROMPT MARCA
    // =========================================================

    private static string ConstruirCodigoPromptMarca(
        string marca)
    {
        if (
            string.IsNullOrWhiteSpace(
                marca)
        )
        {
            throw new ReglasdeNegocioException(
                "La publicación no tiene una marca válida.");
        }


        var marcaNormalizada =
            NormalizarTexto(
                marca)
                .Replace(
                    " ",
                    "_");


        return
            $"MOTO_VENTAS_{marcaNormalizada}";
    }


    // =========================================================
    // ENLACES QUE PANAMBI NO PUEDE INSPECCIONAR
    // =========================================================

    private static bool EsEnlaceExternoNoVerificable(string? mensaje)
    {
        if (string.IsNullOrWhiteSpace(mensaje))
            return false;

        // No inferimos el producto por el dominio, la ruta de un video,
        // las miniaturas ni las previsualizaciones de WhatsApp.
        var enlaces = Regex.Matches(
            mensaje,
            @"(?i)(?<![\w@])(?:https?://|www\.)[^\s<>""']+");

        foreach (Match enlace in enlaces)
        {
            var textoUrl = enlace.Value.TrimEnd('.', ',', ';', ')', ']', '!', '?');
            if (!textoUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                textoUrl = "https://" + textoUrl;

            if (!Uri.TryCreate(textoUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                continue;

            // Nuestra URL de publicación con ID se maneja con
            // ExtraerIdPublicacion() antes de llegar a esta validación.
            // Un link público que no es una ficha con ID también necesita
            // aclaración, incluso si lleva palabras como moto o Shark.
            return true;
        }

        return false;
    }

    private static bool EsReferenciaAmbiguaAlEnlace(string? mensaje)
    {
        var texto = NormalizarTexto(mensaje);
        if (texto.Length == 0 || texto.Length > 80)
            return false;

        // Si ya dijo qué producto es, seguimos por el flujo normal.
        if (Regex.IsMatch(texto,
            @"\b(MOTO|MOTOS|MOTOCICLETA|SCOOTER|KENTON|TERRENO|TERRENOS|CASA|DEPARTAMENTO|INMUEBLE|AUTO|AUTOMOVIL|CELULAR|NOTEBOOK|TELEVISOR|ELECTRODOMESTICO)\b"))
            return false;

        return Regex.IsMatch(texto,
            @"^(ESE|ESA|ESO|ESTE|ESTA|ES ESE|ES ESA|EL DEL VIDEO|LA DEL VIDEO|EL DEL LINK|LA DEL LINK|EL DEL ENLACE|LA DEL ENLACE|EL DE LA FOTO|LA DE LA FOTO|SI|DALE|OK|ME INTERESA|ME INTERESA ESE|ME INTERESA ESA|CUANTO|CUANTO SALE|CUANTO CUESTA|Y EL PRECIO|QUE PRECIO TIENE|PRECIO|QUIERO ESE|QUIERO ESA)[!? .]*$");
    }

    // =========================================================
    // EXTRAER ID PUBLICACION
    // =========================================================

    private static int? ExtraerIdPublicacion(
        string mensaje)
    {
        if (
            string.IsNullOrWhiteSpace(
                mensaje)
        )
        {
            return null;
        }

        var patrones =
            new[]
            {
                @"\[?\s*TV_PRODUCTO\s*:\s*(?<id>\d+)\s*\]?",
                @"\bTVP\s*[-:#]?\s*(?<id>\d+)\b",
                @"\bTV-(?<id>\d+)\b",
                @"https?://(?:www\.)?tuvendedor\.com\.py/(?:share/)?producto/(?<id>\d+)\b",
                @"(?:www\.)?tuvendedor\.com\.py/(?:share/)?producto/(?<id>\d+)\b",
                @"https?://(?:localhost|127\.0\.0\.1)(?::\d+)?/(?:share/)?producto/(?<id>\d+)\b"
            };

        foreach (var patron in patrones)
        {
            var match =
                Regex.Match(
                    mensaje,
                    patron,
                    RegexOptions.IgnoreCase);

            if (
                match.Success
                &&
                int.TryParse(
                    match.Groups["id"].Value,
                    out var id)
                &&
                id > 0
            )
            {
                return id;
            }
        }

        return null;
    }


    // =========================================================
    // CONTEXTO DE PRODUCTO VIGENTE
    // =========================================================

    private bool EsContextoProductoVigente(
        MotoConversacionContextoDto? contexto)
    {
        if (contexto is null)
        {
            return false;
        }

        if (
            !contexto.IdModeloProductoActual.HasValue
            &&
            !contexto.IdPublicacion.HasValue
        )
        {
            return false;
        }

        if (
            contexto.FechaActualizacion
            ==
            default
        )
        {
            return false;
        }

        var antiguedad =
            DateTime.Now
            -
            contexto.FechaActualizacion;

        return
            antiguedad
            >=
            TimeSpan.Zero
            &&
            antiguedad
            <=
            TimeSpan.FromMinutes(
                _contextoMinutos);
    }


    // =========================================================
    // FOLLOW-UP DEL MODELO ACTUAL
    // =========================================================

    private static bool EsSeguimientoDelModeloActual(
        string mensaje)
    {
        var texto =
            NormalizarTexto(
                mensaje);

        if (
            string.IsNullOrWhiteSpace(
                texto)
        )
        {
            return false;
        }

        if (
            EsConsultaPrecioOCuotas(mensaje)
            ||
            EsConsultaPromociones(mensaje)
            ||
            EsConsultaDisponibilidad(mensaje)
            ||
            EsConsultaContadoEspecifica(mensaje)
            ||
            EsConsultaCreditoEspecifica(mensaje)
            ||
            !string.IsNullOrWhiteSpace(
                DetectarInicioOperacion(mensaje))
        )
        {
            return true;
        }

        var respuestasCortas =
            new HashSet<string>(
                new[]
                {
                    "SI", "NO", "OK", "OKAY", "DALE",
                    "BUENO", "PERFECTO", "LISTO", "ME INTERESA",
                    "QUIERO ESA", "QUIERO ESE", "ESA", "ESE"
                },
                StringComparer.OrdinalIgnoreCase);

        if (
            respuestasCortas.Contains(
                texto)
        )
        {
            return true;
        }

        /*
         * Respuestas naturales de continuación.
         *
         * Ejemplos reales:
         * "si pasame porfa"
         * "pasame nomas"
         * "dale pasame"
         * "mandame"
         * "decime"
         *
         * Estas frases deben conservar el modelo actual en contexto.
         * Antes caían al router general, que limpiaba el contexto del
         * producto y terminaba respondiendo como conversación abierta.
         */
        var marcadoresContinuacion =
            new[]
            {
                "PASAME",
                "MANDAME",
                "ENVIAME",
                "DECIME",
                "MOSTRAME",
                "CONTAME",
                "SI PORFA",
                "SI POR FAVOR",
                "DALE PASAME",
                "QUIERO SABER"
            };

        if (
            ObtenerTokens(
                texto)
                .Count
            <=
            8
            &&
            marcadoresContinuacion.Any(
                x => texto.Contains(x))
        )
        {
            return true;
        }

        var referenciasAlProducto =
            new[]
            {
                "ESA MOTO",
                "ESE MODELO",
                "ESTA MOTO",
                "ESTE MODELO",
                "LA MOTO",
                "EL MODELO",
                "DE ESA",
                "DE ESE",
                "PARA ESA",
                "PARA ESE"
            };

        var temasComerciales =
            new[]
            {
                "PRECIO", "CUOTA", "CONTADO", "CREDITO", "FINANCI",
                "ENTREGA", "PROMO", "OFERTA", "DISPON", "COMPR",
                "REQUISITO", "DOCUMENTO", "COLOR", "CC", "CILINDR"
            };

        return
            referenciasAlProducto.Any(
                x => texto.Contains(x))
            ||
            temasComerciales.Any(
                x => texto.Contains(x));
    }


    private static bool EsTipoMensaje(
        MotoConversacionRequest request,
        string tipo)
    {
        return string.Equals(
            (request.TipoMensaje ?? "TEXTO").Trim(),
            tipo,
            StringComparison.OrdinalIgnoreCase);
    }


    private async Task<bool> MensajeCorrespondeAMotoCatalogo(
        string mensaje)
    {
        var texto =
            NormalizarTexto(
                mensaje);

        if (
            ContieneFrase(
                texto,
                "MOTO")
            ||
            ContieneFrase(
                texto,
                "MOTOCICLETA")
            ||
            ContieneFrase(
                texto,
                "SCOOTER")
        )
        {
            return true;
        }

        var modelos =
            await _repository
                .ObtenerModelosMotoActivos();

        return
            ResolverModelosDesdeTexto(
                mensaje,
                modelos)
                .Count
            >
            0;
    }


    private static bool PareceConsultaProductoNuevoNoResuelto(
        string mensaje)
    {
        var texto =
            NormalizarTexto(
                mensaje);

        if (
            string.IsNullOrWhiteSpace(
                texto)
        )
        {
            return false;
        }

        var referenciasContexto =
            new[]
            {
                "ESA MOTO", "ESE MODELO", "ESTA MOTO", "ESTE MODELO",
                "DE ESA", "DE ESE", "PARA ESA", "PARA ESE"
            };

        if (
            referenciasContexto.Any(
                referencia =>
                    ContieneFrase(
                        texto,
                        referencia))
        )
        {
            return false;
        }

        var patrones =
            new[]
            {
                "QUIERO COMPRAR ",
                "QUIERO UNA ",
                "QUIERO UN ",
                "BUSCO UNA ",
                "BUSCO UN ",
                "BUSCO ",
                "ME INTERESA LA ",
                "ME INTERESA EL ",
                "ME INTERESA ",
                "PRECIO DE ",
                "PRECIO DEL ",
                "PRECIO DE LA ",
                "PRECIO ",
                "CUANTO CUESTA ",
                "CUANTO SALE ",
                "CUOTAS DE ",
                "CREDITO PARA ",
                "CONTADO PARA "
            };

        var restosGenericos =
            new HashSet<string>(
                new[]
                {
                    "ESA", "ESE", "ESTA", "ESTE",
                    "MOTO", "MOTOS", "MODELO", "MODELOS",
                    "CONTADO", "CREDITO", "CUOTA", "CUOTAS",
                    "PRECIO", "PRECIOS"
                },
                StringComparer.OrdinalIgnoreCase);

        foreach (var patron in patrones)
        {
            var posicion =
                texto.IndexOf(
                    patron,
                    StringComparison.OrdinalIgnoreCase);

            if (posicion < 0)
            {
                continue;
            }

            var resto =
                texto[
                    (posicion + patron.Length)..]
                    .Trim();

            if (
                !string.IsNullOrWhiteSpace(
                    resto)
                &&
                !restosGenericos.Contains(
                    resto)
            )
            {
                return true;
            }
        }

        return false;
    }


    private static bool EsConsultaComercialEvidenteOtroProducto(
        string mensaje)
    {
        var texto =
            NormalizarTexto(
                mensaje);

        if (
            string.IsNullOrWhiteSpace(
                texto)
        )
        {
            return false;
        }

        var productosNoMoto =
            new[]
            {
                "TERRENO", "TERRENOS", "LOTE", "LOTES",
                "CASA", "CASAS", "DEPARTAMENTO", "DEPARTAMENTOS",
                "INMUEBLE", "INMUEBLES", "PROPIEDAD", "PROPIEDADES",
                "DUPLEX", "LOCAL", "LOCALES",
                "AUTO", "AUTOS", "AUTOMOVIL", "AUTOMOVILES",
                "CAMIONETA", "CAMIONETAS", "VEHICULO", "VEHICULOS",
                "CELULAR", "CELULARES", "TELEFONO", "TELEFONOS",
                "IPHONE", "NOTEBOOK", "NOTEBOOKS",
                "COMPUTADORA", "COMPUTADORAS", "TELEVISOR", "TELEVISORES",
                "HELADERA", "HELADERAS", "MUEBLE", "MUEBLES",
                "SOMIER", "CAMA", "CAMAS", "BICICLETA", "BICICLETAS",
                "SERVICIO", "SERVICIOS"
            };

        var intencionComercial =
            new[]
            {
                "PRECIO", "CUESTA", "CUANTO SALE", "COMPRAR",
                "VENDER", "BUSCO", "ME INTERESA", "DISPONIBLE",
                "TIENEN", "TIENES", "TENES", "HAY",
                "CUOTA", "CREDITO", "CONTADO", "ALQUILER",
                "ALQUILAR", "PUBLICACION", "OFERTA"
            };

        return
            productosNoMoto.Any(
                producto =>
                    ContieneFrase(
                        texto,
                        producto))
            &&
            intencionComercial.Any(
                intencion =>
                    texto.Contains(
                        intencion,
                        StringComparison.OrdinalIgnoreCase));
    }


    private static bool EsMensajeDerivacionOtroProducto(
        string? mensaje)
    {
        var texto =
            NormalizarTexto(
                mensaje);

        return
            !string.IsNullOrWhiteSpace(
                texto)
            &&
            ContieneFrase(
                texto,
                MARCADOR_DERIVACION_OTRO_PRODUCTO);
    }


    // =========================================================
    // NORMALIZAR IDENTIFICADOR
    // =========================================================

    private static string NormalizarIdentificador(
        string telefono)
    {
        if (
            string.IsNullOrWhiteSpace(
                telefono)
        )
        {
            return string.Empty;
        }


        var numeros =
            Regex.Replace(
                telefono,
                @"\D",
                string.Empty);


        return string.IsNullOrWhiteSpace(
            numeros)

                ? telefono.Trim()
                : numeros;
    }


    // =========================================================
    // NORMALIZAR TEXTO
    // =========================================================

    private static string NormalizarTexto(
        string? valor)
    {
        if (
            string.IsNullOrWhiteSpace(
                valor)
        )
        {
            return string.Empty;
        }


        var normalizado =
            valor
                .Normalize(
                    NormalizationForm.FormD);


        var sb =
            new StringBuilder();


        foreach (
            var caracter
            in normalizado)
        {
            var categoria =
                CharUnicodeInfo
                    .GetUnicodeCategory(
                        caracter);


            if (
                categoria
                ==
                UnicodeCategory.NonSpacingMark
            )
            {
                continue;
            }


            if (
                char.IsLetterOrDigit(
                    caracter)
            )
            {
                sb.Append(
                    char.ToUpperInvariant(
                        caracter));
            }
            else
            {
                sb.Append(
                    ' ');
            }
        }


        return Regex
            .Replace(
                sb.ToString(),
                @"\s+",
                " ")
            .Trim();
    }


    // =========================================================
    // TOKENS
    // =========================================================

    private static HashSet<string> ObtenerTokens(
        string texto)
    {
        return texto
            .Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries
                |
                StringSplitOptions.TrimEntries)
            .ToHashSet(
                StringComparer.OrdinalIgnoreCase);
    }


    private static bool EsTokenSoloNumerico(
        string token)
    {
        return token.All(
            char.IsDigit);
    }


    private static bool ContieneFrase(
        string texto,
        string frase)
    {
        if (
            string.IsNullOrWhiteSpace(
                texto)
            ||
            string.IsNullOrWhiteSpace(
                frase)
        )
        {
            return false;
        }


        return
            $" {texto} "
                .Contains(
                    $" {frase} ",
                    StringComparison.OrdinalIgnoreCase);
    }


    // =========================================================
    // SYSTEM PROMPT
    // =========================================================

    private string ConstruirPromptSistema(
     string promptBase,
     string promptMarca,
     MotoOfertaDto oferta,
     string instruccionesComerciales)
    {
        var sb =
            new StringBuilder();


        // =========================================================
        // REGLAS GENERALES
        // =========================================================

        sb.AppendLine(
            "========================================");

        sb.AppendLine(
            "REGLAS GENERALES TUVENDEDOR");

        sb.AppendLine(
            "========================================");

        sb.AppendLine(
            promptBase.Trim());


        sb.AppendLine();


        // =========================================================
        // REGLAS DE MARCA
        // =========================================================

        sb.AppendLine(
            "========================================");

        sb.AppendLine(
            $"REGLAS COMERCIALES DE {oferta.Modelo.Marca.ToUpperInvariant()}");

        sb.AppendLine(
            "========================================");

        sb.AppendLine(
            promptMarca.Trim());


        sb.AppendLine();


        // =========================================================
        // PRODUCTO
        // =========================================================

        sb.AppendLine(
            "========================================");

        sb.AppendLine(
            "PRODUCTO IDENTIFICADO");

        sb.AppendLine(
            "========================================");


        sb.AppendLine(
            $"Marca: {oferta.Modelo.Marca}");


        sb.AppendLine(
            $"Modelo: {oferta.Modelo.Nombre}");


        /*
         * No enviamos al modelo:
         *
         * - CodigoReferencia
         * - porcentaje descuento
         * - precio lista
         * - precio base
         * - precio distribuidor
         *
         * porque no son necesarios para conversar
         * con el cliente.
         */


        // =========================================================
        // CONTADO
        // =========================================================

        if (
            !string.IsNullOrWhiteSpace(
                oferta.Contado.PrecioFinalFormateado)
        )
        {
            sb.AppendLine();

            sb.AppendLine(
                "PRECIO FINAL CONTADO:");

            sb.AppendLine(
                oferta.Contado.PrecioFinalFormateado);
        }


        // =========================================================
        // FINANCIACION
        // =========================================================

        if (
            oferta.Credito.Planes is not null
            &&
            oferta.Credito.Planes.Count > 0
        )
        {
            sb.AppendLine();

            sb.AppendLine(
                "FINANCIACION DISPONIBLE:");


            /*
             * A Qwen solamente le interesa saber
             * si puede comunicar que este mes existe
             * una promoción en cuotas.
             *
             * NO enviamos fechas.
             */
            if (
                oferta.Credito.TienePromo
            )
            {
                sb.AppendLine(
                    Texto("MOTO_PROMO_FACTO_IA"));
            }


            foreach (
                var plan
                in oferta.Credito.Planes)
            {
                sb.AppendLine(
                    $"- {plan.Resumen}");


                if (
                    plan.EntregaInicial > 0
                )
                {
                    sb.AppendLine(
                        $"  Entrega inicial: {plan.EntregaInicialFormateada}");
                }
            }
        }
        else
        {
            sb.AppendLine();

            sb.AppendLine(
                "FINANCIACION:");

            sb.AppendLine(
                Texto("MOTO_NO_CREDITO_FACTO_IA"));
        }


        // Las instrucciones editables se administran únicamente en SQL Server.
        // La oferta, el precio y los planes verificados siguen viniendo de BBDD.
        sb.AppendLine();
        sb.AppendLine(instruccionesComerciales);

        return sb.ToString();
    }


    // =========================================================
    // DETECTAR CONSULTA DE PROMOCIONES
    // =========================================================

    private static bool EsConsultaPromociones(
        string mensaje)
    {
        var texto =
            NormalizarTexto(
                mensaje);

        if (
            string.IsNullOrWhiteSpace(
                texto)
        )
        {
            return false;
        }

        return
            texto.Contains("PROMO")
            ||
            texto.Contains("PROMOCION")
            ||
            texto.Contains("PROMOCIONES")
            ||
            texto.Contains("OFERTA DEL MES")
            ||
            texto.Contains("EN OFERTA");
    }


    private static bool EsConsultaCatalogoPromociones(
        string mensaje)
    {
        if (
            !EsConsultaPromociones(
                mensaje)
        )
        {
            return false;
        }

        var texto =
            NormalizarTexto(
                mensaje);

        return
            texto.Contains("MODELOS")
            ||
            texto.Contains("MOTOS")
            ||
            texto.Contains("CUALES")
            ||
            texto.Contains("QUE TIENEN")
            ||
            texto.Contains("QUE HAY")
            ||
            texto.Contains("OTROS")
            ||
            texto.Contains("OTRAS")
            ||
            texto.Contains("PROMOS DISPONIBLES")
            ||
            texto.StartsWith("PROMOS")
            ||
            texto.StartsWith("PROMOCIONES");
    }


    // =========================================================
    // DETECTAR CONSULTA DE OTROS MODELOS
    // =========================================================

    private static bool EsConsultaOtrosModelos(
        string mensaje)
    {
        var texto =
            NormalizarTexto(
                mensaje);

        if (
            string.IsNullOrWhiteSpace(
                texto)
        )
        {
            return false;
        }

        var hablaDeCatalogo =
            texto.Contains("MODELO")
            ||
            texto.Contains("MODELOS")
            ||
            texto.Contains("MOTO")
            ||
            texto.Contains("MOTOS")
            ||
            texto.Contains("CATALOGO")
            ||
            texto.Contains("OPCIONES");

        if (!hablaDeCatalogo)
        {
            return false;
        }

        /*
         * Frases naturales:
         *
         * "que modelos tenes"
         * "me pasas los modelos"
         * "que motos hay"
         * "mostrame las opciones"
         * "que otras motos tenes"
         *
         * Los nombres reales de los modelos NO estan aca.
         * Siempre salen de la BBDD.
         */
        return
            texto.Contains("QUE ")
            ||
            texto.StartsWith("QUE")
            ||
            texto.Contains("CUALES")
            ||
            texto.Contains("PASAME")
            ||
            texto.Contains("PASAS")
            ||
            texto.Contains("MOSTRAME")
            ||
            texto.Contains("MOSTRAR")
            ||
            texto.Contains("TENES")
            ||
            texto.Contains("TIENES")
            ||
            texto.Contains("HAY")
            ||
            texto.Contains("OTRO")
            ||
            texto.Contains("OTRA")
            ||
            texto.Contains("OTROS")
            ||
            texto.Contains("OTRAS")
            ||
            texto.Contains("CATALOGO")
            ||
            texto.Contains("OPCIONES");
    }


    // =========================================================
    // CONSULTA DE PRECIO / CUOTAS SIN MODELO ELEGIDO
    // =========================================================

    private static bool EsConsultaPrecioOCuotas(
        string mensaje)
    {
        var texto =
            NormalizarTexto(
                mensaje);

        if (
            string.IsNullOrWhiteSpace(
                texto)
        )
        {
            return false;
        }

        return
            texto.Contains("PRECIO")
            ||
            texto.Contains("PRECIOS")
            ||
            texto.Contains("CUOTA")
            ||
            texto.Contains("CUOTAS")
            ||
            texto.Contains("CONTADO")
            ||
            texto.Contains("CREDITO")
            ||
            texto.Contains("FINANCI")
            ||
            texto.Contains("CUANTO SALE")
            ||
            texto.Contains("CUANTO CUESTA")
            ||
            texto.Contains("VALOR");
    }


    // =========================================================
    // SALUDO SIMPLE
    // =========================================================

    // DICCIONARIO_SOCIAL: una expresión por línea, TIPO|EXPRESION.
    // No usa coincidencias parciales: "hola" nunca intercepta "hola, precio del terreno".
    private static string? DetectarIntencionSocial(string? mensaje, string? diccionario)
    {
        var texto = NormalizarTexto(mensaje);
        if (texto.Length == 0 || texto.Length > 120 || string.IsNullOrWhiteSpace(diccionario))
            return null;
        foreach (var linea in diccionario.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var partes = linea.Trim().Split('|', 2);
            if (partes.Length != 2) continue;
            var categoria = partes[0].Trim().ToUpperInvariant();
            if (categoria is not ("SALUDO" or "RESPUESTA_SOCIAL" or "AGRADECIMIENTO" or "DESPEDIDA" or "ACLARACION"))
                continue;
            var expresion = NormalizarTexto(partes[1]);
            if (expresion.Length < 2) continue;
            // Coincidencia exacta, o saludo seguido del nombre/apodo de la persona
            // (hasta dos palabras); se evita interceptar preguntas de negocio.
            if (texto == expresion) return categoria;
            if (categoria == "SALUDO" && texto.StartsWith(expresion + " ", StringComparison.Ordinal))
            {
                var resto = texto[(expresion.Length + 1)..];
                if (resto.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 2 &&
                    Regex.IsMatch(resto, @"^[A-Z]{2,25}( [A-Z]{2,25})?$") &&
                    !ContieneSolicitudComercialExplicita(resto) &&
                    !Regex.IsMatch(resto, @"\b(QUE|COMO|CUANTO|DONDE|TIENEN|VENDEN|BUSCO|QUIERO|PRECIO|TERRENO|MOTO|INMUEBLE|CASA|CUOTAS|DISPONIBLE|PRODUCTOS|OFRECEN)\b"))
                    return categoria;
            }
        }
        return null;
    }

    private static bool EsMensajeSocialAmbiguo(string? mensaje)
    {
        var texto = NormalizarTexto(mensaje);
        if (texto.Length < 3 || texto.Length > 95) return false;
        // Nunca interrumpir respuestas a una publicación, cotización o negociación.
        if (Regex.IsMatch(texto, @"\b(ME INTERESA|INTERESA|ESE|ESA|AQUEL|AQUELLA|PRIMERO|SEGUNDO|TERCERO|QUIERO|BUSCO|NECESITO|TENES|TIENEN|PASAME|MANDAME|COTIZ|SIMUL|CUANTO|CUANTOS|DONDE|ENTREGA|MODELO|VERSION|DISPONIBLE|VISITAR|MOSTRAME|VER|COMPRAR|RESERVAR|PAGAR|COMO HAGO|QUE OFRECEN|QUE TIENEN|QUE VENDEN|PREPARA|OFERTA|PROMOCION|OK|DALE|SI|NO)\b"))
            return false;
        return Regex.IsMatch(texto, @"\b(HOLA|HOLI|HOLIS|BUENAS|SALUDOS|QUE TAL|COMO ESTAS|COMO ANDAS|QUE CONTAS|TODO BIEN|TODO TRANQUI|MBAEICHAPA|MBAETEKO|MAITEI|GRACIAS|AGUYJE|BIEN Y VOS|Y VOS|CHE AMIGO|CHE AMIGA|NO ENTIENDO)\b");
    }

    private static bool ContieneSolicitudComercialExplicita(string? mensaje)
    {
        var texto = NormalizarTexto(mensaje);
        return Regex.IsMatch(texto, @"\b(MOTO|MOTOS|TERRENO|TERRENOS|INMUEBLE|INMUEBLES|CASA|CASAS|DEPARTAMENTO|DEPARTAMENTOS|LOTE|LOTES|PRECIO|PRECIOS|CUESTA|SALE|CUOTA|CUOTAS|CREDITO|CONTADO|COMPRA|COMPRAR|VENDEN|VENDER|OFRECEN|PRODUCTO|PRODUCTOS|CATALOGO|PUBLICACION|PUBLICACIONES|FINANCIACION|COTIZACION|DISPONIBLE|DISPONIBILIDAD)\b");
    }


    // =========================================================
    // CONSULTA GENERAL / PUNTO DE PARTIDA POR MARCAS
    // =========================================================

    private static bool EsConsultaGenericaMarcas(
        string mensaje)
    {
        var texto =
            NormalizarTexto(
                mensaje);

        if (
            string.IsNullOrWhiteSpace(
                texto)
        )
        {
            return false;
        }

        /*
         * Si pidió explícitamente "modelos" o "catálogo",
         * dejamos que EsConsultaOtrosModelos muestre los modelos.
         */
        if (
            texto.Contains("MODELO")
            ||
            texto.Contains("MODELOS")
            ||
            texto.Contains("CATALOGO")
        )
        {
            return false;
        }

        return
            texto == "QUE TIENEN"
            ||
            texto == "QUE TENES"
            ||
            texto == "QUE HAY"
            ||
            texto.Contains("QUE MOTOS TIENEN")
            ||
            texto.Contains("QUE MOTOS TENES")
            ||
            texto.Contains("QUE MARCAS TIENEN")
            ||
            texto.Contains("QUE MARCAS TENES")
            ||
            texto.Contains("QUIERO UNA MOTO")
            ||
            texto.Contains("BUSCO UNA MOTO")
            ||
            texto.Contains("ESTOY BUSCANDO UNA MOTO")
            ||
            texto.Contains("QUIERO VER MOTOS")
            ||
            texto.Contains("MOSTRAME MOTOS")
            ||
            texto.Contains("MOSTRAR MOTOS")
            ||
            texto.Contains("OPCIONES DE MOTO")
            ||
            texto.Contains("OPCIONES DE MOTOS");
    }


    // =========================================================
    // CONSULTA POR UNA MARCA ESPECIFICA
    //
    // Este parser es deliberadamente determinista: para preguntas
    // de disponibilidad de marca NO usamos Ollama. Primero buscamos
    // contra las marcas reales de BBDD; si no existe, informamos eso
    // y mostramos solamente las marcas realmente cargadas.
    // =========================================================

    private static (
        bool EsConsulta,
        string? MarcaCatalogo,
        string? MarcaSolicitada)
        ResolverConsultaMarcaCatalogo(
            string mensaje,
            IReadOnlyList<MotoModeloCandidatoDto> modelos,
            bool permitirMarcaDesconocida)
    {
        var texto =
            NormalizarTexto(
                mensaje);

        if (
            string.IsNullOrWhiteSpace(
                texto)
        )
        {
            return (false, null, null);
        }

        var tieneIntencionMarca =
            TieneIntencionConsultaMarca(
                texto);

        if (!tieneIntencionMarca)
        {
            return (false, null, null);
        }

        var marcasCatalogo =
            modelos
                .Where(
                    x =>
                        !string.IsNullOrWhiteSpace(
                            x.Marca))
                .Select(
                    x => x.Marca.Trim())
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(
                    x => NormalizarTexto(x).Length)
                .ToList();

        foreach (
            var marca
            in marcasCatalogo)
        {
            var marcaNormalizada =
                NormalizarTexto(
                    marca);

            if (
                !string.IsNullOrWhiteSpace(
                    marcaNormalizada)
                &&
                ContieneFrase(
                    texto,
                    marcaNormalizada)
            )
            {
                return (
                    true,
                    marca,
                    marca);
            }
        }

        if (!permitirMarcaDesconocida)
        {
            return (false, null, null);
        }

        var marcaSolicitada =
            ExtraerMarcaSolicitada(
                texto);

        if (
            string.IsNullOrWhiteSpace(
                marcaSolicitada)
        )
        {
            return (false, null, null);
        }

        return (
            true,
            null,
            marcaSolicitada);
    }


    private static bool TieneIntencionConsultaMarca(
        string textoNormalizado)
    {
        return
            ContieneFrase(
                textoNormalizado,
                "MARCA")
            ||
            ContieneFrase(
                textoNormalizado,
                "TIENEN")
            ||
            ContieneFrase(
                textoNormalizado,
                "TIENES")
            ||
            ContieneFrase(
                textoNormalizado,
                "TENES")
            ||
            ContieneFrase(
                textoNormalizado,
                "VENDEN")
            ||
            ContieneFrase(
                textoNormalizado,
                "TRABAJAN CON")
            ||
            ContieneFrase(
                textoNormalizado,
                "MANEJAN")
            ||
            ContieneFrase(
                textoNormalizado,
                "DISTRIBUYEN")
            ||
            textoNormalizado.StartsWith(
                "HAY ",
                StringComparison.OrdinalIgnoreCase);
    }


    private static string? ExtraerMarcaSolicitada(
        string textoNormalizado)
    {
        var tokens =
            textoNormalizado
                .Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries)
                .ToList();

        if (tokens.Count == 0)
        {
            return null;
        }

        var indiceMarca =
            tokens.FindIndex(
                x => x == "MARCA");

        if (indiceMarca >= 0)
        {
            return ConstruirCandidatoMarcaDesdeTokens(
                tokens,
                indiceMarca + 1);
        }

        var prefijos =
            new[]
            {
                new[] { "TRABAJAN", "CON" },
                new[] { "DISTRIBUYEN" },
                new[] { "MANEJAN" },
                new[] { "VENDEN" },
                new[] { "TIENEN" },
                new[] { "TIENES" },
                new[] { "TENES" },
                new[] { "HAY" }
            };

        foreach (
            var prefijo
            in prefijos)
        {
            var indice =
                BuscarSecuenciaTokens(
                    tokens,
                    prefijo);

            if (indice >= 0)
            {
                var candidato =
                    ConstruirCandidatoMarcaDesdeTokens(
                        tokens,
                        indice + prefijo.Length);

                if (
                    !string.IsNullOrWhiteSpace(
                        candidato)
                )
                {
                    return candidato;
                }
            }
        }

        var indiceNo =
            -1;

        for (
            var i = 1;
            i + 1 < tokens.Count;
            i++
        )
        {
            if (
                tokens[i] == "NO"
                &&
                (
                    tokens[i + 1] == "TIENEN"
                    ||
                    tokens[i + 1] == "TIENES"
                    ||
                    tokens[i + 1] == "TENES"
                )
            )
            {
                indiceNo = i;
                break;
            }
        }

        if (indiceNo > 0)
        {
            var anteriores =
                tokens
                    .Take(
                        indiceNo)
                    .ToList();

            while (
                anteriores.Count > 0
                &&
                EsTokenIntroductorioMarca(
                    anteriores[0])
            )
            {
                anteriores.RemoveAt(0);
            }

            if (anteriores.Count > 0)
            {
                var candidato =
                    string.Join(
                        " ",
                        anteriores.TakeLast(3));

                if (
                    EsCandidatoMarcaValido(
                        candidato)
                )
                {
                    return candidato;
                }
            }
        }

        return null;
    }


    private static int BuscarSecuenciaTokens(
        IReadOnlyList<string> tokens,
        IReadOnlyList<string> secuencia)
    {
        if (
            secuencia.Count == 0
            ||
            tokens.Count < secuencia.Count
        )
        {
            return -1;
        }

        for (
            var i = 0;
            i <= tokens.Count - secuencia.Count;
            i++
        )
        {
            var coincide =
                true;

            for (
                var j = 0;
                j < secuencia.Count;
                j++
            )
            {
                if (
                    !string.Equals(
                        tokens[i + j],
                        secuencia[j],
                        StringComparison.OrdinalIgnoreCase)
                )
                {
                    coincide = false;
                    break;
                }
            }

            if (coincide)
            {
                return i;
            }
        }

        return -1;
    }


    private static string? ConstruirCandidatoMarcaDesdeTokens(
        IReadOnlyList<string> tokens,
        int inicio)
    {
        var resultado =
            new List<string>();

        for (
            var i = inicio;
            i < tokens.Count && resultado.Count < 3;
            i++
        )
        {
            var token =
                tokens[i];

            if (EsTokenCorteMarca(token))
            {
                break;
            }

            if (
                EsTokenIntroductorioMarca(
                    token)
            )
            {
                continue;
            }

            resultado.Add(
                token);
        }

        if (resultado.Count == 0)
        {
            return null;
        }

        var candidato =
            string.Join(
                " ",
                resultado);

        return
            EsCandidatoMarcaValido(
                candidato)
                ? candidato
                : null;
    }


    private static bool EsTokenIntroductorioMarca(
        string token)
    {
        return
            token == "LA"
            ||
            token == "EL"
            ||
            token == "DE"
            ||
            token == "UNA"
            ||
            token == "UN"
            ||
            token == "MARCA"
            ||
            token == "MOTO"
            ||
            token == "MOTOS";
    }


    private static bool EsTokenCorteMarca(
        string token)
    {
        return
            token == "NO"
            ||
            token == "SI"
            ||
            token == "TIENEN"
            ||
            token == "TIENES"
            ||
            token == "TENES"
            ||
            token == "VENDEN"
            ||
            token == "TRABAJAN"
            ||
            token == "MANEJAN"
            ||
            token == "DISTRIBUYEN"
            ||
            token == "DISPONIBLE"
            ||
            token == "DISPONIBLES"
            ||
            token == "ACTUALMENTE"
            ||
            token == "AHORA"
            ||
            token == "TODAVIA"
            ||
            token == "TAMBIEN"
            ||
            token == "ACA"
            ||
            token == "AQUI";
    }


    private static bool EsCandidatoMarcaValido(
        string candidato)
    {
        var texto =
            NormalizarTexto(
                candidato);

        if (
            string.IsNullOrWhiteSpace(
                texto)
        )
        {
            return false;
        }

        var invalidos =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase)
            {
                "MOTO",
                "MOTOS",
                "MODELO",
                "MODELOS",
                "MARCA",
                "MARCAS",
                "ALGUNA",
                "ALGUN",
                "ALGO",
                "OPCION",
                "OPCIONES",
                "STOCK",
                "CATALOGO",
                "CATALOGOS",
                "CUOTA",
                "CUOTAS",
                "CREDITO",
                "CONTADO",
                "PRECIO",
                "PRECIOS",
                "PROMO",
                "PROMOS",
                "PROMOCION",
                "PROMOCIONES",
                "FINANCIACION",
                "FINANCIAMIENTO",
                "DELIVERY",
                "STOCK",
                "COLOR",
                "COLORES",
                "ROJO",
                "ROJA",
                "AZUL",
                "NEGRO",
                "NEGRA",
                "BLANCO",
                "BLANCA"
            };

        return
            !invalidos.Contains(
                texto);
    }


    private string ConstruirRespuestaMarcaNoDisponible(
        IReadOnlyList<MotoModeloCandidatoDto> modelos,
        string? marcaSolicitada)
    {
        var marcas =
            modelos
                .Where(
                    x =>
                        !string.IsNullOrWhiteSpace(
                            x.Marca))
                .Select(
                    x => x.Marca.Trim())
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .OrderBy(
                    x => x)
                .ToList();

        var nombreConsultado =
            string.IsNullOrWhiteSpace(
                marcaSolicitada)
                ? "esa marca"
                : $"*{marcaSolicitada.Trim()}*";

        var sb =
            new StringBuilder();

        sb.AppendLine(
            Texto("MOTO_MARCA_NO_DISPONIBLE", ("MARCA", nombreConsultado)));

        if (marcas.Count == 0)
        {
            sb.Append(
                Texto("MOTO_MARCA_NO_DISPONIBLE_WEB"));

            return sb.ToString();
        }

        sb.AppendLine();
        sb.AppendLine(
            Texto("MOTO_MARCAS_ALTERNATIVAS_TITULO"));
        sb.AppendLine();

        foreach (
            var marca
            in marcas)
        {
            sb.AppendLine(
                $"• {marca}");
        }

        sb.AppendLine();
        sb.Append(
            Texto("MOTO_MARCAS_ALTERNATIVAS_CIERRE"));

        return sb.ToString();
    }


    // =========================================================
    // DETECTAR SI SOLO MENCIONO LA MARCA
    //
    // Ejemplo:
    // "Kenton"
    // "mostrame Kenton"
    //
    // Si tambien escribio un modelo concreto,
    // ResolverModelosDesdeTexto ya lo procesa antes.
    // =========================================================

    private static bool EsSeleccionSoloMarca(
        string mensaje,
        string marca)
    {
        var texto =
            NormalizarTexto(
                mensaje);

        var marcaNormalizada =
            NormalizarTexto(
                marca);

        if (
            string.IsNullOrWhiteSpace(
                texto)
            ||
            string.IsNullOrWhiteSpace(
                marcaNormalizada)
        )
        {
            return false;
        }

        if (
            string.Equals(
                texto,
                marcaNormalizada,
                StringComparison.OrdinalIgnoreCase)
        )
        {
            return true;
        }

        var frasesPermitidas =
            new[]
            {
                $"QUIERO {marcaNormalizada}",
                $"MOSTRAME {marcaNormalizada}",
                $"MOSTRAR {marcaNormalizada}",
                $"QUE TENES DE {marcaNormalizada}",
                $"QUE TIENES DE {marcaNormalizada}",
                $"MODELOS {marcaNormalizada}",
                $"MODELOS DE {marcaNormalizada}",
                $"MOTOS {marcaNormalizada}",
                $"MOTOS DE {marcaNormalizada}"
            };

        return frasesPermitidas.Any(
            frase =>
                ContieneFrase(
                    texto,
                    frase));
    }


    // =========================================================
    // MOSTRAR MODELOS DE UNA MARCA DESDE BBDD
    // =========================================================

    private string ConstruirRespuestaModelosMarca(
        IReadOnlyList<MotoModeloCandidatoDto> modelos,
        string marca)
    {
        var encontrados =
            modelos
                .Where(
                    x =>
                        string.Equals(
                            x.Marca,
                            marca,
                            StringComparison.OrdinalIgnoreCase))
                .DistinctBy(
                    x => x.IdModeloProducto)
                .OrderBy(
                    x => x.Modelo)
                .ToList();

        if (
            encontrados.Count == 0
        )
        {
            return
                Texto("MOTO_MODELOS_MARCA_VACIOS", ("MARCA", marca));
        }

        var nombres =
            encontrados
                .Select(
                    x => x.Modelo)
                .ToList();

        var sb =
            new StringBuilder();

        sb.AppendLine(
            Texto("MOTO_MODELOS_MARCA_TITULO", ("MARCA", marca)));

        sb.AppendLine();

        sb.AppendLine(
            ConstruirGrillaTresColumnas(
                nombres));

        sb.AppendLine();
        sb.AppendLine();

        sb.Append(
            Texto("MOTO_MODELOS_MARCA_CIERRE"));

        return sb.ToString();
    }


    // =========================================================
    // GRILLA DE 3 COLUMNAS PARA WHATSAPP
    // =========================================================

    private static string ConstruirGrillaTresColumnas(
        IReadOnlyList<string> valores)
    {
        if (
            valores is null
            ||
            valores.Count == 0
        )
        {
            return string.Empty;
        }

        const int columnas = 3;

        var filas =
            (int)Math.Ceiling(
                valores.Count
                /
                (double)columnas);

        var ancho =
            Math.Min(
                22,
                Math.Max(
                    14,
                    valores
                        .Where(
                            x =>
                                !string.IsNullOrWhiteSpace(
                                    x))
                        .Select(
                            x => x.Trim().Length)
                        .DefaultIfEmpty(14)
                        .Max()
                    +
                    2));

        var sb =
            new StringBuilder();

        sb.AppendLine("```");

        for (
            var fila = 0;
            fila < filas;
            fila++
        )
        {
            for (
                var columna = 0;
                columna < columnas;
                columna++
            )
            {
                var indice =
                    fila
                    +
                    columna * filas;

                if (
                    indice >= valores.Count
                )
                {
                    continue;
                }

                var valor =
                    valores[indice]
                        ?.Trim()
                    ??
                    string.Empty;

                if (
                    valor.Length >= ancho
                )
                {
                    valor =
                        valor[..(ancho - 2)]
                        +
                        "…";
                }

                sb.Append(
                    valor.PadRight(
                        ancho));
            }

            sb.AppendLine();
        }

        sb.Append("```");

        return sb.ToString();
    }


    // =========================================================
    // MOSTRAR MARCAS COMO PUNTO DE PARTIDA
    // =========================================================

    private string ConstruirRespuestaMarcasDisponibles(
        IReadOnlyList<MotoModeloCandidatoDto> modelos,
        string encabezado)
    {
        var marcas =
            modelos
                .Where(
                    x =>
                        !string.IsNullOrWhiteSpace(
                            x.Marca))
                .Select(
                    x => x.Marca.Trim())
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .OrderBy(
                    x => x)
                .ToList();

        if (
            marcas.Count == 0
        )
        {
            return
                Texto("MOTO_MARCAS_VACIAS");
        }

        var sb =
            new StringBuilder();

        sb.AppendLine(
            encabezado);

        sb.AppendLine();

        if (
            marcas.Count == 1
        )
        {
            sb.AppendLine(
                Texto("MOTO_MARCA_UNICA", ("MARCA", marcas[0])));

            sb.AppendLine();

            sb.Append(
                Texto("MOTO_MARCA_UNICA_PREGUNTA", ("MARCA", marcas[0])));

            return sb.ToString();
        }

        sb.AppendLine(
            Texto("MOTO_MARCAS_TITULO"));

        sb.AppendLine();

        foreach (
            var marca
            in marcas)
        {
            sb.AppendLine(
                $"• {marca}");
        }

        sb.AppendLine();

        sb.Append(
            Texto("MOTO_MARCAS_CIERRE"));

        return sb.ToString();
    }


    // =========================================================
    // RESPUESTA DE MODELOS EN PROMO
    // =========================================================

    private string ConstruirRespuestaPromociones(
        IReadOnlyList<MotoModeloCandidatoDto> modelos,
        string? marca)
    {
        var disponibles =
            modelos
                .DistinctBy(
                    x => x.IdModeloProducto)
                .OrderBy(
                    x => x.Marca)
                .ThenBy(
                    x => x.Modelo)
                .ToList();

        if (
            disponibles.Count == 0
        )
        {
            if (
                !string.IsNullOrWhiteSpace(
                    marca)
            )
            {
                return
                    Texto("MOTO_PROMOS_MARCA_VACIAS", ("MARCA", marca));
            }

            return
                Texto("MOTO_PROMOS_VACIAS");
        }

        var sb =
            new StringBuilder();

        if (
            !string.IsNullOrWhiteSpace(
                marca)
        )
        {
            sb.AppendLine(
                Texto("MOTO_PROMOS_MARCA_TITULO", ("MARCA", marca)));
        }
        else
        {
            sb.AppendLine(
                Texto("MOTO_PROMOS_TITULO"));
        }

        sb.AppendLine();

        var grupos =
            disponibles
                .GroupBy(
                    x => x.Marca,
                    StringComparer.OrdinalIgnoreCase)
                .OrderBy(
                    x => x.Key)
                .ToList();

        foreach (
            var grupo
            in grupos)
        {
            if (
                grupos.Count > 1
            )
            {
                sb.AppendLine(
                    $" *{grupo.Key}*");
            }

            var nombres =
                grupo
                    .Select(
                        x => x.Modelo)
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase)
                    .OrderBy(
                        x => x)
                    .ToList();

            if (
                nombres.Count <= 3
            )
            {
                foreach (
                    var nombre
                    in nombres)
                {
                    sb.AppendLine(
                        $"• *{nombre}*");
                }
            }
            else
            {
                sb.AppendLine(
                    ConstruirGrillaTresColumnas(
                        nombres));
            }

            sb.AppendLine();
        }

        sb.Append(
            Texto("MOTO_PROMOS_CIERRE"));

        return sb.ToString();
    }


    // =========================================================
    // RESPUESTA GUIADA
    //
    // Si hay pocos modelos, los muestra.
    // Si mañana hay 50, muestra primero las marcas.
    // TODO sale de BBDD.
    // =========================================================

    private string ConstruirRespuestaGuiada(
        IReadOnlyList<MotoModeloCandidatoDto> modelos,
        string encabezado)
    {
        var disponibles =
            modelos
                .DistinctBy(
                    x => x.IdModeloProducto)
                .OrderBy(
                    x => x.Marca)
                .ThenBy(
                    x => x.Modelo)
                .ToList();

        if (
            disponibles.Count == 0
        )
        {
            return
                Texto("MOTO_MODELOS_VACIOS");
        }

        var marcas =
            disponibles
                .Select(
                    x => x.Marca)
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .OrderBy(
                    x => x)
                .ToList();

        /*
         * Si hay varias marcas, guiamos primero por marca.
         * Esto evita soltar una lista enorme sin contexto.
         */
        if (
            marcas.Count > 1
        )
        {
            return
                ConstruirRespuestaMarcasDisponibles(
                    disponibles,
                    encabezado);
        }

        /*
         * Si solo hay una marca, mostramos sus modelos
         * directamente en 3 columnas.
         */
        var unicaMarca =
            marcas[0];

        return
            ConstruirRespuestaModelosMarca(
                disponibles,
                unicaMarca);
    }


    // =========================================================
    // RESPUESTAS COMERCIALES DIRECTAS SIN OLLAMA
    //
    // Los casos normales de venta no necesitan IA generativa.
    // Esto hace la respuesta mucho más rápida y evita colas.
    // =========================================================

    private static bool DebeResponderOfertaSinIA(
        string mensaje)
    {
        var texto =
            NormalizarTexto(
                mensaje);

        if (
            string.IsNullOrWhiteSpace(
                texto)
        )
        {
            return true;
        }

        var consultaTecnica =
            texto.Contains("VELOCIDAD")
            ||
            texto.Contains("MOTOR")
            ||
            texto.Contains("CONSUMO")
            ||
            texto.Contains("FRENO")
            ||
            texto.Contains("POTENCIA")
            ||
            texto.Contains("PESO")
            ||
            texto.Contains("ALTURA")
            ||
            texto.Contains("COLOR")
            ||
            texto.Contains("COLORES")
            ||
            texto.Contains("CARACTERISTICA")
            ||
            texto.Contains("FICHA TECNICA")
            ||
            texto.Contains("TANQUE");

        if (consultaTecnica)
        {
            return false;
        }

        return
            EsConsultaPrecioOCuotas(
                mensaje)
            ||
            EsConsultaDisponibilidad(
                mensaje)
            ||
            EsInteresModeloSimple(
                mensaje);
    }


    private static bool EsConsultaDisponibilidad(
        string mensaje)
    {
        var texto =
            NormalizarTexto(
                mensaje);

        return
            texto.Contains("DISPONIBLE")
            ||
            texto.Contains("DISPONIBILIDAD")
            ||
            texto.Contains("TENES LA")
            ||
            texto.Contains("TIENES LA")
            ||
            texto.Contains("HAY LA")
            ||
            texto.Contains("LA TENES")
            ||
            texto.Contains("LA TIENES");
    }


    private static bool EsInteresModeloSimple(
        string mensaje)
    {
        var texto =
            NormalizarTexto(
                mensaje);

        if (
            string.IsNullOrWhiteSpace(
                texto)
        )
        {
            return false;
        }

        if (
            texto.Contains("ME INTERESA")
            ||
            texto.Contains("ESTOY INTERESADO")
            ||
            texto.Contains("ESTOY INTERESADA")
            ||
            texto.Contains("QUIERO VER")
            ||
            texto.Contains("QUIERO SABER")
            ||
            texto.Contains("MOSTRAME")
            ||
            texto.Contains("MOSTRAR")
            ||
            texto.Contains("CONTAME")
            ||
            texto.Contains("INFORMACION")
            ||
            texto.Contains("INFO")
        )
        {
            return true;
        }

        /*
         * Si el resolver ya encontró UN modelo y el mensaje
         * es muy corto ("Shark", "Viva 110", "la Nova"),
         * respondemos directamente sin cargar Ollama.
         */
        var cantidadTokens =
            ObtenerTokens(
                texto)
                .Count;

        return
            cantidadTokens <= 5;
    }


    private static bool EsConsultaContadoEspecifica(
        string mensaje)
    {
        var texto =
            NormalizarTexto(
                mensaje);

        return
            texto.Contains("CONTADO")
            ||
            texto.Contains("EFECTIVO");
    }


    private static bool EsConsultaCreditoEspecifica(
        string mensaje)
    {
        var texto =
            NormalizarTexto(
                mensaje);

        return
            texto.Contains("CREDITO")
            ||
            texto.Contains("CUOTA")
            ||
            texto.Contains("CUOTAS")
            ||
            texto.Contains("FINANCI")
            ||
            texto.Contains("PROMO");
    }


    private string ConstruirRespuestaDirectaModelo(MotoOfertaDto oferta, string mensaje)
    {
        // Las condiciones comerciales y disponibilidad NO son editables;
        // solo la redacción de cada escenario proviene de SQL.
        var nombre = $"{oferta.Modelo.Marca} {oferta.Modelo.Nombre}";
        var tieneContado = !string.IsNullOrWhiteSpace(oferta.Contado.PrecioFinalFormateado);
        var planes = oferta.Credito.Planes ?? new List<MotoPlanOfertaDto>();
        var tieneCredito = planes.Count > 0;
        var quiereContado = EsConsultaContadoEspecifica(mensaje);
        var quiereCredito = EsConsultaCreditoEspecifica(mensaje);
        var preguntaPromo = EsConsultaPromociones(mensaje);
        var precio = oferta.Contado.PrecioFinalFormateado ?? string.Empty;
        var textoPlanes = tieneCredito ? ConstruirTextoPlanesCredito(planes) : string.Empty;
        string R(string codigo, params (string Campo, string Valor)[] extra) => Texto(codigo,
            new[] { ("MODELO", nombre), ("PRECIO", precio), ("PLANES", textoPlanes) }
            .Concat(extra).ToArray());

        if (quiereContado && !quiereCredito)
            return tieneContado ? R("MOTO_CONTADO_CON_PRECIO") : R("MOTO_CONTADO_SIN_PRECIO");

        if (quiereCredito && !quiereContado)
        {
            if (preguntaPromo)
            {
                if (oferta.Credito.TienePromo && tieneCredito) return R("MOTO_PROMO_CREDITO_VIGENTE");
                if (tieneCredito) return R("MOTO_PROMO_NO_VIGENTE_HAY_CREDITO");
                return R("MOTO_PROMO_NO_VIGENTE_SIN_CREDITO");
            }
            if (tieneCredito)
                return oferta.Credito.TienePromo ? R("MOTO_CREDITO_CON_PROMO") : R("MOTO_CREDITO_SIN_PROMO");
            return R("MOTO_CREDITO_NO_CARGADO");
        }

        if (tieneContado && tieneCredito)
            return oferta.Credito.TienePromo ? R("MOTO_CONTADO_Y_CREDITO_PROMO") : R("MOTO_CONTADO_Y_CREDITO");
        if (tieneContado) return R("MOTO_SOLO_CONTADO");
        if (tieneCredito) return R("MOTO_SOLO_CREDITO");
        return R("MOTO_OFERTA_SIN_DATOS");
    }


    private static string ConstruirTextoPlanesCredito(
        IReadOnlyList<MotoPlanOfertaDto> planes)
    {
        var resumenes =
            planes
                .Take(3)
                .Select(
                    plan =>
                        !string.IsNullOrWhiteSpace(
                            plan.Resumen)
                            ? plan.Resumen.Trim()
                            : $"{plan.CantidadCuotas} cuotas de {plan.ImporteCuotaFormateado}")
                .Where(
                    x =>
                        !string.IsNullOrWhiteSpace(
                            x))
                .ToList();

        if (
            resumenes.Count == 0
        )
        {
            return
                "una opción de financiación disponible";
        }

        return
            UnirNaturalmente(
                resumenes);
    }


    // =========================================================
    // FALLBACK SEGURO SI QWEN DEVUELVE VACIO
    // =========================================================

    private string ConstruirRespuestaComercialSegura(
        MotoOfertaDto oferta)
    {
        /*
         * Si Ollama está ocupado, tarda o falla,
         * seguimos conversando con información real de BBDD.
         * Nunca devolvemos al cliente un error técnico.
         */
        return
            ConstruirRespuestaDirectaModelo(
                oferta,
                string.Empty);
    }


    // =========================================================
    // CONSTRUIR RESPUESTA DESDE BBDD
    // =========================================================

    private string ConstruirRespuestaOtrosModelos(
        IReadOnlyList<MotoModeloCandidatoDto> modelos,
        MotoModeloCandidatoDto? modeloActual)
    {
        var disponibles =
            modelos
                .DistinctBy(
                    x => x.IdModeloProducto)
                .OrderBy(
                    x => x.Marca)
                .ThenBy(
                    x => x.Modelo)
                .ToList();

        if (
            disponibles.Count == 0
        )
        {
            return
                Texto("MOTO_MODELOS_VACIOS");
        }

        var sb =
            new StringBuilder();

        sb.AppendLine(
            Texto("MOTO_MODELOS_TITULO"));

        sb.AppendLine();

        var gruposPorMarca =
            disponibles
                .GroupBy(
                    x => x.Marca,
                    StringComparer.OrdinalIgnoreCase)
                .OrderBy(
                    g => g.Key);

        foreach (
            var grupo
            in gruposPorMarca)
        {
            var nombres =
                grupo
                    .OrderBy(
                        x => x.Modelo)
                    .Select(
                        x => x.Modelo)
                    .ToList();

            sb.AppendLine(
                $" *{grupo.Key}*");

            sb.AppendLine(
                ConstruirGrillaTresColumnas(
                    nombres));

            sb.AppendLine();
            sb.AppendLine();
        }

        sb.Append(
            Texto("MOTO_MODELOS_CIERRE"));

        return sb.ToString();
    }


    // =========================================================
    // UNIR MODELOS DE FORMA NATURAL
    // =========================================================

    private static string UnirNaturalmente(
        IReadOnlyList<string> valores)
    {
        if (
            valores.Count == 0
        )
        {
            return string.Empty;
        }


        if (
            valores.Count == 1
        )
        {
            return valores[0];
        }


        if (
            valores.Count == 2
        )
        {
            return
                $"{valores[0]} y {valores[1]}";
        }


        return
            $"{string.Join(", ", valores.Take(valores.Count - 1))} y {valores.Last()}";
    }

    // =========================================================
    // INICIO DE COMPRA / CREDITO
    // =========================================================

    private static string? DetectarInicioOperacion(
        string mensaje)
    {
        var texto =
            NormalizarTexto(
                mensaje);

        if (
            string.IsNullOrWhiteSpace(
                texto)
        )
        {
            return null;
        }

        if (
            texto.Contains("NO QUIERO CREDITO")
            || texto.Contains("NO QUIERO A CREDITO")
            || texto.Contains("NO A CREDITO")
            || texto.Contains("NO QUIERO FINANCIAR")
        )
        {
            return null;
        }

        if (
            texto == "CREDITO"
            || texto == "A CREDITO"
            || texto == "SI CREDITO"
            || texto == "SI A CREDITO"
            || texto.StartsWith("SI A CREDITO ")
            || texto.StartsWith("SI CREDITO ")
            || texto.Contains("CREDITO QUE NECESITO")
            || texto.Contains("QUE NECESITO PARA CREDITO")
            || texto.Contains("QUE NECESITO PARA EL CREDITO")
            || texto.Contains("REQUISITOS PARA CREDITO")
            || texto.Contains("REQUISITOS DEL CREDITO")
            || texto.Contains("DOCUMENTOS PARA CREDITO")
            || texto.Contains("PAPELES PARA CREDITO")
            || (texto.Contains("DONDE PASO") && texto.Contains("CREDITO"))
            || (texto.Contains("DONDE ENVIO") && texto.Contains("CREDITO"))
            || texto.Contains("ME GUSTARIA A CREDITO")
            || texto.Contains("ME GUSTARIA COMPRAR A CREDITO")
            || texto.Contains("QUIERO A CREDITO")
            || texto.Contains("PREFIERO A CREDITO")
            || texto.Contains("LO QUIERO A CREDITO")
            || texto.Contains("LA QUIERO A CREDITO")
            || texto.Contains("QUIERO FINANCIAR")
            || texto.Contains("QUIERO FINANCIADA")
            || texto.Contains("QUIERO FINANCIADO")
        )
        {
            return "CREDITO";
        }

        if (
            texto == "CONTADO"
            || texto == "AL CONTADO"
            || texto.Contains("ME GUSTARIA AL CONTADO")
            || texto.Contains("PREFIERO AL CONTADO")
            || texto.Contains("LO QUIERO AL CONTADO")
            || texto.Contains("LA QUIERO AL CONTADO")
        )
        {
            return "CONTADO";
        }

        var intencionCompra =
            texto.Contains("QUIERO")
            || texto.Contains("ME GUSTARIA")
            || texto.Contains("PREFIERO")
            || texto.Contains("COMPRAR")
            || texto.Contains("SACAR")
            || texto.Contains("SOLICITAR")
            || texto.Contains("INICIAR")
            || texto.Contains("TRAMITAR")
            || texto.Contains("ADQUIRIR");

        if (
            intencionCompra
            &&
            (
                texto.Contains("CREDITO")
                || texto.Contains("FINANCI")
            )
        )
        {
            return "CREDITO";
        }

        if (
            intencionCompra
            && texto.Contains("CONTADO")
        )
        {
            return "CONTADO";
        }

        return null;
    }


    private static string MensajeParaHistorial(
        MotoConversacionRequest request)
    {
        if (
            !string.IsNullOrWhiteSpace(
                request.Mensaje)
        )
        {
            return request.Mensaje;
        }

        var tipo =
            (request.TipoMensaje ?? "TEXTO")
                .Trim()
                .ToUpperInvariant();

        return tipo switch
        {
            "IMAGEN" => "[IMAGEN RECIBIDA]",
            "DOCUMENTO" => "[DOCUMENTO RECIBIDO]",
            "AUDIO" => "[AUDIO RECIBIDO]",
            _ => "[MENSAJE RECIBIDO]"
        };
    }


    // =========================================================
    // RETOMAR IA DESDE MODO HUMANO
    // =========================================================

    private static bool SolicitaRetomarIA(
        string mensaje)
    {
        var texto =
            NormalizarTexto(
                mensaje);

        if (
            string.IsNullOrWhiteSpace(
                texto)
        )
        {
            return false;
        }

        var frases =
            new[]
            {
                "PANAMBI",
                "VOLVER CON PANAMBI",
                "VOLVE CON PANAMBI",
                "QUIERO HABLAR CON PANAMBI",
                "QUE ME ATIENDA PANAMBI",
                "RETOMAR CON PANAMBI",
                "SEGUI CON PANAMBI",
                "SEGUIR CON PANAMBI",
                "VOLVER A IA",
                "RETOMAR IA"
            };

        return frases.Any(
            frase =>
                ContieneFrase(
                    texto,
                    frase));
    }


    // =========================================================
    // IDENTIFICAR MENSAJE DE DERIVACION HUMANA
    // =========================================================

    private static bool EsMensajeDerivacionHumana(
        string? mensaje)
    {
        var texto =
            NormalizarTexto(
                mensaje);

        if (
            string.IsNullOrWhiteSpace(
                texto)
        )
        {
            return false;
        }

        if (
            EsMensajeDerivacionOtroProducto(
                mensaje)
        )
        {
            return true;
        }

        return
            texto.Contains(
                "ASESOR",
                StringComparison.OrdinalIgnoreCase)
            &&
            (
                texto.Contains(
                    "PASO",
                    StringComparison.OrdinalIgnoreCase)
                ||
                texto.Contains(
                    "ATENDER",
                    StringComparison.OrdinalIgnoreCase)
                ||
                texto.Contains(
                    "PERSONALMENTE",
                    StringComparison.OrdinalIgnoreCase)
            );
    }


    private static bool SolicitaAtencionHumana(
    string mensaje)
    {
        var texto =
            NormalizarTexto(
                mensaje);


        if (
            string.IsNullOrWhiteSpace(
                texto)
        )
        {
            return false;
        }


        var frases =
            new[]
            {
            "QUIERO HABLAR CON UNA PERSONA",
            "QUIERO HABLAR CON UN ASESOR",
            "QUIERO HABLAR CON UN VENDEDOR",

            "PASAME CON UN ASESOR",
            "PASAME CON UNA PERSONA",
            "PASAME CON UN VENDEDOR",

            "NECESITO UN ASESOR",
            "NECESITO HABLAR CON ALGUIEN",

            "ATENCION HUMANA",
            "ASESOR HUMANO"
            };


        return frases.Any(
            frase =>
                ContieneFrase(
                    texto,
                    frase));
    }
}
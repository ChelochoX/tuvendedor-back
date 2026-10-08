using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using tuvendedorback.DTOs;
using tuvendedorback.Exceptions;
using tuvendedorback.Repositories.Interfaces;
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


    private readonly IIAConversacionRepository
        _repository;

    private readonly IMotoOfertaService
        _motoOfertaService;

    private readonly IOllamaService
        _ollamaService;

    private readonly ISolicitudMotoService
        _solicitudMotoService;

    private readonly IClientesService
        _clientesService;

    private readonly ILogger<MotoConversacionService>
        _logger;


    public MotoConversacionService(
        IIAConversacionRepository repository,
        IMotoOfertaService motoOfertaService,
        IOllamaService ollamaService,
        ISolicitudMotoService solicitudMotoService,
        IClientesService clientesService,
        ILogger<MotoConversacionService> logger)
    {
        _repository =
            repository;

        _motoOfertaService =
            motoOfertaService;

        _ollamaService =
            ollamaService;

        _solicitudMotoService =
            solicitudMotoService;

        _clientesService =
            clientesService;

        _logger =
            logger;
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

        var idConversacion =
            await _repository
                .ObtenerOCrearConversacion(
                    identificador);

        // Desde el primer mensaje ya conservamos al contacto como
        // oportunidad comercial. Si luego consulta un modelo concreto,
        // actualizamos esta misma ficha en vez de crear duplicados.
        await RegistrarInteresadoSeguro(
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
                    request.IdPublicacion,

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

        // =====================================================
        // 2. CONTROL DE MODO HUMANO
        //
        // - Si el cliente pide volver con Panambi: vuelve ya.
        // - Si pasaron 15 minutos desde la derivacion: vuelve sola.
        // - Si todavia esta dentro de la ventana humana: Panambi calla.
        //
        // IMPORTANTE:
        // al volver a IA NO limpiamos el contexto del producto.
        // Asi Panambi puede retomar la conversacion donde quedo.
        // =====================================================

        var modoConversacion =
            await _repository
                .ObtenerModoConversacion(
                    idConversacion);

        if (
            string.Equals(
                modoConversacion,
                "HUMANO",
                StringComparison.OrdinalIgnoreCase)
        )
        {
            var volverAhora =
                SolicitaRetomarIA(
                    request.Mensaje);

            var historialHumano =
                await _repository
                    .ObtenerUltimosMensajes(
                        idConversacion,
                        20);

            var ultimaDerivacionHumana =
                historialHumano
                    .Where(
                        x =>
                            string.Equals(
                                x.Emisor,
                                "IA",
                                StringComparison.OrdinalIgnoreCase)
                            &&
                            EsMensajeDerivacionHumana(
                                x.Mensaje))
                    .OrderByDescending(
                        x => x.Fecha)
                    .FirstOrDefault();

            var tiempoHumanoVencido =
                ultimaDerivacionHumana is not null
                &&
                DateTime.Now
                    >=
                ultimaDerivacionHumana.Fecha
                    .AddMinutes(
                        MINUTOS_MODO_HUMANO);

            if (
                volverAhora
                ||
                tiempoHumanoVencido
            )
            {
                await _repository
                    .CambiarModoConversacion(
                        idConversacion,
                        "IA");

                _logger.LogInformation(
                    "Panambi retoma conversacion. IdConversacion={IdConversacion}, Motivo={Motivo}",
                    idConversacion,
                    volverAhora
                        ? "CLIENTE_SOLICITO_RETORNO"
                        : "VENCIO_TIEMPO_HUMANO");

                /*
                 * NO hacemos return.
                 *
                 * Seguimos procesando ESTE MISMO mensaje
                 * para que el cliente reciba respuesta ahora,
                 * sin tener que escribir otra vez.
                 */
            }
            else
            {
                await RegistrarInteresadoSeguro(
                    new InteresadoWhatsAppEventoRequest
                    {
                        IdConversacion =
                            idConversacion,

                        EstadoConsulta =
                            "DERIVADO_HUMANO",

                        MotivoSeguimiento =
                            "Conversación actualmente atendida en modo humano.",

                        EsEntradaCliente =
                            false
                    });

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
        }

        // =====================================================
        // 3. REGISTRAR MENSAJE DEL CLIENTE
        // =====================================================

        await _repository
            .RegistrarMensaje(
                idConversacion,
                "CLIENTE",
                MensajeParaHistorial(request));

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

            const string respuestaHumano =
                "Claro  te paso con uno de nuestros asesores para que pueda atenderte personalmente.";

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
                        EsCreditoOperacion(
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
                        "Solicitud en proceso. Revisar avance si el cliente deja de responder.",

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
        // 6. PUBLICACION EXPLICITA
        // =====================================================

        int? idPublicacion =
            request.IdPublicacion;

        if (!idPublicacion.HasValue)
        {
            idPublicacion =
                ExtraerIdPublicacion(
                    request.Mensaje);
        }

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
        // 6. CATALOGO REAL DESDE BBDD
        // =====================================================

        var modelos =
            await _repository
                .ObtenerModelosMotoActivos();

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
        )
        {
            idModeloContextoParaPromo =
                await _repository
                    .ObtenerIdModeloActual(
                        idConversacion);
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

        // =====================================================
        // 7B. SALUDO / CONSULTA GENERAL SIN MODELO
        //
        // Un saludo o una pregunta general NO debe reutilizar
        // el modelo viejo que quedó en contexto.
        //
        // Ejemplos:
        // "hola"
        // "que tienen?"
        // "quiero ver motos"
        // =====================================================

        if (
            coincidencias.Count == 0
            &&
            EsSaludoSimple(
                request.Mensaje)
        )
        {
            await _repository
                .LimpiarProductoContexto(
                    idConversacion);

            var respuestaSaludo =
                ConstruirRespuestaMarcasDisponibles(
                    modelos,
                    "¡Hola! ¿Qué tal?  Soy Panambí, asistente de TuVendedor. Con gusto te ayudo a encontrar tu moto.");

            await _repository
                .RegistrarMensaje(
                    idConversacion,
                    "IA",
                    respuestaSaludo);

            return new MotoConversacionResponseDto
            {
                IdConversacion = idConversacion,
                IdPublicacion = null,
                Marca = null,
                Modelo = null,
                Respuesta = respuestaSaludo,
                RequierePublicacion = false
            };
        }

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
                    "¡Claro!  Contame qué estás buscando y te ayudo. Podemos empezar por la marca:");

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

            if (modeloSolicitud is null)
            {
                var idModeloContexto =
                    await _repository
                        .ObtenerIdModeloActual(
                            idConversacion);

                if (idModeloContexto.HasValue)
                {
                    modeloSolicitud =
                        modelos.FirstOrDefault(
                            x =>
                                x.IdModeloProducto
                                ==
                                idModeloContexto.Value);
                }
            }

            if (modeloSolicitud is null)
            {
                var respuestaElegirModelo =
                    ConstruirRespuestaGuiada(
                        modelos,
                        $"Claro  Para iniciar la compra {tipoOperacionSolicitada.ToLowerInvariant()}, primero decime qué modelo querés:");

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
                    "Claro  Decime cuál de estos modelos te interesa:");

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
        // 9A. PARECE QUE EL CLIENTE DIJO UN MODELO,
        //     PERO NO PUDIMOS IDENTIFICARLO
        //
        // No reutilizamos silenciosamente el modelo anterior.
        // Preguntamos de nuevo de forma natural.
        // =====================================================

        if (
            coincidencias.Count == 0
            &&
            PareceMencionModeloNoResuelta(
                request.Mensaje)
        )
        {
            await _repository
                .LimpiarProductoContexto(
                    idConversacion);

            var respuestaNoReconocida =
                ConstruirRespuestaMarcasDisponibles(
                    modelos,
                    "Quiero asegurarme de entenderte bien . No alcancé a identificar ese modelo. Podemos ubicarlo por la marca:");

            await _repository
                .RegistrarMensaje(
                    idConversacion,
                    "IA",
                    respuestaNoReconocida);

            return new MotoConversacionResponseDto
            {
                IdConversacion = idConversacion,
                IdPublicacion = null,
                Marca = null,
                Modelo = null,
                Respuesta = respuestaNoReconocida,
                RequierePublicacion = false
            };
        }

        // =====================================================
        // 9B. MENSAJE CLARAMENTE INCOMPRENSIBLE
        //
        // No repetimos automaticamente la ultima moto del contexto
        // frente a texto aleatorio o incomprensible. Guiamos al
        // cliente de forma amable para que la conversacion siga.
        // =====================================================

        if (
            coincidencias.Count == 0
            &&
            EsMensajeClaramenteIncomprensible(
                request.Mensaje)
        )
        {
            var respuestaIncomprensible =
                ConstruirRespuestaMarcasDisponibles(
                    modelos,
                    "No alcancé a entenderte bien . No hay problema: decime el nombre de la moto, alguna parte del modelo o podemos empezar por la marca:");

            await _repository
                .RegistrarMensaje(
                    idConversacion,
                    "IA",
                    respuestaIncomprensible);

            return new MotoConversacionResponseDto
            {
                IdConversacion = idConversacion,
                IdPublicacion = null,
                Marca = null,
                Modelo = null,
                Respuesta = respuestaIncomprensible,
                RequierePublicacion = false
            };
        }

        // =====================================================
        // 10. RECUPERAR MODELO DEL CONTEXTO
        //
        // Si ya eligio una VIVA 110, frases como:
        // "y al contado?"
        // "que precio tiene?"
        // "cuanto es la cuota?"
        // siguen trabajando sobre la VIVA 110.
        // =====================================================

        var idModeloActual =
            await _repository
                .ObtenerIdModeloActual(
                    idConversacion);

        if (idModeloActual.HasValue)
        {
            var modeloActual =
                modelos.FirstOrDefault(
                    x =>
                        x.IdModeloProducto
                        ==
                        idModeloActual.Value);

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
        // 11. COMPATIBILIDAD CON CONTEXTO VIEJO
        // =====================================================

        var idPublicacionContexto =
            await _repository
                .ObtenerIdPublicacionContexto(
                    idConversacion);

        if (idPublicacionContexto.HasValue)
        {
            return await ProcesarPorPublicacion(
                idConversacion,
                idPublicacionContexto.Value,
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
                    "Claro  ¿De cuál modelo querés que te pase el precio o las cuotas?");

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

        // =====================================================
        // 13. SALUDO SIMPLE
        // =====================================================

        if (
            EsSaludoSimple(
                request.Mensaje)
        )
        {
            const string respuestaSaludo =
                "¡Hola! ¿Qué tal?  Soy Panambí, asistente de TuVendedor. ¿Qué modelo de moto tenés en mente?";

            await _repository
                .RegistrarMensaje(
                    idConversacion,
                    "IA",
                    respuestaSaludo);

            return new MotoConversacionResponseDto
            {
                IdConversacion = idConversacion,
                IdPublicacion = null,
                Marca = null,
                Modelo = null,
                Respuesta = respuestaSaludo,
                RequierePublicacion = false
            };
        }

        // =====================================================
        // 14. NO ENTENDIMOS DEL TODO
        //
        // IMPORTANTE:
        // NO PASAMOS A HUMANO.
        // NO INVENTAMOS.
        // GUIAMOS AL CLIENTE CON EL CATALOGO REAL.
        // =====================================================

        var respuestaOrientacion =
            ConstruirRespuestaGuiada(
                modelos,
                "Para ayudarte bien  decime cuál de estas opciones te interesa:");

        await _repository
            .RegistrarMensaje(
                idConversacion,
                "IA",
                respuestaOrientacion);

        return new MotoConversacionResponseDto
        {
            IdConversacion = idConversacion,
            IdPublicacion = null,
            Marca = null,
            Modelo = null,
            Respuesta = respuestaOrientacion,
            RequierePublicacion = false
        };
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
                    $"Encontré la {modelo.Marca} {modelo.Modelo}, pero necesito confirmar el precio comercial antes de darte un dato incorrecto. Te paso con uno de nuestros asesores para que pueda ayudarte personalmente.");

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

        var promptBase =
            await _repository
                .ObtenerPromptActivo(
                    CODIGO_PROMPT_BASE);


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

        var promptMarca =
            await _repository
                .ObtenerPromptActivo(
                    codigoPromptMarca);


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
                oferta);


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
                return
                    "Hola, soy Panambí, asistente de TuVendedor. Con gusto te ayudo.\n\n"
                    + limpia;
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

    private async Task RegistrarInteresadoSeguro(
        InteresadoWhatsAppEventoRequest evento)
    {
        try
        {
            await _clientesService
                .RegistrarInteraccionWhatsApp(
                    evento);
        }
        catch (Exception ex)
        {
            /*
             * El CRM nunca debe cortar una conversación de venta.
             * Si el registro comercial falla, Panambí continúa y dejamos
             * trazabilidad en log para corregir el problema.
             */
            _logger.LogWarning(
                ex,
                "No se pudo actualizar interesado desde WhatsApp. IdConversacion={IdConversacion}",
                evento.IdConversacion);
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

    private static string ConstruirPreguntaAmbigua(
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
                "Claro  ¿Qué modelo de moto te interesa?";
        }

        if (
            opciones.Count == 1
        )
        {
            return
                $"Creo que te referís a la *{opciones[0].Marca} {opciones[0].Modelo}* . ¿Es esa?";
        }

        var sb =
            new StringBuilder();

        sb.AppendLine(
            "¡Claro!  Encontré varias opciones que coinciden con lo que me dijiste:");

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
            "¿Cuál de estas te interesa? Podés escribirme el nombre del modelo y seguimos desde ahí ");

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


        var match =
            Regex.Match(
                mensaje,
                @"(?:share/producto/|producto/|TV-)(\d+)",
                RegexOptions.IgnoreCase);


        if (
            !match.Success
            ||
            match.Groups.Count < 2
        )
        {
            return null;
        }


        return int.TryParse(
            match.Groups[1].Value,
            out var id)

                ? id
                : null;
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

    private static string ConstruirPromptSistema(
     string promptBase,
     string promptMarca,
     MotoOfertaDto oferta)
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
                    "Este mes existe una promoción en cuotas.");
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
                "No hay un plan de financiación activo cargado para este modelo.");
        }


        // =========================================================
        // REGLAS DE RESPUESTA
        // =========================================================

        sb.AppendLine();
        sb.AppendLine(
            "========================================");

        sb.AppendLine(
            "INSTRUCCIONES PARA ESTA RESPUESTA");

        sb.AppendLine(
            "========================================");


        sb.AppendLine(
            "- El modelo ya fue identificado. No preguntes nuevamente qué modelo desea.");


        sb.AppendLine(
            "- Nunca menciones porcentajes de descuento.");


        sb.AppendLine(
            "- Nunca expliques cómo se calculó el precio contado.");


        sb.AppendLine(
            "- Nunca menciones fechas de promociones.");


        sb.AppendLine(
            "- Nunca menciones precio de lista, precio base o precio distribuidor.");


        sb.AppendLine(
            "- Nunca inventes características o cualidades de la motocicleta.");

        sb.AppendLine(
            "- En Paraguay, para identificar al cliente, usá siempre Cédula de Identidad paraguaya o CI.");

        sb.AppendLine(
            "- Nunca uses la palabra DNI. Para este flujo el documento es únicamente Cédula de Identidad paraguaya (CI).");

        sb.AppendLine(
            "- Nunca inventes requisitos ni documentación de crédito. Si el cliente quiere iniciar un crédito, el backend controla el flujo paso a paso.");

        sb.AppendLine(
            "- Para procesos de validación, hablá solamente de verificación de datos o evaluación de crédito.");


        sb.AppendLine(
            "- Si el cliente pregunta por contado, respondé únicamente el precio final contado.");


        sb.AppendLine(
            "- Si pregunta por crédito y no hay un plan activo cargado, decilo claramente y no inventes cuotas.");


        sb.AppendLine(
            "- Si no hay financiación cargada, igualmente podés informar el precio final contado disponible.");


        sb.AppendLine(
            "- Si corresponde mencionar promoción, decí solamente que este mes tenemos una promo en cuotas.");


        sb.AppendLine(
              "- Finalizá con una pregunta natural relacionada con la compra.");

        sb.AppendLine(
            "- Si presentaste contado y crédito, preguntá preferentemente: ¿Cómo te gustaría adquirirla, al contado o a crédito?");

        sb.AppendLine(
            "- No uses las palabras avanzar, gestión, operación o alternativa para cerrar la respuesta.");


        sb.AppendLine(
            "- Evitá preguntas abiertas que distraigan al cliente.");


        sb.AppendLine(
            "- Respondé breve, natural y comercial como vendedor por WhatsApp.");


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

    private static bool EsSaludoSimple(
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
         * No exigimos coincidencia exacta.
         * La gente escribe de muchas formas en WhatsApp:
         *
         * hola
         * holaa
         * holla
         * ola
         * hola otra vez
         * buenas
         * buen dia
         * que tal
         * como estas
         */
        var iniciosSaludo =
            new[]
            {
                "HOLA",
                "HOLAA",
                "HOLAAA",
                "HOLI",
                "HOLIS",
                "HOLLA",
                "OLA",
                "BUEN DIA",
                "BUENOS DIAS",
                "BUENAS",
                "BUENAS TARDES",
                "BUENAS NOCHES",
                "QUE TAL",
                "COMO ESTAS",
                "COMO ESTA",
                "COMO ANDAS",
                "COMO VA",
                "HEY",
                "EY",
                "SALUDOS"
            };

        return iniciosSaludo.Any(
            saludo =>
                string.Equals(
                    texto,
                    saludo,
                    StringComparison.OrdinalIgnoreCase)
                ||
                texto.StartsWith(
                    saludo + " ",
                    StringComparison.OrdinalIgnoreCase));
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

    private static string ConstruirRespuestaModelosMarca(
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
                $"Por el momento no tengo modelos de {marca} cargados .";
        }

        var nombres =
            encontrados
                .Select(
                    x => x.Modelo)
                .ToList();

        var sb =
            new StringBuilder();

        sb.AppendLine(
            $"¡Claro!  Estos son los modelos *{marca}* que tenemos:");

        sb.AppendLine();

        sb.AppendLine(
            ConstruirGrillaTresColumnas(
                nombres));

        sb.AppendLine();
        sb.AppendLine();

        sb.Append(
            "¿Cuál te interesa? Podés escribirme solamente el nombre del modelo ");

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

    private static string ConstruirRespuestaMarcasDisponibles(
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
                "Con gusto te ayudo . En este momento no tengo marcas cargadas para mostrarte.";
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
                $" Por ahora tenemos *{marcas[0]}*.");

            sb.AppendLine();

            sb.Append(
                $"¿Querés que te muestre los modelos {marcas[0]} disponibles? ");

            return sb.ToString();
        }

        sb.AppendLine(
            " *Marcas disponibles:*");

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
            "¿Cuál marca te gustaría ver? ");

        return sb.ToString();
    }


    // =========================================================
    // RESPUESTA DE MODELOS EN PROMO
    // =========================================================

    private static string ConstruirRespuestaPromociones(
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
                    $"Por ahora no tengo una promo vigente cargada para *{marca}* . Si querés, te muestro los modelos disponibles de esa marca.";
            }

            return
                "Por ahora no tengo promociones vigentes cargadas . Si querés, te muestro los modelos disponibles y vemos cuál te gusta.";
        }

        var sb =
            new StringBuilder();

        if (
            !string.IsNullOrWhiteSpace(
                marca)
        )
        {
            sb.AppendLine(
                $"¡Sí!  Estos son los modelos *{marca}* que tenemos en promo este mes:");
        }
        else
        {
            sb.AppendLine(
                "¡Sí!  Estos son los modelos que tenemos en promo este mes:");
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
            "¿Cuál te interesa? Decime el modelo y te paso la promo que tiene ");

        return sb.ToString();
    }


    // =========================================================
    // RESPUESTA GUIADA
    //
    // Si hay pocos modelos, los muestra.
    // Si mañana hay 50, muestra primero las marcas.
    // TODO sale de BBDD.
    // =========================================================

    private static string ConstruirRespuestaGuiada(
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
                "En este momento no tengo modelos cargados para mostrarte .";
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


    private static string ConstruirRespuestaDirectaModelo(
        MotoOfertaDto oferta,
        string mensaje)
    {
        var nombre =
            $"{oferta.Modelo.Marca} {oferta.Modelo.Nombre}";

        var tieneContado =
            !string.IsNullOrWhiteSpace(
                oferta.Contado.PrecioFinalFormateado);

        var planes =
            oferta.Credito.Planes
            ??
            new List<MotoPlanOfertaDto>();

        var tieneCredito =
            planes.Count > 0;

        var quiereContado =
            EsConsultaContadoEspecifica(
                mensaje);

        var quiereCredito =
            EsConsultaCreditoEspecifica(
                mensaje);

        var preguntaPromo =
            EsConsultaPromociones(
                mensaje);

        if (
            quiereContado
            &&
            !quiereCredito
        )
        {
            if (tieneContado)
            {
                return
                    $"Claro. La *{nombre}* al contado te queda en *{oferta.Contado.PrecioFinalFormateado}*. ¿Querés que sigamos con la compra al contado?";
            }

            return
                $"Encontré la *{nombre}*. El precio al contado todavía necesita confirmación. Si querés, puedo ayudarte a revisar otra opción.";
        }

        if (
            quiereCredito
            &&
            !quiereContado
        )
        {
            if (preguntaPromo)
            {
                if (
                    oferta.Credito.TienePromo
                    &&
                    tieneCredito
                )
                {
                    var textoPromo =
                        ConstruirTextoPlanesCredito(
                            planes);

                    return
                        $"Sí. La *{nombre}* está en promoción este mes: {textoPromo}. ¿Querés que sigamos con esta opción a crédito?";
                }

                if (tieneCredito)
                {
                    var textoFinanciacion =
                        ConstruirTextoPlanesCredito(
                            planes);

                    return
                        $"Por ahora la *{nombre}* no tiene una promoción vigente cargada, pero sí tenemos financiación: {textoFinanciacion}. ¿Querés que revisemos esa opción?";
                }

                return
                    $"Por ahora la *{nombre}* no tiene una promoción de crédito vigente cargada. Si querés, puedo mostrarte otros modelos con financiación disponible.";
            }

            if (tieneCredito)
            {
                var textoPlanes =
                    ConstruirTextoPlanesCredito(
                        planes);

                var inicio =
                    oferta.Credito.TienePromo
                        ? $"Este mes tenemos una promoción para la *{nombre}*"
                        : $"Para la *{nombre}* tenemos financiación";

                return
                    $"{inicio}: {textoPlanes}. ¿Querés que sigamos con la opción a crédito?";
            }

            return
                $"Encontré la *{nombre}*, pero por ahora no tengo un plan de crédito vigente cargado. Si querés, puedo mostrarte otras opciones.";
        }

        if (
            tieneContado
            &&
            tieneCredito
        )
        {
            var textoPlanes =
                ConstruirTextoPlanesCredito(
                    planes);

            var credito =
                oferta.Credito.TienePromo
                    ? $"y este mes tenemos promoción a crédito: {textoPlanes}"
                    : $"y también tenemos financiación: {textoPlanes}";

            return
                $"Sí, la *{nombre}* está disponible. Al contado te queda en *{oferta.Contado.PrecioFinalFormateado}* {credito}. ¿Cómo te gustaría adquirirla, al contado o a crédito?";
        }

        if (tieneContado)
        {
            return
                $"Sí, la *{nombre}* está disponible. Al contado te queda en *{oferta.Contado.PrecioFinalFormateado}*. ¿Te interesa esta opción?";
        }

        if (tieneCredito)
        {
            var textoPlanes =
                ConstruirTextoPlanesCredito(
                    planes);

            return
                $"Sí, la *{nombre}* está disponible. A crédito contamos con {textoPlanes}. ¿Querés que sigamos con esta opción?";
        }

        return
            $"Encontré la *{nombre}*. Necesito confirmar las condiciones comerciales antes de darte un dato incorrecto. Si querés, puedo mostrarte otras opciones mientras tanto.";
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

    private static string ConstruirRespuestaComercialSegura(
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

    private static string ConstruirRespuestaOtrosModelos(
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
                "En este momento no tengo modelos cargados para mostrarte .";
        }

        var sb =
            new StringBuilder();

        sb.AppendLine(
            "¡Claro!  Estos son los modelos que tenemos:");

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
            "¿Cuál te interesa? ");

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
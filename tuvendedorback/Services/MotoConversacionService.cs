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

    private readonly ILogger<MotoConversacionService>
        _logger;


    public MotoConversacionService(
        IIAConversacionRepository repository,
        IMotoOfertaService motoOfertaService,
        IOllamaService ollamaService,
        ISolicitudMotoService solicitudMotoService,
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
                "Claro 😊 te paso con uno de nuestros asesores para que pueda atenderte personalmente.";

            await _repository
                .RegistrarMensaje(
                    idConversacion,
                    "IA",
                    respuestaHumano);

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
                        $"Claro 😊 Para iniciar la compra {tipoOperacionSolicitada.ToLowerInvariant()}, primero decime qué modelo querés:");

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
        // 8. SALUDO / CONSULTA GENERAL / CATALOGO
        //
        // IMPORTANTE:
        // Estas intenciones se resuelven ANTES de recuperar el
        // modelo guardado en contexto. De esa forma un "hola",
        // "que tienen?" o "mostrame las motos" no vuelve a disparar
        // una consulta sobre el modelo anterior.
        //
        // Siempre arrancamos mostrando MARCAS.
        // =====================================================

        if (
            EsSaludoSimple(
                request.Mensaje)
            ||
            EsConsultaGeneralVenta(
                request.Mensaje)
            ||
            EsConsultaOtrosModelos(
                request.Mensaje)
        )
        {
            await _repository
                .LimpiarProductoContexto(
                    idConversacion);

            var encabezado =
                EsSaludoSimple(
                    request.Mensaje)

                    ? "¡Hola! ¿Qué tal? 😊 Soy Panambí, asistente de TuVendedor. Con gusto te ayudo a encontrar tu moto."

                    : "¡Claro! 😊 Te ayudo a encontrar la moto que estás buscando.";

            var respuestaMarcas =
                ConstruirRespuestaMarcas(
                    modelos,
                    encabezado);

            await _repository
                .RegistrarMensaje(
                    idConversacion,
                    "IA",
                    respuestaMarcas);

            return new MotoConversacionResponseDto
            {
                IdConversacion = idConversacion,
                IdPublicacion = null,
                Marca = null,
                Modelo = null,
                Respuesta = respuestaMarcas,
                RequierePublicacion = false
            };
        }

        // =====================================================
        // 9. DETECTAR QUE QUIERE CAMBIAR DE MODELO
        //
        // Al cambiar de producto limpiamos el contexto anterior
        // y volvemos al punto de partida: selección de MARCA.
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
                ConstruirRespuestaMarcas(
                    modelos,
                    "¡Claro! 😊 Veamos otra opción.");

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
        // 10. CORTESIA SIN CONSULTA
        //
        // Un "gracias", "perfecto", etc. no debe despertar Qwen
        // ni reutilizar accidentalmente el modelo anterior.
        // Conservamos el contexto por si el cliente continúa.
        // =====================================================

        if (
            EsMensajeCortesia(
                request.Mensaje)
        )
        {
            const string respuestaCortesia =
                "¡A vos! 😊 Cuando quieras seguimos. Si querés ver otra moto, decime y te muestro las marcas disponibles.";

            await _repository
                .RegistrarMensaje(
                    idConversacion,
                    "IA",
                    respuestaCortesia);

            return new MotoConversacionResponseDto
            {
                IdConversacion = idConversacion,
                IdPublicacion = null,
                Marca = null,
                Modelo = null,
                Respuesta = respuestaCortesia,
                RequierePublicacion = false
            };
        }

        // =====================================================
        // 11. RECUPERAR MODELO DEL CONTEXTO
        //
        // Solamente llegamos aquí si el mensaje NO fue saludo,
        // NO fue consulta general y NO pidió cambiar de modelo.
        //
        // Ejemplos válidos:
        // "y al contado?"
        // "que precio tiene?"
        // "cuanto es la cuota?"
        // "y a credito?"
        // "me interesa"
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
                    cancellationToken);
            }
        }

        // =====================================================
        // 12. COMPATIBILIDAD CON CONTEXTO VIEJO
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
                cancellationToken);
        }

        // =====================================================
        // 13. PREGUNTA COMERCIAL SIN MODELO ELEGIDO
        //
        // No inventamos un modelo. Volvemos a las marcas para
        // que el cliente elija y después mostramos sus modelos.
        // =====================================================

        if (
            EsConsultaPrecioOCuotas(
                request.Mensaje)
        )
        {
            var respuestaSeleccion =
                ConstruirRespuestaMarcas(
                    modelos,
                    "¡Claro! 😊 Te paso precios o cuotas. Primero elegí la marca que te interesa:");

            await _repository
                .RegistrarMensaje(
                    idConversacion,
                    "IA",
                    respuestaSeleccion);

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
        // 14. NO ENTENDIMOS DEL TODO
        //
        // NO usamos el producto anterior.
        // NO inventamos.
        // Volvemos amablemente a las marcas reales de la BBDD.
        // =====================================================

        await _repository
            .LimpiarProductoContexto(
                idConversacion);

        var respuestaOrientacion =
            ConstruirRespuestaMarcas(
                modelos,
                "Con gusto te ayudo 😊. Para empezar, elegí una de las marcas que tenemos:");

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
            CancellationToken cancellationToken)
    {
        var oferta =
            await _motoOfertaService
                .ObtenerOfertaPorPublicacion(
                    idPublicacion);


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
                $"Encontré la {modelo.Marca} {modelo.Modelo} 😊, pero necesito confirmar el precio comercial antes de darte un dato incorrecto. Te paso con uno de nuestros asesores para que te ayude.";

            await _repository
                .RegistrarMensaje(
                    idConversacion,
                    "IA",
                    respuestaSinPrecio);

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
                    6);


        // =====================================================
        // QWEN
        // =====================================================

        var respuestaIA =
            await _ollamaService
                .GenerarRespuesta(
                    promptSistema,
                    historial,
                    cancellationToken);

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


        // =====================================================
        // GUARDAR RESPUESTA
        // =====================================================

        await _repository
            .RegistrarMensaje(
                idConversacion,
                "IA",
                respuestaIA);


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


            var tieneTokenDistintivo =
                cantidadTokensModeloCoincidentes
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
    // PREGUNTA AMBIGUA
    // =========================================================

    private static string ConstruirPreguntaAmbigua(
        IReadOnlyList<MotoModeloCandidatoDto> modelos)
    {
        var nombres =
            modelos
                .Select(
                    x =>
                        $"{x.Marca} {x.Modelo}")
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .Take(9)
                .ToList();

        if (
            nombres.Count == 0
        )
        {
            return
                "Claro 😊 ¿Qué modelo de moto te interesa?";
        }

        if (
            nombres.Count == 1
        )
        {
            return
                $"¿Te referís a la {nombres[0]}?";
        }

        var sb =
            new StringBuilder();

        sb.AppendLine(
            "Claro 😊 encontré varias opciones que pueden coincidir:");

        sb.AppendLine();

        sb.AppendLine(
            ConstruirGrillaTresColumnas(
                nombres));

        sb.AppendLine();
        sb.AppendLine();

        sb.Append(
            "¿Cuál de estas te interesa?");

        return sb.ToString();
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

        var palabrasCatalogo =
            new[]
            {
                "MODELO",
                "MODELOS",
                "MOTO",
                "MOTOS",
                "MARCA",
                "MARCAS",
                "CATALOGO",
                "OPCION",
                "OPCIONES",
                "DISPONIBLE",
                "DISPONIBLES"
            };

        var hablaDeCatalogo =
            palabrasCatalogo.Any(
                palabra =>
                    texto.Contains(
                        palabra,
                        StringComparison.OrdinalIgnoreCase));

        if (!hablaDeCatalogo)
        {
            return false;
        }

        var expresiones =
            new[]
            {
                "QUE",
                "CUALES",
                "PASAME",
                "PASAS",
                "MOSTRAME",
                "MOSTRAR",
                "MOSTRARME",
                "TENES",
                "TIENES",
                "HAY",
                "VER",
                "CONOCER",
                "OTRO",
                "OTRA",
                "OTROS",
                "OTRAS",
                "CATALOGO",
                "OPCIONES",
                "DISPONIBLE",
                "DISPONIBLES"
            };

        return expresiones.Any(
            expresion =>
                texto.Contains(
                    expresion,
                    StringComparison.OrdinalIgnoreCase));
    }


    // =========================================================
    // CONSULTA GENERAL DE VENTA
    //
    // Casos:
    // "que tienen?"
    // "que venden?"
    // "quiero informacion"
    // "quiero ver que tienen"
    // "que ofrecen?"
    //
    // Si menciona un modelo real, ese modelo ya fue resuelto antes
    // y nunca llega a este método.
    // =========================================================

    private static bool EsConsultaGeneralVenta(
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
                "QUE TENES",
                "QUE TIENES",
                "QUE TIENEN",
                "QUE HAY",
                "QUE VENDEN",
                "QUE VENDES",
                "QUE OFRECEN",
                "QUE OFRECES",
                "QUIERO INFORMACION",
                "QUIERO INFO",
                "QUIERO VER",
                "QUIERO CONOCER",
                "MOSTRAME LO QUE TENES",
                "MOSTRAME LO QUE TIENEN",
                "QUE OPCIONES TENES",
                "QUE OPCIONES TIENEN"
            };

        return frases.Any(
            frase =>
                ContieneFrase(
                    texto,
                    frase)
                ||
                texto.StartsWith(
                    frase + " ",
                    StringComparison.OrdinalIgnoreCase)
                ||
                string.Equals(
                    texto,
                    frase,
                    StringComparison.OrdinalIgnoreCase));
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
         * No buscamos una oración exacta.
         * Detectamos cómo EMPIEZA el mensaje para tolerar:
         *
         * "hola"
         * "hola otra vez"
         * "hola que tal"
         * "holla"
         * "ola"
         * "buenas"
         * "buenas tardes"
         * "como estas"
         *
         * Si el mismo mensaje menciona un modelo real, el modelo
         * se procesa antes y esta regla no le roba prioridad.
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
                "OLAA",
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
    // CORTESIA SIN CONSULTA
    // =========================================================

    private static bool EsMensajeCortesia(
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
                "GRACIAS",
                "MUCHAS GRACIAS",
                "MIL GRACIAS",
                "OK GRACIAS",
                "DALE GRACIAS",
                "PERFECTO GRACIAS",
                "GENIAL GRACIAS",
                "MUY AMABLE"
            };

        return frases.Any(
            frase =>
                string.Equals(
                    texto,
                    frase,
                    StringComparison.OrdinalIgnoreCase)
                ||
                texto.StartsWith(
                    frase + " ",
                    StringComparison.OrdinalIgnoreCase));
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
                $"Por el momento no tengo modelos de {marca} cargados 😊.";
        }

        var nombres =
            encontrados
                .Select(
                    x => x.Modelo)
                .ToList();

        var sb =
            new StringBuilder();

        sb.AppendLine(
            $"¡Claro! 😊 Estos son los modelos *{marca}* que tenemos:");

        sb.AppendLine();

        sb.AppendLine(
            ConstruirGrillaTresColumnas(
                nombres));

        sb.AppendLine();
        sb.AppendLine();

        sb.Append(
            "¿Cuál te gustaría ver?");

        return sb.ToString();
    }


    // =========================================================
    // RESPUESTA GUIADA
    //
    // Si todavía no existe un modelo elegido, NO largamos toda
    // la lista de motos. Empezamos por MARCAS.
    // Cuando el cliente elige una marca, recién ahí mostramos
    // sus modelos en 3 columnas.
    // =========================================================

    private static string ConstruirRespuestaGuiada(
        IReadOnlyList<MotoModeloCandidatoDto> modelos,
        string encabezado)
    {
        return
            ConstruirRespuestaMarcas(
                modelos,
                encabezado);
    }


    // =========================================================
    // MOSTRAR MARCAS DISPONIBLES
    // =========================================================

    private static string ConstruirRespuestaMarcas(
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
                "En este momento no tengo marcas de motos cargadas para mostrarte 😊.";
        }

        var sb =
            new StringBuilder();

        sb.AppendLine(
            encabezado);

        sb.AppendLine();
        sb.AppendLine(
            "🏍️ *Marcas disponibles*");
        sb.AppendLine();

        foreach (
            var marca
            in marcas)
        {
            sb.AppendLine(
                $"• *{marca}*");
        }

        sb.AppendLine();

        if (
            marcas.Count == 1
        )
        {
            sb.Append(
                $"Tenemos *{marcas[0]}*. Decime *{marcas[0]}* y te muestro todos los modelos disponibles 😊.");
        }
        else
        {
            sb.Append(
                "¿Qué marca te gustaría ver? 😊");
        }

        return sb.ToString();
    }


    // =========================================================
    // GRILLA DE MODELOS EN 3 COLUMNAS PARA WHATSAPP
    //
    // Usamos bloque monoespaciado para que las columnas mantengan
    // alineación. Distribuimos verticalmente para que sea fácil
    // recorrer la lista de arriba hacia abajo.
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

        const int cantidadColumnas =
            3;

        const int anchoColumna =
            18;

        var cantidadFilas =
            (int)Math.Ceiling(
                valores.Count
                /
                (double)cantidadColumnas);

        var sb =
            new StringBuilder();

        sb.AppendLine(
            "```");

        for (
            var fila = 0;
            fila < cantidadFilas;
            fila++)
        {
            for (
                var columna = 0;
                columna < cantidadColumnas;
                columna++)
            {
                var indice =
                    fila
                    +
                    (
                        columna
                        *
                        cantidadFilas
                    );

                if (
                    indice >= valores.Count
                )
                {
                    continue;
                }

                var valor =
                    (valores[indice] ?? string.Empty)
                        .Trim();

                if (
                    valor.Length
                    >=
                    anchoColumna
                )
                {
                    valor =
                        valor[..(anchoColumna - 2)]
                        +
                        "…";
                }

                sb.Append(
                    valor.PadRight(
                        anchoColumna));
            }

            sb.AppendLine();
        }

        sb.Append(
            "```");

        return sb.ToString();
    }


    // =========================================================
    // FALLBACK SEGURO SI QWEN DEVUELVE VACIO
    // =========================================================

    private static string ConstruirRespuestaComercialSegura(
        MotoOfertaDto oferta)
    {
        var tieneContado =
            !string.IsNullOrWhiteSpace(
                oferta.Contado.PrecioFinalFormateado);

        var tieneCredito =
            oferta.Credito.Planes is not null
            &&
            oferta.Credito.Planes.Count > 0;

        if (
            tieneContado
            &&
            tieneCredito
        )
        {
            return
                $"Tengo la información de la {oferta.Modelo.Marca} {oferta.Modelo.Nombre} 😊. ¿Querés que te pase el precio al contado o las cuotas?";
        }

        if (tieneContado)
        {
            return
                $"La {oferta.Modelo.Marca} {oferta.Modelo.Nombre} está al contado por {oferta.Contado.PrecioFinalFormateado}. ¿Querés seguir con esta opción?";
        }

        if (tieneCredito)
        {
            var primerPlan =
                oferta.Credito.Planes.First();

            return
                $"Para la {oferta.Modelo.Marca} {oferta.Modelo.Nombre} tengo {primerPlan.Resumen}. ¿Querés que te cuente más sobre esta opción?";
        }

        return
            $"Encontré la {oferta.Modelo.Marca} {oferta.Modelo.Nombre}, pero todavía no tengo una oferta comercial cargada para responderte sin inventar datos. Si querés, te muestro otros modelos.";
    }


    // =========================================================
    // CONSTRUIR RESPUESTA DESDE BBDD
    // =========================================================

    private static string ConstruirRespuestaOtrosModelos(
        IReadOnlyList<MotoModeloCandidatoDto> modelos,
        MotoModeloCandidatoDto? modeloActual)
    {
        /*
         * Una consulta general vuelve al inicio del catálogo:
         * primero marcas, después modelos.
         *
         * modeloActual queda en la firma para mantener
         * compatibilidad con llamadas existentes.
         */
        return
            ConstruirRespuestaMarcas(
                modelos,
                "¡Claro! 😊 Estas son las marcas que tenemos disponibles:");
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
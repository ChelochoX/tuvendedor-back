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


    private readonly IIAConversacionRepository
        _repository;

    private readonly IMotoOfertaService
        _motoOfertaService;

    private readonly IOllamaService
        _ollamaService;

    private readonly ILogger<MotoConversacionService>
        _logger;


    public MotoConversacionService(
        IIAConversacionRepository repository,
        IMotoOfertaService motoOfertaService,
        IOllamaService ollamaService,
        ILogger<MotoConversacionService> logger)
    {
        _repository =
            repository;

        _motoOfertaService =
            motoOfertaService;

        _ollamaService =
            ollamaService;

        _logger =
            logger;
    }


    public async Task<MotoConversacionResponseDto>
        ProcesarMensaje(
            MotoConversacionRequest request,
            CancellationToken cancellationToken = default)
    {
        /*
         * ====================================================
         * 1. IDENTIFICAR CONVERSACION
         * ====================================================
         */

        var identificador =
            NormalizarIdentificador(
                request.Telefono);


        if (
            string.IsNullOrWhiteSpace(
                identificador)
        )
        {
            throw new ReglasdeNegocioException(
                "No se pudo identificar el contacto de WhatsApp.");
        }


        var idConversacion =
            await _repository
                .ObtenerOCrearConversacion(
                    identificador);


        /*
         * ====================================================
         * 2. GUARDAR MENSAJE CLIENTE
         * ====================================================
         */

        await _repository
            .RegistrarMensaje(
                idConversacion,
                "CLIENTE",
                request.Mensaje);


        /*
         * ====================================================
         * 3. RESOLVER PUBLICACION
         *
         * Prioridad:
         *
         * A. Request.IdPublicacion
         * B. URL incluida en el mensaje
         * C. Contexto anterior
         * ====================================================
         */

        int? idPublicacion =
            request.IdPublicacion;


        if (!idPublicacion.HasValue)
        {
            idPublicacion =
                ExtraerIdPublicacion(
                    request.Mensaje);
        }


        if (!idPublicacion.HasValue)
        {
            idPublicacion =
                await _repository
                    .ObtenerIdPublicacionContexto(
                        idConversacion);
        }


        /*
         * ====================================================
         * 4. SIN PUBLICACION
         *
         * No llamamos a Qwen porque no queremos
         * que invente qué modelo consulta.
         * ====================================================
         */

        if (!idPublicacion.HasValue)
        {
            const string respuesta =
                "¡Hola! 😊 Para darte el precio y la financiación correctos, enviame la publicación de la moto que te interesa desde TuVendedor.";


            await _repository
                .RegistrarMensaje(
                    idConversacion,
                    "IA",
                    respuesta);


            return new MotoConversacionResponseDto
            {
                IdConversacion =
                    idConversacion,

                IdPublicacion =
                    null,

                Marca =
                    null,

                Modelo =
                    null,

                Respuesta =
                    respuesta,

                RequierePublicacion =
                    true
            };
        }


        /*
         * ====================================================
         * 5. OBTENER OFERTA REAL
         *
         * Acá viene:
         *
         * - marca
         * - modelo
         * - precio contado
         * - descuento
         * - promoción
         * - financiación
         * ====================================================
         */

        var oferta =
            await _motoOfertaService
                .ObtenerOfertaPorPublicacion(
                    idPublicacion.Value);


        /*
         * ====================================================
         * 6. RESOLVER PROMPT SEGUN MARCA
         *
         * KENTON
         *   -> MOTO_VENTAS_KENTON
         *
         * YAMAHA
         *   -> MOTO_VENTAS_YAMAHA
         *
         * LEOPARD
         *   -> MOTO_VENTAS_LEOPARD
         * ====================================================
         */

        var codigoPromptMarca =
            ConstruirCodigoPromptMarca(
                oferta.Modelo.Marca);


        _logger.LogInformation(
            "Resolviendo estrategia comercial IA. Marca={Marca}, Prompt={Prompt}",
            oferta.Modelo.Marca,
            codigoPromptMarca);


        /*
         * ====================================================
         * 7. PROMPT BASE
         * ====================================================
         */

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
                $"No existe el prompt base activo '{CODIGO_PROMPT_BASE}'.");
        }


        /*
         * ====================================================
         * 8. PROMPT DE MARCA
         *
         * IMPORTANTE:
         *
         * Si no existe, NO usamos solamente el genérico.
         *
         * Cada marca tendrá su propia forma de vender.
         * Por eso detenemos el flujo antes de permitir
         * que Qwen improvise condiciones.
         * ====================================================
         */

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


        /*
         * ====================================================
         * 9. ACTUALIZAR CONTEXTO
         *
         * Guardamos el prompt específico utilizado.
         * ====================================================
         */

        await _repository
            .ActualizarContexto(
                idConversacion,
                idPublicacion.Value,
                codigoPromptMarca);


        /*
         * ====================================================
         * 10. CONSTRUIR SYSTEM PROMPT
         *
         * BASE
         * +
         * MARCA
         * +
         * DATOS REALES
         * ====================================================
         */

        var promptSistema =
            ConstruirPromptSistema(
                promptBase,
                promptMarca,
                oferta);


        /*
         * ====================================================
         * 11. HISTORIAL RECIENTE
         * ====================================================
         */

        var historial =
            await _repository
                .ObtenerUltimosMensajes(
                    idConversacion,
                    12);


        /*
         * ====================================================
         * 12. CONSULTAR QWEN / OLLAMA
         * ====================================================
         */

        var respuestaIA =
            await _ollamaService
                .GenerarRespuesta(
                    promptSistema,
                    historial,
                    cancellationToken);


        /*
         * ====================================================
         * 13. GUARDAR RESPUESTA
         * ====================================================
         */

        await _repository
            .RegistrarMensaje(
                idConversacion,
                "IA",
                respuestaIA);


        /*
         * ====================================================
         * 14. RESPUESTA API
         * ====================================================
         */

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


    /*
     * ========================================================
     * CODIGO PROMPT POR MARCA
     *
     * KENTON
     * -> MOTO_VENTAS_KENTON
     *
     * Yamaha Motor
     * -> MOTO_VENTAS_YAMAHA_MOTOR
     *
     * ========================================================
     */

    private static string
        ConstruirCodigoPromptMarca(
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
            marca
                .Trim()
                .ToUpperInvariant();


        marcaNormalizada =
            Regex.Replace(
                marcaNormalizada,
                @"[^A-Z0-9]+",
                "_");


        marcaNormalizada =
            marcaNormalizada
                .Trim('_');


        return
            $"MOTO_VENTAS_{marcaNormalizada}";
    }


    /*
     * ========================================================
     * EXTRAER PUBLICACION DESDE WHATSAPP
     *
     * Soporta:
     *
     * /share/producto/1020
     * /producto/1020
     * TV-1020
     * ========================================================
     */

    private static int?
        ExtraerIdPublicacion(
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


    /*
     * ========================================================
     * NORMALIZAR IDENTIFICADOR WHATSAPP
     * ========================================================
     */

    private static string
        NormalizarIdentificador(
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


    /*
     * ========================================================
     * PROMPT FINAL
     *
     * ORDEN:
     *
     * 1. Reglas generales
     * 2. Reglas específicas de marca
     * 3. Datos comerciales reales
     *
     * ========================================================
     */

    private static string ConstruirPromptSistema(
        string promptBase,
        string promptMarca,
        MotoOfertaDto oferta)
    {
        var sb =
            new StringBuilder();


        /*
         * BASE
         */
        sb.AppendLine(
            "========================================");

        sb.AppendLine(
            "REGLAS GENERALES TUVENDEDOR");

        sb.AppendLine(
            "========================================");

        sb.AppendLine(
            promptBase.Trim());


        sb.AppendLine();


        /*
         * MARCA
         */
        sb.AppendLine(
            "========================================");

        sb.AppendLine(
            $"REGLAS COMERCIALES DE {oferta.Modelo.Marca.ToUpperInvariant()}");

        sb.AppendLine(
            "========================================");

        sb.AppendLine(
            promptMarca.Trim());


        sb.AppendLine();


        /*
         * JERARQUIA
         */
        sb.AppendLine(
            "========================================");

        sb.AppendLine(
            "JERARQUIA DE INFORMACION");

        sb.AppendLine(
            "========================================");

        sb.AppendLine(
            "Ante cualquier diferencia o conflicto:");

        sb.AppendLine(
            "1. Los DATOS COMERCIALES DEL BACKEND tienen máxima prioridad.");

        sb.AppendLine(
            "2. Luego se aplican las REGLAS COMERCIALES DE LA MARCA.");

        sb.AppendLine(
            "3. Finalmente se aplican las REGLAS GENERALES.");

        sb.AppendLine();


        /*
         * DATOS REALES
         */
        sb.AppendLine(
            "========================================");

        sb.AppendLine(
            "DATOS COMERCIALES ACTUALES DEL BACKEND");

        sb.AppendLine(
            "========================================");


        sb.AppendLine(
            $"Publicación: {oferta.PublicacionId}");


        sb.AppendLine(
            $"Marca: {oferta.Modelo.Marca}");


        sb.AppendLine(
            $"Modelo: {oferta.Modelo.Nombre}");


        sb.AppendLine(
            $"Código de referencia: {oferta.Modelo.CodigoReferencia}");


        if (
            oferta.Modelo.Cilindrada.HasValue)
        {
            sb.AppendLine(
                $"Cilindrada: {oferta.Modelo.Cilindrada.Value} cc");
        }


        /*
         * CONTADO
         */
        sb.AppendLine();
        sb.AppendLine(
            "CONTADO:");

        sb.AppendLine(
            $"Precio de lista: {oferta.Contado.PrecioListaFormateado}");


        if (
            !string.IsNullOrWhiteSpace(
                oferta.Contado.PorcentajeDescuentoFormateado)
        )
        {
            sb.AppendLine(
                $"Descuento contado: {oferta.Contado.PorcentajeDescuentoFormateado}");
        }


        if (
            !string.IsNullOrWhiteSpace(
                oferta.Contado.PrecioFinalFormateado)
        )
        {
            sb.AppendLine(
                $"Precio final contado: {oferta.Contado.PrecioFinalFormateado}");
        }


        /*
         * CREDITO
         */
        sb.AppendLine();
        sb.AppendLine(
            "CREDITO:");


        sb.AppendLine(
            $"Tipo: {oferta.Credito.Tipo}");


        sb.AppendLine(
            $"Tiene promoción vigente: {(oferta.Credito.TienePromo ? "SI" : "NO")}");


        if (
            !string.IsNullOrWhiteSpace(
                oferta.Credito.FechaDesde)
        )
        {
            sb.AppendLine(
                $"Vigente desde: {oferta.Credito.FechaDesde}");
        }


        if (
            !string.IsNullOrWhiteSpace(
                oferta.Credito.FechaHasta)
        )
        {
            sb.AppendLine(
                $"Vigente hasta: {oferta.Credito.FechaHasta}");
        }


        if (
            oferta.Credito.Planes.Count > 0)
        {
            sb.AppendLine(
                "Planes vigentes:");


            foreach (
                var plan
                in oferta.Credito.Planes)
            {
                sb.AppendLine(
                    $"- {plan.Resumen}");


                if (
                    plan.EntregaInicial > 0)
                {
                    sb.AppendLine(
                        $"  Entrega inicial: {plan.EntregaInicialFormateada}");
                }
            }
        }
        else
        {
            sb.AppendLine(
                "No hay planes de financiación suministrados.");
        }


        /*
         * RESTRICCIONES FINALES
         */
        sb.AppendLine();
        sb.AppendLine(
            "========================================");

        sb.AppendLine(
            "RESTRICCIONES FINALES");

        sb.AppendLine(
            "========================================");


        sb.AppendLine(
            "- No uses ningún precio diferente de los valores suministrados arriba.");

        sb.AppendLine(
            "- No confirmes stock físico si el backend no lo informa.");

        sb.AppendLine(
            "- No calcules nuevas cuotas por tu cuenta.");

        sb.AppendLine(
            "- No inventes promociones, entregas ni condiciones.");

        sb.AppendLine(
            "- No muestres información interna.");

        sb.AppendLine(
            "- Respondé de manera breve, natural y comercial.");

        sb.AppendLine(
            "- La respuesta será enviada directamente al cliente por WhatsApp.");


        return sb.ToString();
    }
}

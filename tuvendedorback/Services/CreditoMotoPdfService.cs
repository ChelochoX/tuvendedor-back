using System.Collections;
using System.Globalization;
using Dapper;
using iText.IO.Font.Constants;
using iText.IO.Image;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Borders;
using iText.Layout.Element;
using iText.Layout.Properties;
using tuvendedorback.Data;
using tuvendedorback.DTOs;
using tuvendedorback.Services.Interfaces;

namespace tuvendedorback.Services;

public class CreditoMotoPdfService
    : ICreditoMotoPdfService
{
    private readonly ICreditoMotoGestionService _gestionService;
    private readonly DbConnections _conexion;
    private readonly ILogger<CreditoMotoPdfService> _logger;

    private static readonly CultureInfo CulturaPy =
        CultureInfo.GetCultureInfo("es-PY");

    public CreditoMotoPdfService(
        ICreditoMotoGestionService gestionService,
        DbConnections conexion,
        ILogger<CreditoMotoPdfService> logger)
    {
        _gestionService = gestionService;
        _conexion = conexion;
        _logger = logger;
    }


    // =========================================================
    // GENERAR PDF
    // =========================================================

    public async Task<CreditoMotoArchivoDto> GenerarPdf(
        int idSolicitudCredito)
    {
        var detalle =
            await _gestionService.ObtenerDetalle(
                idSolicitudCredito);

        var condicion =
            await ObtenerCondicionComercial(
                idSolicitudCredito);

        using var stream =
            new MemoryStream();

        var writer =
            new PdfWriter(stream);

        var pdf =
            new PdfDocument(writer);

        var document =
            new Document(
                pdf,
                PageSize.A4);

        document.SetMargins(
            28,
            32,
            30,
            32);

        try
        {
            AgregarEncabezado(
                document,
                detalle);

            AgregarSeccionCliente(
                document,
                detalle);

            AgregarSeccionMoto(
                document,
                detalle);

            AgregarSeccionCondicionComercial(
                document,
                condicion);

            AgregarSeccionDomicilio(
                document,
                detalle);

            AgregarSeccionLaboral(
                document,
                detalle);

            AgregarSeccionReferencias(
                document,
                detalle);

            AgregarSeccionAutorizacion(
                document,
                detalle);

            await AgregarSeccionDocumentos(
                document,
                detalle);

            AgregarSeccionControl(
                document,
                detalle);

            AgregarPie(
                document);

            document.Close();

            return new CreditoMotoArchivoDto
            {
                NombreArchivo =
                    CrearNombreArchivo(
                        detalle),

                MimeType =
                    "application/pdf",

                Contenido =
                    stream.ToArray()
            };
        }
        catch
        {
            try
            {
                document.Close();
            }
            catch
            {
                // No ocultar la excepción original.
            }

            throw;
        }
    }


    // =========================================================
    // ENCABEZADO
    // =========================================================

    private static void AgregarEncabezado(
        Document document,
        CreditoMotoGestionDetalleDto detalle)
    {
        var titulo =
            new Paragraph()
                .Add(
                    TextoNegrita(
                        "TUVENDEDOR"))
                .SetFontSize(18)
                .SetFontColor(
                    new DeviceRgb(
                        214,
                        164,
                        0))
                .SetMarginBottom(2);

        document.Add(
            titulo);

        document.Add(
            new Paragraph()
                .Add(
                    TextoNegrita(
                        "SOLICITUD DE CREDITO DE MOTOCICLETA"))
                .SetFontSize(14)
                .SetMarginTop(0)
                .SetMarginBottom(4));

        document.Add(
            new Paragraph(
                $"Solicitud #{detalle.IdSolicitudCredito} - " +
                $"Generada {DateTime.Now:dd/MM/yyyy HH:mm}")
                .SetFontSize(9)
                .SetFontColor(
                    ColorConstants.GRAY)
                .SetMarginTop(0)
                .SetMarginBottom(8));

        document.Add(
            new Paragraph(
                "Ficha preparada para gestión y carga de crédito. " +
                "Los datos corresponden a la información registrada " +
                "en TuVendedor al momento de generar el documento.")
                .SetFontSize(8.5f)
                .SetFontColor(
                    ColorConstants.DARK_GRAY)
                .SetMarginTop(0)
                .SetMarginBottom(12));
    }


    // =========================================================
    // CLIENTE
    // =========================================================

    private static void AgregarSeccionCliente(
        Document document,
        CreditoMotoGestionDetalleDto detalle)
    {
        AgregarTituloSeccion(
            document,
            "DATOS DEL CLIENTE");

        var tabla =
            CrearTablaDosColumnas();

        AgregarDato(
            tabla,
            "Nombre y apellido",
            detalle.NombreCompleto);

        AgregarDato(
            tabla,
            "Cedula",
            detalle.Cedula);

        AgregarDato(
            tabla,
            "Telefono",
            detalle.Telefono);

        AgregarDato(
            tabla,
            "Fecha de nacimiento",
            FormatearFechaCorta(
                detalle.FechaNacimiento));

        document.Add(
            tabla);
    }


    // =========================================================
    // MOTO
    // =========================================================

    private static void AgregarSeccionMoto(
        Document document,
        CreditoMotoGestionDetalleDto detalle)
    {
        AgregarTituloSeccion(
            document,
            "MOTO SOLICITADA");

        var tabla =
            CrearTablaDosColumnas();

        AgregarDato(
            tabla,
            "Marca",
            detalle.Marca);

        AgregarDato(
            tabla,
            "Modelo",
            detalle.Modelo);

        AgregarDato(
            tabla,
            "Codigo / articulo",
            detalle.CodigoReferencia);

        AgregarDato(
            tabla,
            "Cilindrada",
            detalle.Cilindrada.HasValue
                ? $"{detalle.Cilindrada.Value} cc"
                : null);

        document.Add(
            tabla);
    }


    // =========================================================
    // CONDICION COMERCIAL
    // =========================================================

    private static void AgregarSeccionCondicionComercial(
        Document document,
        IReadOnlyList<CampoPdf> campos)
    {
        if (campos.Count == 0)
        {
            return;
        }

        AgregarTituloSeccion(
            document,
            "CONDICION COMERCIAL SOLICITADA");

        var tabla =
            CrearTablaDosColumnas();

        foreach (var campo in campos)
        {
            AgregarDato(
                tabla,
                campo.Etiqueta,
                campo.Valor);
        }

        document.Add(
            tabla);
    }


    // =========================================================
    // DOMICILIO
    // =========================================================

    private static void AgregarSeccionDomicilio(
        Document document,
        CreditoMotoGestionDetalleDto detalle)
    {
        AgregarTituloSeccion(
            document,
            "DOMICILIO PARTICULAR");

        var tabla =
            CrearTablaDosColumnas();

        AgregarDato(
            tabla,
            "Ciudad",
            detalle.Ciudad);

        AgregarDato(
            tabla,
            "Barrio",
            detalle.Barrio);

        AgregarDato(
            tabla,
            "Direccion",
            detalle.Direccion);

        document.Add(
            tabla);
    }


    // =========================================================
    // LABORAL
    // =========================================================

    private static void AgregarSeccionLaboral(
        Document document,
        CreditoMotoGestionDetalleDto detalle)
    {
        AgregarTituloSeccion(
            document,
            "DATOS LABORALES");

        if (detalle.Laboral == null)
        {
            document.Add(
                TextoVacio(
                    "Sin datos laborales registrados."));

            return;
        }

        var laboral =
            detalle.Laboral;

        var tabla =
            CrearTablaDosColumnas();

        AgregarDato(
            tabla,
            "Empresa",
            laboral.Empresa);

        AgregarDato(
            tabla,
            "Antiguedad",
            $"{laboral.AntiguedadMeses} meses");

        AgregarDato(
            tabla,
            "Aporta IPS",
            laboral.AportaIPS
                ? "SI"
                : "NO");

        AgregarDato(
            tabla,
            "Cantidad de aportes IPS",
            laboral.CantidadAportesIPS.ToString());

        AgregarDato(
            tabla,
            "Cargo",
            laboral.Cargo);

        AgregarDato(
            tabla,
            "Salario",
            laboral.Salario.HasValue
                ? FormatearGuaranies(
                    laboral.Salario.Value)
                : null);

        AgregarDato(
            tabla,
            "Tipo de pago",
            laboral.TipoPago);

        AgregarDato(
            tabla,
            "Telefono empresa",
            laboral.TelefonoEmpresa);

        AgregarDato(
            tabla,
            "Direccion empresa",
            laboral.DireccionEmpresa);

        AgregarDato(
            tabla,
            "Jefe / encargado",
            laboral.NombreJefeEncargado);

        document.Add(
            tabla);
    }


    // =========================================================
    // REFERENCIAS
    // =========================================================

    private static void AgregarSeccionReferencias(
        Document document,
        CreditoMotoGestionDetalleDto detalle)
    {
        AgregarTituloSeccion(
            document,
            "REFERENCIAS");

        if (
            detalle.Referencias == null
            ||
            detalle.Referencias.Count == 0)
        {
            document.Add(
                TextoVacio(
                    "Sin referencias registradas."));

            return;
        }

        var tabla =
            new Table(
                UnitValue.CreatePercentArray(
                    new float[]
                    {
                        18,
                        30,
                        22,
                        15,
                        15
                    }))
                .UseAllAvailableWidth();

        AgregarCabeceraTabla(
            tabla,
            "Tipo");

        AgregarCabeceraTabla(
            tabla,
            "Nombre");

        AgregarCabeceraTabla(
            tabla,
            "Telefono");

        AgregarCabeceraTabla(
            tabla,
            "Parentesco");

        AgregarCabeceraTabla(
            tabla,
            "Observacion");

        foreach (
            var referencia
            in detalle.Referencias)
        {
            AgregarCelda(
                tabla,
                referencia.Tipo);

            AgregarCelda(
                tabla,
                referencia.Nombre);

            AgregarCelda(
                tabla,
                referencia.Telefono);

            AgregarCelda(
                tabla,
                referencia.Parentesco);

            AgregarCelda(
                tabla,
                referencia.Observacion);
        }

        document.Add(
            tabla);
    }


    // =========================================================
    // AUTORIZACION
    // =========================================================

    private static void AgregarSeccionAutorizacion(
        Document document,
        CreditoMotoGestionDetalleDto detalle)
    {
        AgregarTituloSeccion(
            document,
            "AUTORIZACION DEL CLIENTE");

        if (detalle.Autorizacion == null)
        {
            document.Add(
                TextoVacio(
                    "Sin autorizacion registrada."));

            return;
        }

        var autorizacion =
            detalle.Autorizacion;

        var tabla =
            CrearTablaDosColumnas();

        AgregarDato(
            tabla,
            "Titular",
            autorizacion.NombreCompleto);

        AgregarDato(
            tabla,
            "Cedula",
            autorizacion.NumeroCedula);

        AgregarDato(
            tabla,
            "Canal",
            autorizacion.Canal);

        AgregarDato(
            tabla,
            "Fecha",
            autorizacion.FechaAutorizacion
                .ToString(
                    "dd/MM/yyyy HH:mm"));

        document.Add(
            tabla);

        document.Add(
            new Paragraph()
                .Add(
                    TextoNegrita(
                        "Mensaje original de aceptacion:"))
                .SetFontSize(8.5f)
                .SetMarginTop(5)
                .SetMarginBottom(2));

        document.Add(
            new Paragraph(
                ValorOGuion(
                    autorizacion.MensajeOriginal))
                .SetFontSize(8.5f)
                .SetBackgroundColor(
                    new DeviceRgb(
                        245,
                        245,
                        245))
                .SetPadding(7)
                .SetMarginTop(0)
                .SetMarginBottom(5));

        document.Add(
            new Paragraph(
                "Nota: esta ficha conserva el texto de autorizacion " +
                "registrado por el sistema. Si Chacomer requiere " +
                "especificamente la captura visual de WhatsApp con el " +
                "numero visible, dicha captura se adjunta por separado.")
                .SetFontSize(7.8f)
                .SetFontColor(
                    ColorConstants.GRAY)
                .SetMarginTop(2));
    }


    // =========================================================
    // DOCUMENTOS
    // =========================================================

    private async Task AgregarSeccionDocumentos(
        Document document,
        CreditoMotoGestionDetalleDto detalle)
    {
        AgregarTituloSeccion(
            document,
            "DOCUMENTOS");

        if (
            detalle.Documentos == null
            ||
            detalle.Documentos.Count == 0)
        {
            document.Add(
                TextoVacio(
                    "Sin documentos registrados."));

            return;
        }

        var tabla =
            new Table(
                UnitValue.CreatePercentArray(
                    new float[]
                    {
                        35,
                        30,
                        20,
                        15
                    }))
                .UseAllAvailableWidth();

        AgregarCabeceraTabla(
            tabla,
            "Tipo");

        AgregarCabeceraTabla(
            tabla,
            "Archivo");

        AgregarCabeceraTabla(
            tabla,
            "Estado");

        AgregarCabeceraTabla(
            tabla,
            "Recepcion");

        foreach (
            var documento
            in detalle.Documentos)
        {
            AgregarCelda(
                tabla,
                documento.TipoDocumento);

            AgregarCelda(
                tabla,
                documento.NombreArchivo);

            AgregarCelda(
                tabla,
                documento.EstadoRevision);

            AgregarCelda(
                tabla,
                documento.FechaRecepcion
                    .ToString(
                        "dd/MM/yyyy HH:mm"));
        }

        document.Add(
            tabla);

        var imagenes =
            new List<ImagenDocumentoPdf>();

        foreach (
            var documento
            in detalle.Documentos
                .Where(
                    EsDocumentoCedula))
        {
            try
            {
                var archivo =
                    await _gestionService.ObtenerDocumento(
                        detalle.IdSolicitudCredito,
                        documento.Id);

                if (
                    archivo.Contenido.Length == 0
                    ||
                    !EsImagen(
                        archivo.MimeType))
                {
                    continue;
                }

                imagenes.Add(
                    new ImagenDocumentoPdf(
                        documento.TipoDocumento,
                        archivo.Contenido));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "No se pudo incorporar el documento {IdDocumento} al PDF de solicitud {IdSolicitudCredito}.",
                    documento.Id,
                    detalle.IdSolicitudCredito);
            }
        }

        if (imagenes.Count == 0)
        {
            return;
        }

        document.Add(
            new Paragraph()
                .Add(
                    TextoNegrita(
                        "Imagenes de cedula"))
                .SetFontSize(9)
                .SetMarginTop(8)
                .SetMarginBottom(4));

        var tablaImagenes =
            new Table(
                UnitValue.CreatePercentArray(
                    new float[]
                    {
                        50,
                        50
                    }))
                .UseAllAvailableWidth();

        foreach (
            var imagenDocumento
            in imagenes.Take(2))
        {
            var celda =
                new Cell()
                    .SetPadding(6)
                    .SetBorder(
                        new SolidBorder(
                            new DeviceRgb(
                                220,
                                220,
                                220),
                            0.6f));

            celda.Add(
                new Paragraph()
                    .Add(
                        TextoNegrita(
                            EtiquetaDocumento(
                                imagenDocumento.TipoDocumento)))
                    .SetFontSize(8)
                    .SetMarginBottom(4));

            try
            {
                var imageData =
                    ImageDataFactory.Create(
                        imagenDocumento.Contenido);

                var image =
                    new Image(
                        imageData);

                image.SetMaxWidth(
                    235);

                image.SetMaxHeight(
                    175);

                celda.Add(
                    image);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "No se pudo renderizar una imagen de cedula dentro del PDF.");

                celda.Add(
                    new Paragraph(
                        "Imagen no disponible para renderizado.")
                        .SetFontSize(8)
                        .SetFontColor(
                            ColorConstants.GRAY));
            }

            tablaImagenes.AddCell(
                celda);
        }

        if (imagenes.Count == 1)
        {
            tablaImagenes.AddCell(
                new Cell()
                    .SetBorder(
                        Border.NO_BORDER));
        }

        document.Add(
            tablaImagenes);
    }


    // =========================================================
    // CONTROL INTERNO
    // =========================================================

    private static void AgregarSeccionControl(
        Document document,
        CreditoMotoGestionDetalleDto detalle)
    {
        AgregarTituloSeccion(
            document,
            "CONTROL DE SOLICITUD");

        var tabla =
            CrearTablaDosColumnas();

        AgregarDato(
            tabla,
            "Pre-evaluacion",
            detalle.ResultadoPreEvaluacion);

        AgregarDato(
            tabla,
            "Estado solicitud",
            detalle.EstadoSolicitud);

        AgregarDato(
            tabla,
            "Paso",
            detalle.PasoActual);

        AgregarDato(
            tabla,
            "Estado interno",
            detalle.EstadoControl);

        AgregarDato(
            tabla,
            "Recepcion",
            detalle.FechaRecepcion
                .ToString(
                    "dd/MM/yyyy HH:mm"));

        AgregarDato(
            tabla,
            "Estado de cedula",
            detalle.EstadoCedula);

        document.Add(
            tabla);
    }


    // =========================================================
    // CONDICION COMERCIAL DESDE SNAPSHOT
    // =========================================================
    //
    // Se usa SELECT * de forma intencional:
    // el flujo de motos fue evolucionando y algunas instalaciones
    // ya poseen columnas snapshot de la condicion comercial.
    //
    // Si una columna aun no existe, simplemente no se muestra.
    // Esto evita romper el PDF mientras terminamos de consolidar
    // los nombres definitivos de esos campos.
    // =========================================================

    private async Task<IReadOnlyList<CampoPdf>>
        ObtenerCondicionComercial(
            int idSolicitudCredito)
    {
        using var conn =
            _conexion.CreateSqlConnection();

        try
        {
            var row =
                await conn.QueryFirstOrDefaultAsync(
                    @"
SELECT TOP (1) *
FROM dbo.SolicitudesCredito
WHERE Id = @IdSolicitudCredito;
",
                    new
                    {
                        IdSolicitudCredito =
                            idSolicitudCredito
                    });

            if (row == null)
            {
                return Array.Empty<CampoPdf>();
            }

            var valores =
                (IDictionary<string, object>)row;

            var resultado =
                new List<CampoPdf>();

            AgregarSiExiste(
                resultado,
                "Tipo de credito / plan",
                ObtenerTexto(
                    valores,
                    "TipoCreditoSolicitado",
                    "TipoPlanSolicitado",
                    "TipoPlan",
                    "PlanSeleccionado"));

            var esPromo =
                ObtenerBool(
                    valores,
                    "EsPromo");

            if (esPromo.HasValue)
            {
                resultado.Add(
                    new CampoPdf(
                        "Promocion",
                        esPromo.Value
                            ? "SI"
                            : "NO"));
            }

            AgregarSiExiste(
                resultado,
                "Codigo de plan",
                ObtenerTexto(
                    valores,
                    "CodigoPlan"));

            AgregarSiExiste(
                resultado,
                "Precio publico",
                FormatearDineroSiExiste(
                    valores,
                    "PrecioPublico",
                    "PrecioLista",
                    "Precio"));

            AgregarSiExiste(
                resultado,
                "Cantidad de cuotas",
                ObtenerTexto(
                    valores,
                    "CantidadCuotas",
                    "Cuotas"));

            AgregarSiExiste(
                resultado,
                "Entrega del plan",
                FormatearDineroSiExiste(
                    valores,
                    "EntregaInicialPlan"));

            AgregarSiExiste(
                resultado,
                "Entrega solicitada por cliente",
                FormatearDineroSiExiste(
                    valores,
                    "EntregaInicialCliente",
                    "MontoEntrega",
                    "EntregaInicial"));

            AgregarSiExiste(
                resultado,
                "Cuota del plan",
                FormatearDineroSiExiste(
                    valores,
                    "ImporteCuotaPlan"));

            AgregarSiExiste(
                resultado,
                "Cuota final solicitada",
                FormatearDineroSiExiste(
                    valores,
                    "ImporteCuotaFinal",
                    "ImporteCuota",
                    "Cuota"));

            AgregarSiExiste(
                resultado,
                "Interes",
                ObtenerPorcentaje(
                    valores,
                    "Interes"));

            AgregarSiExiste(
                resultado,
                "Fecha condicion comercial",
                ObtenerFechaTexto(
                    valores,
                    "FechaCondicionComercial"));

            return resultado;
        }
        catch (Exception ex)
        {
            // El PDF debe seguir siendo generable aunque una rama
            // antigua de la BD todavia no tenga snapshot comercial.
            _logger.LogWarning(
                ex,
                "No se pudo obtener la condicion comercial para el PDF de la solicitud {IdSolicitudCredito}.",
                idSolicitudCredito);

            return Array.Empty<CampoPdf>();
        }
    }


    // =========================================================
    // HELPERS PDF
    // =========================================================

    private static void AgregarTituloSeccion(
        Document document,
        string titulo)
    {
        document.Add(
            new Paragraph()
                .Add(
                    TextoNegrita(titulo))
                .SetFontSize(9.5f)
                .SetFontColor(
                    new DeviceRgb(
                        120,
                        90,
                        0))
                .SetMarginTop(10)
                .SetMarginBottom(4));
    }


    private static Table CrearTablaDosColumnas()
    {
        return new Table(
                UnitValue.CreatePercentArray(
                    new float[]
                    {
                        32,
                        68
                    }))
            .UseAllAvailableWidth();
    }


    private static void AgregarDato(
        Table tabla,
        string etiqueta,
        string? valor)
    {
        tabla.AddCell(
            new Cell()
                .Add(
                    new Paragraph()
                        .Add(
                            TextoNegrita(etiqueta))
                        .SetFontSize(8.2f)
                        .SetMargin(0))
                .SetPadding(5)
                .SetBackgroundColor(
                    new DeviceRgb(
                        247,
                        247,
                        247))
                .SetBorder(
                    new SolidBorder(
                        new DeviceRgb(
                            225,
                            225,
                            225),
                        0.5f)));

        tabla.AddCell(
            new Cell()
                .Add(
                    new Paragraph(
                        ValorOGuion(
                            valor))
                        .SetFontSize(8.2f)
                        .SetMargin(0))
                .SetPadding(5)
                .SetBorder(
                    new SolidBorder(
                        new DeviceRgb(
                            225,
                            225,
                            225),
                        0.5f)));
    }


    private static void AgregarCabeceraTabla(
        Table tabla,
        string texto)
    {
        tabla.AddHeaderCell(
            new Cell()
                .Add(
                    new Paragraph()
                        .Add(
                            TextoNegrita(texto))
                        .SetFontSize(7.8f)
                        .SetMargin(0))
                .SetPadding(4)
                .SetBackgroundColor(
                    new DeviceRgb(
                        238,
                        238,
                        238))
                .SetBorder(
                    new SolidBorder(
                        new DeviceRgb(
                            210,
                            210,
                            210),
                        0.5f)));
    }


    private static void AgregarCelda(
        Table tabla,
        string? texto)
    {
        tabla.AddCell(
            new Cell()
                .Add(
                    new Paragraph(
                        ValorOGuion(
                            texto))
                        .SetFontSize(7.8f)
                        .SetMargin(0))
                .SetPadding(4)
                .SetBorder(
                    new SolidBorder(
                        new DeviceRgb(
                            225,
                            225,
                            225),
                        0.5f)));
    }


    private static Paragraph TextoVacio(
        string texto)
    {
        return new Paragraph(
                texto)
            .SetFontSize(8.2f)
            .SetFontColor(
                ColorConstants.GRAY)
            .SetMarginTop(0)
            .SetMarginBottom(4);
    }


    private static void AgregarPie(
        Document document)
    {
        document.Add(
            new Paragraph(
                "Documento generado por TuVendedor. Uso interno para gestion comercial y de credito.")
                .SetFontSize(7)
                .SetFontColor(
                    ColorConstants.GRAY)
                .SetTextAlignment(
                    TextAlignment.CENTER)
                .SetMarginTop(14));
    }


    // =========================================================
    // HELPERS DATOS
    // =========================================================

    private static string ValorOGuion(
        string? valor)
    {
        return string.IsNullOrWhiteSpace(
            valor)
                ? "-"
                : valor.Trim();
    }


    private static string? FormatearFechaCorta(
        DateTime? fecha)
    {
        return fecha.HasValue
            ? fecha.Value.ToString(
                "dd/MM/yyyy")
            : null;
    }


    private static string FormatearGuaranies(
        decimal valor)
    {
        return $"Gs. {valor:N0}";
    }


    private static string CrearNombreArchivo(
        CreditoMotoGestionDetalleDto detalle)
    {
        var nombre =
            string.IsNullOrWhiteSpace(
                detalle.NombreCompleto)
                ? "cliente"
                : detalle.NombreCompleto;

        var limpio =
            new string(
                nombre
                    .Where(
                        c =>
                            char.IsLetterOrDigit(c)
                            ||
                            c == ' '
                            ||
                            c == '-')
                    .ToArray())
                .Trim()
                .Replace(
                    ' ',
                    '_');

        if (
            string.IsNullOrWhiteSpace(
                limpio))
        {
            limpio =
                "cliente";
        }

        return
            $"Solicitud_Credito_{detalle.IdSolicitudCredito}_{limpio}.pdf";
    }


    private static bool EsDocumentoCedula(
        CreditoMotoDocumentoDto documento)
    {
        var tipo =
            documento.TipoDocumento
                ?.Trim()
                .ToUpperInvariant()
                ?? string.Empty;

        return
            tipo.Contains(
                "CEDULA")
            ||
            tipo.Contains(
                "CI_")
            ||
            tipo.Contains(
                "CI ");
    }


    private static bool EsImagen(
        string? mimeType)
    {
        return
            !string.IsNullOrWhiteSpace(
                mimeType)
            &&
            mimeType.StartsWith(
                "image/",
                StringComparison.OrdinalIgnoreCase);
    }


    private static string EtiquetaDocumento(
        string tipoDocumento)
    {
        var tipo =
            tipoDocumento
                .Trim()
                .ToUpperInvariant();

        if (
            tipo.Contains(
                "FRENTE"))
        {
            return
                "CEDULA - FRENTE";
        }

        if (
            tipo.Contains(
                "DORSO"))
        {
            return
                "CEDULA - DORSO";
        }

        return
            tipoDocumento;
    }


    private static void AgregarSiExiste(
        ICollection<CampoPdf> campos,
        string etiqueta,
        string? valor)
    {
        if (
            !string.IsNullOrWhiteSpace(
                valor))
        {
            campos.Add(
                new CampoPdf(
                    etiqueta,
                    valor));
        }
    }


    private static object? BuscarValor(
        IDictionary<string, object> valores,
        params string[] nombres)
    {
        foreach (
            var nombre
            in nombres)
        {
            var coincidencia =
                valores.FirstOrDefault(
                    x =>
                        string.Equals(
                            x.Key,
                            nombre,
                            StringComparison.OrdinalIgnoreCase));

            if (
                !string.IsNullOrWhiteSpace(
                    coincidencia.Key))
            {
                if (
                    coincidencia.Value
                    is DBNull)
                {
                    return null;
                }

                return
                    coincidencia.Value;
            }
        }

        return null;
    }


    private static string? ObtenerTexto(
        IDictionary<string, object> valores,
        params string[] nombres)
    {
        var valor =
            BuscarValor(
                valores,
                nombres);

        return valor == null
            ? null
            : Convert.ToString(
                valor,
                CulturaPy);
    }


    private static bool? ObtenerBool(
        IDictionary<string, object> valores,
        params string[] nombres)
    {
        var valor =
            BuscarValor(
                valores,
                nombres);

        if (valor == null)
        {
            return null;
        }

        if (valor is bool booleano)
        {
            return booleano;
        }

        if (
            int.TryParse(
                Convert.ToString(
                    valor,
                    CulturaPy),
                out var numero))
        {
            return numero != 0;
        }

        if (
            bool.TryParse(
                Convert.ToString(
                    valor,
                    CulturaPy),
                out var resultado))
        {
            return resultado;
        }

        return null;
    }


    private static string? FormatearDineroSiExiste(
        IDictionary<string, object> valores,
        params string[] nombres)
    {
        var valor =
            BuscarValor(
                valores,
                nombres);

        if (valor == null)
        {
            return null;
        }

        try
        {
            var decimalValor =
                Convert.ToDecimal(
                    valor,
                    CulturaPy);

            return FormatearGuaranies(
                decimalValor);
        }
        catch
        {
            return Convert.ToString(
                valor,
                CulturaPy);
        }
    }


    private static string? ObtenerPorcentaje(
        IDictionary<string, object> valores,
        params string[] nombres)
    {
        var valor =
            BuscarValor(
                valores,
                nombres);

        if (valor == null)
        {
            return null;
        }

        return
            $"{Convert.ToString(valor, CulturaPy)}%";
    }


    private static string? ObtenerFechaTexto(
        IDictionary<string, object> valores,
        params string[] nombres)
    {
        var valor =
            BuscarValor(
                valores,
                nombres);

        if (valor == null)
        {
            return null;
        }

        if (valor is DateTime fecha)
        {
            return fecha.ToString(
                "dd/MM/yyyy HH:mm");
        }

        if (
            DateTime.TryParse(
                Convert.ToString(
                    valor,
                    CulturaPy),
                CulturaPy,
                DateTimeStyles.None,
                out var fechaParseada))
        {
            return fechaParseada.ToString(
                "dd/MM/yyyy HH:mm");
        }

        return Convert.ToString(
            valor,
            CulturaPy);
    }


    private sealed record CampoPdf(
        string Etiqueta,
        string Valor);


    private sealed record ImagenDocumentoPdf(
        string TipoDocumento,
        byte[] Contenido);

    // =========================================================
    // FUENTE NEGRITA - COMPATIBLE CON iText 9.x
    // =========================================================

    private static Text TextoNegrita(
        string texto)
    {
        var fuente =
            PdfFontFactory.CreateFont(
                StandardFonts.HELVETICA_BOLD);

        return new Text(
            texto)
            .SetFont(
                fuente);
    }


}

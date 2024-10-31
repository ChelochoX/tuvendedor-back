using iText.IO.Font;
using iText.Kernel.Font;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using iText.Layout.Properties;
using tuvendedorback.DTOs;
using tuvendedorback.Exceptions;
using tuvendedorback.Models;
using tuvendedorback.Repositories.Interfaces;
using tuvendedorback.Request;
using tuvendedorback.Services.Interfaces;

namespace tuvendedorback.Services;

public class MotoService : IMotoService
{
    private readonly string _baseImagesPath;
    private readonly ILogger<MotoService> _logger;
    private readonly IMotoRepository _repository;
    private readonly IConfiguration _config;

    public MotoService(IConfiguration configuration, ILogger<MotoService> logger, IMotoRepository repository, IConfiguration config)
    {
        _baseImagesPath = configuration["ImagenesMotosPath"];
        _logger = logger;
        _repository = repository;
        _config = config;
    }

    public async Task<List<ModeloMotosporCategoria>> ObtenerModelosPorCategoriaAsync(string categoria)
    {
        if (string.IsNullOrEmpty(_baseImagesPath) || string.IsNullOrEmpty(categoria))
        {
            throw new ArgumentNullException("El valor de la ruta base o la categoría es nulo");
        }

        var rutaBase = _baseImagesPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var categoriaPath = Path.Combine(rutaBase, categoria);

        _logger.LogInformation($"Ruta base: {rutaBase}");
        _logger.LogInformation($"Categoría: {categoria}");
        _logger.LogInformation($"Ruta completa de la categoría: {categoriaPath}");

        if (!Directory.Exists(categoriaPath))
        {
            _logger.LogInformation($"No se encontró la carpeta en la ruta: {categoriaPath}");
            return null; // Retornar null si la categoría no existe
        }

        var subCarpetas = Directory.GetDirectories(categoriaPath);

        if (subCarpetas.Length == 0)
        {
            _logger.LogInformation("No se encontraron subcarpetas dentro de la categoría.");
            return null; // Retornar null si no hay subcarpetas
        }

        var modelos = new List<ModeloMotosporCategoria>();

        foreach (var subCarpeta in subCarpetas)
        {
            var nombreModelo = Path.GetFileName(subCarpeta);
            _logger.LogInformation($"Procesando modelo: {nombreModelo} en la subcarpeta: {subCarpeta}");

            // Obtener la lista de todas las imágenes del modelo
            var imagenes = ObtenerImagenesDeModelo(subCarpeta, categoria);

            if (imagenes == null || imagenes.Count == 0)
            {
                _logger.LogInformation($"No se encontraron imágenes para el modelo: {nombreModelo}");
            }
            else
            {
                _logger.LogInformation($"Se encontraron imágenes para el modelo: {nombreModelo}, URLs: {string.Join(", ", imagenes)}");
            }

            modelos.Add(new ModeloMotosporCategoria
            {
                Nombre = nombreModelo,
                Imagenes = imagenes // Asignar la lista de imágenes al modelo
            });
        }

        if (modelos.Count == 0)
        {
            _logger.LogInformation("No se encontraron modelos con imágenes.");
            return null;
        }

        return modelos;
    }  

    public async Task<ProductoDTO> ObtenerProductoConPlanes(string modelo)
    {
        return await _repository.ObtenerProductoConPlanes(modelo);
    }

    public async Task<decimal> ObtenerMontoCuotaConEntregaMayor(CalculoCuotaRequest request)
    {
        //primeramente obtenemos el monto del precio base
        var precioBase = await _repository.ObtenerPrecioBase(request.ModeloSolicitado);

        //realizamos el calculo
        // Precio base menos la entrega inicial
        decimal resultado = precioBase - request.EntregaInicial;

        // Cantidad de cuotas multiplicada por el interés
        var interesConfig = _config.GetValue<decimal>("InteresCalculoCuota");
        decimal valorInteresTotal = request.CantidadCuotas * (interesConfig / 100);

        // Resultado multiplicado por el interés total
        decimal montoTotalInteres = resultado * valorInteresTotal;

        // Monto total (resultado más el interés)
        decimal montoTotal = resultado + montoTotalInteres;

        // Cálculo de la cuota
        decimal montoCuota = montoTotal / request.CantidadCuotas;

        // Retornamos el monto de la cuota formateado
        return Math.Round(montoCuota, 0);
    }

    public async Task<int> GuardarSolicitudCredito(SolicitudCredito solicitud)
    {
        var resultado = await _repository.GuardarSolicitudCredito(solicitud);

        //Generamos el pdf
        await GenerarPdfSolicitud(solicitud, resultado);

        return resultado;
    }

    public async Task GenerarPdfSolicitud(SolicitudCredito solicitud, int idSolicitud)
    {
        try
        {
            // Define el directorio de salida
            var pdfDirectory = Path.Combine(Directory.GetCurrentDirectory(), "PDFs");
           Directory.CreateDirectory(pdfDirectory); // Asegura que la carpeta exista

            // Define el nombre del archivo PDF
            var outputPath = Path.Combine(pdfDirectory, $"Solicitud_{idSolicitud}.pdf");

            // Obtener entorno actual desde las variables de entorno
            string env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

            // Definir la ruta de la fuente dependiendo del entorno
            string fontPath;
            if (env == "Development")
            {
                // Ruta en entorno de desarrollo (local)
                fontPath = "C:/Windows/Fonts/Arial.ttf"; // O la fuente que tengas en local
            }
            else
            {
                fontPath = Path.Combine(Directory.GetCurrentDirectory(), "assets", "fonts", "calibri.ttf");
            }


            // Cargar la fuente de manera dinámica
            var customFont = PdfFontFactory.CreateFont(fontPath, PdfEncodings.IDENTITY_H);

            // Crea el archivo PDF
            using (var writer = new PdfWriter(outputPath))
            {
                using (var pdf = new PdfDocument(writer))
                {
                    var document = new Document(pdf);
                    document.SetFont(customFont).SetFontSize(10); // Configura la fuente global y el tamaño de 10

                    // Título del documento subrayado, centrado y en tamaño 18
                    document.Add(new Paragraph("Solicitud de Crédito")
                        .SetFontSize(18)
                        .SetBold()
                        .SetUnderline()
                        .SetTextAlignment(TextAlignment.CENTER));

                    // Añadir detalles del modelo
                    document.Add(new Paragraph($"Modelo Solicitado: {solicitud.ModeloSolicitado}"));

                    // Entrega Inicial, Cantidad de Cuotas y Monto por Cuota en una misma línea
                    var entregaCuotasMonto = new Paragraph()
                        .Add(new Text($"Entrega Inicial: G. {solicitud.EntregaInicial:N0}   "))
                        .Add(new Text($"Cantidad de Cuotas: {solicitud.CantidadCuotas}   "))
                        .Add(new Text($"Monto por Cuota: G. {solicitud.MontoPorCuota:N0}"));
                    document.Add(entregaCuotasMonto);

                    // Sección: Datos Personales (centrado, subrayado y tamaño 11)
                    document.Add(new Paragraph("Datos Personales")
                        .SetFontSize(11)
                        .SetBold()
                        .SetUnderline()
                        .SetTextAlignment(TextAlignment.CENTER));

                    document.Add(new Paragraph($"Cédula: {solicitud.CedulaIdentidad}"));
                    document.Add(new Paragraph($"Teléfono Móvil: {solicitud.TelefonoMovil}"));
                    document.Add(new Paragraph($"Fecha de Nacimiento: {solicitud.FechaNacimiento:dd/MM/yyyy}"));
                    document.Add(new Paragraph($"Barrio: {solicitud.Barrio}"));
                    document.Add(new Paragraph($"Ciudad: {solicitud.Ciudad}"));
                    document.Add(new Paragraph($"Dirección Particular: {solicitud.DireccionParticular}"));

                    // Sección: Datos Laborales (centrado, subrayado y tamaño 11)
                    document.Add(new Paragraph("Datos Laborales")
                        .SetFontSize(11)
                        .SetBold()
                        .SetUnderline()
                        .SetTextAlignment(TextAlignment.CENTER));

                    document.Add(new Paragraph($"Empresa: {solicitud.Empresa}"));
                    document.Add(new Paragraph($"Dirección Laboral: {solicitud.DireccionLaboral}"));
                    document.Add(new Paragraph($"Teléfono Laboral: {solicitud.TelefonoLaboral}"));
                    document.Add(new Paragraph($"Antigüedad: {solicitud.AntiguedadAnios} años"));
                    document.Add(new Paragraph($"Aporta IPS: {(solicitud.AportaIPS ? "Sí" : "No")}"));
                    document.Add(new Paragraph($"Cantidad de Aportes: {solicitud.CantidadAportes}"));
                    document.Add(new Paragraph($"Salario: G. {solicitud.Salario:N0}"));

                    // Sección: Referencias Comerciales (centrado, subrayado y tamaño 11)
                    document.Add(new Paragraph("Referencias Comerciales")
                        .SetFontSize(11)
                        .SetBold()
                        .SetUnderline()
                        .SetTextAlignment(TextAlignment.CENTER));

                    foreach (var referencia in solicitud.ReferenciasComerciales)
                    {
                        // Nombre y Teléfono en una misma línea
                        var referenciaComercial = new Paragraph()
                            .Add(new Text($"Nombre del Local: {referencia.NombreLocal}   "))
                            .Add(new Text($"Teléfono: {referencia.Telefono}"));
                        document.Add(referenciaComercial);
                    }

                    // Sección: Referencias Personales (centrado, subrayado y tamaño 11)
                    document.Add(new Paragraph("Referencias Personales")
                        .SetFontSize(11)
                        .SetBold()
                        .SetUnderline()
                        .SetTextAlignment(TextAlignment.CENTER));

                    foreach (var referencia in solicitud.ReferenciasPersonales)
                    {
                        // Nombre y Teléfono en una misma línea
                        var referenciaPersonal = new Paragraph()
                            .Add(new Text($"Nombre: {referencia.Nombre}   "))
                            .Add(new Text($"Teléfono: {referencia.Telefono}"));
                        document.Add(referenciaPersonal);
                    }

                    // Cierra el documento
                    document.Close();
                }
            }
        }      
        catch (Exception ex)
        {
            throw new ServiceException("Ocurrió un error inesperado al generar documento pdf", ex);
        }
    }

    public async Task<List<ModeloMotosporCategoria>> ListarProductosConPlanesPromo()
    {
        //Obtener listado de modelos en promo
        var modelosEnPromo = await _repository.ListarProductosConPlanesPromo();

        //Obtenemos las fotos de los modelos en promo
        return await ObtenerModelosEnPromoPorCategoria(modelosEnPromo);
    }

    public async Task<List<ModeloMotosporCategoria>> ObtenerModelosEnPromoPorCategoria(List<ProductosDTOPromo> modelosPromo)
    {
        if (string.IsNullOrEmpty(_baseImagesPath))
        {
            throw new ArgumentNullException("El valor de la ruta base es nulo");
        }

        var rutaBase = _baseImagesPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // Obtén todas las carpetas de categorías excepto 'Promociones'
        var carpetasCategorias = Directory.GetDirectories(rutaBase)
                                          .Where(c => !c.EndsWith("Promociones", StringComparison.OrdinalIgnoreCase))
                                          .ToList();

        if (carpetasCategorias.Count == 0)
        {
            _logger.LogInformation("No se encontraron carpetas de categorías.");
            return null;
        }

        var modelos = new List<ModeloMotosporCategoria>();

        // Iteramos sobre todas las carpetas de categorías
        foreach (var categoriaPath in carpetasCategorias)
        {
            var subCarpetas = Directory.GetDirectories(categoriaPath);

            foreach (var subCarpeta in subCarpetas)
            {
                var nombreModelo = Path.GetFileName(subCarpeta); // El nombre del modelo es el nombre de la carpeta

                // Verificamos si el modelo obtenido del endpoint de promociones existe en esta categoría
                var modeloPromo = modelosPromo.FirstOrDefault(m => m.Modelo.Equals(nombreModelo, StringComparison.OrdinalIgnoreCase));

                if (modeloPromo != null)
                {
                    // Obtener la lista de todas las imágenes del modelo
                    var imagenes = ObtenerImagenesDeModeloenPromo(subCarpeta, categoriaPath);

                    if (imagenes == null || imagenes.Count == 0)
                    {
                        _logger.LogInformation($"No se encontraron imágenes para el modelo: {nombreModelo}");
                    }
                    else
                    {
                        _logger.LogInformation($"Se encontraron imágenes para el modelo: {nombreModelo}, URLs: {string.Join(", ", imagenes)}");

                        // Agregamos el modelo con las imágenes encontradas a la lista de retorno
                        modelos.Add(new ModeloMotosporCategoria
                        {
                            Nombre = nombreModelo,
                            Imagenes = imagenes // Asignar la lista de imágenes al modelo
                        });
                    }
                }
            }
        }

        if (modelos.Count == 0)
        {
            _logger.LogInformation("No se encontraron modelos con imágenes.");
            return null;
        }

        return modelos;
    }

    private List<string> ObtenerImagenesDeModelo(string modeloPath, string categoria)
    {
        var extensionesPermitidas = new[] { ".jpg", ".jpeg", ".png" };
        var archivos = Directory.GetFiles(modeloPath)
                                .Where(archivo => extensionesPermitidas.Contains(Path.GetExtension(archivo).ToLower()));

        var listaDeImagenes = new List<string>();

        foreach (var archivo in archivos)
        {
            var nombreArchivo = Path.GetFileName(archivo);

            // Obtener el nombre del modelo (nombre de la carpeta) y reemplazar caracteres especiales y espacios
            var nombreModelo = Path.GetFileName(modeloPath);
            var nombreCategoria = categoria;

            // Codificar los nombres de las carpetas y archivos para que sean válidos en una URL
            var nombreModeloEncoded = Uri.EscapeDataString(nombreModelo);
            var nombreCategoriaEncoded = Uri.EscapeDataString(nombreCategoria);
            var nombreArchivoEncoded = Uri.EscapeDataString(nombreArchivo);

            // Construir la URL codificada correctamente
            var imagenUrl = $"/uploads/{nombreCategoriaEncoded}/{nombreModeloEncoded}/{nombreArchivoEncoded}";

            // Agregar la URL a la lista de imágenes
            listaDeImagenes.Add(imagenUrl);
        }

        return listaDeImagenes;
    }
    private List<string> ObtenerImagenesDeModeloenPromo(string modeloPath, string categoriaPath)
    {
        var imagenes = new List<string>();

        // Buscamos todos los archivos de imagen en la carpeta del modelo
        var archivos = Directory.GetFiles(modeloPath, "*.*")
                                .Where(file => file.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                               file.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                               file.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                                .ToList();

        // Obtener el nombre de la categoría y del modelo
        var nombreCategoria = Path.GetFileName(categoriaPath);
        var nombreModelo = Path.GetFileName(modeloPath);

        // Codificar los nombres de la categoría y del modelo para que sean válidos en una URL
        var nombreCategoriaEncoded = Uri.EscapeDataString(nombreCategoria); // Codificar nombre de la categoría
        var nombreModeloEncoded = Uri.EscapeDataString(nombreModelo);       // Codificar nombre del modelo

        // Convertimos la ruta de los archivos a URLs o rutas accesibles para el frontend
        foreach (var archivo in archivos)
        {
            // Codificamos el nombre del archivo
            var nombreArchivo = Path.GetFileName(archivo);
            var nombreArchivoEncoded = Uri.EscapeDataString(nombreArchivo); // Codificar nombre del archivo

            // Construir la URL codificada correctamente
            var imagenUrl = $"/uploads/{nombreCategoriaEncoded}/{nombreModeloEncoded}/{nombreArchivoEncoded}";

            // Agregar la URL a la lista de imágenes
            imagenes.Add(imagenUrl);
        }

        return imagenes;
    }

    public async Task<ProductoDTOPromo> ObtenerProductoConPlanesPromo(string modelo)
    {        
        return await _repository.ObtenerProductoConPlanesPromo(modelo);       
    }

    public async Task<List<ImagenHomeCarrusel>> ObtenerImagenesDesdeHomeCarrusel()
    {
        if (string.IsNullOrEmpty(_baseImagesPath))
        {
            throw new ArgumentNullException("La ruta base de imágenes no está configurada.");
        }

        var homeCarruselPath = Path.Combine(_baseImagesPath, "HomeCarrusel");

        _logger.LogInformation($"Ruta de HomeCarrusel: {homeCarruselPath}");

        if (!Directory.Exists(homeCarruselPath))
        {
            _logger.LogInformation($"No se encontró la carpeta en la ruta: {homeCarruselPath}");
            throw new NoDataFoundException("La carpeta HomeCarrusel no existe o no contiene imágenes.");
        }

        var extensionesPermitidas = new[] { ".jpg", ".jpeg", ".png", ".gif" };
        var imagenes = Directory.GetFiles(homeCarruselPath)
                                .Where(file => extensionesPermitidas.Contains(Path.GetExtension(file).ToLower()))
                                .Select(file => new ImagenHomeCarrusel
                                {
                                    Nombre = Path.GetFileName(file),
                                    Url = $"/imagenes/homecarrusel/{Uri.EscapeDataString(Path.GetFileName(file))}" // URL relativa con nombre codificado
                                })
                                .ToList();

        if (imagenes.Count == 0)
        {
            throw new NoDataFoundException("No se encontraron imágenes en la carpeta HomeCarrusel.");
        }

        return imagenes;
    }







}





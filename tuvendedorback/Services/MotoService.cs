using iText.IO.Font;
using iText.Kernel.Colors;
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
using tuvendedorback.Wrappers;

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

        return resultado;
    }

    public async Task<byte[]> GenerarPdfSolicitud(SolicitudCredito solicitud, int idSolicitud)
    {
        try
        {
            using (var memoryStream = new MemoryStream())
            {
                string env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
                string fontPath = env == "Development"
                    ? "C:/Windows/Fonts/Arial.ttf"
                    : Path.Combine(Directory.GetCurrentDirectory(), "assets", "fonts", "calibri.ttf");

                var customFont = PdfFontFactory.CreateFont(fontPath, PdfEncodings.IDENTITY_H);

                using (var writer = new PdfWriter(memoryStream))
                {
                    using (var pdf = new PdfDocument(writer))
                    {
                        var document = new Document(pdf);
                        document.SetFont(customFont).SetFontSize(10);

                        // Título principal
                        document.Add(new Paragraph("Detalles de la Solicitud de Crédito")
                            .SetFontSize(16)
                            .SetBold()
                            .SetBackgroundColor(new DeviceRgb(144, 238, 144))
                            .SetTextAlignment(TextAlignment.CENTER)
                            .SetMarginBottom(15));

                        // Sección: Modelo Solicitado
                        document.Add(new Paragraph("Modelo Solicitado").SetFontSize(12).SetBold());
                        document.Add(new Paragraph(solicitud.ModeloSolicitado));

                        // Plan Solicitado
                        document.Add(new Paragraph("Plan Solicitado").SetFontSize(12).SetBold());
                        var planTable = new Table(UnitValue.CreatePercentArray(3)).UseAllAvailableWidth();
                        planTable.AddCell(new Cell().Add(new Paragraph("Entrega").SetBold()));
                        planTable.AddCell(new Cell().Add(new Paragraph("Cuotas").SetBold()));
                        planTable.AddCell(new Cell().Add(new Paragraph("Monto Cuota").SetBold()));
                        planTable.AddCell(new Cell().Add(new Paragraph($"G. {solicitud.EntregaInicial:N0}")));
                        planTable.AddCell(new Cell().Add(new Paragraph($"{solicitud.CantidadCuotas}")));
                        planTable.AddCell(new Cell().Add(new Paragraph($"G. {solicitud.MontoPorCuota:N0}")));
                        document.Add(planTable);

                        // Sección: Datos Personales
                        document.Add(new Paragraph("\nDatos Personales").SetFontSize(12).SetBold().SetUnderline());
                        var personalTable = new Table(UnitValue.CreatePercentArray(new float[] { 1, 1, 1 })).UseAllAvailableWidth();

                        // Primera Fila
                        personalTable.AddCell(new Cell().Add(new Paragraph("Nombre y Apellidos").SetBold()));
                        personalTable.AddCell(new Cell().Add(new Paragraph("Cédula").SetBold()));
                        personalTable.AddCell(new Cell().Add(new Paragraph("Teléfono Móvil").SetBold()));
                        personalTable.AddCell(new Cell().Add(new Paragraph(solicitud.NombresApellidos)));
                        personalTable.AddCell(new Cell().Add(new Paragraph(solicitud.CedulaIdentidad)));
                        personalTable.AddCell(new Cell().Add(new Paragraph(solicitud.TelefonoMovil)));

                        // Segunda Fila
                        personalTable.AddCell(new Cell().Add(new Paragraph("Fecha de Nacimiento").SetBold()));
                        personalTable.AddCell(new Cell().Add(new Paragraph("Barrio").SetBold()));
                        personalTable.AddCell(new Cell().Add(new Paragraph("Ciudad").SetBold()));
                        personalTable.AddCell(new Cell().Add(new Paragraph(solicitud.FechaNacimiento.ToString("dd/MM/yyyy"))));
                        personalTable.AddCell(new Cell().Add(new Paragraph(solicitud.Barrio)));
                        personalTable.AddCell(new Cell().Add(new Paragraph(solicitud.Ciudad)));

                        // Tercera Fila
                        personalTable.AddCell(new Cell(1, 3).Add(new Paragraph("Dirección Particular").SetBold()));
                        personalTable.AddCell(new Cell(1, 3).Add(new Paragraph(solicitud.DireccionParticular)));
                        document.Add(personalTable);

                        // Sección: Datos Laborales
                        document.Add(new Paragraph("\nDatos Laborales").SetFontSize(12).SetBold().SetUnderline());
                        var laboralesTable = new Table(UnitValue.CreatePercentArray(new float[] { 1, 1, 1 })).UseAllAvailableWidth();
                        laboralesTable.AddCell(new Cell().Add(new Paragraph("Empresa").SetBold()));
                        laboralesTable.AddCell(new Cell().Add(new Paragraph("Dirección Laboral").SetBold()));
                        laboralesTable.AddCell(new Cell().Add(new Paragraph("Teléfono Laboral").SetBold()));
                        laboralesTable.AddCell(new Cell().Add(new Paragraph(solicitud.Empresa)));
                        laboralesTable.AddCell(new Cell().Add(new Paragraph(solicitud.DireccionLaboral)));
                        laboralesTable.AddCell(new Cell().Add(new Paragraph(solicitud.TelefonoLaboral)));
                        laboralesTable.AddCell(new Cell().Add(new Paragraph("Antigüedad (Años)").SetBold()));
                        laboralesTable.AddCell(new Cell().Add(new Paragraph("Aporta IPS").SetBold()));
                        laboralesTable.AddCell(new Cell().Add(new Paragraph("Cantidad de Aportes").SetBold()));
                        laboralesTable.AddCell(new Cell().Add(new Paragraph($"{solicitud.AntiguedadAnios}")));
                        laboralesTable.AddCell(new Cell().Add(new Paragraph(solicitud.AportaIPS ? "Sí" : "No")));
                        laboralesTable.AddCell(new Cell().Add(new Paragraph($"{solicitud.CantidadAportes}")));
                        laboralesTable.AddCell(new Cell().Add(new Paragraph("Salario Percibido").SetBold()));
                        laboralesTable.AddCell(new Cell(2, 1).Add(new Paragraph($"G. {solicitud.Salario:N0}")));
                        document.Add(laboralesTable);

                        // Sección: Referencias Personales
                        document.Add(new Paragraph("\nReferencias Personales").SetFontSize(12).SetBold().SetUnderline());
                        var referenciasPersonalesTable = new Table(UnitValue.CreatePercentArray(new float[] { 1, 1 })).UseAllAvailableWidth();
                        for (int i = 0; i < solicitud.ReferenciasPersonales.Count; i++)
                        {
                            var refPersonal = solicitud.ReferenciasPersonales[i];
                            referenciasPersonalesTable.AddCell(new Cell().Add(new Paragraph($"Nombre {i + 1}: {refPersonal.Nombre}")));
                            referenciasPersonalesTable.AddCell(new Cell().Add(new Paragraph($"Teléfono: {refPersonal.Telefono}")));
                        }
                        document.Add(referenciasPersonalesTable);

                        // Sección: Referencias Comerciales
                        document.Add(new Paragraph("\nReferencias Comerciales").SetFontSize(12).SetBold().SetUnderline());
                        var referenciasComercialesTable = new Table(UnitValue.CreatePercentArray(new float[] { 1, 1 })).UseAllAvailableWidth();
                        for (int i = 0; i < solicitud.ReferenciasComerciales.Count; i++)
                        {
                            var refComercial = solicitud.ReferenciasComerciales[i];
                            referenciasComercialesTable.AddCell(new Cell().Add(new Paragraph($"Nombre Local {i + 1}: {refComercial.NombreLocal}")));
                            referenciasComercialesTable.AddCell(new Cell().Add(new Paragraph($"Teléfono: {refComercial.Telefono}")));
                        }
                        document.Add(referenciasComercialesTable);

                        document.Close();
                    }
                }

                return memoryStream.ToArray(); // Retorna el PDF como un arreglo de bytes
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

    public async Task<List<string>> ObtenerImagenesPorModelo(string nombreModelo)
    {
        // Usar el path base de las imágenes (por ejemplo: "C:/ImagenesMotos")
        string rutaBase = _baseImagesPath;

        // Obtener las carpetas de las categorías en la ruta base
        var categorias = Directory.GetDirectories(rutaBase);

        // Extensiones de archivos de imagen permitidas
        var extensionesPermitidas = new[] { ".jpg", ".jpeg", ".png", ".gif" };

        // Lista para almacenar las URLs de las imágenes encontradas
        var imagenes = new List<string>();

        // Iterar sobre cada categoría para buscar el modelo específico
        foreach (var categoria in categorias)
        {
            // Construir la ruta de la subcarpeta del modelo dentro de cada categoría
            var rutaModelo = Path.Combine(categoria, nombreModelo);

            // Verificar si existe una carpeta para el modelo dentro de esta categoría
            if (Directory.Exists(rutaModelo))
            {
                // Obtener archivos de imagen en la carpeta del modelo
                var archivos = Directory.GetFiles(rutaModelo)
                                        .Where(file => extensionesPermitidas.Contains(Path.GetExtension(file).ToLower()));

                // Construir URLs para cada archivo de imagen y agregar a la lista
                foreach (var archivo in archivos)
                {
                    var nombreCategoria = Path.GetFileName(categoria); // Obtener el nombre de la categoría
                    var nombreArchivo = Path.GetFileName(archivo); // Obtener el nombre del archivo de imagen

                    // Construir la URL relativa con encoding para que sea compatible con URLs
                    var urlImagen = $"/imagenes_motos/{Uri.EscapeDataString(nombreCategoria)}/{Uri.EscapeDataString(nombreModelo)}/{Uri.EscapeDataString(nombreArchivo)}";

                    imagenes.Add(urlImagen);
                }

                // Detener la búsqueda en categorías adicionales, ya que el modelo se encontró
                break;
            }
        }

        // Validar si no se encontraron imágenes y lanzar excepción
        if (imagenes.Count == 0)
        {
            throw new NoDataFoundException("No se encontraron imágenes para el modelo especificado.");
        }

        return imagenes;
    }

    public async Task RegistrarVisitaAsync(string page)
    {
        await _repository.RegistrarVisitaAsync(page);
    }

    public async Task<List<string>> GuardarDocumentosAdjuntos(List<IFormFile> archivos, string cedulaCliente)
    {
        // Ruta base donde se almacenarán los documentos adjuntos
        string rutaDocumentosAdjuntos = Path.Combine(_baseImagesPath, "DocumentosAdjuntos");

        // Crear la carpeta de documentos adjuntos si no existe
        Directory.CreateDirectory(rutaDocumentosAdjuntos);

        // Lista para almacenar las rutas de los archivos guardados
        var rutasGuardadas = new List<string>();

        // Iterar sobre cada archivo adjunto
        foreach (var archivo in archivos)
        {
            // Generar un nombre único para el archivo usando la cédula del cliente y un GUID
            string nombreArchivo = $"{cedulaCliente}_{Guid.NewGuid()}{Path.GetExtension(archivo.FileName)}";
            string rutaArchivo = Path.Combine(rutaDocumentosAdjuntos, nombreArchivo);

            // Guardar el archivo en el sistema de archivos
            using (var stream = new FileStream(rutaArchivo, FileMode.Create))
            {
                await archivo.CopyToAsync(stream);
            }

            // Agregar la ruta del archivo guardado a la lista
            rutasGuardadas.Add(rutaArchivo);
        }

        return rutasGuardadas;
    }
    public async Task<Datos<IEnumerable<SolicitudesdeCreditoDTO>>> ObtenerSolicitudesCredito(SolicitudCreditoRequest request)
    {
        return await _repository.ObtenerSolicitudesCredito(request);
    }

    public async Task<CreditoSolicitudDetalleDto> ObtenerDetalleCreditoSolicitudAsync(int id)
    {
        return await _repository.ObtenerDetalleCreditoSolicitudAsync(id);
    }

    public async Task<bool> ActualizarSolicitudCredito(int idSolicitud, SolicitudCredito solicitud)
    {
        return await _repository.ActualizarSolicitudCredito(idSolicitud, solicitud);    
    }
}





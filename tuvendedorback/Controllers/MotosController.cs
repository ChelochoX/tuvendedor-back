using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using System.ComponentModel;
using tuvendedorback.Exceptions;
using tuvendedorback.Models;
using tuvendedorback.Request;
using tuvendedorback.Services.Interfaces;

namespace tuvendedorback.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MotosController : ControllerBase
{
    private readonly IMotoService _motoService;

    public MotosController(IMotoService motoService)
    {
        _motoService = motoService;
    }

    [HttpGet("modelo/{categoria}")]
    [SwaggerOperation(
    Summary = "Permite obtener los modelos que pertenecen a esta categoria",
    Description = "Obtener los modelos que pertenecen a esta categoria")]
    public async Task<IActionResult> ObtenerModelosPorCategoriaAsync(
    [FromRoute][Description("Propiedad que determina la categoria al que pertenece la moto")]
    string categoria)
    {
        var modelos = await _motoService.ObtenerModelosPorCategoriaAsync(categoria);

        if (modelos == null || !modelos.Any())
        {
            return NotFound(new { mensaje = "Categoría no encontrada o no tiene modelos" });
        }

        return Ok(modelos);
    }


    [HttpGet("producto/{modelo}")]
    [SwaggerOperation(
    Summary = "Permite obtener los productos y sus planes que pertenecen a este modelo",
    Description = "Obtener los productos y planes que pertenecen a este modelo")]
    public async Task<IActionResult> ObtenerProductosPorModeloAsync(
    [FromRoute][Description("Propiedad que determina el modelo del producto")]
    string modelo)
    {
        if (string.IsNullOrWhiteSpace(modelo))
        {
            return BadRequest(new { mensaje = "El parámetro modelo no puede estar vacío" });
        }
        var producto = await _motoService.ObtenerProductoConPlanes(modelo);  

        return Ok(producto);
    }


    [HttpPost("producto/calcularcuota")]
    [SwaggerOperation(
    Summary = "Calcula el monto de la cuota para un modelo, basado en una entrega inicial y la cantidad de cuotas.",
    Description = "Permite calcular el monto de la cuota para un modelo específico, utilizando una entrega inicial y la cantidad de cuotas especificadas.")]
    public async Task<IActionResult> CalcularCuotaProductoAsync(
    [FromBody] CalculoCuotaRequest request)
    {
        // Validamos que el modelo no esté vacío
        if (string.IsNullOrWhiteSpace(request.ModeloSolicitado))
        {
            return BadRequest(new { mensaje = "El campo modelo no puede estar vacío" });
        }

        // Validamos que la entrega inicial sea un número positivo
        if (request.EntregaInicial < 0)
        {
            return BadRequest(new { mensaje = "La entrega inicial no puede ser negativa" });
        }       
  
        var montoCuota = await _motoService.ObtenerMontoCuotaConEntregaMayor(request);

        return Ok(montoCuota);       
    }


    [HttpPost("solicitudcredito")]
    [SwaggerOperation(
    Summary = "Permite Crear la solicitud de credito en la base de datos",
    Description = "Permitir Crear la solicitud de credito en la base de datos")]
    public async Task<IActionResult> CrearSolicitud(
    [FromBody][Description("Propiedad necesarias para crear la solicitud de credito")] SolicitudCredito solicitud)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }
       
        var solicitudId = await _motoService.GuardarSolicitudCredito(solicitud);
        return Ok(new { message = "Solicitud creada exitosamente", solicitudId });         
                
    }


    [HttpGet("listarproductopromo")]
    [SwaggerOperation(
    Summary = "Permite obtener los productos y sus planes que pertenecen a este modelo que esta en promocion",
    Description = "Obtener los productos y planes que pertenecen a este modelo que esta en promocion")]
    public async Task<IActionResult> ListarProductosPorModeloenPromo()
    {      
        var producto = await _motoService.ListarProductosConPlanesPromo();

        return Ok(producto);
    }


    [HttpGet("productopromo/{modelo}")]
    [SwaggerOperation(
    Summary = "Permite obtener el producto y su plan que pertenecen a este modelo que esta en promocion",
    Description = "Obtener el producto y su plan que pertenecen a este modelo que esta en promocion")]
    public async Task<IActionResult> ObtenerProductosPorModeloenPromo(
    [FromRoute][Description("Propiedad que determina el modelo del producto")]
    string modelo)
    {
        var producto = await _motoService.ObtenerProductoConPlanesPromo(modelo);

        return Ok(producto);
    }


    [HttpGet("homecarrusel/imagenes")]
    [SwaggerOperation(
    Summary = "Permite obtener todas las imágenes del carrusel",
    Description = "Obtiene una lista de nombres de las imágenes en la carpeta HomeCarrusel para mostrarlas en el carrusel de la página principal")]
    public async Task<IActionResult> ObtenerImagenesCarrusel()
    {
        var imagenes = await _motoService.ObtenerImagenesDesdeHomeCarrusel();      

        return Ok(imagenes);
    }


    [HttpGet("modelo/{nombreModelo}/imagenes")]
    [SwaggerOperation(
    Summary = "Obtiene las imágenes de un modelo específico",
    Description = "Permite obtener todas las imágenes de un modelo especificado por su nombre")]
    public async Task<IActionResult> ObtenerImagenesPorModelo(
    [FromRoute][Description("Nombre del modelo")] string nombreModelo)
    {
       
        if (string.IsNullOrWhiteSpace(nombreModelo))
        {
            return BadRequest(new { Message = "El nombre del modelo no puede ser nulo o vacío." });
        }

        var imagenes = await _motoService.ObtenerImagenesPorModelo(nombreModelo);
        return Ok(imagenes);
       
    }


    [HttpPost("registrarvisita")]
    [SwaggerOperation(
    Summary = "Registra una visita para una página específica",
    Description = "Registra una visita a la página especificada en el cuerpo de la solicitud")]
    public async Task<IActionResult> RegistrarVisita(
    [FromQuery][Description("Nombre de la página visitada")] string page)
    {
        if (string.IsNullOrWhiteSpace(page))
        {
            throw new ReglasdeNegocioException("El nombre de la página no puede ser nulo o vacío.");
        }

        await _motoService.RegistrarVisitaAsync(page);
        return Ok(new { Message = "Visita registrada exitosamente" });
    }


    [HttpPost("guardardocumentos")]
    [SwaggerOperation(
    Summary = "Sube documentos adjuntos para un cliente",
    Description = "Recibe archivos adjuntos y los guarda en el servidor, renombrándolos con la cédula del cliente.")]
    public async Task<IActionResult> SubirDocumentos(
    [FromQuery][Description("Cédula del cliente")] string cedula,
    [FromForm][Description("Archivos adjuntos")] List<IFormFile> archivos)
    {
        if (string.IsNullOrWhiteSpace(cedula))
        {
            throw new ReglasdeNegocioException("La cédula del cliente no puede ser nula o vacía.");
        }

        if (archivos == null || !archivos.Any())
        {
            throw new ReglasdeNegocioException("Debe adjuntar al menos un archivo.");
        }

        var rutasGuardadas = await _motoService.GuardarDocumentosAdjuntos(archivos, cedula);
        return Ok(new { Message = "Documentos subidos exitosamente", Rutas = rutasGuardadas });
    }



}

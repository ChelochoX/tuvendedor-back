using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using System.ComponentModel;
using tuvendedorback.DTOs;
using tuvendedorback.Exceptions;
using tuvendedorback.Models;
using tuvendedorback.Request;
using tuvendedorback.Services.Interfaces;
using tuvendedorback.Wrappers;

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


    [HttpPost("solicitudcredito/generarpdf")]
    [SwaggerOperation(
    Summary = "Genera el PDF de una solicitud de crédito",
    Description = "Genera un documento PDF con los datos de la solicitud de crédito específica")]
    public async Task<IActionResult> GenerarPdfSolicitud(
    [FromBody][Description("Datos necesarios para generar el PDF de la solicitud de crédito")] SolicitudCredito solicitud,
    [FromQuery][Description("ID de la solicitud de crédito")] int idSolicitud)
    {
        // Llama al servicio para generar el PDF y obtener los bytes
        var archivoBytes = await _motoService.GenerarPdfSolicitud(solicitud,idSolicitud);
        var nombreArchivo = $"Solicitud_{idSolicitud}.pdf";

        // Retorna el archivo PDF como respuesta
        return File(archivoBytes, "application/pdf", nombreArchivo);
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


    [HttpGet("obtener-solicitudes-credito")]
    [SwaggerOperation(
     Summary = "Obtiene una lista de solicitudes de crédito con filtros y paginación",
     Description = "Permite obtener una lista de solicitudes de crédito aplicando filtros por modelo, fecha de creación y búsqueda avanzada, con soporte de paginación")]
    public async Task<IActionResult> ObtenerSolicitudesCredito(
    [FromQuery][Description("Parámetros para filtrar y paginar las solicitudes de crédito")] SolicitudCreditoRequest request)
    {
        var validationResult = new SolicitudCreditoRequestValidator().Validate(request);

        if (!validationResult.IsValid)
        {
            return BadRequest(new Response<object>
            {
                Success = false,
                Errors = validationResult.Errors.Select(e => e.ErrorMessage).ToList()
            });
        }

        var resultado = await _motoService.ObtenerSolicitudesCredito(request);
        return Ok(resultado);        
         
    }


    [HttpGet("obtener-detalle-solicitud-credito/{id}")]
    [SwaggerOperation(
    Summary = "Obtiene el detalle completo de una solicitud de crédito por ID",
    Description = "Permite obtener todos los datos asociados a una solicitud de crédito específica, incluyendo datos personales, laborales y referencias comerciales y personales.")]
    public async Task<IActionResult> ObtenerDetalleSolicitudCredito(
    [FromRoute][Description("ID de la solicitud de crédito a obtener")] int id)
    {
        if (id <= 0)
        {
            return BadRequest(new Response<object>
            {
                Success = false,
                Errors = new List<string> { "El ID de la solicitud debe ser un número positivo." }
            });
        }
        
        var resultado = await _motoService.ObtenerDetalleCreditoSolicitudAsync(id);           

        return Ok(new Response<CreditoSolicitudDetalleDto>
        {
            Success = true,
            Data = resultado
        });       
    }

    [HttpPut("solicitudcredito/actualizar/{id}")]
    [SwaggerOperation(
    Summary = "Actualiza una solicitud de crédito",
    Description = "Actualiza los datos de una solicitud de crédito específica")]
    public async Task<IActionResult> ActualizarSolicitudCredito(int id, [FromBody] SolicitudCredito solicitud)
    {
        var resultado = await _motoService.ActualizarSolicitudCredito(id, solicitud);

        if (resultado)
        {
            return Ok(new { message = "Solicitud de crédito actualizada exitosamente" });
        }
        else
        {
            return BadRequest(new { message = "No se pudo actualizar la solicitud de crédito" });
        }
    }


    [HttpGet("obtener-estadisticas-acceso")]
    [SwaggerOperation(
    Summary = "Obtiene las estadísticas de acceso por página",
    Description = "Permite obtener la cantidad de accesos y la última fecha de visita para cada página registrada, o filtrar por el nombre de una página específica.")]
    public async Task<IActionResult> ObtenerEstadisticasAcceso()
    {    
        
        var resultado = await _motoService.ObtenerEstadisticasDeAcceso();

        return Ok(new Response<IEnumerable<VisitaPagina>>
        {
            Success = true,
            Data = resultado
        });                
    }


    [HttpGet("obtener-estadisticas-creditos")]
    [SwaggerOperation(
    Summary = "Obtiene estadísticas de créditos",
    Description = "Permite obtener la cantidad total de créditos cargados, cantidad de créditos por modelo, por mes y por modelo en cada mes.")]
    public async Task<IActionResult> ObtenerEstadisticasCreditos()
    {     
        var resultado = await _motoService.ObtenerEstadisticasCreditos();

        return Ok(new Response<IEnumerable<CreditoEstadisticasDto>>
        {
            Success = true,
            Data = resultado
        });       
    }
}

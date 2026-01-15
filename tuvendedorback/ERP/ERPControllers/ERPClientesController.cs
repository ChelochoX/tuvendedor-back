using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using tuvendedorback.Common;
using tuvendedorback.ERP.ERPRequest;
using tuvendedorback.ERP.ERPServices.Interfaces;
using tuvendedorback.Wrappers;

namespace tuvendedorback.ERP.ERPControllers;

[ApiController]
[Route("api/erp/clientes")]
public class ERPClientesController : ControllerBase
{
    private readonly IERPClienteService _service;
    private readonly UserContext _userContext;

    public ERPClientesController(
        IERPClienteService service,
        UserContext userContext)
    {
        _service = service;
        _userContext = userContext;
    }

    [HttpPost("crear")]
    [SwaggerOperation(
        Summary = "Crea un cliente ERP",
        Description = "Crea un nuevo cliente en el módulo ERP. " +
                      "El cliente puede estar vinculado opcionalmente a un interesado del CRM.")]
    public async Task<IActionResult> CrearCliente(
        [FromBody] CrearERPClienteRequest request)
    {
        var clienteId = await _service.CrearCliente(
            request,
            _userContext.IdUsuario
        );

        return Ok(new Response<object>
        {
            Success = true,
            Message = "Cliente creado correctamente",
            Data = new { ClienteId = clienteId }
        });
    }

    [HttpGet("listar")]
    [SwaggerOperation(
        Summary = "Lista clientes ERP",
        Description = "Devuelve el listado de clientes activos registrados en el ERP.")]
    public async Task<IActionResult> ObtenerClientes()
    {
        var clientes = await _service.ObtenerClientes();

        return Ok(new Response<object>
        {
            Success = true,
            Data = clientes,
            Message = "Clientes obtenidos correctamente"
        });
    }

    [HttpGet("{clienteId}")]
    [SwaggerOperation(
        Summary = "Obtiene un cliente por ID",
        Description = "Devuelve la información de un cliente ERP según su identificador.")]
    public async Task<IActionResult> ObtenerPorId(int clienteId)
    {
        var cliente = await _service.ObtenerPorId(clienteId);

        if (cliente == null)
        {
            return NotFound(new Response<object>
            {
                Success = false,
                Message = "Cliente no encontrado"
            });
        }

        return Ok(new Response<object>
        {
            Success = true,
            Data = cliente
        });
    }

    [HttpPut("actualizar")]
    [SwaggerOperation(
        Summary = "Actualiza un cliente ERP",
        Description = "Actualiza los datos principales de un cliente ERP.")]
    public async Task<IActionResult> ActualizarCliente(
        [FromBody] ActualizarERPClienteRequest request)
    {
        await _service.ActualizarCliente(
            request,
            _userContext.IdUsuario
        );

        return Ok(new Response<object>
        {
            Success = true,
            Message = "Cliente actualizado correctamente"
        });
    }

    [HttpDelete("{clienteId}")]
    [SwaggerOperation(
        Summary = "Desactiva un cliente ERP",
        Description = "Desactiva un cliente ERP (baja lógica). El registro no se elimina físicamente.")]
    public async Task<IActionResult> DesactivarCliente(int clienteId)
    {
        await _service.DesactivarCliente(
            clienteId,
            _userContext.IdUsuario
        );

        return Ok(new Response<object>
        {
            Success = true,
            Message = "Cliente desactivado correctamente"
        });
    }

    [HttpGet("{clienteId}/direcciones")]
    [SwaggerOperation(
        Summary = "Obtiene las direcciones del cliente",
        Description = "Devuelve todas las direcciones asociadas a un cliente ERP, " +
                      "ordenadas por dirección principal y fecha de creación.")]
    public async Task<IActionResult> ObtenerDirecciones(int clienteId)
    {
        var direcciones = await _service.ObtenerDirecciones(clienteId);

        return Ok(new Response<object>
        {
            Success = true,
            Data = direcciones
        });
    }

    [HttpPost("direcciones")]
    [SwaggerOperation(
        Summary = "Agrega una dirección al cliente",
        Description = "Permite agregar una nueva dirección (Casa, Trabajo, Entrega, etc.) " +
                      "a un cliente del ERP.")]
    public async Task<IActionResult> AgregarDireccion(
        [FromBody] AgregarDireccionClienteRequest request)
    {
        await _service.AgregarDireccion(request);

        return Ok(new Response<object>
        {
            Success = true,
            Message = "Dirección agregada correctamente"
        });
    }

    [HttpPut("direcciones")]
    [SwaggerOperation(
        Summary = "Actualiza una dirección del cliente",
        Description = "Actualiza los datos de una dirección existente asociada a un cliente ERP.")]
    public async Task<IActionResult> ActualizarDireccion(
        [FromBody] ActualizarDireccionClienteRequest request)
    {
        await _service.ActualizarDireccion(request);

        return Ok(new Response<object>
        {
            Success = true,
            Message = "Dirección actualizada correctamente"
        });
    }

    [HttpDelete("direcciones/{direccionId}")]
    [SwaggerOperation(
        Summary = "Elimina una dirección del cliente",
        Description = "Elimina una dirección específica asociada a un cliente ERP.")]
    public async Task<IActionResult> EliminarDireccion(int direccionId)
    {
        await _service.EliminarDireccion(direccionId);

        return Ok(new Response<object>
        {
            Success = true,
            Message = "Dirección eliminada correctamente"
        });
    }
}

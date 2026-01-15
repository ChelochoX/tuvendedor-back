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

    public ERPClientesController(IERPClienteService service, UserContext userContext)
    {
        _service = service;
        _userContext = userContext;
    }

    [HttpPost("crear")]
    [SwaggerOperation(
       Summary = "Crea un cliente ERP",
       Description = "Crea un nuevo cliente en el módulo ERP. " +
                     "Puede estar vinculado opcionalmente a un interesado del CRM."
   )]
    public async Task<IActionResult> CrearCliente(
       [FromBody] CrearERPClienteRequest request)
    {
        var clienteId = await _service.CrearCliente(request, _userContext.IdUsuario);

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
        Description = "Devuelve el listado de clientes activos registrados en el ERP."
    )]
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

    [HttpPost("agregar-direccion")]
    [SwaggerOperation(
        Summary = "Agrega una dirección a un cliente",
        Description = "Permite agregar una dirección (Casa, Trabajo, Entrega, etc.) " +
                      "a un cliente del ERP."
    )]
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
}

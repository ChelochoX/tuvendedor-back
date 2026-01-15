using Microsoft.AspNetCore.Mvc;
using tuvendedorback.Common;
using tuvendedorback.ERP.ERPServices.Interfaces;

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


}

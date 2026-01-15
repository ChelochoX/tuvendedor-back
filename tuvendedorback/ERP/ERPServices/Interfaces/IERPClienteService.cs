using tuvendedorback.ERP.ERPDTOs;
using tuvendedorback.ERP.ERPRequest;

namespace tuvendedorback.ERP.ERPServices.Interfaces;

public interface IERPClienteService
{
    Task<int> CrearCliente(CrearERPClienteRequest request, int? usuarioId);
    Task<IEnumerable<ERPClienteDto>> ObtenerClientes();
    Task AgregarDireccion(AgregarDireccionClienteRequest request);
}

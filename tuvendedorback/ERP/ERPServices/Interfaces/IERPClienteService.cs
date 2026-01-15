using tuvendedorback.ERP.ERPDTOs;
using tuvendedorback.ERP.ERPRequest;

namespace tuvendedorback.ERP.ERPServices.Interfaces;

public interface IERPClienteService
{
    Task<int> CrearCliente(CrearERPClienteRequest request, int? usuarioId);
    Task<IEnumerable<ERPClienteDto>> ObtenerClientes();

    Task<ERPClienteDto?> ObtenerPorId(int clienteId);
    Task ActualizarCliente(ActualizarERPClienteRequest request, int? usuarioId);
    Task DesactivarCliente(int clienteId, int? usuarioId);

    Task<IEnumerable<ERPClienteDireccionDto>> ObtenerDirecciones(int clienteId);
    Task AgregarDireccion(AgregarDireccionClienteRequest request);
    Task ActualizarDireccion(ActualizarDireccionClienteRequest request);
    Task EliminarDireccion(int direccionId);
}

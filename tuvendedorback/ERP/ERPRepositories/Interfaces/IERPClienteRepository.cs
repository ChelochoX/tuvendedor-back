using tuvendedorback.ERP.ERPModels;

namespace tuvendedorback.ERP.ERPRepositories.Interfaces;

public interface IERPClienteRepository
{
    Task<int> CrearCliente(ERPCliente cliente);
    Task<IEnumerable<ERPCliente>> ObtenerClientes();
    Task<ERPCliente?> ObtenerPorId(int clienteId);
    Task AgregarDireccion(ERPClienteDireccion direccion);
}

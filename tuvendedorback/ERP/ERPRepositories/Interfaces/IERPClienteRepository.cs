using tuvendedorback.ERP.ERPModels;

namespace tuvendedorback.ERP.ERPRepositories.Interfaces;

public interface IERPClienteRepository
{
    Task<int> CrearCliente(ERPCliente cliente);
    Task<IEnumerable<ERPCliente>> ObtenerClientes();
    Task<ERPCliente?> ObtenerPorId(int clienteId);

    Task ActualizarCliente(ERPCliente cliente);
    Task DesactivarCliente(int clienteId, int? usuarioId);

    Task<IEnumerable<ERPClienteDireccion>> ObtenerDirecciones(int clienteId);
    Task AgregarDireccion(ERPClienteDireccion direccion);
    Task ActualizarDireccion(ERPClienteDireccion direccion);
    Task EliminarDireccion(int direccionId);
}

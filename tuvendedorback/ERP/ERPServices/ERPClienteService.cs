using tuvendedorback.ERP.ERPDTOs;
using tuvendedorback.ERP.ERPModels;
using tuvendedorback.ERP.ERPRepositories.Interfaces;
using tuvendedorback.ERP.ERPRequest;
using tuvendedorback.ERP.ERPServices.Interfaces;

namespace tuvendedorback.ERP.ERPServices;

public class ERPClienteService : IERPClienteService
{
    private readonly IERPClienteRepository _repo;

    public ERPClienteService(IERPClienteRepository repo)
    {
        _repo = repo;
    }

    public async Task<int> CrearCliente(CrearERPClienteRequest request, int? usuarioId)
    {
        var cliente = new ERPCliente
        {
            TipoDocumento = request.TipoDocumento,
            NumeroDocumento = request.NumeroDocumento,
            RazonSocial = request.RazonSocial,
            NombreFantasia = request.NombreFantasia,
            Telefono = request.Telefono,
            Email = request.Email,
            CRMInteresadoId = request.CRMInteresadoId,
            UsuarioCreacion = usuarioId
        };

        return await _repo.CrearCliente(cliente);
    }

    public async Task<IEnumerable<ERPClienteDto>> ObtenerClientes()
    {
        var clientes = await _repo.ObtenerClientes();

        return clientes.Select(c => new ERPClienteDto
        {
            ClienteId = c.ClienteId,
            RazonSocial = c.RazonSocial,
            TipoDocumento = c.TipoDocumento,
            NumeroDocumento = c.NumeroDocumento,
            Telefono = c.Telefono,
            Email = c.Email,
            Activo = c.Activo
        });
    }

    public async Task AgregarDireccion(AgregarDireccionClienteRequest request)
    {
        var direccion = new ERPClienteDireccion
        {
            ClienteId = request.ClienteId,
            TipoDireccion = request.TipoDireccion,
            Direccion = request.Direccion,
            Ciudad = request.Ciudad,
            Latitud = request.Latitud,
            Longitud = request.Longitud,
            EsPrincipal = request.EsPrincipal
        };

        await _repo.AgregarDireccion(direccion);
    }
}

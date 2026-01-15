using tuvendedorback.Common;
using tuvendedorback.ERP.ERPDTOs;
using tuvendedorback.ERP.ERPModels;
using tuvendedorback.ERP.ERPRepositories.Interfaces;
using tuvendedorback.ERP.ERPRequest;
using tuvendedorback.ERP.ERPServices.Interfaces;

namespace tuvendedorback.ERP.ERPServices;

public class ERPClienteService : IERPClienteService
{
    private readonly IERPClienteRepository _repo;
    private readonly IServiceProvider _serviceProvider;

    public ERPClienteService(
        IERPClienteRepository repo,
        IServiceProvider serviceProvider)
    {
        _repo = repo;
        _serviceProvider = serviceProvider;
    }

    public async Task<int> CrearCliente(CrearERPClienteRequest request, int? usuarioId)
    {
        await ValidationHelper.ValidarAsync(request, _serviceProvider);

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
            TipoDocumento = c.TipoDocumento,
            NumeroDocumento = c.NumeroDocumento,
            RazonSocial = c.RazonSocial,
            Telefono = c.Telefono,
            Email = c.Email,
            Activo = c.Activo
        });
    }

    public async Task<ERPClienteDto?> ObtenerPorId(int clienteId)
    {
        var cliente = await _repo.ObtenerPorId(clienteId);
        if (cliente == null) return null;

        return new ERPClienteDto
        {
            ClienteId = cliente.ClienteId,
            TipoDocumento = cliente.TipoDocumento,
            NumeroDocumento = cliente.NumeroDocumento,
            RazonSocial = cliente.RazonSocial,
            Telefono = cliente.Telefono,
            Email = cliente.Email,
            Activo = cliente.Activo
        };
    }

    public async Task ActualizarCliente(
        ActualizarERPClienteRequest request,
        int? usuarioId)
    {
        await ValidationHelper.ValidarAsync(request, _serviceProvider);

        await _repo.ActualizarCliente(new ERPCliente
        {
            ClienteId = request.ClienteId,
            RazonSocial = request.RazonSocial,
            NombreFantasia = request.NombreFantasia,
            Telefono = request.Telefono,
            Email = request.Email,
            UsuarioModificacion = usuarioId
        });
    }

    public async Task DesactivarCliente(int clienteId, int? usuarioId)
    {
        if (clienteId <= 0)
            throw new ArgumentException("ClienteId inválido");

        await _repo.DesactivarCliente(clienteId, usuarioId);
    }

    public async Task<IEnumerable<ERPClienteDireccionDto>> ObtenerDirecciones(int clienteId)
    {
        var direcciones = await _repo.ObtenerDirecciones(clienteId);

        return direcciones.Select(d => new ERPClienteDireccionDto
        {
            DireccionId = d.DireccionId,
            ClienteId = d.ClienteId,
            TipoDireccion = d.TipoDireccion,
            Direccion = d.Direccion,
            Ciudad = d.Ciudad,
            Latitud = d.Latitud,
            Longitud = d.Longitud,
            EsPrincipal = d.EsPrincipal,
            FechaCreacion = d.FechaCreacion
        });
    }

    public async Task AgregarDireccion(AgregarDireccionClienteRequest request)
    {
        await ValidationHelper.ValidarAsync(request, _serviceProvider);

        await _repo.AgregarDireccion(new ERPClienteDireccion
        {
            ClienteId = request.ClienteId,
            TipoDireccion = request.TipoDireccion,
            Direccion = request.Direccion,
            Ciudad = request.Ciudad,
            Latitud = request.Latitud,
            Longitud = request.Longitud,
            EsPrincipal = request.EsPrincipal
        });
    }

    public async Task ActualizarDireccion(ActualizarDireccionClienteRequest request)
    {
        await ValidationHelper.ValidarAsync(request, _serviceProvider);

        await _repo.ActualizarDireccion(new ERPClienteDireccion
        {
            DireccionId = request.DireccionId,
            TipoDireccion = request.TipoDireccion,
            Direccion = request.Direccion,
            Ciudad = request.Ciudad,
            Latitud = request.Latitud,
            Longitud = request.Longitud,
            EsPrincipal = request.EsPrincipal
        });
    }

    public async Task EliminarDireccion(int direccionId)
    {
        if (direccionId <= 0)
            throw new ArgumentException("DireccionId inválido");

        await _repo.EliminarDireccion(direccionId);
    }
}

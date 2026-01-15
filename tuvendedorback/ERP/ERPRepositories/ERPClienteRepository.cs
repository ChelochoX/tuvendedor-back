using Dapper;
using tuvendedorback.Data;
using tuvendedorback.ERP.ERPModels;
using tuvendedorback.ERP.ERPRepositories.Interfaces;

namespace tuvendedorback.ERP.ERPRepositories;

public class ERPClienteRepository : IERPClienteRepository
{
    private readonly DbConnections _conexion;

    public ERPClienteRepository(DbConnections conexion)
    {
        _conexion = conexion;
    }

    public async Task<int> CrearCliente(ERPCliente cliente)
    {
        using var conn = _conexion.CreateSqlConnection();

        var sql = @"
        INSERT INTO ERP_Clientes
        (TipoDocumento, NumeroDocumento, RazonSocial, NombreFantasia, Telefono, Email, CRMInteresadoId, UsuarioCreacion)
        VALUES
        (@TipoDocumento, @NumeroDocumento, @RazonSocial, @NombreFantasia, @Telefono, @Email, @CRMInteresadoId, @UsuarioCreacion);
        SELECT SCOPE_IDENTITY();";

        return await conn.ExecuteScalarAsync<int>(sql, cliente);
    }

    public async Task<IEnumerable<ERPCliente>> ObtenerClientes()
    {
        using var conn = _conexion.CreateSqlConnection();
        return await conn.QueryAsync<ERPCliente>("SELECT * FROM ERP_Clientes WHERE Activo = 1");
    }

    public async Task<ERPCliente?> ObtenerPorId(int clienteId)
    {
        using var conn = _conexion.CreateSqlConnection();
        return await conn.QueryFirstOrDefaultAsync<ERPCliente>(
            "SELECT * FROM ERP_Clientes WHERE ClienteId = @clienteId",
            new { clienteId });
    }

    public async Task AgregarDireccion(ERPClienteDireccion direccion)
    {
        using var conn = _conexion.CreateSqlConnection();

        var sql = @"
        INSERT INTO ERP_ClientesDirecciones
        (ClienteId, TipoDireccion, Direccion, Ciudad, Latitud, Longitud, EsPrincipal)
        VALUES
        (@ClienteId, @TipoDireccion, @Direccion, @Ciudad, @Latitud, @Longitud, @EsPrincipal);";

        await conn.ExecuteAsync(sql, direccion);
    }
}

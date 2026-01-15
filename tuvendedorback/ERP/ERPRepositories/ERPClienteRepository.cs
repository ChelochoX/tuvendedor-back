using Dapper;
using tuvendedorback.Data;
using tuvendedorback.ERP.ERPModels;
using tuvendedorback.ERP.ERPRepositories.Interfaces;
using tuvendedorback.Exceptions;

namespace tuvendedorback.ERP.ERPRepositories;

public class ERPClienteRepository : IERPClienteRepository
{
    private readonly DbConnections _conexion;
    private readonly ILogger<ERPClienteRepository> _logger;

    public ERPClienteRepository(DbConnections conexion, ILogger<ERPClienteRepository> logger)
    {
        _conexion = conexion;
        _logger = logger;
    }

    public async Task<int> CrearCliente(ERPCliente cliente)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
                INSERT INTO ERP_Clientes
                (TipoDocumento, NumeroDocumento, RazonSocial, NombreFantasia, Telefono, Email, CRMInteresadoId, UsuarioCreacion)
                VALUES
                (@TipoDocumento, @NumeroDocumento, @RazonSocial, @NombreFantasia, @Telefono, @Email, @CRMInteresadoId, @UsuarioCreacion);
                SELECT SCOPE_IDENTITY();";

            var id = await conn.ExecuteScalarAsync<int>(sql, cliente);

            _logger.LogInformation(
                "ERP Cliente creado correctamente. ClienteId: {ClienteId}",
                id
            );

            return id;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al crear cliente ERP. Documento: {Tipo}-{Numero}",
                cliente.TipoDocumento,
                cliente.NumeroDocumento
            );

            throw new RepositoryException(
                "Error al crear el cliente ERP.",
                ex
            );
        }
    }

    public async Task<IEnumerable<ERPCliente>> ObtenerClientes()
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
                SELECT *
                FROM ERP_Clientes
                WHERE Activo = 1
                ORDER BY FechaCreacion DESC";

            return await conn.QueryAsync<ERPCliente>(sql);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener listado de clientes ERP");

            throw new RepositoryException(
                "Error al obtener los clientes ERP.",
                ex
            );
        }
    }

    public async Task<ERPCliente?> ObtenerPorId(int clienteId)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
                SELECT *
                FROM ERP_Clientes
                WHERE ClienteId = @ClienteId";

            return await conn.QueryFirstOrDefaultAsync<ERPCliente>(
                sql,
                new { ClienteId = clienteId }
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al obtener cliente ERP por id {ClienteId}",
                clienteId
            );

            throw new RepositoryException(
                "Error al obtener el cliente ERP.",
                ex
            );
        }
    }

    public async Task AgregarDireccion(ERPClienteDireccion direccion)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
                INSERT INTO ERP_ClientesDirecciones
                (ClienteId, TipoDireccion, Direccion, Ciudad, Latitud, Longitud, EsPrincipal)
                VALUES
                (@ClienteId, @TipoDireccion, @Direccion, @Ciudad, @Latitud, @Longitud, @EsPrincipal);";

            await conn.ExecuteAsync(sql, direccion);

            _logger.LogInformation(
                "Dirección agregada al cliente ERP. ClienteId: {ClienteId}, Tipo: {TipoDireccion}",
                direccion.ClienteId,
                direccion.TipoDireccion
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al agregar dirección al cliente ERP. ClienteId: {ClienteId}",
                direccion.ClienteId
            );

            throw new RepositoryException(
                "Error al agregar dirección al cliente ERP.",
                ex
            );
        }
    }
}

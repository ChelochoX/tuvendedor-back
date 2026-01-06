using Dapper;
using tuvendedorback.Data;
using tuvendedorback.DTOs;
using tuvendedorback.Exceptions;
using tuvendedorback.Repositories.Interfaces;
using tuvendedorback.Request;

namespace tuvendedorback.Repositories;

public class ModeloProductoRepository : IModeloProductoRepository
{
    private readonly DbConnections _conexion;
    private readonly ILogger<ModeloProductoRepository> _logger;

    public ModeloProductoRepository(DbConnections conexion, ILogger<ModeloProductoRepository> logger)
    {
        _conexion = conexion;
        _logger = logger;
    }

    public async Task<int> CrearModelo(CrearModeloProductoRequest request)
    {
        using var conn = _conexion.CreateSqlConnection();
        try
        {
            const string sql = @"
            INSERT INTO ModelosProducto
            (IdMarca, Rubro, CodigoReferencia, NombreModelo, Estado, FechaCreacion)
            VALUES
            (@IdMarca, @Rubro, @CodigoReferencia, @NombreModelo, 'Activo', GETDATE());
            SELECT SCOPE_IDENTITY();";

            return await conn.ExecuteScalarAsync<int>(sql, request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al crear modelo {@Request}", request);
            throw new RepositoryException("Error al crear modelo", ex);
        }
    }

    public async Task EditarModelo(int id, string nombreModelo, string? codigoReferencia)
    {
        using var conn = _conexion.CreateSqlConnection();

        const string sql = @"
        UPDATE ModelosProducto
        SET 
            NombreModelo = @NombreModelo,
            CodigoReferencia = @CodigoReferencia
        WHERE Id = @Id;";

        await conn.ExecuteAsync(sql, new
        {
            Id = id,
            NombreModelo = nombreModelo,
            CodigoReferencia = codigoReferencia
        });
    }


    public Task ActivarModelo(int id)
        => EjecutarEstado(id, "Activo");

    public Task DesactivarModelo(int id)
        => EjecutarEstado(id, "Inactivo");

    private async Task EjecutarEstado(int id, string estado)
    {
        using var conn = _conexion.CreateSqlConnection();
        await conn.ExecuteAsync(
            "UPDATE ModelosProducto SET Estado = @Estado WHERE Id = @Id;",
            new { Id = id, Estado = estado }
        );
    }

    public async Task<bool> ExisteModelo(
      int idMarca,
      string nombreModelo,
      int? excluirId = null
  )
    {
        using var conn = _conexion.CreateSqlConnection();

        var sql = @"
        SELECT COUNT(1)
        FROM ModelosProducto
        WHERE LOWER(NombreModelo) = LOWER(@NombreModelo)
    ";

        if (idMarca > 0)
            sql += " AND IdMarca = @IdMarca ";

        if (excluirId.HasValue)
            sql += " AND Id <> @ExcluirId ";

        var count = await conn.ExecuteScalarAsync<int>(
            sql,
            new
            {
                NombreModelo = nombreModelo,
                IdMarca = idMarca,
                ExcluirId = excluirId
            }
        );

        return count > 0;
    }


    public async Task<List<ModeloProductoDto>> ObtenerModelos(int? idMarca, bool soloActivos)
    {
        using var conn = _conexion.CreateSqlConnection();

        var sql = @"
        SELECT mp.Id, mp.IdMarca, m.Nombre AS Marca,
               mp.Rubro, mp.CodigoReferencia, mp.NombreModelo, mp.Estado
        FROM ModelosProducto mp
        INNER JOIN Marcas m ON m.Id = mp.IdMarca
        WHERE 1 = 1
        ";

        if (idMarca.HasValue)
            sql += " AND mp.IdMarca = @IdMarca ";

        if (soloActivos)
            sql += " AND mp.Estado = 'Activo' ";

        sql += " ORDER BY m.Nombre, mp.NombreModelo;";

        return (await conn.QueryAsync<ModeloProductoDto>(sql, new { idMarca })).ToList();
    }
}

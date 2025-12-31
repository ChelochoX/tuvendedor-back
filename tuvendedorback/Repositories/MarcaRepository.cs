using Dapper;
using tuvendedorback.Data;
using tuvendedorback.DTOs;
using tuvendedorback.Exceptions;
using tuvendedorback.Repositories.Interfaces;

namespace tuvendedorback.Repositories;

public class MarcaRepository : IMarcaRepository
{
    private readonly DbConnections _conexion;
    private readonly ILogger<MarcaRepository> _logger;

    public MarcaRepository(DbConnections conexion, ILogger<MarcaRepository> logger)
    {
        _conexion = conexion;
        _logger = logger;
    }

    public async Task<int> CrearMarca(string nombre)
    {
        using var conn = _conexion.CreateSqlConnection();
        try
        {
            const string sql = @"
            INSERT INTO Marcas (Nombre, Estado, FechaCreacion)
            VALUES (@Nombre, 'Activo', GETDATE());
            SELECT SCOPE_IDENTITY();";

            return await conn.ExecuteScalarAsync<int>(sql, new { Nombre = nombre });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al crear marca {Nombre}", nombre);
            throw new RepositoryException("Error al crear marca", ex);
        }
    }

    public async Task EditarMarca(int id, string nombre)
    {
        using var conn = _conexion.CreateSqlConnection();
        try
        {
            const string sql = @"UPDATE Marcas SET Nombre = @Nombre WHERE Id = @Id;";
            await conn.ExecuteAsync(sql, new { Id = id, Nombre = nombre });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al editar marca {Id}", id);
            throw new RepositoryException("Error al editar marca", ex);
        }
    }

    public async Task DesactivarMarca(int id)
    {
        using var conn = _conexion.CreateSqlConnection();
        await conn.ExecuteAsync(
            "UPDATE Marcas SET Estado = 'Inactivo' WHERE Id = @Id;",
            new { Id = id }
        );
    }

    public async Task ActivarMarca(int id)
    {
        using var conn = _conexion.CreateSqlConnection();
        await conn.ExecuteAsync(
            "UPDATE Marcas SET Estado = 'Activo' WHERE Id = @Id;",
            new { Id = id }
        );
    }

    public async Task<bool> ExisteMarcaConNombre(string nombre, int? excluirId = null)
    {
        using var conn = _conexion.CreateSqlConnection();

        var sql = @"
        SELECT COUNT(1)
        FROM Marcas
        WHERE LOWER(Nombre) = LOWER(@Nombre)
        ";

        if (excluirId.HasValue)
            sql += " AND Id <> @ExcluirId ";

        var count = await conn.ExecuteScalarAsync<int>(sql, new { Nombre = nombre, ExcluirId = excluirId });
        return count > 0;
    }

    public async Task<List<MarcaDto>> ObtenerMarcas(bool soloActivas)
    {
        using var conn = _conexion.CreateSqlConnection();

        var sql = @"
        SELECT Id, Nombre, Estado
        FROM Marcas
        ";

        if (soloActivas)
            sql += " WHERE Estado = 'Activo' ";

        sql += " ORDER BY Nombre ASC;";

        return (await conn.QueryAsync<MarcaDto>(sql)).ToList();
    }
}

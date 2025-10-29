using Dapper;
using System.Text;
using tuvendedorback.Data;
using tuvendedorback.DTOs;
using tuvendedorback.Exceptions;
using tuvendedorback.Repositories.Interfaces;
using tuvendedorback.Request;

namespace tuvendedorback.Repositories;

public class ClientesRepository : IClientesRepository
{
    private readonly DbConnections _conexion;
    private readonly ILogger<ClientesRepository> _logger;

    public ClientesRepository(ILogger<ClientesRepository> logger, DbConnections conexion)
    {
        _logger = logger;
        _conexion = conexion;
    }

    public async Task<int> InsertarInteresado(InteresadoDto interesado)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
                INSERT INTO Interesados
                (Nombre, Telefono, Email, Ciudad, ProductoInteres, AportaIPS, CantidadAportes,
                 Estado, FechaRegistro, FechaProximoContacto, Descripcion, ArchivoUrl, UsuarioResponsable)
                VALUES
                (@Nombre, @Telefono, @Email, @Ciudad, @ProductoInteres, @AportaIPS, @CantidadAportes,
                 @Estado, @FechaRegistro, @FechaProximoContacto, @Descripcion, @ArchivoUrl, @UsuarioResponsable);

                SELECT CAST(SCOPE_IDENTITY() AS INT);";

            int nuevoId = await conn.ExecuteScalarAsync<int>(sql, interesado);
            _logger.LogInformation("Interesado {Nombre} registrado con Id {Id}", interesado.Nombre, nuevoId);
            return nuevoId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al insertar interesado {@interesado}", interesado);
            throw new RepositoryException("Error al insertar interesado", ex);
        }
    }

    public async Task<int> InsertarSeguimiento(SeguimientoDto seguimiento)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
                INSERT INTO Seguimientos
                (IdInteresado, Fecha, Descripcion, Usuario)
                VALUES (@IdInteresado, @Fecha, @Descripcion, @Usuario);

                SELECT CAST(SCOPE_IDENTITY() AS INT);";

            int id = await conn.ExecuteScalarAsync<int>(sql, seguimiento);
            _logger.LogInformation("Seguimiento agregado a interesado {IdInteresado} por {Usuario}", seguimiento.IdInteresado, seguimiento.Usuario);
            return id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al insertar seguimiento {@seguimiento}", seguimiento);
            throw new RepositoryException("Error al insertar seguimiento", ex);
        }
    }

    public async Task<(List<InteresadoDto> Items, int TotalRegistros)> ObtenerInteresados(FiltroInteresadosRequest filtro)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            var sql = new StringBuilder(@"
            SELECT 
                i.Id,
                i.Nombre,
                i.Telefono,
                i.Email,
                i.Ciudad,
                i.ProductoInteres,
                i.AportaIPS,
                i.CantidadAportes,
                i.Estado,
                i.FechaRegistro,
                i.FechaProximoContacto,
                i.Descripcion,
                i.ArchivoUrl,
                i.UsuarioResponsable
            FROM Interesados i
            WHERE 1 = 1
        ");

            // 🔹 Filtros dinámicos
            if (!string.IsNullOrEmpty(filtro.Nombre))
                sql.AppendLine("AND i.Nombre LIKE '%' + @Nombre + '%'");

            if (!string.IsNullOrEmpty(filtro.Estado) && filtro.Estado != "Todos")
            {
                sql.AppendLine("AND i.Estado = @Estado");
            }

            if (filtro.FechaRegistroDesde.HasValue)
                sql.AppendLine("AND CONVERT(date, i.FechaRegistro) >= CONVERT(date, @FechaRegistroDesde)");

            if (filtro.FechaRegistroHasta.HasValue)
                sql.AppendLine("AND CONVERT(date, i.FechaRegistro) <= CONVERT(date, @FechaRegistroHasta)");

            if (filtro.FechaProximoContactoDesde.HasValue)
                sql.AppendLine("AND CONVERT(date, i.FechaProximoContacto) >= CONVERT(date, @FechaProximoContactoDesde)");

            if (filtro.FechaProximoContactoHasta.HasValue)
                sql.AppendLine("AND CONVERT(date, i.FechaProximoContacto) <= CONVERT(date, @FechaProximoContactoHasta)");


            // 🔹 Conteo total
            var sqlCount = $"SELECT COUNT(1) FROM ({sql}) AS Conteo";

            // 🔹 Orden y paginación
            sql.AppendLine("ORDER BY i.FechaRegistro DESC");
            sql.AppendLine("OFFSET (@Offset) ROWS FETCH NEXT (@Limit) ROWS ONLY;");

            var parametros = new
            {
                filtro.Nombre,
                filtro.Estado,
                filtro.FechaRegistroDesde,
                filtro.FechaRegistroHasta,
                filtro.FechaProximoContactoDesde,
                filtro.FechaProximoContactoHasta,
                Offset = (filtro.NumeroPagina - 1) * filtro.RegistrosPorPagina,
                Limit = filtro.RegistrosPorPagina
            };

            var total = await conn.ExecuteScalarAsync<int>(sqlCount, parametros);
            var items = await conn.QueryAsync<InteresadoDto>(sql.ToString(), parametros);

            _logger.LogInformation("Consulta de interesados devuelta {Count} registros (total {Total})", items.Count(), total);
            return (items.ToList(), total);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener interesados filtrados");
            throw new RepositoryException("Error al obtener interesados filtrados", ex);
        }
    }

    public async Task<List<SeguimientoDto>> ObtenerSeguimientosPorInteresado(int idInteresado)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
            SELECT 
                s.Id,
                s.IdInteresado,
                s.Fecha,
                s.Comentario,
                s.Usuario
            FROM Seguimientos s
            WHERE s.IdInteresado = @id
            ORDER BY s.Fecha DESC";

            var result = await conn.QueryAsync<SeguimientoDto>(sql, new { id = idInteresado });
            _logger.LogInformation("Se obtuvieron {Count} seguimientos del interesado {Id}", result.Count(), idInteresado);
            return result.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener seguimientos del interesado {Id}", idInteresado);
            throw new RepositoryException("Error al obtener seguimientos", ex);
        }
    }

    public async Task<InteresadoDto?> ObtenerInteresadoPorId(int id)
    {
        using var conn = _conexion.CreateSqlConnection();
        const string sql = @"
        SELECT Id, Nombre, Telefono, Email, Ciudad, ProductoInteres,
               AportaIPS, CantidadAportes, Estado,
               FechaProximoContacto, Descripcion, ArchivoUrl
        FROM Interesados
        WHERE Id = @Id";

        return await conn.QueryFirstOrDefaultAsync<InteresadoDto>(sql, new { Id = id });
    }

    public async Task ActualizarInteresado(InteresadoDto interesado)
    {
        using var conn = _conexion.CreateSqlConnection();

        const string sql = @"
        UPDATE Interesados SET
            Nombre = @Nombre,
            Telefono = @Telefono,
            Email = @Email,
            Ciudad = @Ciudad,
            ProductoInteres = @ProductoInteres,
            AportaIPS = @AportaIPS,
            CantidadAportes = @CantidadAportes,
            Estado = @Estado,
            FechaProximoContacto = @FechaProximoContacto,
            Descripcion = @Descripcion,
            ArchivoUrl = @ArchivoUrl            
        WHERE Id = @Id;";

        try
        {
            await conn.ExecuteAsync(sql, interesado);
            _logger.LogInformation("Interesado {Id} actualizado correctamente", interesado.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al actualizar interesado {@interesado}", interesado);
            throw new RepositoryException("Error al actualizar interesado", ex);
        }
    }


}

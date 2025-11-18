using Dapper;
using tuvendedorback.Data;
using tuvendedorback.DTOs;
using tuvendedorback.Exceptions;
using tuvendedorback.Models;
using tuvendedorback.Repositories.Interfaces;
using tuvendedorback.Request;

namespace tuvendedorback.Repositories;

public class PublicacionRepository : IPublicacionRepository
{
    private readonly DbConnections _conexion;
    private readonly ILogger<PublicacionRepository> _logger;

    public PublicacionRepository(DbConnections conexion, ILogger<PublicacionRepository> logger)
    {
        _conexion = conexion;
        _logger = logger;
    }

    public async Task<int> InsertarPublicacion(CrearPublicacionRequest request, int idUsuario, List<ImagenDto> imagenes)
    {
        using var conn = _conexion.CreateSqlConnection();
        conn.Open();
        using var tran = conn.BeginTransaction();

        try
        {
            const string insertPub = @"
            INSERT INTO Publicaciones (Titulo, Descripcion, Precio, Categoria, IdUsuario, MostrarBotonesCompra, Fecha,Ubicacion)
            VALUES (@Titulo, @Descripcion, @Precio, @Categoria, @IdUsuario, @MostrarBotonesCompra, GETDATE(), @Ubicacion);
            SELECT SCOPE_IDENTITY();";

            var publicacionId = await conn.ExecuteScalarAsync<int>(insertPub, new
            {
                request.Titulo,
                request.Descripcion,
                request.Precio,
                request.Categoria,
                IdUsuario = idUsuario,
                request.MostrarBotonesCompra,
                request.Ubicacion
            }, tran);

            foreach (var img in imagenes)
            {
                await conn.ExecuteAsync(
                    "INSERT INTO ImagenesPublicacion (IdPublicacion, Url, ThumbUrl) VALUES (@Id, @Url, @ThumbUrl);",
                    new { Id = publicacionId, Url = img.MainUrl, ThumbUrl = img.ThumbUrl },
                    tran
                );
            }

            if (request.MostrarBotonesCompra && request.PlanCredito != null)
            {
                foreach (var plan in request.PlanCredito)
                {
                    await conn.ExecuteAsync(@"INSERT INTO PlanesCredito (IdPublicacion, Cuotas, ValorCuota)
                                              VALUES (@IdPublicacion, @Cuotas, @ValorCuota);", new
                    {
                        IdPublicacion = publicacionId,
                        Cuotas = plan.Cuotas,
                        ValorCuota = plan.ValorCuota
                    }, tran);
                }
            }

            tran.Commit();
            return publicacionId;
        }
        catch (Exception ex)
        {
            tran.Rollback();
            _logger.LogError(ex, "Error al insertar la publicación");
            throw new RepositoryException("Error al insertar la publicación", ex);
        }
    }

    public async Task<List<Publicacion>> ObtenerPublicaciones(string? categoria, string? nombre)
    {
        using var conn = _conexion.CreateSqlConnection();
        try
        {
            var sql = @"
        SELECT 
            p.Id               AS Id,
            p.Titulo           AS Titulo,
            p.Descripcion      AS Descripcion,
            p.Precio           AS Precio,
            p.Categoria        AS Categoria,
            p.Ubicacion        AS Ubicacion,
            p.MostrarBotonesCompra AS MostrarBotonesCompra,
            v.NombreNegocio    AS VendedorNombre,
            NULL               AS VendedorAvatar,
            u.Telefono         AS VendedorTelefono, 
            CASE WHEN d.Id IS NOT NULL THEN 1 ELSE 0 END AS EsDestacada,
            d.FechaFin AS FechaFinDestacado

        FROM Publicaciones p
        LEFT JOIN Vendedores v ON v.IdUsuario = p.IdUsuario
        LEFT JOIN Usuarios u ON u.Id = p.IdUsuario
        LEFT JOIN PublicacionesDestacadas d
        ON d.IdPublicacion = p.Id
        AND d.Estado = 'Activo'
        AND d.FechaFin >= GETDATE()
        WHERE (@Categoria IS NULL OR p.Categoria = @Categoria)
          AND (@Nombre IS NULL OR p.Titulo LIKE '%' + @Nombre + '%')
        ORDER BY 
            CASE WHEN d.Id IS NOT NULL THEN 0 ELSE 1 END,
            p.Fecha DESC";

            var publicaciones = (await conn.QueryAsync<Publicacion>(
                sql,
                new { Categoria = categoria, Nombre = nombre }
            )).ToList();

            foreach (var pub in publicaciones)
            {
                // 📸 Imágenes
                var imagenes = await conn.QueryAsync<ImagenPublicacion>(@"
                SELECT 
                    i.Id           AS Id,
                    i.Url          AS Url,
                    i.IdPublicacion AS PublicacionId
                FROM ImagenesPublicacion i
                WHERE i.IdPublicacion = @Id",
                    new { Id = pub.Id });

                // 💳 Planes de crédito
                var planes = await conn.QueryAsync<PlanCredito>(@"
                SELECT 
                    pc.Id            AS Id,
                    pc.IdPublicacion AS PublicacionId,
                    pc.Cuotas        AS Cuotas,
                    pc.ValorCuota    AS ValorCuota
                FROM PlanesCredito pc
                WHERE pc.IdPublicacion = @Id",
                    new { Id = pub.Id });

                pub.Imagenes = imagenes.ToList();
                pub.PlanCredito = planes.ToList();
            }

            return publicaciones;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener publicaciones");
            throw new RepositoryException("Error al obtener publicaciones", ex);
        }
    }

    public async Task<IEnumerable<ImagenDto>> ObtenerImagenesPorPublicacion(int idPublicacion, int idUsuario)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
            SELECT i.Url AS MainUrl, i.ThumbUrl
            FROM ImagenesPublicacion i
            INNER JOIN Publicaciones p ON p.Id = i.IdPublicacion
            WHERE p.Id = @idPublicacion AND p.IdUsuario = @idUsuario;";

            var imagenes = await conn.QueryAsync<ImagenDto>(sql, new { idPublicacion, idUsuario });

            _logger.LogInformation("Se obtuvieron {Cantidad} imágenes para la publicación {IdPublicacion} del usuario {IdUsuario}",
                imagenes.Count(), idPublicacion, idUsuario);

            return imagenes;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error al obtener las imágenes de la publicación {IdPublicacion} del usuario {IdUsuario}",
                idPublicacion, idUsuario);

            throw new RepositoryException("Error al obtener las imágenes de la publicación", ex);
        }
    }

    public async Task<int> EliminarPublicacion(int idPublicacion, int idUsuario)
    {
        using var conn = _conexion.CreateSqlConnection();
        conn.Open();
        using var tran = conn.BeginTransaction();

        try
        {
            _logger.LogInformation("🗑️ Iniciando eliminación de la publicación {IdPublicacion} del usuario {IdUsuario}", idPublicacion, idUsuario);

            // 🔹 Eliminar dependencias primero
            var filasPlanes = await conn.ExecuteAsync(
                "DELETE FROM PlanesCredito WHERE IdPublicacion = @idPublicacion;",
                new { idPublicacion }, tran
            );

            if (filasPlanes > 0)
                _logger.LogInformation("Se eliminaron {Cantidad} planes de crédito asociados a la publicación {IdPublicacion}", filasPlanes, idPublicacion);

            var filasImagenes = await conn.ExecuteAsync(
                "DELETE FROM ImagenesPublicacion WHERE IdPublicacion = @idPublicacion;",
                new { idPublicacion }, tran
            );

            if (filasImagenes > 0)
                _logger.LogInformation("Se eliminaron {Cantidad} imágenes asociadas a la publicación {IdPublicacion}", filasImagenes, idPublicacion);

            // 🔹 Eliminar publicación principal
            var filasPublicacion = await conn.ExecuteAsync(
                "DELETE FROM Publicaciones WHERE Id = @idPublicacion AND IdUsuario = @idUsuario;",
                new { idPublicacion, idUsuario },
                tran
            );

            if (filasPublicacion > 0)
            {
                _logger.LogInformation("✅ Publicación {IdPublicacion} eliminada correctamente por el usuario {IdUsuario}", idPublicacion, idUsuario);
            }
            else
            {
                _logger.LogWarning("⚠️ No se encontró la publicación {IdPublicacion} para el usuario {IdUsuario} o ya fue eliminada", idPublicacion, idUsuario);
            }

            tran.Commit();
            return filasPublicacion;
        }
        catch (Exception ex)
        {
            tran.Rollback();
            _logger.LogError(ex, "❌ Error al eliminar la publicación {IdPublicacion} del usuario {IdUsuario}", idPublicacion, idUsuario);
            throw new RepositoryException("Error al eliminar la publicación", ex);
        }
    }

    public async Task<List<Publicacion>> ObtenerMisPublicaciones(int idUsuario)
    {
        using var conn = _conexion.CreateSqlConnection();
        try
        {
            var sql = @"
        SELECT 
            p.Id               AS Id,
            p.Titulo           AS Titulo,
            p.Descripcion      AS Descripcion,
            p.Precio           AS Precio,
            p.Categoria        AS Categoria,
            p.Ubicacion        AS Ubicacion,
            p.MostrarBotonesCompra AS MostrarBotonesCompra,
            v.NombreNegocio    AS VendedorNombre,
            NULL               AS VendedorAvatar,
            CASE WHEN d.Id IS NOT NULL THEN 1 ELSE 0 END AS EsDestacada,
            d.FechaFin AS FechaFinDestacado

        FROM Publicaciones p
        LEFT JOIN Vendedores v ON v.IdUsuario = p.IdUsuario
        LEFT JOIN PublicacionesDestacadas d
            ON d.IdPublicacion = p.Id
           AND d.Estado = 'Activo'
           AND d.FechaFin >= GETDATE()
        WHERE p.IdUsuario = @IdUsuario
        ORDER BY 
            CASE WHEN d.Id IS NOT NULL THEN 0 ELSE 1 END,
            p.Fecha DESC";

            var publicaciones = (await conn.QueryAsync<Publicacion>(sql, new { IdUsuario = idUsuario })).ToList();

            foreach (var pub in publicaciones)
            {
                // 📸 Imágenes
                var imagenes = await conn.QueryAsync<ImagenPublicacion>(@"
                SELECT Id, Url, IdPublicacion AS PublicacionId
                FROM ImagenesPublicacion
                WHERE IdPublicacion = @Id", new { Id = pub.Id });

                // 💳 Planes de crédito
                var planes = await conn.QueryAsync<PlanCredito>(@"
                SELECT Id, IdPublicacion, Cuotas, ValorCuota
                FROM PlanesCredito
                WHERE IdPublicacion = @Id", new { Id = pub.Id });

                pub.Imagenes = imagenes.ToList();
                pub.PlanCredito = planes.ToList();
            }

            return publicaciones;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener publicaciones del usuario {IdUsuario}", idUsuario);
            throw new RepositoryException("Error al obtener tus publicaciones", ex);
        }
    }

    public async Task<List<CategoriaDto>> ObtenerCategoriasActivas()
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            var sql = @"
                SELECT Id, Nombre, Descripcion
                FROM Categorias
                WHERE Estado = 'Activo'
                ORDER BY Nombre ASC";

            var categorias = await conn.QueryAsync<CategoriaDto>(sql);

            return categorias.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener categorías");
            throw new RepositoryException("Error_Obtener_Categorias", "Error al obtener la lista de categorías.", ex);
        }
    }

    public async Task<bool> EsPublicacionDeUsuario(int idPublicacion, int idUsuario)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
            SELECT COUNT(1)
            FROM Publicaciones
            WHERE Id = @idPublicacion AND IdUsuario = @idUsuario";

            var count = await conn.ExecuteScalarAsync<int>(sql, new { idPublicacion, idUsuario });

            return count > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al validar publicación {IdPublicacion} del usuario {IdUsuario}", idPublicacion, idUsuario);
            throw new RepositoryException("Error al validar la publicación del usuario.", ex);
        }
    }

    public async Task CrearOActualizarDestacado(int idPublicacion, DateTime fechaInicio, DateTime fechaFin)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string updateSql = @"
            UPDATE PublicacionesDestacadas
            SET FechaInicio = @FechaInicio,
                FechaFin = @FechaFin,
                Estado = 'Activo'
            WHERE IdPublicacion = @IdPublicacion
              AND Estado = 'Activo'
              AND FechaFin >= GETDATE();";

            var filas = await conn.ExecuteAsync(updateSql, new
            {
                IdPublicacion = idPublicacion,
                FechaInicio = fechaInicio,
                FechaFin = fechaFin
            });

            if (filas == 0)
            {
                const string insertSql = @"
                INSERT INTO PublicacionesDestacadas (IdPublicacion, FechaInicio, FechaFin, Estado)
                VALUES (@IdPublicacion, @FechaInicio, @FechaFin, 'Activo');";

                await conn.ExecuteAsync(insertSql, new
                {
                    IdPublicacion = idPublicacion,
                    FechaInicio = fechaInicio,
                    FechaFin = fechaFin
                });
            }

            _logger.LogInformation("✅ Publicación {IdPublicacion} destacada desde {Inicio} hasta {Fin}",
                idPublicacion, fechaInicio, fechaFin);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al crear/actualizar destacado para la publicación {IdPublicacion}", idPublicacion);
            throw new RepositoryException("Error al destacar la publicación.", ex);
        }
    }

    public async Task<bool> EstaPublicacionDestacada(int idPublicacion)
    {
        using var conn = _conexion.CreateSqlConnection();

        const string sql = @"
        SELECT COUNT(1)
        FROM PublicacionesDestacadas
        WHERE IdPublicacion = @IdPublicacion
          AND Estado = 'Activo'
          AND FechaFin >= GETDATE();";

        var count = await conn.ExecuteScalarAsync<int>(sql, new { IdPublicacion = idPublicacion });

        return count > 0;
    }


}

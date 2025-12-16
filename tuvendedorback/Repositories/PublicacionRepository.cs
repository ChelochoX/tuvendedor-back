using Dapper;
using Microsoft.Data.SqlClient;
using tuvendedorback.Common;
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
    private readonly UserContext _userContext;

    public PublicacionRepository(DbConnections conexion, ILogger<PublicacionRepository> logger, UserContext userContext)
    {
        _conexion = conexion;
        _logger = logger;
        _userContext = userContext;
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

    public async Task<List<Publicacion>> ObtenerPublicaciones(string? categoria, string? nombre, int? idUsuario)
    {
        using var conn = _conexion.CreateSqlConnection();
        try
        {
            // Verificar si el usuario es administrador
            bool esAdministrador = await EsAdministrador(idUsuario);

            var sql = @"
                SELECT 
                    p.Id,
                    p.Titulo              AS Titulo,
                    p.Descripcion         AS Descripcion,
                    p.Precio              AS Precio,
                    p.Categoria           AS Categoria,
                    p.Ubicacion           AS Ubicacion,
                    p.MostrarBotonesCompra,
                    p.Estado              AS Estado,  
                    v.NombreNegocio       AS VendedorNombre,
                    u.Telefono            AS VendedorTelefono,

                    -- ⭐ DESTACADO
                    CASE WHEN d.Id IS NOT NULL THEN 1 ELSE 0 END AS EsDestacada,
                    d.FechaFin AS FechaFinDestacado,

                    -- ⭐ TEMPORADA
                    CASE WHEN t.Id IS NOT NULL THEN 1 ELSE 0 END AS EsTemporada,
                    t.FechaFin   AS FechaFinTemporada,
                    t.BadgeTexto AS BadgeTexto,
                    t.BadgeColor AS BadgeColor
                FROM Publicaciones p
                LEFT JOIN Vendedores v ON v.IdUsuario = p.IdUsuario
                LEFT JOIN Usuarios u   ON u.Id = p.IdUsuario
                LEFT JOIN PublicacionesDestacadas d
                    ON d.IdPublicacion = p.Id
                    AND d.Estado = 'Activo'
                    AND d.FechaFin >= GETDATE()
                LEFT JOIN PublicacionesTemporada t
                    ON t.IdPublicacion = p.Id
                    AND t.Estado = 'Activo'
                    AND t.FechaFin >= GETDATE()
                WHERE (@Categoria IS NULL OR p.Categoria = @Categoria)
                    AND (@Nombre IS NULL OR p.Titulo LIKE '%' + @Nombre + '%')
                ";

            if (!esAdministrador)
            {
                sql += " AND p.IdUsuario = @IdUsuario ";
            }

            sql += @"

                ORDER BY 
                    CASE WHEN t.Id IS NOT NULL THEN 0 ELSE 1 END,
                    CASE WHEN d.Id IS NOT NULL THEN 0 ELSE 1 END,
                    p.Fecha DESC;
                ";


            var publicaciones = (await conn.QueryAsync<Publicacion>(
                sql,
                new
                {
                    Categoria = categoria,
                    Nombre = nombre,
                    IdUsuario = idUsuario
                }
            )).ToList();


            foreach (var pub in publicaciones)
            {
                // 📸 Imágenes
                var imagenes = await conn.QueryAsync<string>(@"
                    SELECT Url
                    FROM ImagenesPublicacion
                    WHERE IdPublicacion = @Id",
                    new { Id = pub.Id });

                // 💳 Planes de crédito
                var planes = await conn.QueryAsync<PlanCredito>(@"
                    SELECT 
                        pc.Id,
                        pc.IdPublicacion,
                        pc.Cuotas,
                        pc.ValorCuota
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

    public async Task<bool> EsAdministrador(int? idUsuario)
    {
        using var conn = _conexion.CreateSqlConnection();
        try
        {
            var sql = @"
            SELECT COUNT(1)
            FROM UsuarioRoles ur
            INNER JOIN Roles r ON ur.IdRol = r.Id
            WHERE ur.IdUsuario = @IdUsuario AND r.NombreRol = 'Administrador'";

            var result = await conn.ExecuteScalarAsync<int>(sql, new { IdUsuario = idUsuario });
            return result > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al verificar el rol de usuario");
            throw new RepositoryException("Error al verificar el rol de usuario", ex);
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
            _logger.LogInformation("Iniciando eliminación de la publicación {IdPublicacion} del usuario {IdUsuario}", idPublicacion, idUsuario);

            // 🔹 Eliminar destacados (PublicacionesDestacadas)
            var filasDestacados = await conn.ExecuteAsync(
                "DELETE FROM PublicacionesDestacadas WHERE IdPublicacion = @idPublicacion;",
                new { idPublicacion },
                tran
            );

            if (filasDestacados > 0)
                _logger.LogInformation("Se eliminaron {Cantidad} destacados asociados a la publicación {IdPublicacion}", filasDestacados, idPublicacion);

            // 🔹 Eliminar temporada (PublicacionesTemporada)
            var filasTemporada = await conn.ExecuteAsync(
                "DELETE FROM PublicacionesTemporada WHERE IdPublicacion = @idPublicacion;",
                new { idPublicacion },
                tran
            );

            if (filasTemporada > 0)
                _logger.LogInformation("Se eliminaron {Cantidad} registros de temporada asociados a la publicación {IdPublicacion}", filasTemporada, idPublicacion);

            // 🔹 Eliminar planes de crédito
            var filasPlanes = await conn.ExecuteAsync(
                "DELETE FROM PlanesCredito WHERE IdPublicacion = @idPublicacion;",
                new { idPublicacion },
                tran
            );

            if (filasPlanes > 0)
                _logger.LogInformation("Se eliminaron {Cantidad} planes de crédito asociados a la publicación {IdPublicacion}", filasPlanes, idPublicacion);

            // 🔹 Eliminar imágenes
            var filasImagenes = await conn.ExecuteAsync(
                "DELETE FROM ImagenesPublicacion WHERE IdPublicacion = @idPublicacion;",
                new { idPublicacion },
                tran
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
            // 🔐 Verificar si es admin (DESDE BD)
            var esAdmin = await EsAdministrador(idUsuario);

            var sql = @"
            SELECT 
                p.Id                    AS Id,
                p.Titulo                AS Titulo,
                p.Descripcion           AS Descripcion,
                p.Precio                AS Precio,
                p.Categoria             AS Categoria,
                p.Ubicacion             AS Ubicacion,
                p.Estado                AS Estado,
                p.MostrarBotonesCompra  AS MostrarBotonesCompra,

                v.NombreNegocio         AS VendedorNombre,
                NULL                    AS VendedorAvatar,
                NULL                    AS VendedorTelefono,

                -- ⭐ Destacado
                CASE WHEN d.Id IS NOT NULL THEN 1 ELSE 0 END AS EsDestacada,
                d.FechaFin              AS FechaFinDestacado,

                -- 🎉 Temporada
                CASE WHEN pt.Id IS NOT NULL THEN 1 ELSE 0 END AS EsTemporada,
                pt.FechaFin             AS FechaFinTemporada,
                pt.BadgeTexto           AS BadgeTexto,
                pt.BadgeColor           AS BadgeColor

            FROM Publicaciones p
            LEFT JOIN Vendedores v 
                ON v.IdUsuario = p.IdUsuario

            LEFT JOIN PublicacionesDestacadas d
                ON d.IdPublicacion = p.Id
                AND d.Estado = 'Activo'
                AND d.FechaFin >= GETDATE()

            LEFT JOIN (
                SELECT *
                FROM PublicacionesTemporada
                WHERE Estado = 'Activo'
                  AND FechaFin >= GETDATE()
            ) pt
                ON pt.IdPublicacion = p.Id
        ";

            // 👇 SOLO si NO es admin filtramos por usuario
            if (!esAdmin)
            {
                sql += " WHERE p.IdUsuario = @IdUsuario ";
            }

            sql += @"
            ORDER BY 
                CASE WHEN d.Id IS NOT NULL THEN 0 ELSE 1 END,
                p.Fecha DESC;
        ";

            var publicaciones = (await conn.QueryAsync<Publicacion>(
                sql,
                new { IdUsuario = idUsuario }
            )).ToList();

            foreach (var pub in publicaciones)
            {
                pub.Imagenes = (await conn.QueryAsync<string>(
                    "SELECT Url FROM ImagenesPublicacion WHERE IdPublicacion = @Id",
                    new { Id = pub.Id }
                )).ToList();

                pub.PlanCredito = (await conn.QueryAsync<PlanCredito>(
                    @"SELECT Id, IdPublicacion, Cuotas, ValorCuota
                  FROM PlanesCredito
                  WHERE IdPublicacion = @Id",
                    new { Id = pub.Id }
                )).ToList();
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

    public async Task ActivarTemporada(ActivarTemporadaRequest request)
    {
        await using var conn = (SqlConnection)_conexion.CreateSqlConnection();
        await conn.OpenAsync();

        await using var tran = await conn.BeginTransactionAsync();
        try
        {
            // 1) Temporada válida y activa
            var temporada = await conn.QueryFirstOrDefaultAsync<TemporadaDto>(@"
            SELECT Id, Nombre, BadgeTexto, BadgeColor, FechaInicio, FechaFin
            FROM Temporadas
            WHERE Id = @IdTemporada AND Estado = 'Activo'",
                new { request.IdTemporada }, tran);

            if (temporada == null)
                throw new RepositoryException("La temporada no existe o no está activa.");

            // 2) ¿Ya existe una temporada activa para esta publicación?
            var existeActiva = await conn.ExecuteScalarAsync<int>(@"
            SELECT CASE WHEN EXISTS(
                SELECT 1 FROM PublicacionesTemporada
                WHERE IdPublicacion = @IdPublicacion 
                AND Estado = 'Activo'
                AND GETDATE() BETWEEN FechaInicio AND FechaFin
            ) THEN 1 ELSE 0 END",
                new { request.IdPublicacion }, tran) == 1;

            if (existeActiva)
                throw new RepositoryException("La publicación ya tiene una temporada activa. Esperá a que termine para activar otra.");

            // 3) Insertar nueva temporada activa
            const string insertSql = @"
            INSERT INTO PublicacionesTemporada
            (IdPublicacion, IdTemporada, FechaInicio, FechaFin, BadgeTexto, BadgeColor, Estado)
            VALUES (@IdPublicacion, @IdTemporada, @FechaInicio, @FechaFin, @BadgeTexto, @BadgeColor, 'Activo');";

            await conn.ExecuteAsync(insertSql, new
            {
                request.IdPublicacion,
                request.IdTemporada,
                FechaInicio = temporada.FechaInicio,
                FechaFin = temporada.FechaFin,
                temporada.BadgeTexto,
                temporada.BadgeColor
            }, tran);

            await tran.CommitAsync();
            _logger.LogInformation("Publicación {IdPublicacion} activada en temporada {IdTemporada}.",
                request.IdPublicacion, request.IdTemporada);
        }
        catch (Exception ex)
        {
            await tran.RollbackAsync();
            _logger.LogError(ex, "Error al activar temporada para publicación {IdPublicacion}", request.IdPublicacion);
            throw new RepositoryException("Error al activar temporada", ex);
        }
    }

    public async Task QuitarDestacado(int idPublicacion)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
        UPDATE PublicacionesDestacadas
        SET Estado = 'Inactivo'
        WHERE IdPublicacion = @IdPublicacion
          AND Estado = 'Activo'
          AND FechaFin >= GETDATE();";

            var filas = await conn.ExecuteAsync(sql, new
            {
                IdPublicacion = idPublicacion
            });

            if (filas == 0)
                throw new RepositoryException(
                    "No se encontró un destacado activo para quitar."
                );

            _logger.LogInformation(
                "❌ Destacado quitado para la publicación {IdPublicacion}",
                idPublicacion
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al quitar destacado para la publicación {IdPublicacion}",
                idPublicacion
            );
            throw new RepositoryException("Error al quitar el destacado.", ex);
        }
    }


    public async Task DesactivarTemporada(int idPublicacion)
    {
        using var conn = _conexion.CreateSqlConnection();
        try
        {
            var sql = @"
            UPDATE PublicacionesTemporada
            SET Estado = 'Inactivo'
            WHERE IdPublicacion = @IdPublicacion
              AND Estado = 'Activo';";

            await conn.ExecuteAsync(sql, new { IdPublicacion = idPublicacion });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al desactivar temporada para la publicación {IdPublicacion}", idPublicacion);
            throw new RepositoryException("Error al desactivar temporada", ex);
        }
    }

    public async Task<bool> UsuarioTienePermiso(int idUsuario, string nombrePermiso)
    {
        using var conn = _conexion.CreateSqlConnection();
        try
        {
            var sql = @"
            SELECT COUNT(*)
            FROM UsuarioPermisos up
            INNER JOIN Permisos p ON p.Id = up.IdPermiso
            WHERE up.IdUsuario = @IdUsuario
              AND p.Nombre = @NombrePermiso";

            var cantidad = await conn.ExecuteScalarAsync<int>(sql, new
            {
                IdUsuario = idUsuario,
                NombrePermiso = nombrePermiso
            });

            return cantidad > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error validando permiso {Permiso} para usuario {UsuarioId}",
                nombrePermiso, idUsuario);

            throw new RepositoryException("Error al validar permisos del usuario", ex);
        }
    }

    public async Task<bool> EstaPublicacionEnTemporada(int idPublicacion)
    {
        using var conn = _conexion.CreateSqlConnection();
        try
        {
            var sql = @"
            SELECT COUNT(*)
            FROM PublicacionesTemporada
            WHERE IdPublicacion = @IdPublicacion
              AND Estado = 'Activo'
              AND FechaFin >= GETDATE()";

            var cantidad = await conn.ExecuteScalarAsync<int>(sql, new { IdPublicacion = idPublicacion });

            return cantidad > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error consultando temporada de publicación {Id}", idPublicacion);
            throw new RepositoryException("Error verificando temporada", ex);
        }
    }

    public async Task<List<TemporadaDto>> ObtenerTemporadasActivas()
    {
        using var conn = _conexion.CreateSqlConnection();

        var sql = @"SELECT Id, Nombre, BadgeTexto, BadgeColor, FechaInicio, FechaFin
                FROM Temporadas
                WHERE Estado = 'Activo'
                ORDER BY FechaInicio DESC";

        return (await conn.QueryAsync<TemporadaDto>(sql)).ToList();
    }

    public async Task<int> CrearSugerencia(int? idUsuario, string comentario)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
            INSERT INTO Sugerencias (UsuarioId, Comentario, Fecha)
            VALUES (@UsuarioId, @Comentario, GETDATE());
            SELECT SCOPE_IDENTITY();";

            var id = await conn.ExecuteScalarAsync<int>(sql, new
            {
                UsuarioId = idUsuario,
                Comentario = comentario
            });

            _logger.LogInformation("Sugerencia creada con Id {Id} por usuario {Usuario}", id, idUsuario);

            return id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al guardar sugerencia del usuario {Usuario}", idUsuario);
            throw new RepositoryException("Error al guardar sugerencia", ex);
        }
    }

    public async Task<bool> PublicacionEstaVendida(int idPublicacion)
    {
        using var conn = _conexion.CreateSqlConnection();

        var sql = @"SELECT CASE WHEN Estado = 'Vendido' THEN 1 ELSE 0 END 
                FROM Publicaciones 
                WHERE Id = @Id";

        return await conn.ExecuteScalarAsync<bool>(sql, new { Id = idPublicacion });
    }

    public async Task MarcarComoVendido(int idPublicacion)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            var sql = @"
            UPDATE Publicaciones
            SET Estado = 'Vendido'
            WHERE Id = @IdPublicacion";

            var filas = await conn.ExecuteAsync(sql, new { IdPublicacion = idPublicacion });

            if (filas == 0)
                throw new RepositoryException("No se pudo marcar la publicación como vendida.");

            _logger.LogInformation("Publicación {IdPublicacion} marcada como VENDIDA", idPublicacion);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marcando publicación {IdPublicacion} como vendida", idPublicacion);
            throw new RepositoryException("Error al marcar como vendida", ex);
        }
    }


}

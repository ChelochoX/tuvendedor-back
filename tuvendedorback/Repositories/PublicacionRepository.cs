using System.Data;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
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
                INSERT INTO Publicaciones 
                (
                    Titulo, 
                    Descripcion, 
                    Precio,
                    Moneda,
                    Categoria,
                    CanalPublicacion,
                    IdUsuario, 
                    MostrarBotonesCompra, 
                    PermiteDelivery,
                    Fecha,
                    Ubicacion,
                    Latitud,
                    Longitud,
                    GoogleMapsUrl
                )
                VALUES 
                (
                    @Titulo, 
                    @Descripcion, 
                    @Precio,
                    @Moneda,
                    @Categoria,
                    @CanalPublicacion,
                    @IdUsuario, 
                    @MostrarBotonesCompra, 
                    @PermiteDelivery,
                    GETDATE(), 
                    @Ubicacion,
                    @Latitud,
                    @Longitud,
                    @GoogleMapsUrl
                );

                SELECT CAST(SCOPE_IDENTITY() AS INT);";

            var publicacionId = await conn.ExecuteScalarAsync<int>(
                insertPub,
                new
                {
                    request.Titulo,
                    request.Descripcion,
                    request.Precio,

                    Moneda = string.IsNullOrWhiteSpace(request.Moneda)
                        ? "PYG"
                        : request.Moneda.Trim().ToUpper(),

                    request.Categoria,

                    CanalPublicacion =
                        request.CanalPublicacion,

                    IdUsuario =
                        idUsuario,

                    request.MostrarBotonesCompra,
                    request.PermiteDelivery,

                    Ubicacion =
                        request.Ubicacion?.Trim(),

                    request.Latitud,
                    request.Longitud,

                    GoogleMapsUrl =
                        request.GoogleMapsUrl?.Trim()
                },
                tran
            );

            // =============================================================
            // VINCULO PUBLICACION -> MODELO DE PRODUCTO (MOTOS)
            // =============================================================
            var idModeloProducto = await ResolverModeloProductoParaPublicacionAsync(
                conn,
                tran,
                request.IdModeloProducto,
                request.Categoria,
                request.Titulo,
                request.Descripcion);

            if (idModeloProducto.HasValue)
            {
                await SincronizarModeloPrincipalPublicacionAsync(
                    conn,
                    tran,
                    publicacionId,
                    idModeloProducto.Value);
            }
            else if (EsCategoriaMoto(request.Categoria))
            {
                _logger.LogWarning(
                    "Publicación de moto creada sin vínculo de modelo. IdPublicacion={IdPublicacion}, Titulo={Titulo}",
                    publicacionId,
                    request.Titulo);
            }

            foreach (var img in imagenes)
            {
                await conn.ExecuteAsync(
                    @"INSERT INTO ImagenesPublicacion 
                  (
                      IdPublicacion, 
                      Url, 
                      ThumbUrl
                  ) 
                  VALUES 
                  (
                      @Id, 
                      @Url, 
                      @ThumbUrl
                  );",
                    new
                    {
                        Id = publicacionId,
                        Url = img.MainUrl,
                        ThumbUrl = img.ThumbUrl
                    },
                    tran
                );
            }

            if (request.MostrarBotonesCompra && request.PlanCredito != null)
            {
                foreach (var plan in request.PlanCredito)
                {
                    await conn.ExecuteAsync(
                        @"INSERT INTO PlanesCredito 
                      (
                          IdPublicacion, 
                          Cuotas, 
                          ValorCuota
                      )
                      VALUES 
                      (
                          @IdPublicacion, 
                          @Cuotas, 
                          @ValorCuota
                      );",
                        new
                        {
                            IdPublicacion = publicacionId,
                            Cuotas = plan.Cuotas,
                            ValorCuota = plan.ValorCuota
                        },
                        tran
                    );
                }
            }

            tran.Commit();

            _logger.LogInformation(
                "Publicación creada correctamente. IdPublicacion={IdPublicacion}, IdUsuario={IdUsuario}, Categoria={Categoria}, Ubicacion={Ubicacion}, Latitud={Latitud}, Longitud={Longitud}",
                publicacionId,
                idUsuario,
                request.Categoria,
                request.Ubicacion,
                request.Latitud,
                request.Longitud
            );

            return publicacionId;
        }
        catch (ReglasdeNegocioException)
        {
            tran.Rollback();
            throw;
        }
        catch (Exception ex)
        {
            tran.Rollback();

            _logger.LogError(
                ex,
                "Error al insertar la publicación. IdUsuario={IdUsuario}, Titulo={Titulo}, Categoria={Categoria}, Ubicacion={Ubicacion}",
                idUsuario,
                request.Titulo,
                request.Categoria,
                request.Ubicacion
            );

            throw new RepositoryException("Error al insertar la publicación", ex);
        }
    }

    public async Task<List<Publicacion>> ObtenerPublicaciones(
    string? categoria,
    string? nombre,
    int? idUsuario,
    string? visitorId)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
            SELECT 
                p.Id,
                p.Titulo              AS Titulo,
                p.Descripcion         AS Descripcion,
                p.Precio              AS Precio,
                p.Moneda              AS Moneda,
                p.Categoria           AS Categoria,
                modelo.IdModeloProducto AS IdModeloProducto,
                p.CanalPublicacion    AS CanalPublicacion,
                p.Ubicacion           AS Ubicacion,
                p.Latitud             AS Latitud,
                p.Longitud            AS Longitud,
                p.GoogleMapsUrl       AS GoogleMapsUrl,
                p.MostrarBotonesCompra,
                p.PermiteDelivery AS PermiteDelivery,
                p.Estado              AS Estado,
                v.NombreNegocio       AS VendedorNombre,
                u.Telefono            AS VendedorTelefono,

                CASE WHEN d.Id IS NOT NULL THEN 1 ELSE 0 END AS EsDestacada,
                d.FechaFin            AS FechaFinDestacado,

                CASE WHEN t.Id IS NOT NULL THEN 1 ELSE 0 END AS EsTemporada,
                t.FechaFin            AS FechaFinTemporada,
                t.BadgeTexto          AS BadgeTexto,
                t.BadgeColor          AS BadgeColor,

                (
                    SELECT COUNT(1)
                    FROM PublicacionFavoritos pf
                    WHERE pf.IdPublicacion = p.Id
                      AND pf.Activo = 1
                ) AS CantidadFavoritos,

                (
                    SELECT COUNT(1)
                    FROM PublicacionEventos pe
                    WHERE pe.IdPublicacion = p.Id
                      AND pe.TipoEvento = 'VIEW_DETAIL'
                ) AS CantidadVistas,

                (
                    SELECT COUNT(1)
                    FROM PublicacionEventos pe
                    WHERE pe.IdPublicacion = p.Id
                      AND pe.TipoEvento = 'CLICK_WHATSAPP'
                ) AS CantidadClicksWhatsapp,

                CASE 
                    WHEN EXISTS
                    (
                        SELECT 1
                        FROM PublicacionFavoritos pf
                        WHERE pf.IdPublicacion = p.Id
                          AND pf.Activo = 1
                          AND (
                                (@IdUsuario IS NOT NULL AND pf.IdUsuario = @IdUsuario)
                                OR
                                (@IdUsuario IS NULL AND @VisitorId IS NOT NULL AND pf.VisitorId = @VisitorId)
                              )
                    )
                    THEN 1
                    ELSE 0
                END AS EsFavorito

            FROM Publicaciones p
            LEFT JOIN Vendedores v ON v.IdUsuario = p.IdUsuario
            LEFT JOIN Usuarios u   ON u.Id = p.IdUsuario
            LEFT JOIN PublicacionesDestacadas d
                ON d.IdPublicacion = p.Id
                AND d.Estado = 'Activo'
                AND GETDATE() BETWEEN d.FechaInicio AND d.FechaFin
            LEFT JOIN PublicacionesTemporada t
                ON t.IdPublicacion = p.Id
                AND t.Estado = 'Activo'
                AND GETDATE() BETWEEN t.FechaInicio AND t.FechaFin
            OUTER APPLY
            (
                SELECT TOP (1)
                    pmp.IdModeloProducto
                FROM dbo.PublicacionModeloProducto pmp
                WHERE pmp.IdPublicacion = p.Id
                  AND pmp.Estado = 'Activo'
                ORDER BY
                    pmp.EsPrincipal DESC,
                    pmp.Id ASC
            ) modelo
            WHERE p.Estado = 'Activo'
               AND p.CanalPublicacion = 'MARKETPLACE'
              AND (@Categoria IS NULL OR p.Categoria = @Categoria)
              AND (
                    @Nombre IS NULL
                    OR p.Titulo LIKE '%' + @Nombre + '%'
                    OR p.Descripcion LIKE '%' + @Nombre + '%'
                    OR p.Ubicacion LIKE '%' + @Nombre + '%'
                    OR p.Categoria LIKE '%' + @Nombre + '%'
                  )
            ORDER BY
                CASE WHEN t.Id IS NOT NULL THEN 0 ELSE 1 END,
                CASE WHEN d.Id IS NOT NULL THEN 0 ELSE 1 END,
                p.Fecha DESC;";

            var publicaciones = (await conn.QueryAsync<Publicacion>(
                new CommandDefinition(
                    sql,
                    new
                    {
                        Categoria = categoria,
                        Nombre = nombre,
                        IdUsuario = idUsuario,
                        VisitorId = string.IsNullOrWhiteSpace(visitorId)
                            ? null
                            : visitorId.Trim()
                    },
                    commandTimeout: 30
                )
            )).ToList();

            if (!publicaciones.Any())
                return new List<Publicacion>();

            var ids = publicaciones.Select(x => x.Id).Distinct().ToArray();

            var imagenes = (await conn.QueryAsync<(int IdPublicacion, string Url)>(
                new CommandDefinition(
                    @"SELECT IdPublicacion, Url
                  FROM ImagenesPublicacion
                  WHERE IdPublicacion IN @Ids",
                    new { Ids = ids },
                    commandTimeout: 30
                )
            )).ToList();

            var planes = (await conn.QueryAsync<PlanCredito>(
                new CommandDefinition(
                    @"SELECT 
                    pc.Id,
                    pc.IdPublicacion AS PublicacionId,
                    pc.Cuotas,
                    pc.ValorCuota
                  FROM PlanesCredito pc
                  WHERE pc.IdPublicacion IN @Ids",
                    new { Ids = ids },
                    commandTimeout: 30
                )
            )).ToList();

            var imagenesPorPublicacion = imagenes
                .GroupBy(x => x.IdPublicacion)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Url).ToList());

            var planesPorPublicacion = planes
                .GroupBy(x => x.PublicacionId)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var pub in publicaciones)
            {
                pub.Imagenes = imagenesPorPublicacion.TryGetValue(pub.Id, out var imgs)
                    ? imgs
                    : new List<string>();

                pub.PlanCredito = planesPorPublicacion.TryGetValue(pub.Id, out var pcs)
                    ? pcs
                    : new List<PlanCredito>();
            }

            return publicaciones;
        }
        catch (SqlException ex) when (ex.Number == -2)
        {
            _logger.LogError(ex, "Timeout al obtener publicaciones del marketplace.");
            return new List<Publicacion>();
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

    public async Task<bool> PuedeCrearPublicaciones(int idUsuario)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
            SELECT COUNT(1)
            FROM UsuarioRoles ur
            INNER JOIN Roles r ON ur.IdRol = r.Id
            WHERE ur.IdUsuario = @IdUsuario
              AND r.NombreRol IN ('Vendedor', 'Administrador');";

            var cantidad = await conn.ExecuteScalarAsync<int>(
                sql,
                new { IdUsuario = idUsuario }
            );

            return cantidad > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al validar si el usuario {IdUsuario} puede crear publicaciones",
                idUsuario
            );

            throw new RepositoryException(
                "Error al validar si el usuario puede crear publicaciones",
                ex
            );
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
            _logger.LogInformation(
                "Iniciando eliminación de la publicación {IdPublicacion} del usuario {IdUsuario}",
                idPublicacion,
                idUsuario);

            const string sqlValidar = @"
            SELECT COUNT(1)
            FROM Publicaciones
            WHERE Id = @idPublicacion
              AND IdUsuario = @idUsuario;";

            var existeYPertenece = await conn.ExecuteScalarAsync<int>(
                sqlValidar,
                new { idPublicacion, idUsuario },
                tran);

            if (existeYPertenece == 0)
            {
                tran.Rollback();

                _logger.LogWarning(
                    "No se encontró la publicación {IdPublicacion} del usuario {IdUsuario} para eliminar.",
                    idPublicacion,
                    idUsuario);

                return 0;
            }

            // Solicitudes de visita asociadas a la publicación
            await conn.ExecuteAsync(
                @"DELETE FROM SolicitudesVisitaPublicacion 
              WHERE IdPublicacion = @id;",
                new { id = idPublicacion },
                tran);

            // Nuevas interacciones: favoritos de la publicación
            await conn.ExecuteAsync(
                @"DELETE FROM PublicacionFavoritos 
              WHERE IdPublicacion = @id;",
                new { id = idPublicacion },
                tran);

            // Nuevas interacciones: vistas, clicks de WhatsApp, etc.
            await conn.ExecuteAsync(
                @"DELETE FROM PublicacionEventos 
              WHERE IdPublicacion = @id;",
                new { id = idPublicacion },
                tran);

            // Destacados de la publicación
            await conn.ExecuteAsync(
                @"DELETE FROM PublicacionesDestacadas 
              WHERE IdPublicacion = @id;",
                new { id = idPublicacion },
                tran);

            // Temporadas de la publicación
            await conn.ExecuteAsync(
                @"DELETE FROM PublicacionesTemporada 
              WHERE IdPublicacion = @id;",
                new { id = idPublicacion },
                tran);

            // Planes de crédito asociados
            await conn.ExecuteAsync(
                @"DELETE FROM PlanesCredito 
              WHERE IdPublicacion = @id;",
                new { id = idPublicacion },
                tran);

            // Imágenes asociadas
            await conn.ExecuteAsync(
                @"DELETE FROM ImagenesPublicacion 
              WHERE IdPublicacion = @id;",
                new { id = idPublicacion },
                tran);

            // Publicación principal
            var filas = await conn.ExecuteAsync(
                @"DELETE FROM Publicaciones
              WHERE Id = @id
                AND IdUsuario = @idUsuario;",
                new
                {
                    id = idPublicacion,
                    idUsuario
                },
                tran);

            if (filas == 0)
            {
                tran.Rollback();

                _logger.LogWarning(
                    "No se pudo eliminar la publicación {IdPublicacion} del usuario {IdUsuario}.",
                    idPublicacion,
                    idUsuario);

                return 0;
            }

            tran.Commit();

            _logger.LogInformation(
                "Publicación {IdPublicacion} eliminada correctamente para el usuario {IdUsuario}.",
                idPublicacion,
                idUsuario);

            return filas;
        }
        catch (Exception ex)
        {
            tran.Rollback();

            _logger.LogError(
                ex,
                "Error al eliminar la publicación {IdPublicacion} del usuario {IdUsuario}",
                idPublicacion,
                idUsuario);

            throw new RepositoryException("Error al eliminar la publicación", ex);
        }
    }


    public async Task<List<Publicacion>> ObtenerMisPublicaciones(int idUsuario)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
        SELECT 
            p.Id                    AS Id,
            p.Titulo                AS Titulo,
            p.Descripcion           AS Descripcion,
            p.Precio                AS Precio,
            p.Moneda                AS Moneda,
            p.Categoria             AS Categoria,
            modelo.IdModeloProducto AS IdModeloProducto,
            p.CanalPublicacion      AS CanalPublicacion,
            p.Ubicacion             AS Ubicacion,
            p.Latitud               AS Latitud,
            p.Longitud              AS Longitud,
            p.GoogleMapsUrl         AS GoogleMapsUrl,
            p.Estado                AS Estado,
            p.MostrarBotonesCompra  AS MostrarBotonesCompra,
            p.PermiteDelivery       AS PermiteDelivery,

            v.NombreNegocio         AS VendedorNombre,
            NULL                    AS VendedorAvatar,
            NULL                    AS VendedorTelefono,

            CASE 
                WHEN d.Id IS NOT NULL THEN 1 
                ELSE 0 
            END AS EsDestacada,

            d.FechaFin AS FechaFinDestacado,

            CASE 
                WHEN pt.Id IS NOT NULL THEN 1 
                ELSE 0 
            END AS EsTemporada,

            pt.FechaFin   AS FechaFinTemporada,
            pt.BadgeTexto AS BadgeTexto,
            pt.BadgeColor AS BadgeColor,

            (
                SELECT COUNT(1)
                FROM PublicacionFavoritos pf
                WHERE pf.IdPublicacion = p.Id
                  AND pf.Activo = 1
            ) AS CantidadFavoritos,

            (
                SELECT COUNT(1)
                FROM PublicacionEventos pe
                WHERE pe.IdPublicacion = p.Id
                  AND pe.TipoEvento = 'VIEW_DETAIL'
            ) AS CantidadVistas,

            (
                SELECT COUNT(1)
                FROM PublicacionEventos pe
                WHERE pe.IdPublicacion = p.Id
                  AND pe.TipoEvento = 'CLICK_WHATSAPP'
            ) AS CantidadClicksWhatsapp,

            0 AS EsFavorito

        FROM Publicaciones p

        LEFT JOIN Vendedores v
            ON v.IdUsuario = p.IdUsuario

        OUTER APPLY
        (
            SELECT TOP 1
                pd.Id,
                pd.FechaFin
            FROM dbo.PublicacionesDestacadas pd
            WHERE pd.IdPublicacion = p.Id
              AND pd.Estado = 'Activo'
              AND GETDATE() BETWEEN pd.FechaInicio AND pd.FechaFin
            ORDER BY
                pd.FechaFin DESC,
                pd.Id DESC
        ) d

        OUTER APPLY
        (
            SELECT TOP 1
                ptemp.Id,
                ptemp.FechaFin,
                ptemp.BadgeTexto,
                ptemp.BadgeColor
            FROM dbo.PublicacionesTemporada ptemp
            WHERE ptemp.IdPublicacion = p.Id
              AND ptemp.Estado = 'Activo'
              AND GETDATE() BETWEEN ptemp.FechaInicio AND ptemp.FechaFin
            ORDER BY
                ptemp.FechaFin DESC,
                ptemp.Id DESC
        ) pt

        OUTER APPLY
        (
            SELECT TOP (1)
                pmp.IdModeloProducto
            FROM dbo.PublicacionModeloProducto pmp
            WHERE pmp.IdPublicacion = p.Id
              AND pmp.Estado = 'Activo'
            ORDER BY
                pmp.EsPrincipal DESC,
                pmp.Id ASC
        ) modelo

        WHERE p.IdUsuario = @IdUsuario

        ORDER BY
            CASE 
                WHEN d.Id IS NOT NULL THEN 0
                ELSE 1
            END,
            p.Fecha DESC;
        ";

            var publicaciones = (
                await conn.QueryAsync<Publicacion>(
                    sql,
                    new
                    {
                        IdUsuario = idUsuario
                    }
                )
            ).ToList();

            foreach (var publicacion in publicaciones)
            {
                publicacion.Imagenes = (
                    await conn.QueryAsync<string>(
                        @"
                    SELECT Url
                    FROM ImagenesPublicacion
                    WHERE IdPublicacion = @Id
                    ORDER BY Id ASC;",
                        new
                        {
                            Id = publicacion.Id
                        }
                    )
                ).ToList();

                publicacion.PlanCredito = (
                    await conn.QueryAsync<PlanCredito>(
                        @"
                    SELECT
                        Id,
                        IdPublicacion,
                        Cuotas,
                        ValorCuota
                    FROM PlanesCredito
                    WHERE IdPublicacion = @Id
                    ORDER BY Id ASC;",
                        new
                        {
                            Id = publicacion.Id
                        }
                    )
                ).ToList();
            }

            return publicaciones;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al obtener publicaciones del usuario {IdUsuario}",
                idUsuario
            );

            throw new RepositoryException(
                "Error al obtener tus publicaciones",
                ex
            );
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
            UPDATE dbo.PublicacionesDestacadas
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
                INSERT INTO dbo.PublicacionesDestacadas
                (
                    IdPublicacion,
                    FechaInicio,
                    FechaFin,
                    Estado
                )
                VALUES
                (
                    @IdPublicacion,
                    @FechaInicio,
                    @FechaFin,
                    'Activo'
                );";

                await conn.ExecuteAsync(insertSql, new
                {
                    IdPublicacion = idPublicacion,
                    FechaInicio = fechaInicio,
                    FechaFin = fechaFin
                });
            }

            _logger.LogInformation(
                "Publicación {IdPublicacion} destacada desde {Inicio} hasta {Fin}",
                idPublicacion,
                fechaInicio,
                fechaFin);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al crear/actualizar destacado para la publicación {IdPublicacion}",
                idPublicacion);

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
          AND GETDATE() BETWEEN FechaInicio AND FechaFin;";

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
        UPDATE dbo.PublicacionesDestacadas
        SET Estado = 'Inactivo'
        WHERE IdPublicacion = @IdPublicacion
          AND Estado = 'Activo';";

            var filasActualizadas = await conn.ExecuteAsync(
                sql,
                new
                {
                    IdPublicacion = idPublicacion
                }
            );

            if (filasActualizadas == 0)
            {
                throw new RepositoryException(
                    "No se encontró ningún destacado activo para quitar."
                );
            }

            _logger.LogInformation(
                "Se quitaron {Cantidad} registros destacados de la publicación {IdPublicacion}",
                filasActualizadas,
                idPublicacion
            );
        }
        catch (RepositoryException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al quitar el destacado de la publicación {IdPublicacion}",
                idPublicacion
            );

            throw new RepositoryException(
                "Error al quitar el destacado.",
                ex
            );
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
              AND GETDATE() BETWEEN FechaInicio AND FechaFin";

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

    public async Task<int> ActualizarPublicacion(
      int idPublicacion,
      int idUsuario,
      ActualizarPublicacionRequest request,
      List<ImagenDto> nuevasImagenes,
      List<string>? imagenesConservar)
    {
        using var conn = _conexion.CreateSqlConnection();
        conn.Open();
        using var tran = conn.BeginTransaction();

        try
        {
            _logger.LogInformation(
                "Iniciando actualización de la publicación {IdPublicacion} del usuario {IdUsuario}",
                idPublicacion,
                idUsuario);

            const string sqlValidar = @"
            SELECT COUNT(1)
            FROM Publicaciones
            WHERE Id = @idPublicacion
              AND IdUsuario = @idUsuario;";

            var existeYPertenece = await conn.ExecuteScalarAsync<int>(
                sqlValidar,
                new { idPublicacion, idUsuario },
                tran);

            if (existeYPertenece == 0)
            {
                tran.Rollback();

                _logger.LogWarning(
                    "No se encontró la publicación {IdPublicacion} del usuario {IdUsuario} para actualizar.",
                    idPublicacion,
                    idUsuario);

                return 0;
            }

            const string sqlUpdate = @"
                UPDATE Publicaciones
                SET
                    Titulo = @Titulo,
                    Descripcion = @Descripcion,
                    Precio = @Precio,
                    Moneda = @Moneda,
                    Categoria = @Categoria,
                    CanalPublicacion = COALESCE(@CanalPublicacion, CanalPublicacion),
                    Ubicacion = @Ubicacion,
                    Latitud = @Latitud,
                    Longitud = @Longitud,
                    GoogleMapsUrl = @GoogleMapsUrl,
                    MostrarBotonesCompra = @MostrarBotonesCompra,
                    PermiteDelivery = @PermiteDelivery
                WHERE Id = @IdPublicacion
                  AND IdUsuario = @IdUsuario;";

            var filas = await conn.ExecuteAsync(
                 sqlUpdate,
                 new
                 {
                     IdPublicacion = idPublicacion,
                     IdUsuario = idUsuario,
                     request.Titulo,
                     request.Descripcion,
                     request.Precio,
                     Moneda = string.IsNullOrWhiteSpace(request.Moneda)
                         ? "PYG"
                         : request.Moneda.Trim().ToUpper(),
                     request.Categoria,
                     CanalPublicacion =
                        string.IsNullOrWhiteSpace(
                            request.CanalPublicacion)
                            ? null
                            : request.CanalPublicacion,
                     Ubicacion = request.Ubicacion?.Trim(),
                     request.Latitud,
                     request.Longitud,
                     GoogleMapsUrl = request.GoogleMapsUrl?.Trim(),
                     request.MostrarBotonesCompra,
                     request.PermiteDelivery
                 },
                 tran);

            // Si el front envía IdModeloProducto, ese valor manda.
            // Si no lo envía y la publicación todavía no tiene vínculo,
            // intentamos resolverlo una sola vez por título/descripción.
            var idModeloProducto = request.IdModeloProducto;

            if (!idModeloProducto.HasValue && EsCategoriaMoto(request.Categoria))
            {
                var tieneVinculoActivo = await conn.ExecuteScalarAsync<int>(
                    @"SELECT COUNT(1)
                      FROM dbo.PublicacionModeloProducto
                      WHERE IdPublicacion = @IdPublicacion
                        AND Estado = 'Activo';",
                    new { IdPublicacion = idPublicacion },
                    tran);

                if (tieneVinculoActivo == 0)
                {
                    idModeloProducto = await ResolverModeloProductoParaPublicacionAsync(
                        conn,
                        tran,
                        null,
                        request.Categoria,
                        request.Titulo,
                        request.Descripcion);
                }
            }

            if (idModeloProducto.HasValue)
            {
                // Valida el modelo explícito o el resuelto antes de sincronizar.
                var modeloValidado = await ResolverModeloProductoParaPublicacionAsync(
                    conn,
                    tran,
                    idModeloProducto,
                    request.Categoria,
                    request.Titulo,
                    request.Descripcion);

                if (modeloValidado.HasValue)
                {
                    await SincronizarModeloPrincipalPublicacionAsync(
                        conn,
                        tran,
                        idPublicacion,
                        modeloValidado.Value);
                }
            }

            await conn.ExecuteAsync(
                "DELETE FROM PlanesCredito WHERE IdPublicacion = @IdPublicacion;",
                new { IdPublicacion = idPublicacion },
                tran);

            if (request.MostrarBotonesCompra && request.PlanCredito != null && request.PlanCredito.Any())
            {
                foreach (var plan in request.PlanCredito)
                {
                    await conn.ExecuteAsync(
                        @"INSERT INTO PlanesCredito
                      (
                          IdPublicacion,
                          Cuotas,
                          ValorCuota
                      )
                      VALUES
                      (
                          @IdPublicacion,
                          @Cuotas,
                          @ValorCuota
                      );",
                        new
                        {
                            IdPublicacion = idPublicacion,
                            Cuotas = plan.Cuotas,
                            ValorCuota = plan.ValorCuota
                        },
                        tran);
                }
            }

            /*
              Si imagenesConservar viene null:
              - No tocamos las imágenes actuales.
              - Solo agregamos nuevas imágenes si vinieron archivos.

              Si imagenesConservar viene con datos:
              - Conservamos solo esas URLs.
              - Borramos las demás.

              Si imagenesConservar viene lista vacía:
              - Borramos todas las imágenes actuales.
            */
            if (imagenesConservar != null)
            {
                if (imagenesConservar.Any())
                {
                    await conn.ExecuteAsync(
                        @"DELETE FROM ImagenesPublicacion
                      WHERE IdPublicacion = @IdPublicacion
                        AND Url NOT IN @ImagenesConservar;",
                        new
                        {
                            IdPublicacion = idPublicacion,
                            ImagenesConservar = imagenesConservar
                        },
                        tran);
                }
                else
                {
                    await conn.ExecuteAsync(
                        @"DELETE FROM ImagenesPublicacion
                      WHERE IdPublicacion = @IdPublicacion;",
                        new { IdPublicacion = idPublicacion },
                        tran);
                }
            }

            if (nuevasImagenes != null && nuevasImagenes.Any())
            {
                foreach (var img in nuevasImagenes)
                {
                    await conn.ExecuteAsync(
                        @"INSERT INTO ImagenesPublicacion
                      (
                          IdPublicacion,
                          Url,
                          ThumbUrl
                      )
                      VALUES
                      (
                          @IdPublicacion,
                          @Url,
                          @ThumbUrl
                      );",
                        new
                        {
                            IdPublicacion = idPublicacion,
                            Url = img.MainUrl,
                            ThumbUrl = img.ThumbUrl
                        },
                        tran);
                }
            }

            tran.Commit();

            _logger.LogInformation(
                "Publicación {IdPublicacion} actualizada correctamente para el usuario {IdUsuario}",
                idPublicacion,
                idUsuario);

            return filas;
        }
        catch (ReglasdeNegocioException)
        {
            tran.Rollback();
            throw;
        }
        catch (Exception ex)
        {
            tran.Rollback();

            _logger.LogError(
                ex,
                "Error al actualizar la publicación {IdPublicacion} del usuario {IdUsuario}",
                idPublicacion,
                idUsuario);

            throw new RepositoryException("Error al actualizar la publicación", ex);
        }
    }

    public async Task<ProductoSharePreviewDto?>
    ObtenerProductoSharePreview(
        int idPublicacion)
    {
        using var conn =
            _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
            SELECT TOP 1
                p.Id,
                p.Titulo,
                p.Descripcion,
                p.Precio,
                p.Moneda,
                p.Categoria,
                p.Ubicacion,
                p.CanalPublicacion,

                img.Url
                    AS ImagenUrl,

                v.Slug
                    AS SlugVendedor,

                v.NombreNegocio
                    AS NombreVendedor

            FROM dbo.Publicaciones p

            LEFT JOIN dbo.Vendedores v
                ON v.IdUsuario =
                    p.IdUsuario

            LEFT JOIN dbo.Usuarios u
                ON u.Id =
                    p.IdUsuario

            OUTER APPLY
            (
                SELECT TOP 1
                    i.Url

                FROM dbo.ImagenesPublicacion i

                WHERE i.IdPublicacion =
                    p.Id

                ORDER BY i.Id ASC
            ) img

            WHERE p.Id =
                @IdPublicacion

              AND p.Estado =
                'Activo'

              AND
              (
                    p.CanalPublicacion =
                        'MARKETPLACE'

                    OR

                    (
                        p.CanalPublicacion =
                            'VITRINA'

                        AND v.EsPremium = 1

                        AND v.EsPerfilPublico = 1

                        AND NULLIF(
                            LTRIM(
                                RTRIM(v.Slug)
                            ),
                            ''
                        ) IS NOT NULL

                        AND u.Estado =
                            'Activo'
                    )
              );";

            return await conn
                .QueryFirstOrDefaultAsync<
                    ProductoSharePreviewDto
                >(
                    sql,
                    new
                    {
                        IdPublicacion =
                            idPublicacion
                    });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al obtener preview Open Graph para publicación {IdPublicacion}",
                idPublicacion);

            throw new RepositoryException(
                "Error al obtener preview de publicación",
                ex);
        }
    }

    public async Task<bool> PuedePublicarEnVitrina(
    int idUsuario)
    {
        using var conn =
            _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
            SELECT COUNT(1)
            FROM dbo.Vendedores
            WHERE IdUsuario = @IdUsuario
              AND EsPremium = 1;";

            var cantidad =
                await conn.ExecuteScalarAsync<int>(
                    sql,
                    new
                    {
                        IdUsuario =
                            idUsuario
                    });

            return cantidad > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al validar acceso a vitrina del usuario {IdUsuario}",
                idUsuario);

            throw new RepositoryException(
                "Error al validar acceso a la vitrina.",
                ex);
        }
    }

    public async Task<string?>
        ObtenerCanalPublicacionDeUsuario(
            int idPublicacion,
            int idUsuario)
    {
        using var conn =
            _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
            SELECT CanalPublicacion
            FROM dbo.Publicaciones
            WHERE Id = @IdPublicacion
              AND IdUsuario = @IdUsuario;";

            return await conn
                .QueryFirstOrDefaultAsync<string?>(
                    sql,
                    new
                    {
                        IdPublicacion =
                            idPublicacion,

                        IdUsuario =
                            idUsuario
                    });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al obtener canal de publicación {IdPublicacion}",
                idPublicacion);

            throw new RepositoryException(
                "Error al obtener el canal de la publicación.",
                ex);
        }
    }



    // =========================================================
    // VINCULO PUBLICACION -> MODELO DE PRODUCTO
    // =========================================================

    private async Task<int?> ResolverModeloProductoParaPublicacionAsync(
        IDbConnection conn,
        IDbTransaction tran,
        int? idModeloProductoSolicitado,
        string? categoria,
        string? titulo,
        string? descripcion)
    {
        if (!EsCategoriaMoto(categoria) && !idModeloProductoSolicitado.HasValue)
        {
            return null;
        }

        if (idModeloProductoSolicitado.HasValue)
        {
            const string sqlValidarModelo = @"
SELECT TOP (1)
    mp.Id
FROM dbo.ModelosProducto mp
INNER JOIN dbo.Marcas m
    ON m.Id = mp.IdMarca
WHERE mp.Id = @IdModeloProducto
  AND mp.Estado = 'Activo'
  AND m.Estado = 'Activo';";

            var idValido = await conn.QueryFirstOrDefaultAsync<int?>(
                sqlValidarModelo,
                new
                {
                    IdModeloProducto = idModeloProductoSolicitado.Value
                },
                tran);

            if (!idValido.HasValue)
            {
                throw new ReglasdeNegocioException(
                    $"El modelo de producto {idModeloProductoSolicitado.Value} no existe o está inactivo.");
            }

            return idValido.Value;
        }

        if (!EsCategoriaMoto(categoria))
        {
            return null;
        }

        const string sqlModelos = @"
SELECT
    mp.Id AS IdModeloProducto,
    m.Nombre AS Marca,
    mp.NombreModelo AS Modelo
FROM dbo.ModelosProducto mp
INNER JOIN dbo.Marcas m
    ON m.Id = mp.IdMarca
WHERE mp.Estado = 'Activo'
  AND m.Estado = 'Activo';";

        var modelos = (
            await conn.QueryAsync<ModeloVinculoPublicacionData>(
                sqlModelos,
                transaction: tran)
        ).ToList();

        // Primero usamos SOLO el título. Es la fuente más confiable y evita
        // que una descripción que mencione otros modelos genere un vínculo
        // incorrecto.
        var elegido = ElegirModeloUnicoPorTexto(
            modelos,
            titulo);

        // Compatibilidad con publicaciones antiguas donde el modelo quedó
        // únicamente en la descripción.
        elegido ??= ElegirModeloUnicoPorTexto(
            modelos,
            $"{titulo} {descripcion}");

        if (elegido is null)
        {
            _logger.LogWarning(
                "No se pudo resolver un modelo único para publicación de moto. Titulo={Titulo}",
                titulo);

            return null;
        }

        _logger.LogInformation(
            "Modelo resuelto automáticamente para publicación. IdModeloProducto={IdModeloProducto}, Marca={Marca}, Modelo={Modelo}, Titulo={Titulo}",
            elegido.IdModeloProducto,
            elegido.Marca,
            elegido.Modelo,
            titulo);

        return elegido.IdModeloProducto;
    }


    private static ModeloVinculoPublicacionData? ElegirModeloUnicoPorTexto(
        IReadOnlyCollection<ModeloVinculoPublicacionData> modelos,
        string? textoOrigen)
    {
        var texto = NormalizarTextoModelo(textoOrigen);

        if (string.IsNullOrWhiteSpace(texto))
        {
            return null;
        }

        var candidatos = modelos
            .Select(x => new
            {
                Modelo = x,
                MarcaNormalizada = NormalizarTextoModelo(x.Marca),
                ModeloNormalizado = NormalizarTextoModelo(x.Modelo)
            })
            .Where(x =>
                !string.IsNullOrWhiteSpace(x.ModeloNormalizado)
                && ContieneFraseModelo(texto, x.ModeloNormalizado))
            .ToList();

        if (candidatos.Count == 0)
        {
            return null;
        }

        var candidatosConMarca = candidatos
            .Where(x =>
                !string.IsNullOrWhiteSpace(x.MarcaNormalizada)
                && ContieneFraseModelo(texto, x.MarcaNormalizada))
            .ToList();

        if (candidatosConMarca.Count > 0)
        {
            candidatos = candidatosConMarca;
        }

        // Si hay una variante más específica (por ejemplo black edition),
        // la coincidencia con el nombre de modelo más largo tiene prioridad.
        var longitudMaxima = candidatos.Max(x => x.ModeloNormalizado.Length);

        var mejores = candidatos
            .Where(x => x.ModeloNormalizado.Length == longitudMaxima)
            .GroupBy(x => x.Modelo.IdModeloProducto)
            .Select(g => g.First().Modelo)
            .ToList();

        return mejores.Count == 1
            ? mejores[0]
            : null;
    }

    private async Task SincronizarModeloPrincipalPublicacionAsync(
        IDbConnection conn,
        IDbTransaction tran,
        int idPublicacion,
        int idModeloProducto)
    {
        const string sql = @"
UPDATE dbo.PublicacionModeloProducto
SET
    Estado = 'Inactivo',
    EsPrincipal = 0
WHERE IdPublicacion = @IdPublicacion;

DECLARE @IdVinculo INT;

SELECT TOP (1)
    @IdVinculo = Id
FROM dbo.PublicacionModeloProducto
WHERE IdPublicacion = @IdPublicacion
  AND IdModeloProducto = @IdModeloProducto
ORDER BY Id ASC;

IF @IdVinculo IS NULL
BEGIN
    INSERT INTO dbo.PublicacionModeloProducto
    (
        IdPublicacion,
        IdModeloProducto,
        EsPrincipal,
        Estado
    )
    VALUES
    (
        @IdPublicacion,
        @IdModeloProducto,
        1,
        'Activo'
    );
END
ELSE
BEGIN
    UPDATE dbo.PublicacionModeloProducto
    SET
        Estado = 'Activo',
        EsPrincipal = 1
    WHERE Id = @IdVinculo;
END;";

        await conn.ExecuteAsync(
            sql,
            new
            {
                IdPublicacion = idPublicacion,
                IdModeloProducto = idModeloProducto
            },
            tran);

        _logger.LogInformation(
            "Vínculo publicación-modelo sincronizado. IdPublicacion={IdPublicacion}, IdModeloProducto={IdModeloProducto}",
            idPublicacion,
            idModeloProducto);
    }

    private static bool EsCategoriaMoto(string? categoria)
    {
        if (string.IsNullOrWhiteSpace(categoria))
        {
            return false;
        }

        var valor = NormalizarTextoModelo(categoria);

        return valor == "vehiculos motos"
            || valor == "motos"
            || valor.EndsWith(" motos", StringComparison.Ordinal)
            || valor.Contains(" motocicleta", StringComparison.Ordinal)
            || valor.Contains(" motocicletas", StringComparison.Ordinal);
    }

    private static string NormalizarTextoModelo(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return string.Empty;
        }

        var normalizado = valor
            .Normalize(NormalizationForm.FormD);

        var sb = new StringBuilder(normalizado.Length);

        foreach (var c in normalizado)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }

        var limpio = Regex.Replace(
            sb.ToString().Normalize(NormalizationForm.FormC),
            @"[^a-z0-9]+",
            " ");

        return Regex.Replace(limpio, @"\s+", " ").Trim();
    }

    private static bool ContieneFraseModelo(string texto, string frase)
    {
        if (string.IsNullOrWhiteSpace(texto) || string.IsNullOrWhiteSpace(frase))
        {
            return false;
        }

        return $" {texto} ".Contains(
            $" {frase} ",
            StringComparison.Ordinal);
    }

    private sealed class ModeloVinculoPublicacionData
    {
        public int IdModeloProducto { get; set; }
        public string Marca { get; set; } = string.Empty;
        public string Modelo { get; set; } = string.Empty;
    }
}

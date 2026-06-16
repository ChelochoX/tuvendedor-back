using Dapper;
using tuvendedorback.Data;
using tuvendedorback.DTOs;
using tuvendedorback.Exceptions;
using tuvendedorback.Repositories.Interfaces;

namespace tuvendedorback.Repositories;

public class ComercialDashboardRepository
    : IComercialDashboardRepository
{
    private readonly DbConnections _conexion;

    private readonly ILogger<ComercialDashboardRepository> _logger;

    public ComercialDashboardRepository(
        DbConnections conexion,
        ILogger<ComercialDashboardRepository> logger)
    {
        _conexion = conexion;
        _logger = logger;
    }

    public async Task<ComercialDashboardDto> ObtenerDashboard(
        DateTime fechaDesde,
        DateTime fechaHasta)
    {
        const string sql = @"
            DECLARE @Desde DATETIME =
                @FechaDesde;

            DECLARE @HastaExclusiva DATETIME =
                DATEADD(DAY, 1, CAST(@FechaHasta AS DATE));

            DECLARE @Ahora DATETIME2(0) =
                SYSDATETIME();

            /*
              ==================================================
              RESUMEN GENERAL
              ==================================================
            */
            SELECT
                (
                    SELECT COUNT(1)
                    FROM dbo.Usuarios
                    WHERE Estado = 'Activo'
                ) AS TotalUsuariosRegistrados,

                (
                    SELECT COUNT(1)
                    FROM dbo.Vendedores
                ) AS TotalVendedores,

                (
                    SELECT COUNT(1)
                    FROM dbo.Publicaciones
                ) AS PublicacionesTotales,

                (
                    SELECT COUNT(1)
                    FROM dbo.Publicaciones
                    WHERE Estado = 'Activo'
                ) AS PublicacionesActivas,

                (
                    SELECT COUNT(1)
                    FROM dbo.PublicacionesDestacadas
                    WHERE Estado = 'Activo'
                      AND FechaInicio <= @Ahora
                      AND FechaFin >= @Ahora
                ) AS PublicacionesDestacadasActivas,

                (
                    SELECT COUNT_BIG(1)
                    FROM dbo.PublicacionEventos
                    WHERE TipoEvento = 'VIEW_DETAIL'
                      AND FechaCreacion >= @Desde
                      AND FechaCreacion < @HastaExclusiva
                ) AS VistasPublicaciones,

                (
                    SELECT COUNT_BIG(1)
                    FROM dbo.PublicacionEventos
                    WHERE TipoEvento = 'CLICK_WHATSAPP'
                      AND FechaCreacion >= @Desde
                      AND FechaCreacion < @HastaExclusiva
                ) AS ClicksWhatsappPublicaciones,

                (
                    SELECT COUNT_BIG(1)
                    FROM dbo.PublicacionFavoritos
                    WHERE Activo = 1
                ) AS FavoritosActivos,

                (
                    SELECT COUNT_BIG(1)
                    FROM dbo.SolicitudesVisitaPublicacion
                    WHERE FechaSolicitud >= @Desde
                      AND FechaSolicitud < @HastaExclusiva
                ) AS SolicitudesVisita,

                (
                    SELECT COUNT(1)
                    FROM dbo.BannersPublicitarios
                    WHERE Eliminado = 0
                      AND Estado = 'ACTIVO'
                      AND FechaInicio <= @Ahora
                      AND FechaFin >= @Ahora
                ) AS BannersActivos,

                (
                    SELECT COUNT_BIG(1)
                    FROM dbo.BannerEventos
                    WHERE TipoEvento = 'IMPRESION'
                      AND FechaCreacion >= @Desde
                      AND FechaCreacion < @HastaExclusiva
                ) AS ImpresionesBanners,

                (
                    SELECT COUNT_BIG(1)
                    FROM dbo.BannerEventos
                    WHERE TipoEvento = 'CLICK'
                      AND FechaCreacion >= @Desde
                      AND FechaCreacion < @HastaExclusiva
                ) AS ClicksBanners,

                (
                    SELECT COUNT_BIG(1)
                    FROM dbo.BannerEventos
                    WHERE TipoEvento = 'WHATSAPP'
                      AND FechaCreacion >= @Desde
                      AND FechaCreacion < @HastaExclusiva
                ) AS WhatsAppBanners,

                CAST
                (
                    CASE
                        WHEN
                        (
                            SELECT COUNT_BIG(1)
                            FROM dbo.BannerEventos
                            WHERE TipoEvento = 'IMPRESION'
                              AND FechaCreacion >= @Desde
                              AND FechaCreacion < @HastaExclusiva
                        ) = 0
                        THEN 0
                        ELSE
                        (
                            SELECT COUNT_BIG(1)
                            FROM dbo.BannerEventos
                            WHERE TipoEvento = 'CLICK'
                              AND FechaCreacion >= @Desde
                              AND FechaCreacion < @HastaExclusiva
                        ) * 100.0
                        /
                        (
                            SELECT COUNT_BIG(1)
                            FROM dbo.BannerEventos
                            WHERE TipoEvento = 'IMPRESION'
                              AND FechaCreacion >= @Desde
                              AND FechaCreacion < @HastaExclusiva
                        )
                    END
                    AS DECIMAL(10, 2)
                ) AS CtrBanners,

                CAST
                (
                    CASE
                        WHEN
                        (
                            SELECT COUNT_BIG(1)
                            FROM dbo.PublicacionEventos
                            WHERE TipoEvento = 'VIEW_DETAIL'
                              AND FechaCreacion >= @Desde
                              AND FechaCreacion < @HastaExclusiva
                        ) = 0
                        THEN 0
                        ELSE
                        (
                            SELECT COUNT_BIG(1)
                            FROM dbo.PublicacionEventos
                            WHERE TipoEvento = 'CLICK_WHATSAPP'
                              AND FechaCreacion >= @Desde
                              AND FechaCreacion < @HastaExclusiva
                        ) * 100.0
                        /
                        (
                            SELECT COUNT_BIG(1)
                            FROM dbo.PublicacionEventos
                            WHERE TipoEvento = 'VIEW_DETAIL'
                              AND FechaCreacion >= @Desde
                              AND FechaCreacion < @HastaExclusiva
                        )
                    END
                    AS DECIMAL(10, 2)
                ) AS TasaWhatsappPublicaciones;

            /*
              ==================================================
              SERIE DIARIA
              ==================================================
            */
            SELECT
                Fecha,
                SUM(VistasPublicaciones) AS VistasPublicaciones,
                SUM(ClicksWhatsappPublicaciones) AS ClicksWhatsappPublicaciones,
                SUM(ImpresionesBanners) AS ImpresionesBanners,
                SUM(ClicksBanners) AS ClicksBanners,
                SUM(WhatsAppBanners) AS WhatsAppBanners,
                SUM(SolicitudesVisita) AS SolicitudesVisita
            FROM
            (
                SELECT
                    CAST(FechaCreacion AS DATE) AS Fecha,
                    COUNT_BIG(1) AS VistasPublicaciones,
                    CAST(0 AS BIGINT) AS ClicksWhatsappPublicaciones,
                    CAST(0 AS BIGINT) AS ImpresionesBanners,
                    CAST(0 AS BIGINT) AS ClicksBanners,
                    CAST(0 AS BIGINT) AS WhatsAppBanners,
                    CAST(0 AS BIGINT) AS SolicitudesVisita
                FROM dbo.PublicacionEventos
                WHERE TipoEvento = 'VIEW_DETAIL'
                  AND FechaCreacion >= @Desde
                  AND FechaCreacion < @HastaExclusiva
                GROUP BY CAST(FechaCreacion AS DATE)

                UNION ALL

                SELECT
                    CAST(FechaCreacion AS DATE) AS Fecha,
                    0,
                    COUNT_BIG(1),
                    0,
                    0,
                    0,
                    0
                FROM dbo.PublicacionEventos
                WHERE TipoEvento = 'CLICK_WHATSAPP'
                  AND FechaCreacion >= @Desde
                  AND FechaCreacion < @HastaExclusiva
                GROUP BY CAST(FechaCreacion AS DATE)

                UNION ALL

                SELECT
                    CAST(FechaCreacion AS DATE) AS Fecha,
                    0,
                    0,
                    COUNT_BIG(1),
                    0,
                    0,
                    0
                FROM dbo.BannerEventos
                WHERE TipoEvento = 'IMPRESION'
                  AND FechaCreacion >= @Desde
                  AND FechaCreacion < @HastaExclusiva
                GROUP BY CAST(FechaCreacion AS DATE)

                UNION ALL

                SELECT
                    CAST(FechaCreacion AS DATE) AS Fecha,
                    0,
                    0,
                    0,
                    COUNT_BIG(1),
                    0,
                    0
                FROM dbo.BannerEventos
                WHERE TipoEvento = 'CLICK'
                  AND FechaCreacion >= @Desde
                  AND FechaCreacion < @HastaExclusiva
                GROUP BY CAST(FechaCreacion AS DATE)

                UNION ALL

                SELECT
                    CAST(FechaCreacion AS DATE) AS Fecha,
                    0,
                    0,
                    0,
                    0,
                    COUNT_BIG(1),
                    0
                FROM dbo.BannerEventos
                WHERE TipoEvento = 'WHATSAPP'
                  AND FechaCreacion >= @Desde
                  AND FechaCreacion < @HastaExclusiva
                GROUP BY CAST(FechaCreacion AS DATE)

                UNION ALL

                SELECT
                    CAST(FechaSolicitud AS DATE) AS Fecha,
                    0,
                    0,
                    0,
                    0,
                    0,
                    COUNT_BIG(1)
                FROM dbo.SolicitudesVisitaPublicacion
                WHERE FechaSolicitud >= @Desde
                  AND FechaSolicitud < @HastaExclusiva
                GROUP BY CAST(FechaSolicitud AS DATE)
            ) datos
            GROUP BY Fecha
            ORDER BY Fecha;

            /*
              ==================================================
              TOP PUBLICACIONES
              ==================================================
            */
            SELECT TOP 10
                p.Id AS IdPublicacion,
                p.Titulo,
                p.Categoria,
                ISNULL(u.NombreUsuario, '') AS Vendedor,

                SUM
                (
                    CASE
                        WHEN pe.TipoEvento = 'VIEW_DETAIL'
                        THEN 1
                        ELSE 0
                    END
                ) AS Vistas,

                SUM
                (
                    CASE
                        WHEN pe.TipoEvento = 'CLICK_WHATSAPP'
                        THEN 1
                        ELSE 0
                    END
                ) AS ClicksWhatsapp,

                ISNULL
                (
                    (
                        SELECT COUNT_BIG(1)
                        FROM dbo.PublicacionFavoritos pf
                        WHERE pf.IdPublicacion = p.Id
                          AND pf.Activo = 1
                    ),
                    0
                ) AS Favoritos

            FROM dbo.Publicaciones p

            LEFT JOIN dbo.Usuarios u
                ON u.Id = p.IdUsuario

            LEFT JOIN dbo.PublicacionEventos pe
                ON pe.IdPublicacion = p.Id
               AND pe.FechaCreacion >= @Desde
               AND pe.FechaCreacion < @HastaExclusiva

            WHERE p.Estado = 'Activo'

            GROUP BY
                p.Id,
                p.Titulo,
                p.Categoria,
                u.NombreUsuario

            ORDER BY
                Vistas DESC,
                ClicksWhatsapp DESC,
                Favoritos DESC;

            /*
              ==================================================
              TOP BANNERS
              ==================================================
            */
            SELECT TOP 10
                b.Id AS IdBanner,
                b.NombreCliente,
                b.Titulo,
                b.Ubicacion,

                SUM
                (
                    CASE
                        WHEN be.TipoEvento = 'IMPRESION'
                        THEN 1
                        ELSE 0
                    END
                ) AS Impresiones,

                SUM
                (
                    CASE
                        WHEN be.TipoEvento = 'CLICK'
                        THEN 1
                        ELSE 0
                    END
                ) AS Clicks,

                SUM
                (
                    CASE
                        WHEN be.TipoEvento = 'WHATSAPP'
                        THEN 1
                        ELSE 0
                    END
                ) AS WhatsApp,

                CAST
                (
                    CASE
                        WHEN SUM
                        (
                            CASE
                                WHEN be.TipoEvento = 'IMPRESION'
                                THEN 1
                                ELSE 0
                            END
                        ) = 0
                        THEN 0
                        ELSE SUM
                        (
                            CASE
                                WHEN be.TipoEvento = 'CLICK'
                                THEN 1
                                ELSE 0
                            END
                        ) * 100.0
                        /
                        SUM
                        (
                            CASE
                                WHEN be.TipoEvento = 'IMPRESION'
                                THEN 1
                                ELSE 0
                            END
                        )
                    END
                    AS DECIMAL(10, 2)
                ) AS Ctr

            FROM dbo.BannersPublicitarios b

            LEFT JOIN dbo.BannerEventos be
                ON be.IdBanner = b.Id
               AND be.FechaCreacion >= @Desde
               AND be.FechaCreacion < @HastaExclusiva

            WHERE b.Eliminado = 0

            GROUP BY
                b.Id,
                b.NombreCliente,
                b.Titulo,
                b.Ubicacion

            ORDER BY
                Impresiones DESC,
                Clicks DESC,
                WhatsApp DESC;

            /*
              ==================================================
              RUBROS / CATEGORÍAS
              ==================================================
            */
            SELECT TOP 10
                p.Categoria AS Rubro,

                COUNT(DISTINCT p.Id) AS PublicacionesActivas,

                SUM
                (
                    CASE
                        WHEN pe.TipoEvento = 'VIEW_DETAIL'
                        THEN 1
                        ELSE 0
                    END
                ) AS Vistas,

                SUM
                (
                    CASE
                        WHEN pe.TipoEvento = 'CLICK_WHATSAPP'
                        THEN 1
                        ELSE 0
                    END
                ) AS ClicksWhatsapp

            FROM dbo.Publicaciones p

            LEFT JOIN dbo.PublicacionEventos pe
                ON pe.IdPublicacion = p.Id
               AND pe.FechaCreacion >= @Desde
               AND pe.FechaCreacion < @HastaExclusiva

            WHERE p.Estado = 'Activo'

            GROUP BY
                p.Categoria

            ORDER BY
                Vistas DESC,
                ClicksWhatsapp DESC,
                PublicacionesActivas DESC;
        ";

        try
        {
            using var conn =
                _conexion.CreateSqlConnection();

            using var multi =
                await conn.QueryMultipleAsync(
                    sql,
                    new
                    {
                        FechaDesde =
                            fechaDesde.Date,

                        FechaHasta =
                            fechaHasta.Date
                    });

            var resumen =
                await multi.ReadSingleAsync<ComercialDashboardResumenDto>();

            var serieDiaria =
                (
                    await multi.ReadAsync<ComercialDashboardSerieDiariaDto>()
                ).ToList();

            var topPublicaciones =
                (
                    await multi.ReadAsync<ComercialDashboardTopPublicacionDto>()
                ).ToList();

            var topBanners =
                (
                    await multi.ReadAsync<ComercialDashboardTopBannerDto>()
                ).ToList();

            var rubros =
                (
                    await multi.ReadAsync<ComercialDashboardRubroDto>()
                ).ToList();

            return new ComercialDashboardDto
            {
                FechaDesde =
                    fechaDesde.Date,

                FechaHasta =
                    fechaHasta.Date,

                Resumen =
                    resumen,

                SerieDiaria =
                    serieDiaria,

                TopPublicaciones =
                    topPublicaciones,

                TopBanners =
                    topBanners,

                Rubros =
                    rubros
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al obtener dashboard comercial.");

            throw new RepositoryException(
                "Error al obtener el dashboard comercial.",
                ex);
        }
    }

    public async Task<bool> EsAdministrador(
        int idUsuario)
    {
        const string sql = @"
            SELECT COUNT(1)

            FROM dbo.UsuarioRoles ur

            INNER JOIN dbo.Roles r
                ON r.Id = ur.IdRol

            WHERE ur.IdUsuario = @IdUsuario
              AND r.NombreRol = 'Administrador';
        ";

        try
        {
            using var conn =
                _conexion.CreateSqlConnection();

            var total =
                await conn.ExecuteScalarAsync<int>(
                    sql,
                    new
                    {
                        IdUsuario =
                            idUsuario
                    });

            return total > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al validar administrador para dashboard comercial.");

            throw new RepositoryException(
                "Error al validar el acceso administrativo.",
                ex);
        }
    }
}

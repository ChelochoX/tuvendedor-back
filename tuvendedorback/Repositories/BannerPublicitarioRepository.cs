using Dapper;
using tuvendedorback.Data;
using tuvendedorback.DTOs;
using tuvendedorback.Exceptions;
using tuvendedorback.Repositories.Interfaces;
using tuvendedorback.Request;

namespace tuvendedorback.Repositories;

public class BannerPublicitarioRepository
    : IBannerPublicitarioRepository
{
    private readonly DbConnections _conexion;

    private readonly ILogger<BannerPublicitarioRepository> _logger;

    public BannerPublicitarioRepository(
        DbConnections conexion,
        ILogger<BannerPublicitarioRepository> logger)
    {
        _conexion = conexion;
        _logger = logger;
    }

    public async Task<int> Crear(
        CrearBannerPublicitarioRequest request,
        int idUsuario,
        string imagenDesktopUrl,
        string? imagenMobileUrl)
    {
        const string sql = @"
            INSERT INTO dbo.BannersPublicitarios
            (
                NombreCliente,
                Ubicacion,
                Titulo,
                Subtitulo,
                Descripcion,
                Etiqueta,
                TextoBoton,
                ImagenDesktopUrl,
                ImagenMobileUrl,
                UrlDestino,
                WhatsappUrl,
                FechaInicio,
                FechaFin,
                Estado,
                Orden,
                Prioridad,
                EsExclusivo,
                AbrirNuevaPestana,
                CreadoPor,
                FechaCreacion,
                Eliminado
            )
            VALUES
            (
                @NombreCliente,
                @Ubicacion,
                @Titulo,
                @Subtitulo,
                @Descripcion,
                @Etiqueta,
                @TextoBoton,
                @ImagenDesktopUrl,
                @ImagenMobileUrl,
                @UrlDestino,
                @WhatsappUrl,
                @FechaInicio,
                @FechaFin,
                @Estado,
                @Orden,
                @Prioridad,
                @EsExclusivo,
                @AbrirNuevaPestana,
                @CreadoPor,
                SYSDATETIME(),
                0
            );

            SELECT CAST(SCOPE_IDENTITY() AS INT);";

        try
        {
            using var conn = _conexion.CreateSqlConnection();

            return await conn.ExecuteScalarAsync<int>(
                sql,
                new
                {
                    request.NombreCliente,
                    request.Ubicacion,
                    request.Titulo,
                    request.Subtitulo,
                    request.Descripcion,
                    request.Etiqueta,
                    request.TextoBoton,

                    ImagenDesktopUrl = imagenDesktopUrl,
                    ImagenMobileUrl = imagenMobileUrl,

                    request.UrlDestino,
                    request.WhatsappUrl,
                    request.FechaInicio,
                    request.FechaFin,
                    request.Estado,
                    request.Orden,
                    request.Prioridad,
                    request.EsExclusivo,
                    request.AbrirNuevaPestana,

                    CreadoPor = idUsuario
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al crear banner publicitario.");

            throw new RepositoryException(
                "Error al crear el banner publicitario.",
                ex);
        }
    }

    public async Task<int> Actualizar(
        int id,
        ActualizarBannerPublicitarioRequest request,
        int idUsuario,
        string imagenDesktopUrl,
        string? imagenMobileUrl)
    {
        const string sql = @"
            UPDATE dbo.BannersPublicitarios
            SET NombreCliente = @NombreCliente,
                Ubicacion = @Ubicacion,
                Titulo = @Titulo,
                Subtitulo = @Subtitulo,
                Descripcion = @Descripcion,
                Etiqueta = @Etiqueta,
                TextoBoton = @TextoBoton,
                ImagenDesktopUrl = @ImagenDesktopUrl,
                ImagenMobileUrl = @ImagenMobileUrl,
                UrlDestino = @UrlDestino,
                WhatsappUrl = @WhatsappUrl,
                FechaInicio = @FechaInicio,
                FechaFin = @FechaFin,
                Estado = @Estado,
                Orden = @Orden,
                Prioridad = @Prioridad,
                EsExclusivo = @EsExclusivo,
                AbrirNuevaPestana = @AbrirNuevaPestana,
                ModificadoPor = @ModificadoPor,
                FechaModificacion = SYSDATETIME()
            WHERE Id = @Id
              AND Eliminado = 0;";

        try
        {
            using var conn = _conexion.CreateSqlConnection();

            return await conn.ExecuteAsync(
                sql,
                new
                {
                    Id = id,

                    request.NombreCliente,
                    request.Ubicacion,
                    request.Titulo,
                    request.Subtitulo,
                    request.Descripcion,
                    request.Etiqueta,
                    request.TextoBoton,

                    ImagenDesktopUrl = imagenDesktopUrl,
                    ImagenMobileUrl = imagenMobileUrl,

                    request.UrlDestino,
                    request.WhatsappUrl,
                    request.FechaInicio,
                    request.FechaFin,
                    request.Estado,
                    request.Orden,
                    request.Prioridad,
                    request.EsExclusivo,
                    request.AbrirNuevaPestana,

                    ModificadoPor = idUsuario
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al actualizar banner publicitario. IdBanner={IdBanner}",
                id);

            throw new RepositoryException(
                "Error al actualizar el banner publicitario.",
                ex);
        }
    }

    public async Task<int> CambiarEstado(
        int id,
        string estado,
        int idUsuario)
    {
        const string sql = @"
            UPDATE dbo.BannersPublicitarios
            SET Estado = @Estado,
                ModificadoPor = @ModificadoPor,
                FechaModificacion = SYSDATETIME()
            WHERE Id = @Id
              AND Eliminado = 0;";

        try
        {
            using var conn = _conexion.CreateSqlConnection();

            return await conn.ExecuteAsync(
                sql,
                new
                {
                    Id = id,
                    Estado = estado,
                    ModificadoPor = idUsuario
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al cambiar estado de banner. IdBanner={IdBanner}",
                id);

            throw new RepositoryException(
                "Error al cambiar el estado del banner publicitario.",
                ex);
        }
    }

    public async Task<int> Eliminar(
        int id,
        int idUsuario)
    {
        const string sql = @"
            UPDATE dbo.BannersPublicitarios
            SET Eliminado = 1,
                Estado = 'PAUSADO',
                ModificadoPor = @ModificadoPor,
                FechaModificacion = SYSDATETIME()
            WHERE Id = @Id
              AND Eliminado = 0;";

        try
        {
            using var conn = _conexion.CreateSqlConnection();

            return await conn.ExecuteAsync(
                sql,
                new
                {
                    Id = id,
                    ModificadoPor = idUsuario
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al eliminar banner publicitario. IdBanner={IdBanner}",
                id);

            throw new RepositoryException(
                "Error al eliminar el banner publicitario.",
                ex);
        }
    }

    public async Task<BannerPublicitarioDto?> ObtenerPorId(int id)
    {
        const string sql = @"
            DECLARE @Ahora DATETIME2(0) = SYSDATETIME();

            SELECT
                b.Id,
                b.NombreCliente,
                b.Ubicacion,
                b.Titulo,
                b.Subtitulo,
                b.Descripcion,
                b.Etiqueta,
                b.TextoBoton,
                b.ImagenDesktopUrl,
                b.ImagenMobileUrl,
                b.UrlDestino,
                b.WhatsappUrl,
                b.FechaInicio,
                b.FechaFin,
                b.Estado,

                CASE
                    WHEN b.Estado = 'BORRADOR' THEN 'BORRADOR'
                    WHEN b.Estado = 'PAUSADO' THEN 'PAUSADO'
                    WHEN @Ahora < b.FechaInicio THEN 'PROGRAMADO'
                    WHEN @Ahora > b.FechaFin THEN 'VENCIDO'
                    ELSE 'ACTIVO'
                END AS EstadoVigencia,

                b.Orden,
                b.Prioridad,
                b.EsExclusivo,
                b.AbrirNuevaPestana,
                b.FechaCreacion,
                b.FechaModificacion,

                ISNULL(
                    SUM(
                        CASE
                            WHEN e.TipoEvento = 'IMPRESION'
                            THEN 1
                            ELSE 0
                        END
                    ),
                    0
                ) AS CantidadImpresiones,

                ISNULL(
                    SUM(
                        CASE
                            WHEN e.TipoEvento = 'CLICK'
                            THEN 1
                            ELSE 0
                        END
                    ),
                    0
                ) AS CantidadClicks,

                ISNULL(
                    SUM(
                        CASE
                            WHEN e.TipoEvento = 'WHATSAPP'
                            THEN 1
                            ELSE 0
                        END
                    ),
                    0
                ) AS CantidadWhatsApp

            FROM dbo.BannersPublicitarios b

            LEFT JOIN dbo.BannerEventos e
                ON e.IdBanner = b.Id

            WHERE b.Id = @Id
              AND b.Eliminado = 0

            GROUP BY
                b.Id,
                b.NombreCliente,
                b.Ubicacion,
                b.Titulo,
                b.Subtitulo,
                b.Descripcion,
                b.Etiqueta,
                b.TextoBoton,
                b.ImagenDesktopUrl,
                b.ImagenMobileUrl,
                b.UrlDestino,
                b.WhatsappUrl,
                b.FechaInicio,
                b.FechaFin,
                b.Estado,
                b.Orden,
                b.Prioridad,
                b.EsExclusivo,
                b.AbrirNuevaPestana,
                b.FechaCreacion,
                b.FechaModificacion;";

        try
        {
            using var conn = _conexion.CreateSqlConnection();

            return await conn.QueryFirstOrDefaultAsync<BannerPublicitarioDto>(
                sql,
                new
                {
                    Id = id
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al obtener banner por ID. IdBanner={IdBanner}",
                id);

            throw new RepositoryException(
                "Error al obtener el banner publicitario.",
                ex);
        }
    }

    public async Task<(
        List<BannerPublicitarioDto> Items,
        int TotalRegistros
    )> ListarAdmin(FiltroBannersPublicitariosRequest filtro)
    {
        const string sql = @"
            DECLARE @Ahora DATETIME2(0) = SYSDATETIME();

            WITH BannersConMetricas AS
            (
                SELECT
                    b.Id,
                    b.NombreCliente,
                    b.Ubicacion,
                    b.Titulo,
                    b.Subtitulo,
                    b.Descripcion,
                    b.Etiqueta,
                    b.TextoBoton,
                    b.ImagenDesktopUrl,
                    b.ImagenMobileUrl,
                    b.UrlDestino,
                    b.WhatsappUrl,
                    b.FechaInicio,
                    b.FechaFin,
                    b.Estado,

                    CASE
                        WHEN b.Estado = 'BORRADOR' THEN 'BORRADOR'
                        WHEN b.Estado = 'PAUSADO' THEN 'PAUSADO'
                        WHEN @Ahora < b.FechaInicio THEN 'PROGRAMADO'
                        WHEN @Ahora > b.FechaFin THEN 'VENCIDO'
                        ELSE 'ACTIVO'
                    END AS EstadoVigencia,

                    b.Orden,
                    b.Prioridad,
                    b.EsExclusivo,
                    b.AbrirNuevaPestana,
                    b.FechaCreacion,
                    b.FechaModificacion,

                    ISNULL(
                        SUM(
                            CASE
                                WHEN e.TipoEvento = 'IMPRESION'
                                THEN 1
                                ELSE 0
                            END
                        ),
                        0
                    ) AS CantidadImpresiones,

                    ISNULL(
                        SUM(
                            CASE
                                WHEN e.TipoEvento = 'CLICK'
                                THEN 1
                                ELSE 0
                            END
                        ),
                        0
                    ) AS CantidadClicks,

                    ISNULL(
                        SUM(
                            CASE
                                WHEN e.TipoEvento = 'WHATSAPP'
                                THEN 1
                                ELSE 0
                            END
                        ),
                        0
                    ) AS CantidadWhatsApp

                FROM dbo.BannersPublicitarios b

                LEFT JOIN dbo.BannerEventos e
                    ON e.IdBanner = b.Id

                WHERE b.Eliminado = 0

                GROUP BY
                    b.Id,
                    b.NombreCliente,
                    b.Ubicacion,
                    b.Titulo,
                    b.Subtitulo,
                    b.Descripcion,
                    b.Etiqueta,
                    b.TextoBoton,
                    b.ImagenDesktopUrl,
                    b.ImagenMobileUrl,
                    b.UrlDestino,
                    b.WhatsappUrl,
                    b.FechaInicio,
                    b.FechaFin,
                    b.Estado,
                    b.Orden,
                    b.Prioridad,
                    b.EsExclusivo,
                    b.AbrirNuevaPestana,
                    b.FechaCreacion,
                    b.FechaModificacion
            )

            SELECT *
            FROM BannersConMetricas
            WHERE
                (
                    @Busqueda IS NULL
                    OR NombreCliente LIKE '%' + @Busqueda + '%'
                    OR Titulo LIKE '%' + @Busqueda + '%'
                )
                AND
                (
                    @Ubicacion IS NULL
                    OR Ubicacion = @Ubicacion
                )
                AND
                (
                    @Estado IS NULL
                    OR EstadoVigencia = @Estado
                    OR Estado = @Estado
                )
                AND
                (
                    @FechaDesde IS NULL
                    OR FechaFin >= @FechaDesde
                )
                AND
                (
                    @FechaHasta IS NULL
                    OR FechaInicio <= @FechaHasta
                )

            ORDER BY
                Prioridad DESC,
                Orden ASC,
                FechaCreacion DESC

            OFFSET @Offset ROWS
            FETCH NEXT @RegistrosPorPagina ROWS ONLY;

            WITH BannersFiltrados AS
            (
                SELECT
                    b.Id,
                    b.NombreCliente,
                    b.Titulo,
                    b.Ubicacion,
                    b.Estado,
                    b.FechaInicio,
                    b.FechaFin,

                    CASE
                        WHEN b.Estado = 'BORRADOR' THEN 'BORRADOR'
                        WHEN b.Estado = 'PAUSADO' THEN 'PAUSADO'
                        WHEN @Ahora < b.FechaInicio THEN 'PROGRAMADO'
                        WHEN @Ahora > b.FechaFin THEN 'VENCIDO'
                        ELSE 'ACTIVO'
                    END AS EstadoVigencia

                FROM dbo.BannersPublicitarios b

                WHERE b.Eliminado = 0
            )

            SELECT COUNT(1)
            FROM BannersFiltrados
            WHERE
                (
                    @Busqueda IS NULL
                    OR NombreCliente LIKE '%' + @Busqueda + '%'
                    OR Titulo LIKE '%' + @Busqueda + '%'
                )
                AND
                (
                    @Ubicacion IS NULL
                    OR Ubicacion = @Ubicacion
                )
                AND
                (
                    @Estado IS NULL
                    OR EstadoVigencia = @Estado
                    OR Estado = @Estado
                )
                AND
                (
                    @FechaDesde IS NULL
                    OR FechaFin >= @FechaDesde
                )
                AND
                (
                    @FechaHasta IS NULL
                    OR FechaInicio <= @FechaHasta
                );";

        try
        {
            using var conn = _conexion.CreateSqlConnection();

            var parametros = new
            {
                Busqueda = string.IsNullOrWhiteSpace(filtro.Busqueda)
                    ? null
                    : filtro.Busqueda.Trim(),

                Ubicacion = string.IsNullOrWhiteSpace(filtro.Ubicacion)
                    ? null
                    : filtro.Ubicacion.Trim().ToUpperInvariant(),

                Estado = string.IsNullOrWhiteSpace(filtro.Estado)
                    ? null
                    : filtro.Estado.Trim().ToUpperInvariant(),

                filtro.FechaDesde,
                filtro.FechaHasta,

                Offset =
                    (filtro.Pagina - 1)
                    * filtro.RegistrosPorPagina,

                filtro.RegistrosPorPagina
            };

            using var multi = await conn.QueryMultipleAsync(
                sql,
                parametros);

            var items =
                (await multi.ReadAsync<BannerPublicitarioDto>())
                .ToList();

            var total =
                await multi.ReadSingleAsync<int>();

            return (items, total);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al listar banners publicitarios.");

            throw new RepositoryException(
                "Error al listar los banners publicitarios.",
                ex);
        }
    }

    public async Task<List<BannerPublicitarioPublicoDto>>
        ObtenerActivosHome()
    {
        const string sql = @"
            DECLARE @Ahora DATETIME2(0) = SYSDATETIME();

            SELECT
                Id,
                NombreCliente,
                Ubicacion,
                Titulo,
                Subtitulo,
                Descripcion,
                Etiqueta,
                TextoBoton,
                ImagenDesktopUrl,
                ImagenMobileUrl,
                UrlDestino,
                WhatsappUrl,
                AbrirNuevaPestana,
                Orden,
                Prioridad,
                EsExclusivo

            FROM dbo.BannersPublicitarios

            WHERE Eliminado = 0
              AND Estado = 'ACTIVO'
              AND FechaInicio <= @Ahora
              AND FechaFin >= @Ahora
              AND Ubicacion IN ('HOME_TOP', 'HOME_INLINE')

            ORDER BY
                Ubicacion,
                Prioridad DESC,
                Orden ASC,
                Id DESC;";

        try
        {
            using var conn = _conexion.CreateSqlConnection();

            return (
                await conn.QueryAsync<BannerPublicitarioPublicoDto>(
                    sql)
            ).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al obtener banners públicos activos.");

            throw new RepositoryException(
                "Error al obtener los banners publicitarios activos.",
                ex);
        }
    }

    public async Task<ResumenBannersPublicitariosDto>
        ObtenerResumen()
    {
        const string sql = @"
            DECLARE @Ahora DATETIME2(0) = SYSDATETIME();

            SELECT
                COUNT(1) AS TotalBanners,

                ISNULL(
                    SUM(
                        CASE
                            WHEN Estado = 'ACTIVO'
                             AND FechaInicio <= @Ahora
                             AND FechaFin >= @Ahora
                            THEN 1
                            ELSE 0
                        END
                    ),
                    0
                ) AS BannersActivos,

                ISNULL(
                    SUM(
                        CASE
                            WHEN Estado = 'ACTIVO'
                             AND FechaInicio > @Ahora
                            THEN 1
                            ELSE 0
                        END
                    ),
                    0
                ) AS BannersProgramados,

                ISNULL(
                    SUM(
                        CASE
                            WHEN FechaFin < @Ahora
                            THEN 1
                            ELSE 0
                        END
                    ),
                    0
                ) AS BannersVencidos,

                ISNULL(
                    (
                        SELECT COUNT_BIG(1)
                        FROM dbo.BannerEventos
                        WHERE TipoEvento = 'IMPRESION'
                    ),
                    0
                ) AS TotalImpresiones,

                ISNULL(
                    (
                        SELECT COUNT_BIG(1)
                        FROM dbo.BannerEventos
                        WHERE TipoEvento = 'CLICK'
                    ),
                    0
                ) AS TotalClicks,

                ISNULL(
                    (
                        SELECT COUNT_BIG(1)
                        FROM dbo.BannerEventos
                        WHERE TipoEvento = 'WHATSAPP'
                    ),
                    0
                ) AS TotalWhatsApp,

                CAST
                (
                    CASE
                        WHEN
                        (
                            SELECT COUNT_BIG(1)
                            FROM dbo.BannerEventos
                            WHERE TipoEvento = 'IMPRESION'
                        ) = 0
                        THEN 0

                        ELSE
                        (
                            SELECT COUNT_BIG(1)
                            FROM dbo.BannerEventos
                            WHERE TipoEvento = 'CLICK'
                        ) * 100.0
                        /
                        (
                            SELECT COUNT_BIG(1)
                            FROM dbo.BannerEventos
                            WHERE TipoEvento = 'IMPRESION'
                        )
                    END

                    AS DECIMAL(10, 2)
                ) AS Ctr

            FROM dbo.BannersPublicitarios

            WHERE Eliminado = 0;";

        try
        {
            using var conn = _conexion.CreateSqlConnection();

            return await conn
                .QuerySingleAsync<ResumenBannersPublicitariosDto>(
                    sql);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al obtener resumen de banners.");

            throw new RepositoryException(
                "Error al obtener el resumen de banners publicitarios.",
                ex);
        }
    }

    public async Task<int> RegistrarEvento(
        RegistrarBannerEventoRequest request,
        string? userAgent)
    {
        const string sql = @"
            DECLARE @Ahora DATETIME2(0) = SYSDATETIME();

            INSERT INTO dbo.BannerEventos
            (
                IdBanner,
                TipoEvento,
                Ubicacion,
                Dispositivo,
                Pagina,
                VisitorId,
                UserAgent,
                FechaCreacion
            )

            SELECT
                b.Id,
                @TipoEvento,
                @Ubicacion,
                @Dispositivo,
                @Pagina,
                @VisitorId,
                @UserAgent,
                @Ahora

            FROM dbo.BannersPublicitarios b

            WHERE b.Id = @IdBanner
              AND b.Ubicacion = @Ubicacion
              AND b.Eliminado = 0
              AND b.Estado = 'ACTIVO'
              AND b.FechaInicio <= @Ahora
              AND b.FechaFin >= @Ahora;";

        try
        {
            using var conn = _conexion.CreateSqlConnection();

            return await conn.ExecuteAsync(
                sql,
                new
                {
                    request.IdBanner,
                    request.TipoEvento,
                    request.Ubicacion,
                    request.Dispositivo,
                    request.Pagina,
                    request.VisitorId,

                    UserAgent = userAgent
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al registrar evento de banner. IdBanner={IdBanner}",
                request.IdBanner);

            throw new RepositoryException(
                "Error al registrar el evento del banner publicitario.",
                ex);
        }
    }

    public async Task<bool> EsAdministrador(int idUsuario)
    {
        const string sql = @"
            SELECT COUNT(1)
            FROM dbo.UsuarioRoles ur

            INNER JOIN dbo.Roles r
                ON r.Id = ur.IdRol

            WHERE ur.IdUsuario = @IdUsuario
              AND r.NombreRol = 'Administrador';";

        try
        {
            using var conn = _conexion.CreateSqlConnection();

            return await conn.ExecuteScalarAsync<int>(
                sql,
                new
                {
                    IdUsuario = idUsuario
                }) > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al validar administrador. IdUsuario={IdUsuario}",
                idUsuario);

            throw new RepositoryException(
                "Error al validar el acceso administrativo.",
                ex);
        }
    }
}

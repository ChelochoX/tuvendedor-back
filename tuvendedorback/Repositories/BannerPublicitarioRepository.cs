using Dapper;
using System.Data;
using tuvendedorback.Common;
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

    /*
      ==========================================================
      CREAR CAMPAÑA Y REGISTRAR REVISIONES INICIALES
      ==========================================================
    */
    public async Task<int> Crear(
        CrearBannerPublicitarioRequest request,
        int idUsuario,
        Guid storageKey,
        BannerArchivoUploadResultDto imagenDesktop,
        BannerArchivoUploadResultDto imagenMobile)
    {
        const string sqlBanner = @"
            INSERT INTO dbo.BannersPublicitarios
            (
                StorageKey,
                NombreCliente,
                Ubicacion,
                Titulo,
                Subtitulo,
                Descripcion,
                Etiqueta,
                TextoBoton,
                ImagenDesktopUrl,
                ImagenMobileUrl,
                TipoDestino,
                UrlDestino,
                MostrarBotonWhatsapp,
                WhatsappUrl,
                TextoBotonWhatsapp,
                FechaInicio,
                FechaFin,
                Estado,
                Orden,
                Prioridad,
                EsExclusivo,
                AbrirNuevaPestana,
                CloudinaryAssetFolder,
                ImagenDesktopPublicId,
                ImagenDesktopAssetId,
                ImagenMobilePublicId,
                ImagenMobileAssetId,
                CreadoPor,
                FechaCreacion,
                Eliminado
            )
            VALUES
            (
                @StorageKey,
                @NombreCliente,
                @Ubicacion,
                @Titulo,
                @Subtitulo,
                @Descripcion,
                @Etiqueta,
                @TextoBoton,
                @ImagenDesktopUrl,
                @ImagenMobileUrl,
                @TipoDestino,
                @UrlDestino,
                @MostrarBotonWhatsapp,
                @WhatsappUrl,
                @TextoBotonWhatsapp,
                @FechaInicio,
                @FechaFin,
                @Estado,
                @Orden,
                @Prioridad,
                @EsExclusivo,
                @AbrirNuevaPestana,
                @CloudinaryAssetFolder,
                @ImagenDesktopPublicId,
                @ImagenDesktopAssetId,
                @ImagenMobilePublicId,
                @ImagenMobileAssetId,
                @CreadoPor,
                SYSDATETIME(),
                0
            );

            SELECT CAST(SCOPE_IDENTITY() AS INT);";

        try
        {
            using var conn =
                _conexion.CreateSqlConnection();

            conn.Open();

            using var transaction =
                conn.BeginTransaction();

            try
            {
                var idBanner =
                    await conn.ExecuteScalarAsync<int>(
                        sqlBanner,
                        new
                        {
                            StorageKey =
                                storageKey,

                            request.NombreCliente,
                            request.Ubicacion,
                            request.Titulo,
                            request.Subtitulo,
                            request.Descripcion,
                            request.Etiqueta,
                            request.TextoBoton,

                            ImagenDesktopUrl =
                                imagenDesktop.SecureUrl,

                            ImagenMobileUrl =
                                imagenMobile.SecureUrl,

                            request.TipoDestino,
                            request.UrlDestino,
                            request.MostrarBotonWhatsapp,
                            request.WhatsappUrl,
                            request.TextoBotonWhatsapp,
                            request.FechaInicio,
                            request.FechaFin,
                            request.Estado,
                            request.Orden,
                            request.Prioridad,
                            request.EsExclusivo,
                            request.AbrirNuevaPestana,

                            CloudinaryAssetFolder =
                                imagenDesktop.AssetFolder,

                            ImagenDesktopPublicId =
                                imagenDesktop.PublicId,

                            ImagenDesktopAssetId =
                                imagenDesktop.AssetId,

                            ImagenMobilePublicId =
                                imagenMobile.PublicId,

                            ImagenMobileAssetId =
                                imagenMobile.AssetId,

                            CreadoPor =
                                idUsuario
                        },
                        transaction);

                await InsertarArchivo(
                    conn,
                    transaction,
                    idBanner,
                    imagenDesktop,
                    idUsuario);

                await InsertarArchivo(
                    conn,
                    transaction,
                    idBanner,
                    imagenMobile,
                    idUsuario);

                transaction.Commit();

                return idBanner;
            }
            catch
            {
                transaction.Rollback();

                throw;
            }
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

    /*
      ==========================================================
      ACTUALIZAR DATOS Y REEMPLAZAR IMÁGENES OPCIONALES

      Si llega una imagen nueva:
      1. La versión activa anterior pasa a pendiente.
      2. Se registra la nueva revisión.
      3. La tabla principal apunta a la nueva URL.
      ==========================================================
    */
    public async Task<int> Actualizar(
        int id,
        ActualizarBannerPublicitarioRequest request,
        int idUsuario,
        BannerArchivoUploadResultDto? imagenDesktop,
        BannerArchivoUploadResultDto? imagenMobile,
        int diasRetencionArchivos)
    {
        const string sqlActualizarBanner = @"
            UPDATE dbo.BannersPublicitarios
            SET
                NombreCliente =
                    @NombreCliente,

                Ubicacion =
                    @Ubicacion,

                Titulo =
                    @Titulo,

                Subtitulo =
                    @Subtitulo,

                Descripcion =
                    @Descripcion,

                Etiqueta =
                    @Etiqueta,

                TextoBoton =
                    @TextoBoton,

                TipoDestino =
                    @TipoDestino,

                UrlDestino =
                    @UrlDestino,

                MostrarBotonWhatsapp =
                    @MostrarBotonWhatsapp,

                WhatsappUrl =
                    @WhatsappUrl,

                TextoBotonWhatsapp =
                    @TextoBotonWhatsapp,

                FechaInicio =
                    @FechaInicio,

                FechaFin =
                    @FechaFin,

                Estado =
                    @Estado,

                Orden =
                    @Orden,

                Prioridad =
                    @Prioridad,

                EsExclusivo =
                    @EsExclusivo,

                AbrirNuevaPestana =
                    @AbrirNuevaPestana,

                CloudinaryAssetFolder =
                    COALESCE(
                        @CloudinaryAssetFolder,
                        CloudinaryAssetFolder
                    ),

                ImagenDesktopUrl =
                    COALESCE(
                        @ImagenDesktopUrl,
                        ImagenDesktopUrl
                    ),

                ImagenDesktopPublicId =
                    COALESCE(
                        @ImagenDesktopPublicId,
                        ImagenDesktopPublicId
                    ),

                ImagenDesktopAssetId =
                    COALESCE(
                        @ImagenDesktopAssetId,
                        ImagenDesktopAssetId
                    ),

                ImagenMobileUrl =
                    COALESCE(
                        @ImagenMobileUrl,
                        ImagenMobileUrl
                    ),

                ImagenMobilePublicId =
                    COALESCE(
                        @ImagenMobilePublicId,
                        ImagenMobilePublicId
                    ),

                ImagenMobileAssetId =
                    COALESCE(
                        @ImagenMobileAssetId,
                        ImagenMobileAssetId
                    ),

                ModificadoPor =
                    @ModificadoPor,

                FechaModificacion =
                    SYSDATETIME()

            WHERE Id = @Id
              AND Eliminado = 0;";

        try
        {
            using var conn =
                _conexion.CreateSqlConnection();

            conn.Open();

            using var transaction =
                conn.BeginTransaction();

            try
            {
                var assetFolder =
                    imagenDesktop?.AssetFolder
                    ?? imagenMobile?.AssetFolder;

                var filas =
                    await conn.ExecuteAsync(
                        sqlActualizarBanner,
                        new
                        {
                            Id =
                                id,

                            request.NombreCliente,
                            request.Ubicacion,
                            request.Titulo,
                            request.Subtitulo,
                            request.Descripcion,
                            request.Etiqueta,
                            request.TextoBoton,
                            request.TipoDestino,
                            request.UrlDestino,
                            request.MostrarBotonWhatsapp,
                            request.WhatsappUrl,
                            request.TextoBotonWhatsapp,
                            request.FechaInicio,
                            request.FechaFin,
                            request.Estado,
                            request.Orden,
                            request.Prioridad,
                            request.EsExclusivo,
                            request.AbrirNuevaPestana,

                            CloudinaryAssetFolder =
                                assetFolder,

                            ImagenDesktopUrl =
                                imagenDesktop?.SecureUrl,

                            ImagenDesktopPublicId =
                                imagenDesktop?.PublicId,

                            ImagenDesktopAssetId =
                                imagenDesktop?.AssetId,

                            ImagenMobileUrl =
                                imagenMobile?.SecureUrl,

                            ImagenMobilePublicId =
                                imagenMobile?.PublicId,

                            ImagenMobileAssetId =
                                imagenMobile?.AssetId,

                            ModificadoPor =
                                idUsuario
                        },
                        transaction);

                if (filas == 0)
                {
                    transaction.Rollback();

                    return 0;
                }

                if (imagenDesktop != null)
                {
                    await ReemplazarArchivoActivo(
                        conn,
                        transaction,
                        id,
                        BannerPublicitarioConstantes
                            .DispositivoDesktop,
                        imagenDesktop,
                        idUsuario,
                        diasRetencionArchivos);
                }

                if (imagenMobile != null)
                {
                    await ReemplazarArchivoActivo(
                        conn,
                        transaction,
                        id,
                        BannerPublicitarioConstantes
                            .DispositivoMobile,
                        imagenMobile,
                        idUsuario,
                        diasRetencionArchivos);
                }

                transaction.Commit();

                return filas;
            }
            catch
            {
                transaction.Rollback();

                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al actualizar banner publicitario. " +
                "IdBanner={IdBanner}",
                id);

            throw new RepositoryException(
                "Error al actualizar el banner publicitario.",
                ex);
        }
    }

    /*
      ==========================================================
      CAMBIAR ESTADO
      ==========================================================
    */
    public async Task<int> CambiarEstado(
        int id,
        string estado,
        int idUsuario)
    {
        const string sql = @"
            UPDATE dbo.BannersPublicitarios
            SET
                Estado =
                    @Estado,

                ModificadoPor =
                    @ModificadoPor,

                FechaModificacion =
                    SYSDATETIME()

            WHERE Id = @Id
              AND Eliminado = 0;";

        try
        {
            using var conn =
                _conexion.CreateSqlConnection();

            return await conn.ExecuteAsync(
                sql,
                new
                {
                    Id =
                        id,

                    Estado =
                        estado,

                    ModificadoPor =
                        idUsuario
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al cambiar estado del banner. " +
                "IdBanner={IdBanner}",
                id);

            throw new RepositoryException(
                "Error al cambiar el estado del banner publicitario.",
                ex);
        }
    }

    /*
      ==========================================================
      ELIMINACIÓN LÓGICA

      No borra físicamente archivos de inmediato.
      Marca las revisiones activas para limpieza posterior.
      ==========================================================
    */
    public async Task<int> Eliminar(
        int id,
        int idUsuario,
        int diasRetencionArchivos)
    {
        const string sqlBanner = @"
            UPDATE dbo.BannersPublicitarios
            SET
                Eliminado =
                    1,

                Estado =
                    @EstadoFinalizado,

                FechaEliminacion =
                    SYSDATETIME(),

                EliminadoPor =
                    @EliminadoPor,

                ModificadoPor =
                    @EliminadoPor,

                FechaModificacion =
                    SYSDATETIME()

            WHERE Id = @Id
              AND Eliminado = 0;";

        const string sqlArchivos = @"
            UPDATE dbo.BannerPublicitarioArchivos
            SET
                Estado =
                    @EstadoPendiente,

                FechaEliminacionProgramada =
                    DATEADD(
                        DAY,
                        @DiasRetencion,
                        SYSDATETIME()
                    )

            WHERE BannerPublicitarioId =
                    @BannerPublicitarioId

              AND Estado =
                    @EstadoActivo;";

        try
        {
            using var conn =
                _conexion.CreateSqlConnection();

            conn.Open();

            using var transaction =
                conn.BeginTransaction();

            try
            {
                var filas =
                    await conn.ExecuteAsync(
                        sqlBanner,
                        new
                        {
                            Id =
                                id,

                            EstadoFinalizado =
                                BannerPublicitarioConstantes
                                    .EstadoFinalizado,

                            EliminadoPor =
                                idUsuario
                        },
                        transaction);

                if (filas == 0)
                {
                    transaction.Rollback();

                    return 0;
                }

                await conn.ExecuteAsync(
                    sqlArchivos,
                    new
                    {
                        BannerPublicitarioId =
                            id,

                        EstadoPendiente =
                            BannerPublicitarioConstantes
                                .EstadoArchivoPendienteEliminacion,

                        EstadoActivo =
                            BannerPublicitarioConstantes
                                .EstadoArchivoActivo,

                        DiasRetencion =
                            diasRetencionArchivos
                    },
                    transaction);

                transaction.Commit();

                return filas;
            }
            catch
            {
                transaction.Rollback();

                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al eliminar banner publicitario. " +
                "IdBanner={IdBanner}",
                id);

            throw new RepositoryException(
                "Error al eliminar el banner publicitario.",
                ex);
        }
    }

    /*
      ==========================================================
      OBTENER DETALLE ADMINISTRATIVO
      ==========================================================
    */
    public async Task<BannerPublicitarioDto?>
        ObtenerPorId(int id)
    {
        const string sql = @"
            DECLARE @Ahora DATETIME2(0) =
                SYSDATETIME();

            SELECT
                b.Id,
                b.StorageKey,
                b.NombreCliente,
                b.Ubicacion,
                b.Titulo,
                b.Subtitulo,
                b.Descripcion,
                b.Etiqueta,
                b.TextoBoton,
                b.ImagenDesktopUrl,
                b.ImagenMobileUrl,
                b.TipoDestino,
                b.UrlDestino,
                b.MostrarBotonWhatsapp,
                b.WhatsappUrl,
                b.TextoBotonWhatsapp,
                b.FechaInicio,
                b.FechaFin,
                b.Estado,

                CASE
                    WHEN b.Estado = 'BORRADOR'
                        THEN 'BORRADOR'

                    WHEN b.Estado = 'PAUSADO'
                        THEN 'PAUSADO'

                    WHEN b.Estado = 'FINALIZADO'
                        THEN 'FINALIZADO'

                    WHEN @Ahora < b.FechaInicio
                        THEN 'PROGRAMADO'

                    WHEN @Ahora > b.FechaFin
                        THEN 'VENCIDO'

                    ELSE 'ACTIVO'
                END AS EstadoVigencia,

                b.Orden,
                b.Prioridad,
                b.EsExclusivo,
                b.AbrirNuevaPestana,
                b.CloudinaryAssetFolder,
                b.ImagenDesktopPublicId,
                b.ImagenDesktopAssetId,
                b.ImagenMobilePublicId,
                b.ImagenMobileAssetId,
                b.FechaCreacion,
                b.FechaModificacion,

                ISNULL
                (
                    SUM
                    (
                        CASE
                            WHEN e.TipoEvento = 'IMPRESION'
                            THEN CAST(1 AS BIGINT)
                            ELSE CAST(0 AS BIGINT)
                        END
                    ),
                    0
                ) AS CantidadImpresiones,

                ISNULL
                (
                    SUM
                    (
                        CASE
                            WHEN e.TipoEvento = 'CLICK'
                            THEN CAST(1 AS BIGINT)
                            ELSE CAST(0 AS BIGINT)
                        END
                    ),
                    0
                ) AS CantidadClicks,

                ISNULL
                (
                    SUM
                    (
                        CASE
                            WHEN e.TipoEvento = 'WHATSAPP'
                            THEN CAST(1 AS BIGINT)
                            ELSE CAST(0 AS BIGINT)
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
                b.StorageKey,
                b.NombreCliente,
                b.Ubicacion,
                b.Titulo,
                b.Subtitulo,
                b.Descripcion,
                b.Etiqueta,
                b.TextoBoton,
                b.ImagenDesktopUrl,
                b.ImagenMobileUrl,
                b.TipoDestino,
                b.UrlDestino,
                b.MostrarBotonWhatsapp,
                b.WhatsappUrl,
                b.TextoBotonWhatsapp,
                b.FechaInicio,
                b.FechaFin,
                b.Estado,
                b.Orden,
                b.Prioridad,
                b.EsExclusivo,
                b.AbrirNuevaPestana,
                b.CloudinaryAssetFolder,
                b.ImagenDesktopPublicId,
                b.ImagenDesktopAssetId,
                b.ImagenMobilePublicId,
                b.ImagenMobileAssetId,
                b.FechaCreacion,
                b.FechaModificacion;";

        try
        {
            using var conn =
                _conexion.CreateSqlConnection();

            return await conn
                .QueryFirstOrDefaultAsync<
                    BannerPublicitarioDto
                >(
                    sql,
                    new
                    {
                        Id =
                            id
                    });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al obtener banner por ID. " +
                "IdBanner={IdBanner}",
                id);

            throw new RepositoryException(
                "Error al obtener el banner publicitario.",
                ex);
        }
    }

    /*
      ==========================================================
      LISTADO ADMINISTRATIVO PAGINADO
      ==========================================================
    */
    public async Task<(
        List<BannerPublicitarioDto> Items,
        int TotalRegistros
    )> ListarAdmin(
        FiltroBannersPublicitariosRequest filtro)
    {
        const string sql = @"
            DECLARE @Ahora DATETIME2(0) =
                SYSDATETIME();

            WITH BannersConMetricas AS
            (
                SELECT
                    b.Id,
                    b.StorageKey,
                    b.NombreCliente,
                    b.Ubicacion,
                    b.Titulo,
                    b.Subtitulo,
                    b.Descripcion,
                    b.Etiqueta,
                    b.TextoBoton,
                    b.ImagenDesktopUrl,
                    b.ImagenMobileUrl,
                    b.TipoDestino,
                    b.UrlDestino,
                    b.MostrarBotonWhatsapp,
                    b.WhatsappUrl,
                    b.TextoBotonWhatsapp,
                    b.FechaInicio,
                    b.FechaFin,
                    b.Estado,

                    CASE
                        WHEN b.Estado = 'BORRADOR'
                            THEN 'BORRADOR'

                        WHEN b.Estado = 'PAUSADO'
                            THEN 'PAUSADO'

                        WHEN b.Estado = 'FINALIZADO'
                            THEN 'FINALIZADO'

                        WHEN @Ahora < b.FechaInicio
                            THEN 'PROGRAMADO'

                        WHEN @Ahora > b.FechaFin
                            THEN 'VENCIDO'

                        ELSE 'ACTIVO'
                    END AS EstadoVigencia,

                    b.Orden,
                    b.Prioridad,
                    b.EsExclusivo,
                    b.AbrirNuevaPestana,
                    b.CloudinaryAssetFolder,
                    b.ImagenDesktopPublicId,
                    b.ImagenDesktopAssetId,
                    b.ImagenMobilePublicId,
                    b.ImagenMobileAssetId,
                    b.FechaCreacion,
                    b.FechaModificacion,

                    ISNULL
                    (
                        SUM
                        (
                            CASE
                                WHEN e.TipoEvento = 'IMPRESION'
                                THEN CAST(1 AS BIGINT)
                                ELSE CAST(0 AS BIGINT)
                            END
                        ),
                        0
                    ) AS CantidadImpresiones,

                    ISNULL
                    (
                        SUM
                        (
                            CASE
                                WHEN e.TipoEvento = 'CLICK'
                                THEN CAST(1 AS BIGINT)
                                ELSE CAST(0 AS BIGINT)
                            END
                        ),
                        0
                    ) AS CantidadClicks,

                    ISNULL
                    (
                        SUM
                        (
                            CASE
                                WHEN e.TipoEvento = 'WHATSAPP'
                                THEN CAST(1 AS BIGINT)
                                ELSE CAST(0 AS BIGINT)
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
                    b.StorageKey,
                    b.NombreCliente,
                    b.Ubicacion,
                    b.Titulo,
                    b.Subtitulo,
                    b.Descripcion,
                    b.Etiqueta,
                    b.TextoBoton,
                    b.ImagenDesktopUrl,
                    b.ImagenMobileUrl,
                    b.TipoDestino,
                    b.UrlDestino,
                    b.MostrarBotonWhatsapp,
                    b.WhatsappUrl,
                    b.TextoBotonWhatsapp,
                    b.FechaInicio,
                    b.FechaFin,
                    b.Estado,
                    b.Orden,
                    b.Prioridad,
                    b.EsExclusivo,
                    b.AbrirNuevaPestana,
                    b.CloudinaryAssetFolder,
                    b.ImagenDesktopPublicId,
                    b.ImagenDesktopAssetId,
                    b.ImagenMobilePublicId,
                    b.ImagenMobileAssetId,
                    b.FechaCreacion,
                    b.FechaModificacion
            )

            SELECT *
            FROM BannersConMetricas

            WHERE
                (
                    @Busqueda IS NULL
                    OR NombreCliente
                        LIKE '%' + @Busqueda + '%'
                    OR Titulo
                        LIKE '%' + @Busqueda + '%'
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
                        WHEN b.Estado = 'BORRADOR'
                            THEN 'BORRADOR'

                        WHEN b.Estado = 'PAUSADO'
                            THEN 'PAUSADO'

                        WHEN b.Estado = 'FINALIZADO'
                            THEN 'FINALIZADO'

                        WHEN @Ahora < b.FechaInicio
                            THEN 'PROGRAMADO'

                        WHEN @Ahora > b.FechaFin
                            THEN 'VENCIDO'

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
                    OR NombreCliente
                        LIKE '%' + @Busqueda + '%'
                    OR Titulo
                        LIKE '%' + @Busqueda + '%'
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
            using var conn =
                _conexion.CreateSqlConnection();

            var parametros =
                new
                {
                    Busqueda =
                        string.IsNullOrWhiteSpace(
                            filtro.Busqueda)
                            ? null
                            : filtro.Busqueda.Trim(),

                    Ubicacion =
                        string.IsNullOrWhiteSpace(
                            filtro.Ubicacion)
                            ? null
                            : filtro.Ubicacion
                                .Trim()
                                .ToUpperInvariant(),

                    Estado =
                        string.IsNullOrWhiteSpace(
                            filtro.Estado)
                            ? null
                            : filtro.Estado
                                .Trim()
                                .ToUpperInvariant(),

                    filtro.FechaDesde,
                    filtro.FechaHasta,

                    Offset =
                        (filtro.Pagina - 1)
                        * filtro.RegistrosPorPagina,

                    filtro.RegistrosPorPagina
                };

            using var multi =
                await conn.QueryMultipleAsync(
                    sql,
                    parametros);

            var items =
                (
                    await multi.ReadAsync<
                        BannerPublicitarioDto
                    >()
                ).ToList();

            var total =
                await multi.ReadSingleAsync<int>();

            return (
                items,
                total
            );
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

    /*
      ==========================================================
      CONSULTA PÚBLICA DEL HOME

      Respeta MostrarBotonWhatsapp.
      El frontend continúa consumiendo el mismo endpoint.
      ==========================================================
    */
    public async Task<List<BannerPublicitarioPublicoDto>>
        ObtenerActivosHome()
    {
        const string sql = @"
            DECLARE @Ahora DATETIME2(0) =
                SYSDATETIME();

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
                TipoDestino,
                UrlDestino,

                CASE
                    WHEN MostrarBotonWhatsapp = 1
                    THEN WhatsappUrl
                    ELSE NULL
                END AS WhatsappUrl,

                CASE
                    WHEN MostrarBotonWhatsapp = 1
                    THEN TextoBotonWhatsapp
                    ELSE NULL
                END AS TextoBotonWhatsapp,

                AbrirNuevaPestana,
                Orden,
                Prioridad,
                EsExclusivo

            FROM dbo.BannersPublicitarios

            WHERE Eliminado = 0

              AND Estado =
                    'ACTIVO'

              AND FechaInicio <=
                    @Ahora

              AND FechaFin >=
                    @Ahora

              AND Ubicacion IN
                    (
                        'HOME_TOP',
                        'HOME_INLINE'
                    )

            ORDER BY
                Ubicacion,
                Prioridad DESC,
                Orden ASC,
                Id DESC;";

        try
        {
            using var conn =
                _conexion.CreateSqlConnection();

            return
                (
                    await conn.QueryAsync<
                        BannerPublicitarioPublicoDto
                    >(sql)
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

    /*
      ==========================================================
      RESUMEN COMERCIAL
      ==========================================================
    */
    public async Task<ResumenBannersPublicitariosDto>
        ObtenerResumen()
    {
        const string sql = @"
            DECLARE @Ahora DATETIME2(0) =
                SYSDATETIME();

            SELECT
                COUNT(1) AS TotalBanners,

                ISNULL
                (
                    SUM
                    (
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

                ISNULL
                (
                    SUM
                    (
                        CASE
                            WHEN Estado = 'ACTIVO'
                             AND FechaInicio > @Ahora
                            THEN 1
                            ELSE 0
                        END
                    ),
                    0
                ) AS BannersProgramados,

                ISNULL
                (
                    SUM
                    (
                        CASE
                            WHEN FechaFin < @Ahora
                            THEN 1
                            ELSE 0
                        END
                    ),
                    0
                ) AS BannersVencidos,

                ISNULL
                (
                    (
                        SELECT COUNT_BIG(1)
                        FROM dbo.BannerEventos
                        WHERE TipoEvento = 'IMPRESION'
                    ),
                    0
                ) AS TotalImpresiones,

                ISNULL
                (
                    (
                        SELECT COUNT_BIG(1)
                        FROM dbo.BannerEventos
                        WHERE TipoEvento = 'CLICK'
                    ),
                    0
                ) AS TotalClicks,

                ISNULL
                (
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
            using var conn =
                _conexion.CreateSqlConnection();

            return await conn
                .QuerySingleAsync<
                    ResumenBannersPublicitariosDto
                >(sql);
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

    /*
      ==========================================================
      REGISTRAR EVENTO PÚBLICO
      ==========================================================
    */
    public async Task<int> RegistrarEvento(
        RegistrarBannerEventoRequest request,
        string? userAgent)
    {
        const string sql = @"
            DECLARE @Ahora DATETIME2(0) =
                SYSDATETIME();

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

            WHERE b.Id =
                    @IdBanner

              AND b.Ubicacion =
                    @Ubicacion

              AND b.Eliminado =
                    0

              AND b.Estado =
                    'ACTIVO'

              AND b.FechaInicio <=
                    @Ahora

              AND b.FechaFin >=
                    @Ahora;";

        try
        {
            using var conn =
                _conexion.CreateSqlConnection();

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

                    UserAgent =
                        userAgent
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al registrar evento de banner. " +
                "IdBanner={IdBanner}",
                request.IdBanner);

            throw new RepositoryException(
                "Error al registrar el evento del banner publicitario.",
                ex);
        }
    }

    /*
      ==========================================================
      VALIDAR ROL ADMINISTRADOR
      ==========================================================
    */
    public async Task<bool> EsAdministrador(
        int idUsuario)
    {
        const string sql = @"
            SELECT COUNT(1)

            FROM dbo.UsuarioRoles ur

            INNER JOIN dbo.Roles r
                ON r.Id = ur.IdRol

            WHERE ur.IdUsuario =
                    @IdUsuario

              AND r.NombreRol =
                    'Administrador';";

        try
        {
            using var conn =
                _conexion.CreateSqlConnection();

            return
                await conn.ExecuteScalarAsync<int>(
                    sql,
                    new
                    {
                        IdUsuario =
                            idUsuario
                    })
                > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al validar administrador. " +
                "IdUsuario={IdUsuario}",
                idUsuario);

            throw new RepositoryException(
                "Error al validar el acceso administrativo.",
                ex);
        }
    }

    /*
      ==========================================================
      OBTENER SIGUIENTE REVISIÓN DEL ARCHIVO
      ==========================================================
    */
    public async Task<int> ObtenerSiguienteRevision(
        int idBanner,
        string tipoDispositivo)
    {
        const string sql = @"
            SELECT
                ISNULL(
                    MAX(Revision),
                    0
                ) + 1

            FROM dbo.BannerPublicitarioArchivos

            WHERE BannerPublicitarioId =
                    @BannerPublicitarioId

              AND TipoDispositivo =
                    @TipoDispositivo;";

        try
        {
            using var conn =
                _conexion.CreateSqlConnection();

            return await conn.ExecuteScalarAsync<int>(
                sql,
                new
                {
                    BannerPublicitarioId =
                        idBanner,

                    TipoDispositivo =
                        tipoDispositivo
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al calcular siguiente revisión. " +
                "IdBanner={IdBanner}, " +
                "TipoDispositivo={TipoDispositivo}",
                idBanner,
                tipoDispositivo);

            throw new RepositoryException(
                "Error al calcular la revisión del archivo.",
                ex);
        }
    }

    /*
      ==========================================================
      HISTORIAL DE ARCHIVOS
      ==========================================================
    */
    public async Task<List<BannerPublicitarioArchivoDto>>
        ObtenerArchivos(int idBanner)
    {
        const string sql = @"
            SELECT
                Id,
                BannerPublicitarioId,
                TipoDispositivo,
                Revision,
                CloudinaryAssetFolder,
                CloudinaryPublicId,
                CloudinaryAssetId,
                SecureUrl,
                Estado,
                FechaCreacion,
                FechaEliminacionProgramada,
                FechaEliminacion,
                IntentosEliminacion,
                UltimoErrorEliminacion

            FROM dbo.BannerPublicitarioArchivos

            WHERE BannerPublicitarioId =
                    @BannerPublicitarioId

            ORDER BY
                TipoDispositivo ASC,
                Revision DESC;";

        try
        {
            using var conn =
                _conexion.CreateSqlConnection();

            return
                (
                    await conn.QueryAsync<
                        BannerPublicitarioArchivoDto
                    >(
                        sql,
                        new
                        {
                            BannerPublicitarioId =
                                idBanner
                        })
                ).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al obtener historial de archivos. " +
                "IdBanner={IdBanner}",
                idBanner);

            throw new RepositoryException(
                "Error al obtener el historial de archivos.",
                ex);
        }
    }

    /*
      ==========================================================
      ARCHIVOS PENDIENTES DE LIMPIEZA
      ==========================================================
    */
    public async Task<
        List<BannerArchivoPendienteEliminacionDto>
    > ObtenerArchivosPendientesEliminacion(
        int limite)
    {
        const string sql = @"
            SELECT TOP (@Limite)
                Id,
                CloudinaryPublicId

            FROM dbo.BannerPublicitarioArchivos

            WHERE Estado =
                    @EstadoPendiente

              AND FechaEliminacionProgramada <=
                    SYSDATETIME()

            ORDER BY
                FechaEliminacionProgramada ASC,
                Id ASC;";

        try
        {
            using var conn =
                _conexion.CreateSqlConnection();

            return
                (
                    await conn.QueryAsync<
                        BannerArchivoPendienteEliminacionDto
                    >(
                        sql,
                        new
                        {
                            Limite =
                                limite,

                            EstadoPendiente =
                                BannerPublicitarioConstantes
                                    .EstadoArchivoPendienteEliminacion
                        })
                ).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al consultar archivos pendientes " +
                "de eliminación.");

            throw new RepositoryException(
                "Error al consultar archivos pendientes " +
                "de eliminación.",
                ex);
        }
    }

    /*
      ==========================================================
      MARCAR ARCHIVO ELIMINADO
      ==========================================================
    */
    public async Task MarcarArchivoEliminado(
        long idArchivo)
    {
        const string sql = @"
            UPDATE dbo.BannerPublicitarioArchivos
            SET
                Estado =
                    @EstadoEliminado,

                FechaEliminacion =
                    SYSDATETIME(),

                UltimoErrorEliminacion =
                    NULL

            WHERE Id =
                    @Id;";

        try
        {
            using var conn =
                _conexion.CreateSqlConnection();

            await conn.ExecuteAsync(
                sql,
                new
                {
                    Id =
                        idArchivo,

                    EstadoEliminado =
                        BannerPublicitarioConstantes
                            .EstadoArchivoEliminado
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al marcar archivo eliminado. " +
                "IdArchivo={IdArchivo}",
                idArchivo);

            throw new RepositoryException(
                "Error al registrar la eliminación del archivo.",
                ex);
        }
    }

    /*
      ==========================================================
      MARCAR ERROR DE ELIMINACIÓN
      ==========================================================
    */
    public async Task MarcarErrorEliminacion(
        long idArchivo,
        string error)
    {
        const string sql = @"
            UPDATE dbo.BannerPublicitarioArchivos
            SET
                Estado =
                    @EstadoError,

                IntentosEliminacion =
                    IntentosEliminacion + 1,

                UltimoErrorEliminacion =
                    @Error

            WHERE Id =
                    @Id;";

        try
        {
            using var conn =
                _conexion.CreateSqlConnection();

            var mensaje =
                string.IsNullOrWhiteSpace(
                    error)
                    ? "Error no especificado."
                    : error.Trim();

            if (mensaje.Length > 1000)
            {
                mensaje =
                    mensaje[..1000];
            }

            await conn.ExecuteAsync(
                sql,
                new
                {
                    Id =
                        idArchivo,

                    EstadoError =
                        BannerPublicitarioConstantes
                            .EstadoArchivoErrorEliminacion,

                    Error =
                        mensaje
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al registrar fallo de eliminación. " +
                "IdArchivo={IdArchivo}",
                idArchivo);

            throw new RepositoryException(
                "Error al registrar el fallo de eliminación.",
                ex);
        }
    }

    /*
      ==========================================================
      HELPERS PRIVADOS TRANSACCIONALES
      ==========================================================
    */

    private static async Task ReemplazarArchivoActivo(
        IDbConnection conn,
        IDbTransaction transaction,
        int idBanner,
        string tipoDispositivo,
        BannerArchivoUploadResultDto archivoNuevo,
        int idUsuario,
        int diasRetencionArchivos)
    {
        const string sqlMarcarAnteriorPendiente = @"
            UPDATE dbo.BannerPublicitarioArchivos
            SET
                Estado =
                    @EstadoPendiente,

                FechaEliminacionProgramada =
                    DATEADD(
                        DAY,
                        @DiasRetencion,
                        SYSDATETIME()
                    )

            WHERE BannerPublicitarioId =
                    @BannerPublicitarioId

              AND TipoDispositivo =
                    @TipoDispositivo

              AND Estado =
                    @EstadoActivo;";

        await conn.ExecuteAsync(
            sqlMarcarAnteriorPendiente,
            new
            {
                BannerPublicitarioId =
                    idBanner,

                TipoDispositivo =
                    tipoDispositivo,

                EstadoPendiente =
                    BannerPublicitarioConstantes
                        .EstadoArchivoPendienteEliminacion,

                EstadoActivo =
                    BannerPublicitarioConstantes
                        .EstadoArchivoActivo,

                DiasRetencion =
                    diasRetencionArchivos
            },
            transaction);

        await InsertarArchivo(
            conn,
            transaction,
            idBanner,
            archivoNuevo,
            idUsuario);
    }

    private static async Task InsertarArchivo(
        IDbConnection conn,
        IDbTransaction transaction,
        int idBanner,
        BannerArchivoUploadResultDto archivo,
        int idUsuario)
    {
        const string sql = @"
            INSERT INTO dbo.BannerPublicitarioArchivos
            (
                BannerPublicitarioId,
                TipoDispositivo,
                Revision,
                CloudinaryAssetFolder,
                CloudinaryPublicId,
                CloudinaryAssetId,
                SecureUrl,
                Estado,
                FechaCreacion,
                CreadoPor
            )
            VALUES
            (
                @BannerPublicitarioId,
                @TipoDispositivo,
                @Revision,
                @CloudinaryAssetFolder,
                @CloudinaryPublicId,
                @CloudinaryAssetId,
                @SecureUrl,
                @Estado,
                SYSDATETIME(),
                @CreadoPor
            );";

        await conn.ExecuteAsync(
            sql,
            new
            {
                BannerPublicitarioId =
                    idBanner,

                archivo.TipoDispositivo,
                archivo.Revision,

                CloudinaryAssetFolder =
                    archivo.AssetFolder,

                CloudinaryPublicId =
                    archivo.PublicId,

                CloudinaryAssetId =
                    archivo.AssetId,

                archivo.SecureUrl,

                Estado =
                    BannerPublicitarioConstantes
                        .EstadoArchivoActivo,

                CreadoPor =
                    idUsuario
            },
            transaction);
    }
}

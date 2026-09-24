using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;
using tuvendedorback.Data;
using tuvendedorback.DTOs;
using tuvendedorback.Exceptions;
using tuvendedorback.Repositories.Interfaces;

namespace tuvendedorback.Repositories;

public class PerfilVendedorRepository : IPerfilVendedorRepository
{
    private readonly DbConnections _conexion;
    private readonly ILogger<PerfilVendedorRepository> _logger;

    public PerfilVendedorRepository(
        DbConnections conexion,
        ILogger<PerfilVendedorRepository> logger)
    {
        _conexion = conexion;
        _logger = logger;
    }

    public async Task<PerfilPublicoVendedorDto?> ObtenerPerfilPublicoPorSlug(string slug)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            _logger.LogInformation(
                "Iniciando obtención de perfil público de vendedor para slug: {Slug}",
                slug);

            const string sql = @"
                SELECT
                    v.Id                                   AS IdVendedor,
                    v.IdUsuario                            AS IdUsuario,
                    v.Slug                                 AS Slug,
                    v.NombreNegocio                        AS NombreNegocio,
                    u.NombreUsuario                        AS NombreUsuario,
                    v.Descripcion                          AS Descripcion,
                    v.BannerUrl                            AS BannerUrl,
                    v.BannerTipo                           AS BannerTipo,
                    u.FotoPerfil                           AS FotoPerfil,
                    v.Rubro                                AS Rubro,
                    COALESCE(v.CiudadVisible, u.Ciudad)    AS CiudadVisible,

                    CASE 
                        WHEN v.MostrarTelefono = 1 THEN v.Whatsapp
                        ELSE NULL
                    END                                    AS Telefono,

                    CASE 
                        WHEN v.MostrarEmail = 1 THEN COALESCE(NULLIF(v.CorreoContacto, ''), u.Email)
                        ELSE NULL
                    END                                    AS Email,

                    v.CorreoContacto                       AS CorreoContacto,
                    v.MostrarEmail                         AS MostrarEmail,

                    v.Whatsapp                             AS Whatsapp,
                    v.InstagramUrl                         AS InstagramUrl,
                    v.FacebookUrl                          AS FacebookUrl,
                    v.EsPerfilPublico                      AS EsPerfilPublico,
                    v.EsPremium                            AS EsPremium,
                    v.MostrarTelefono                      AS MostrarTelefono,

                    v.OfreceDelivery                       AS OfreceDelivery,
                    v.ZonaDelivery                         AS ZonaDelivery,
                    v.CostoDelivery                        AS CostoDelivery,
                    v.TiempoEstimadoDelivery               AS TiempoEstimadoDelivery
                FROM dbo.Vendedores v
                INNER JOIN dbo.Usuarios u
                    ON u.Id = v.IdUsuario
                WHERE v.Slug = @Slug
                  AND v.EsPerfilPublico = 1
                  AND v.EsPremium = 1
                  AND u.Estado = 'Activo';";

            var perfil = await conn.QueryFirstOrDefaultAsync<PerfilPublicoVendedorDto>(
                sql,
                new { Slug = slug });

            _logger.LogInformation(
                "Consulta de perfil público finalizada para slug: {Slug}. Encontrado: {Encontrado}",
                slug,
                perfil != null);

            return perfil;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al obtener perfil público de vendedor para slug: {Slug}",
                slug);

            throw new RepositoryException("Error al obtener el perfil público del vendedor.", ex);
        }
    }

    public async Task<List<PerfilPublicoPublicacionDto>>
    ObtenerPublicacionesActivasPorSlug(
        string slug)
    {
        using var conn =
            _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
            SELECT
                p.Id,
                p.Titulo,
                p.Descripcion,
                p.Precio,
                p.Moneda,
                p.Categoria,
                p.Ubicacion,
                p.Latitud,
                p.Longitud,
                p.GoogleMapsUrl,
                p.Estado,
                p.CanalPublicacion,
                p.MostrarBotonesCompra,
                p.PermiteDelivery,

                img.Url
                    AS ImagenPrincipal,

                img.ThumbUrl,

                CAST(
                    CASE
                        WHEN d.Id IS NOT NULL
                            THEN 1
                        ELSE 0
                    END
                    AS bit
                ) AS EsDestacada,

                d.FechaFin
                    AS FechaFinDestacado,

                CAST(
                    CASE
                        WHEN pt.Id IS NOT NULL
                            THEN 1
                        ELSE 0
                    END
                    AS bit
                ) AS EsTemporada,

                pt.FechaFin
                    AS FechaFinTemporada,

                pt.BadgeTexto,
                pt.BadgeColor,

                (
                    SELECT COUNT(1)
                    FROM dbo.PublicacionFavoritos pf
                    WHERE pf.IdPublicacion = p.Id
                      AND pf.Activo = 1
                ) AS CantidadFavoritos,

                (
                    SELECT COUNT(1)
                    FROM dbo.PublicacionEventos pe
                    WHERE pe.IdPublicacion = p.Id
                      AND pe.TipoEvento = 'VIEW_DETAIL'
                ) AS CantidadVistas,

                (
                    SELECT COUNT(1)
                    FROM dbo.PublicacionEventos pe
                    WHERE pe.IdPublicacion = p.Id
                      AND pe.TipoEvento = 'CLICK_WHATSAPP'
                ) AS CantidadClicksWhatsapp

            FROM dbo.Vendedores v

            INNER JOIN dbo.Usuarios u
                ON u.Id =
                    v.IdUsuario

            INNER JOIN dbo.Publicaciones p
                ON p.IdUsuario =
                    v.IdUsuario

            OUTER APPLY
            (
                SELECT TOP 1
                    i.Url,
                    i.ThumbUrl

                FROM dbo.ImagenesPublicacion i

                WHERE i.IdPublicacion =
                    p.Id

                ORDER BY i.Id ASC
            ) img

            OUTER APPLY
            (
                SELECT TOP 1
                    pd.Id,
                    pd.FechaFin

                FROM dbo.PublicacionesDestacadas pd

                WHERE pd.IdPublicacion =
                    p.Id

                  AND pd.Estado =
                    'Activo'

                  AND GETDATE()
                      BETWEEN
                          pd.FechaInicio
                          AND pd.FechaFin

                ORDER BY
                    pd.FechaFin DESC,
                    pd.Id DESC
            ) d

            OUTER APPLY
            (
                SELECT TOP 1
                    t.Id,
                    t.FechaFin,
                    t.BadgeTexto,
                    t.BadgeColor

                FROM dbo.PublicacionesTemporada t

                WHERE t.IdPublicacion =
                    p.Id

                  AND t.Estado =
                    'Activo'

                  AND GETDATE()
                      BETWEEN
                          t.FechaInicio
                          AND t.FechaFin

                ORDER BY
                    t.FechaFin DESC,
                    t.Id DESC
            ) pt

            WHERE v.Slug =
                @Slug

              AND v.EsPerfilPublico = 1

              AND v.EsPremium = 1

              AND u.Estado =
                'Activo'

              AND p.Estado =
                'Activo'

              AND p.CanalPublicacion =
                'VITRINA'

            ORDER BY

                CASE
                    WHEN pt.Id IS NOT NULL
                        THEN 0
                    ELSE 1
                END,

                CASE
                    WHEN d.Id IS NOT NULL
                        THEN 0
                    ELSE 1
                END,

                p.Fecha DESC;";

            var publicaciones =
                (
                    await conn
                        .QueryAsync<
                            PerfilPublicoPublicacionDto
                        >(
                            sql,
                            new
                            {
                                Slug =
                                    slug
                            })
                )
                .ToList();

            await CargarDetallesPublicaciones(
                conn,
                publicaciones);

            return publicaciones;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al obtener publicaciones activas del perfil público. Slug: {Slug}",
                slug);

            throw new RepositoryException(
                "Error al obtener las publicaciones del perfil público.",
                ex);
        }
    }


    public async Task<List<PerfilPublicoPublicacionDto>>
    ObtenerPublicacionesVitrinaPorUsuario(
        int idUsuario)
    {
        using var conn =
            _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
            SELECT
                p.Id,
                p.Titulo,
                p.Descripcion,
                p.Precio,
                p.Moneda,
                p.Categoria,
                p.Ubicacion,
                p.Latitud,
                p.Longitud,
                p.GoogleMapsUrl,
                p.Estado,
                p.CanalPublicacion,
                p.MostrarBotonesCompra,
                p.PermiteDelivery,

                img.Url
                    AS ImagenPrincipal,

                img.ThumbUrl,

                CAST(
                    CASE
                        WHEN d.Id IS NOT NULL
                            THEN 1
                        ELSE 0
                    END
                    AS bit
                ) AS EsDestacada,

                d.FechaFin
                    AS FechaFinDestacado,

                CAST(
                    CASE
                        WHEN pt.Id IS NOT NULL
                            THEN 1
                        ELSE 0
                    END
                    AS bit
                ) AS EsTemporada,

                pt.FechaFin
                    AS FechaFinTemporada,

                pt.BadgeTexto,
                pt.BadgeColor,

                (
                    SELECT COUNT(1)
                    FROM dbo.PublicacionFavoritos pf
                    WHERE pf.IdPublicacion = p.Id
                      AND pf.Activo = 1
                ) AS CantidadFavoritos,

                (
                    SELECT COUNT(1)
                    FROM dbo.PublicacionEventos pe
                    WHERE pe.IdPublicacion = p.Id
                      AND pe.TipoEvento = 'VIEW_DETAIL'
                ) AS CantidadVistas,

                (
                    SELECT COUNT(1)
                    FROM dbo.PublicacionEventos pe
                    WHERE pe.IdPublicacion = p.Id
                      AND pe.TipoEvento = 'CLICK_WHATSAPP'
                ) AS CantidadClicksWhatsapp

            FROM dbo.Publicaciones p

            OUTER APPLY
            (
                SELECT TOP 1
                    i.Url,
                    i.ThumbUrl

                FROM dbo.ImagenesPublicacion i

                WHERE i.IdPublicacion =
                    p.Id

                ORDER BY i.Id ASC
            ) img

            OUTER APPLY
            (
                SELECT TOP 1
                    pd.Id,
                    pd.FechaFin

                FROM dbo.PublicacionesDestacadas pd

                WHERE pd.IdPublicacion =
                    p.Id

                  AND pd.Estado =
                    'Activo'

                  AND GETDATE()
                      BETWEEN
                          pd.FechaInicio
                          AND pd.FechaFin

                ORDER BY
                    pd.FechaFin DESC,
                    pd.Id DESC
            ) d

            OUTER APPLY
            (
                SELECT TOP 1
                    t.Id,
                    t.FechaFin,
                    t.BadgeTexto,
                    t.BadgeColor

                FROM dbo.PublicacionesTemporada t

                WHERE t.IdPublicacion =
                    p.Id

                  AND t.Estado =
                    'Activo'

                  AND GETDATE()
                      BETWEEN
                          t.FechaInicio
                          AND t.FechaFin

                ORDER BY
                    t.FechaFin DESC,
                    t.Id DESC
            ) pt

            WHERE p.IdUsuario =
                @IdUsuario

              AND p.CanalPublicacion =
                'VITRINA'

              AND p.Estado =
                'Activo'

            ORDER BY

                CASE
                    WHEN pt.Id IS NOT NULL
                        THEN 0
                    ELSE 1
                END,

                CASE
                    WHEN d.Id IS NOT NULL
                        THEN 0
                    ELSE 1
                END,

                p.Fecha DESC;";

            var publicaciones =
                (
                    await conn
                        .QueryAsync<
                            PerfilPublicoPublicacionDto
                        >(
                            sql,
                            new
                            {
                                IdUsuario =
                                    idUsuario
                            })
                )
                .ToList();

            await CargarDetallesPublicaciones(
                conn,
                publicaciones);

            return publicaciones;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al obtener publicaciones de vitrina del usuario {IdUsuario}",
                idUsuario);

            throw new RepositoryException(
                "Error al obtener las publicaciones de la vitrina.",
                ex);
        }
    }

    public async Task<PerfilPublicoVendedorDto?> ObtenerMiPerfilVendedor(int idUsuario)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            _logger.LogInformation(
                "Iniciando obtención de mi perfil vendedor. IdUsuario: {IdUsuario}",
                idUsuario);

            const string sql = @"
                SELECT
                    v.Id                                   AS IdVendedor,
                    v.IdUsuario                            AS IdUsuario,
                    v.Slug                                 AS Slug,
                    v.NombreNegocio                        AS NombreNegocio,
                    u.NombreUsuario                        AS NombreUsuario,
                    v.Descripcion                          AS Descripcion,
                    v.BannerUrl                            AS BannerUrl,
                    v.BannerTipo                           AS BannerTipo,
                    u.FotoPerfil                           AS FotoPerfil,
                    v.Rubro                                AS Rubro,
                    COALESCE(v.CiudadVisible, u.Ciudad)    AS CiudadVisible,

                    CASE 
                        WHEN v.MostrarTelefono = 1 THEN v.Whatsapp
                        ELSE NULL
                    END                                    AS Telefono,

                    CASE 
                        WHEN v.MostrarEmail = 1 THEN COALESCE(NULLIF(v.CorreoContacto, ''), u.Email)
                        ELSE NULL
                    END                                    AS Email,

                    v.CorreoContacto                       AS CorreoContacto,
                    v.MostrarEmail                         AS MostrarEmail,

                    v.Whatsapp                             AS Whatsapp,
                    v.InstagramUrl                         AS InstagramUrl,
                    v.FacebookUrl                          AS FacebookUrl,
                    v.EsPerfilPublico                      AS EsPerfilPublico,
                    v.EsPremium                            AS EsPremium,
                    v.MostrarTelefono                      AS MostrarTelefono,

                    v.OfreceDelivery                       AS OfreceDelivery,
                    v.ZonaDelivery                         AS ZonaDelivery,
                    v.CostoDelivery                        AS CostoDelivery,
                    v.TiempoEstimadoDelivery               AS TiempoEstimadoDelivery
                FROM dbo.Vendedores v
                INNER JOIN dbo.Usuarios u
                    ON u.Id = v.IdUsuario
                WHERE v.IdUsuario = @IdUsuario
                  AND u.Estado = 'Activo';";

            var perfil = await conn.QueryFirstOrDefaultAsync<PerfilPublicoVendedorDto>(
                sql,
                new { IdUsuario = idUsuario });

            _logger.LogInformation(
                "Consulta de mi perfil vendedor finalizada. IdUsuario: {IdUsuario}. Encontrado: {Encontrado}",
                idUsuario,
                perfil != null);

            return perfil;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al obtener mi perfil vendedor. IdUsuario: {IdUsuario}",
                idUsuario);

            throw new RepositoryException("Error al obtener mi perfil vendedor.", ex);
        }
    }

    public async Task<bool> ExisteSlugEnOtroVendedor(string slug, int idUsuario)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
                SELECT COUNT(1)
                FROM dbo.Vendedores
                WHERE Slug = @Slug
                  AND IdUsuario <> @IdUsuario;";

            var cantidad = await conn.ExecuteScalarAsync<int>(
                sql,
                new
                {
                    Slug = slug,
                    IdUsuario = idUsuario
                });

            return cantidad > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al validar existencia de slug {Slug} para usuario {IdUsuario}",
                slug,
                idUsuario);

            throw new RepositoryException("Error al validar el slug del vendedor.", ex);
        }
    }

    public async Task ActualizarMiPerfilVendedor(
        ActualizarMiPerfilVendedorRequest request,
        int idUsuario,
        string? fotoPerfilUrl,
        string? bannerUrl,
        string? bannerTipo)
    {
        using var conn = _conexion.CreateSqlConnection();
        conn.Open();

        using var tran = conn.BeginTransaction();

        try
        {
            _logger.LogInformation(
                "Iniciando actualización de perfil vendedor. IdUsuario: {IdUsuario}",
                idUsuario);

            const string updateUsuarioSql = @"
                UPDATE dbo.Usuarios
                SET FotoPerfil = COALESCE(@FotoPerfilUrl, FotoPerfil)
                WHERE Id = @IdUsuario;";

            await conn.ExecuteAsync(
                updateUsuarioSql,
                new
                {
                    IdUsuario = idUsuario,
                    FotoPerfilUrl = fotoPerfilUrl
                },
                tran);

            const string updateVendedorSql = @"
                UPDATE dbo.Vendedores
                SET
                    NombreNegocio   = COALESCE(NULLIF(@NombreNegocio, ''), NombreNegocio),
                    Slug            = COALESCE(NULLIF(@Slug, ''), Slug),
                    Rubro           = COALESCE(NULLIF(@Rubro, ''), Rubro),
                    Descripcion     = COALESCE(NULLIF(@Descripcion, ''), Descripcion),
                    Whatsapp        = COALESCE(NULLIF(@Whatsapp, ''), Whatsapp),
                    InstagramUrl    = COALESCE(NULLIF(@InstagramUrl, ''), InstagramUrl),
                    FacebookUrl     = COALESCE(NULLIF(@FacebookUrl, ''), FacebookUrl),
                    CorreoContacto  = COALESCE(NULLIF(@CorreoContacto, ''), CorreoContacto),
                    CiudadVisible   = COALESCE(NULLIF(@CiudadVisible, ''), CiudadVisible),
                    BannerUrl       = COALESCE(@BannerUrl, BannerUrl),
                    BannerTipo      = COALESCE(@BannerTipo, BannerTipo),
                    EsPerfilPublico = COALESCE(@EsPerfilPublico, EsPerfilPublico),
                    MostrarTelefono = COALESCE(@MostrarTelefono, MostrarTelefono),
                    MostrarEmail    = COALESCE(@MostrarEmail, MostrarEmail),
                    OfreceDelivery  = COALESCE(@OfreceDelivery, OfreceDelivery),
                    ZonaDelivery    = CASE 
                                          WHEN COALESCE(@OfreceDelivery, OfreceDelivery) = 1 
                                          THEN COALESCE(@ZonaDelivery, ZonaDelivery) 
                                          ELSE NULL 
                                      END,
                    CostoDelivery   = CASE 
                                          WHEN COALESCE(@OfreceDelivery, OfreceDelivery) = 1 
                                          THEN COALESCE(@CostoDelivery, CostoDelivery) 
                                          ELSE NULL 
                                      END,
                    TiempoEstimadoDelivery = CASE 
                                          WHEN COALESCE(@OfreceDelivery, OfreceDelivery) = 1 
                                          THEN COALESCE(@TiempoEstimadoDelivery, TiempoEstimadoDelivery) 
                                          ELSE NULL 
                                      END
                WHERE IdUsuario = @IdUsuario;";

            var filas = await conn.ExecuteAsync(
                updateVendedorSql,
                new
                {
                    IdUsuario = idUsuario,
                    request.NombreNegocio,
                    request.Slug,
                    request.Rubro,
                    request.Descripcion,
                    request.Whatsapp,
                    request.InstagramUrl,
                    request.FacebookUrl,
                    request.CorreoContacto,
                    request.CiudadVisible,
                    BannerUrl = bannerUrl,
                    BannerTipo = bannerTipo,
                    request.EsPerfilPublico,
                    request.MostrarTelefono,
                    request.MostrarEmail,
                    request.OfreceDelivery,
                    request.ZonaDelivery,
                    request.CostoDelivery,
                    request.TiempoEstimadoDelivery
                },
                tran);

            if (filas == 0)
                throw new RepositoryException("No se encontró el perfil vendedor para actualizar.");

            tran.Commit();

            _logger.LogInformation(
                "Perfil vendedor actualizado correctamente. IdUsuario: {IdUsuario}",
                idUsuario);
        }
        catch (Exception ex)
        {
            tran.Rollback();

            _logger.LogError(
                ex,
                "Error al actualizar perfil vendedor. IdUsuario: {IdUsuario}",
                idUsuario);

            throw new RepositoryException("Error al actualizar el perfil vendedor.", ex);
        }
    }


    private static async Task
        CargarDetallesPublicaciones(
            IDbConnection conn,
            List<PerfilPublicoPublicacionDto>
                publicaciones)
    {
        if (publicaciones.Count == 0)
            return;

        var ids =
            publicaciones
                .Select(x => x.Id)
                .Distinct()
                .ToArray();

        var imagenes =
            (
                await conn.QueryAsync<
                    (
                        int IdPublicacion,
                        string Url
                    )
                >(
                    @"
                SELECT
                    IdPublicacion,
                    Url
                FROM dbo.ImagenesPublicacion
                WHERE IdPublicacion IN @Ids
                ORDER BY Id ASC;",
                    new
                    {
                        Ids = ids
                    })
            )
            .ToList();

        var planes =
            (
                await conn.QueryAsync<
                    (
                        int IdPublicacion,
                        int Cuotas,
                        decimal ValorCuota
                    )
                >(
                    @"
                SELECT
                    IdPublicacion,
                    Cuotas,
                    ValorCuota
                FROM dbo.PlanesCredito
                WHERE IdPublicacion IN @Ids
                ORDER BY Id ASC;",
                    new
                    {
                        Ids = ids
                    })
            )
            .ToList();

        var imagenesPorPublicacion =
            imagenes
                .GroupBy(
                    x => x.IdPublicacion)
                .ToDictionary(
                    g => g.Key,
                    g => g
                        .Select(x => x.Url)
                        .ToList());

        var planesPorPublicacion =
            planes
                .GroupBy(
                    x => x.IdPublicacion)
                .ToDictionary(
                    g => g.Key,
                    g => g
                        .Select(
                            x =>
                                new PlanOpcionDto
                                {
                                    Cuotas =
                                        x.Cuotas,

                                    ValorCuota =
                                        x.ValorCuota
                                })
                        .ToList());

        foreach (
            var publicacion
            in publicaciones)
        {
            publicacion.Imagenes =
                imagenesPorPublicacion
                    .TryGetValue(
                        publicacion.Id,
                        out var imagenesPublicacion)

                    ? imagenesPublicacion

                    : new List<string>();

            if (
                string.IsNullOrWhiteSpace(
                    publicacion.ImagenPrincipal)
                &&
                publicacion.Imagenes.Count > 0)
            {
                publicacion.ImagenPrincipal =
                    publicacion.Imagenes[0];
            }

            publicacion.PlanCredito =
                planesPorPublicacion
                    .TryGetValue(
                        publicacion.Id,
                        out var opciones)
                &&
                opciones.Count > 0

                    ? new PlanCreditoDto
                    {
                        Opciones =
                            opciones
                    }

                    : null;
        }
    }
}

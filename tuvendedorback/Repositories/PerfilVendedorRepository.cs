using Dapper;
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
                    v.Id                           AS IdVendedor,
                    v.IdUsuario                    AS IdUsuario,
                    v.Slug                         AS Slug,
                    v.NombreNegocio                AS NombreNegocio,
                    u.NombreUsuario                AS NombreUsuario,
                    v.Descripcion                  AS Descripcion,
                    v.BannerUrl                    AS BannerUrl,
                    u.FotoPerfil                   AS FotoPerfil,
                    v.Rubro                        AS Rubro,
                    COALESCE(v.CiudadVisible, u.Ciudad) AS CiudadVisible,
                    CASE 
                        WHEN v.MostrarTelefono = 1 THEN u.Telefono
                        ELSE NULL
                    END                            AS Telefono,
                    v.Whatsapp                     AS Whatsapp,
                    v.InstagramUrl                 AS InstagramUrl,
                    v.FacebookUrl                  AS FacebookUrl,
                    v.EsPerfilPublico              AS EsPerfilPublico,
                    v.EsPremium                    AS EsPremium,
                    v.MostrarTelefono              AS MostrarTelefono
                FROM dbo.Vendedores v
                INNER JOIN dbo.Usuarios u
                    ON u.Id = v.IdUsuario
                WHERE v.Slug = @Slug
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

    public async Task<List<PerfilPublicoPublicacionDto>> ObtenerPublicacionesActivasPorSlug(string slug)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            _logger.LogInformation(
                "Iniciando obtención de publicaciones activas para perfil público. Slug: {Slug}",
                slug);

            const string sql = @"
                SELECT
                    p.Id                           AS Id,
                    p.Titulo                       AS Titulo,
                    p.Descripcion                  AS Descripcion,
                    p.Precio                       AS Precio,
                    p.Categoria                    AS Categoria,
                    p.Ubicacion                    AS Ubicacion,
                    p.Estado                       AS Estado,
                    img.Url                        AS ImagenPrincipal,
                    img.ThumbUrl                   AS ThumbUrl,
                    CAST(
                        CASE 
                            WHEN d.Id IS NOT NULL THEN 1 
                            ELSE 0 
                        END AS bit
                    )                              AS EsDestacada
                FROM dbo.Vendedores v
                INNER JOIN dbo.Usuarios u
                    ON u.Id = v.IdUsuario
                INNER JOIN dbo.Publicaciones p
                    ON p.IdUsuario = v.IdUsuario
                OUTER APPLY
                (
                    SELECT TOP 1
                        i.Url,
                        i.ThumbUrl
                    FROM dbo.ImagenesPublicacion i
                    WHERE i.IdPublicacion = p.Id
                    ORDER BY i.Id ASC
                ) img
                LEFT JOIN dbo.PublicacionesDestacadas d
                    ON d.IdPublicacion = p.Id
                   AND d.Estado = 'Activo'
                   AND d.FechaFin >= GETDATE()
                WHERE v.Slug = @Slug
                  AND v.EsPerfilPublico = 1
                  AND u.Estado = 'Activo'
                  AND p.Estado = 'Activo'
                ORDER BY
                    CASE WHEN d.Id IS NOT NULL THEN 0 ELSE 1 END,
                    p.Fecha DESC;";

            var publicaciones = (await conn.QueryAsync<PerfilPublicoPublicacionDto>(
                sql,
                new { Slug = slug })).ToList();

            _logger.LogInformation(
                "Se obtuvieron {Cantidad} publicaciones activas para slug: {Slug}",
                publicaciones.Count,
                slug);

            return publicaciones;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al obtener publicaciones activas del perfil público. Slug: {Slug}",
                slug);

            throw new RepositoryException("Error al obtener las publicaciones del perfil público.", ex);
        }
    }
}

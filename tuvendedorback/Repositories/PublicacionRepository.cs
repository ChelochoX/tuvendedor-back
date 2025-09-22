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
            NULL               AS VendedorAvatar
        FROM Publicaciones p
        LEFT JOIN Vendedores v ON v.IdUsuario = p.IdUsuario
        WHERE (@Categoria IS NULL OR p.Categoria = @Categoria)
          AND (@Nombre IS NULL OR p.Titulo LIKE '%' + @Nombre + '%')
        ORDER BY p.Fecha DESC";

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

}

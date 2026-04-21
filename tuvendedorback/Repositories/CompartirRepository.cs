using Dapper;
using System.Data;
using tuvendedorback.Data;
using tuvendedorback.DTOs;
using tuvendedorback.Repositories.Interfaces;

namespace tuvendedorback.Repositories;

public class CompartirRepository : ICompartirRepository
{
    private readonly DbConnections _conexion;
    private readonly ILogger<CompartirRepository> _logger;

    public CompartirRepository(
        DbConnections conexion,
        ILogger<CompartirRepository> logger)
    {
        _conexion = conexion;
        _logger = logger;
    }

    public async Task<ProductoCompartirDto?> ObtenerProductoParaCompartir(int idProducto)
    {
        const string sql = @"
            SELECT TOP 1
                p.Id,
                p.Titulo,
                p.Descripcion,
                p.Precio,
                p.Categoria,
                p.Ubicacion,
                COALESCE(img.Url, img.ThumbUrl) AS ImagenUrl
            FROM Publicaciones p WITH (NOLOCK)
            OUTER APPLY (
                SELECT TOP 1
                    ip.Url,
                    ip.ThumbUrl
                FROM ImagenesPublicacion ip WITH (NOLOCK)
                WHERE ip.IdPublicacion = p.Id
                ORDER BY ip.Id ASC
            ) img
            WHERE p.Id = @IdProducto;
        ";

        try
        {
            using var conn = _conexion.CreateSqlConnection();

            return await conn.QueryFirstOrDefaultAsync<ProductoCompartirDto>(
                sql,
                new { IdProducto = idProducto },
                commandType: CommandType.Text
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al obtener producto para compartir. IdProducto={IdProducto}",
                idProducto
            );

            throw;
        }
    }
}

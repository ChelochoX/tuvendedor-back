using Dapper;
using tuvendedorback.Data;
using tuvendedorback.Exceptions;
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

    public async Task<int> InsertarPublicacion(CrearPublicacionRequest request, int idUsuario, List<string> imagenes)
    {
        using var conn = _conexion.CreateSqlConnection();
        conn.Open();
        using var tran = conn.BeginTransaction();

        try
        {
            const string insertPub = @"
            INSERT INTO Publicaciones (Titulo, Descripcion, Precio, Categoria, IdUsuario, MostrarBotonesCompra, Fecha)
            VALUES (@Titulo, @Descripcion, @Precio, @Categoria, @IdUsuario, @MostrarBotonesCompra, GETDATE());
            SELECT SCOPE_IDENTITY();";

            var publicacionId = await conn.ExecuteScalarAsync<int>(insertPub, new
            {
                request.Titulo,
                request.Descripcion,
                request.Precio,
                request.Categoria,
                IdUsuario = idUsuario,
                request.MostrarBotonesCompra
            }, tran);

            foreach (var url in imagenes)
            {
                await conn.ExecuteAsync("INSERT INTO ImagenesPublicacion (IdPublicacion, Url) VALUES (@Id, @Url);",
                    new { Id = publicacionId, Url = url }, tran);
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
}

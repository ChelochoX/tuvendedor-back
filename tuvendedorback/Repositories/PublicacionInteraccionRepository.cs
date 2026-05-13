using Dapper;
using tuvendedorback.Data;
using tuvendedorback.DTOs;
using tuvendedorback.Exceptions;
using tuvendedorback.Repositories.Interfaces;

namespace tuvendedorback.Repositories;

public class PublicacionInteraccionRepository : IPublicacionInteraccionRepository
{
    private readonly DbConnections _conexion;
    private readonly ILogger<PublicacionInteraccionRepository> _logger;

    public PublicacionInteraccionRepository(
        DbConnections conexion,
        ILogger<PublicacionInteraccionRepository> logger)
    {
        _conexion = conexion;
        _logger = logger;
    }

    public async Task<bool> ExistePublicacionActiva(int idPublicacion)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
                SELECT COUNT(1)
                FROM Publicaciones
                WHERE Id = @IdPublicacion
                  AND Estado = 'Activo';";

            var count = await conn.ExecuteScalarAsync<int>(
                sql,
                new { IdPublicacion = idPublicacion });

            return count > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al validar existencia de publicación {IdPublicacion}",
                idPublicacion);

            throw new RepositoryException("Error al validar la publicación.", ex);
        }
    }

    public async Task<PublicacionFavoritoResponseDto> ToggleFavorito(
        int idPublicacion,
        int? idUsuario,
        string? visitorId)
    {
        using var conn = _conexion.CreateSqlConnection();
        conn.Open();

        using var tran = conn.BeginTransaction();

        try
        {
            const string selectSql = @"
                SELECT TOP 1 Id, Activo
                FROM PublicacionFavoritos
                WHERE IdPublicacion = @IdPublicacion
                  AND (
                        (@IdUsuario IS NOT NULL AND IdUsuario = @IdUsuario)
                        OR
                        (@IdUsuario IS NULL AND @VisitorId IS NOT NULL AND VisitorId = @VisitorId)
                      );";

            var favorito = await conn.QueryFirstOrDefaultAsync<FavoritoDb>(
                selectSql,
                new
                {
                    IdPublicacion = idPublicacion,
                    IdUsuario = idUsuario,
                    VisitorId = visitorId
                },
                tran);

            bool nuevoEstado;

            if (favorito == null)
            {
                const string insertSql = @"
                    INSERT INTO PublicacionFavoritos
                    (
                        IdPublicacion,
                        IdUsuario,
                        VisitorId,
                        Activo,
                        FechaCreacion
                    )
                    VALUES
                    (
                        @IdPublicacion,
                        @IdUsuario,
                        @VisitorId,
                        1,
                        GETDATE()
                    );";

                await conn.ExecuteAsync(
                    insertSql,
                    new
                    {
                        IdPublicacion = idPublicacion,
                        IdUsuario = idUsuario,
                        VisitorId = idUsuario.HasValue ? null : visitorId
                    },
                    tran);

                nuevoEstado = true;
            }
            else
            {
                nuevoEstado = !favorito.Activo;

                const string updateSql = @"
                    UPDATE PublicacionFavoritos
                    SET Activo = @Activo,
                        FechaActualizacion = GETDATE()
                    WHERE Id = @Id;";

                await conn.ExecuteAsync(
                    updateSql,
                    new
                    {
                        Id = favorito.Id,
                        Activo = nuevoEstado
                    },
                    tran);
            }

            const string countSql = @"
                SELECT COUNT(1)
                FROM PublicacionFavoritos
                WHERE IdPublicacion = @IdPublicacion
                  AND Activo = 1;";

            var cantidadFavoritos = await conn.ExecuteScalarAsync<int>(
                countSql,
                new { IdPublicacion = idPublicacion },
                tran);

            tran.Commit();

            return new PublicacionFavoritoResponseDto
            {
                EsFavorito = nuevoEstado,
                CantidadFavoritos = cantidadFavoritos
            };
        }
        catch (Exception ex)
        {
            tran.Rollback();

            _logger.LogError(
                ex,
                "Error al marcar favorito. IdPublicacion={IdPublicacion}, IdUsuario={IdUsuario}, VisitorId={VisitorId}",
                idPublicacion,
                idUsuario,
                visitorId);

            throw new RepositoryException("Error al marcar favorito.", ex);
        }
    }

    public async Task RegistrarEvento(
        int idPublicacion,
        int? idUsuario,
        string? visitorId,
        string tipoEvento)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
                INSERT INTO PublicacionEventos
                (
                    IdPublicacion,
                    IdUsuario,
                    VisitorId,
                    TipoEvento,
                    FechaCreacion
                )
                SELECT
                    @IdPublicacion,
                    @IdUsuario,
                    @VisitorId,
                    @TipoEvento,
                    GETDATE()
                WHERE NOT EXISTS
                (
                    SELECT 1
                    FROM PublicacionEventos
                    WHERE IdPublicacion = @IdPublicacion
                      AND TipoEvento = @TipoEvento
                      AND FechaCreacion >= CAST(GETDATE() AS DATE)
                      AND FechaCreacion < DATEADD(DAY, 1, CAST(GETDATE() AS DATE))
                      AND (
                            (@IdUsuario IS NOT NULL AND IdUsuario = @IdUsuario)
                            OR
                            (@IdUsuario IS NULL AND @VisitorId IS NOT NULL AND VisitorId = @VisitorId)
                          )
                );";

            await conn.ExecuteAsync(
                sql,
                new
                {
                    IdPublicacion = idPublicacion,
                    IdUsuario = idUsuario,
                    VisitorId = idUsuario.HasValue ? null : visitorId,
                    TipoEvento = tipoEvento
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al registrar evento de publicación. IdPublicacion={IdPublicacion}, TipoEvento={TipoEvento}",
                idPublicacion,
                tipoEvento);

            throw new RepositoryException("Error al registrar interacción de publicación.", ex);
        }
    }

    private class FavoritoDb
    {
        public long Id { get; set; }
        public bool Activo { get; set; }
    }
}

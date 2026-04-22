using Dapper;
using tuvendedorback.Data;
using tuvendedorback.DTOs;
using tuvendedorback.Exceptions;
using tuvendedorback.Repositories.Interfaces;
using tuvendedorback.Request;

namespace tuvendedorback.Repositories;

public class SolicitudVisitaRepository : ISolicitudVisitaRepository
{
    private readonly DbConnections _conexion;
    private readonly ILogger<SolicitudVisitaRepository> _logger;

    public SolicitudVisitaRepository(
        DbConnections conexion,
        ILogger<SolicitudVisitaRepository> logger)
    {
        _conexion = conexion;
        _logger = logger;
    }

    public async Task<PublicacionParaVisitaDto?> ObtenerPublicacionParaVisita(int idPublicacion)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
                SELECT
                    p.Id                    AS IdPublicacion,
                    p.Titulo                AS Titulo,
                    p.Ubicacion             AS Ubicacion,
                    p.IdUsuario             AS IdUsuarioVendedor,
                    COALESCE(v.NombreNegocio, u.NombreUsuario) AS NombreVendedor,
                    COALESCE(NULLIF(v.Whatsapp, ''), u.Telefono) AS WhatsappVendedor
                FROM dbo.Publicaciones p
                INNER JOIN dbo.Usuarios u
                    ON u.Id = p.IdUsuario
                LEFT JOIN dbo.Vendedores v
                    ON v.IdUsuario = p.IdUsuario
                WHERE p.Id = @IdPublicacion
                  AND p.Estado = 'Activo'
                  AND u.Estado = 'Activo';";

            return await conn.QueryFirstOrDefaultAsync<PublicacionParaVisitaDto>(
                sql,
                new { IdPublicacion = idPublicacion });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al obtener publicación para solicitud de visita. IdPublicacion={IdPublicacion}",
                idPublicacion);

            throw new RepositoryException("Error al obtener la publicación para la visita.", ex);
        }
    }

    public async Task<int> CrearSolicitudVisita(
        CrearSolicitudVisitaRequest request,
        PublicacionParaVisitaDto publicacion)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
                INSERT INTO dbo.SolicitudesVisitaPublicacion
                (
                    IdPublicacion,
                    IdUsuarioVendedor,
                    NombreInteresado,
                    TelefonoInteresado,
                    FechaVisita,
                    HoraVisita,
                    Mensaje,
                    Estado,
                    NotificadoVendedor,
                    FechaSolicitud
                )
                VALUES
                (
                    @IdPublicacion,
                    @IdUsuarioVendedor,
                    @NombreInteresado,
                    @TelefonoInteresado,
                    @FechaVisita,
                    @HoraVisita,
                    @Mensaje,
                    'Pendiente',
                    0,
                    GETDATE()
                );

                SELECT CAST(SCOPE_IDENTITY() AS INT);";

            var id = await conn.ExecuteScalarAsync<int>(
                sql,
                new
                {
                    request.IdPublicacion,
                    publicacion.IdUsuarioVendedor,
                    NombreInteresado = request.NombreInteresado.Trim(),
                    TelefonoInteresado = request.TelefonoInteresado.Trim(),
                    FechaVisita = request.FechaVisita.Date,
                    request.HoraVisita,
                    Mensaje = request.Mensaje?.Trim()
                });

            _logger.LogInformation(
                "Solicitud de visita creada. IdSolicitudVisita={IdSolicitudVisita}, IdPublicacion={IdPublicacion}, IdUsuarioVendedor={IdUsuarioVendedor}",
                id,
                request.IdPublicacion,
                publicacion.IdUsuarioVendedor);

            return id;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al crear solicitud de visita. IdPublicacion={IdPublicacion}",
                request.IdPublicacion);

            throw new RepositoryException("Error al crear la solicitud de visita.", ex);
        }
    }

    public async Task MarcarNotificacionVendedorExitosa(int idSolicitudVisita)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
                UPDATE dbo.SolicitudesVisitaPublicacion
                SET
                    NotificadoVendedor = 1,
                    FechaNotificacion = GETDATE(),
                    ErrorNotificacion = NULL
                WHERE Id = @IdSolicitudVisita;";

            await conn.ExecuteAsync(sql, new { IdSolicitudVisita = idSolicitudVisita });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al marcar notificación exitosa. IdSolicitudVisita={IdSolicitudVisita}",
                idSolicitudVisita);

            throw new RepositoryException("Error al actualizar la notificación de la visita.", ex);
        }
    }

    public async Task MarcarNotificacionVendedorFallida(
        int idSolicitudVisita,
        string error)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
                UPDATE dbo.SolicitudesVisitaPublicacion
                SET
                    NotificadoVendedor = 0,
                    FechaNotificacion = NULL,
                    ErrorNotificacion = @Error
                WHERE Id = @IdSolicitudVisita;";

            await conn.ExecuteAsync(
                sql,
                new
                {
                    IdSolicitudVisita = idSolicitudVisita,
                    Error = error.Length > 1000 ? error[..1000] : error
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al marcar notificación fallida. IdSolicitudVisita={IdSolicitudVisita}",
                idSolicitudVisita);

            throw new RepositoryException("Error al actualizar el error de notificación de la visita.", ex);
        }
    }

    public async Task<List<SolicitudVisitaDto>> ObtenerSolicitudesPorVendedor(int idUsuarioVendedor)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
                SELECT
                    s.Id,
                    s.IdPublicacion,
                    s.IdUsuarioVendedor,
                    p.Titulo AS TituloPublicacion,
                    p.Ubicacion AS UbicacionPublicacion,
                    s.NombreInteresado,
                    s.TelefonoInteresado,
                    s.FechaVisita,
                    s.HoraVisita,
                    s.Mensaje,
                    s.Estado,
                    s.NotificadoVendedor,
                    s.FechaNotificacion,
                    s.ErrorNotificacion,
                    s.FechaSolicitud
                FROM dbo.SolicitudesVisitaPublicacion s
                INNER JOIN dbo.Publicaciones p
                    ON p.Id = s.IdPublicacion
                WHERE s.IdUsuarioVendedor = @IdUsuarioVendedor
                ORDER BY s.FechaSolicitud DESC;";

            return (await conn.QueryAsync<SolicitudVisitaDto>(
                sql,
                new { IdUsuarioVendedor = idUsuarioVendedor })).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al obtener solicitudes de visita del vendedor. IdUsuarioVendedor={IdUsuarioVendedor}",
                idUsuarioVendedor);

            throw new RepositoryException("Error al obtener las solicitudes de visita.", ex);
        }
    }
}

using Dapper;
using tuvendedorback.Data;
using tuvendedorback.DTOs;
using tuvendedorback.Repositories.Interfaces;

namespace tuvendedorback.Repositories;

public class ConversacionRepository : IConversacionRepository
{
    private readonly DbConnections _conexion;
    private readonly ILogger<ConversacionRepository> _logger;

    public ConversacionRepository(DbConnections conexion, ILogger<ConversacionRepository> logger)
    {
        _conexion = conexion;
        _logger = logger;
    }

    public async Task<int> ObtenerOCrearConversacion(string canal, string identificador)
    {
        using var conn = _conexion.CreateSqlConnection();
        try
        {
            var id = await conn.ExecuteScalarAsync<int?>(@"
                SELECT Id
                FROM Conversaciones
                WHERE Canal = @Canal AND IdentificadorExterno = @Identificador",
                new { Canal = canal, Identificador = identificador });

            if (id.HasValue)
                return id.Value;

            return await conn.ExecuteScalarAsync<int>(@"
                INSERT INTO Conversaciones (Canal, IdentificadorExterno, FechaInicio)
                VALUES (@Canal, @Identificador, GETDATE());
                SELECT SCOPE_IDENTITY();",
                new { Canal = canal, Identificador = identificador });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error obteniendo/creando conversación");
            throw;
        }
    }

    public async Task RegistrarMensaje(int idConversacion, string emisor, string mensaje)
    {
        using var conn = _conexion.CreateSqlConnection();
        conn.Open();

        using var tx = conn.BeginTransaction();

        try
        {
            await conn.ExecuteAsync(@"
            INSERT INTO MensajesConversacion
            (IdConversacion, Emisor, Mensaje, Fecha)
            VALUES (@Id, @Emisor, @Mensaje, GETDATE())",
                new { Id = idConversacion, Emisor = emisor, Mensaje = mensaje },
                tx);

            await conn.ExecuteAsync(@"
            UPDATE Conversaciones
            SET FechaUltimoMensaje = GETDATE()
            WHERE Id = @Id",
                new { Id = idConversacion },
                tx);

            tx.Commit();
        }
        catch (Exception ex)
        {
            tx.Rollback();
            _logger.LogError(ex, "Error registrando mensaje");
            throw;
        }
    }



    public async Task<ConversacionContextoDto?> ObtenerContexto(int idConversacion)
    {
        using var conn = _conexion.CreateSqlConnection();
        try
        {
            return await conn.QueryFirstOrDefaultAsync<ConversacionContextoDto>(@"
            SELECT 
                PasoActual,
                Intencion,
                IdPublicacion,
                CodigoPrompt
            FROM ContextoConversacion
            WHERE IdConversacion = @Id",
                new { Id = idConversacion });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error obteniendo contexto de la conversación {IdConversacion}",
                idConversacion);

            throw;
        }
    }

    public async Task ActualizarContexto(
         int idConversacion,
         string pasoActual,
         string? intencion = null,
         int? idPublicacion = null,
         string? codigoPrompt = null)
    {
        using var conn = _conexion.CreateSqlConnection();
        try
        {
            await conn.ExecuteAsync(@"
                MERGE ContextoConversacion AS t
                USING (SELECT @Id AS Id) s
                ON t.IdConversacion = s.Id
                WHEN MATCHED THEN
                    UPDATE SET
                        PasoActual = @Paso,
                        Intencion = @Intencion,
                        IdPublicacion = @IdPublicacion,
                        CodigoPrompt = ISNULL(@CodigoPrompt, CodigoPrompt),
                        FechaActualizacion = GETDATE()
                WHEN NOT MATCHED THEN
                    INSERT (IdConversacion, PasoActual, Intencion, IdPublicacion, CodigoPrompt)
                    VALUES (@Id, @Paso, @Intencion, @IdPublicacion, @CodigoPrompt);",
                new
                {
                    Id = idConversacion,
                    Paso = pasoActual,
                    Intencion = intencion,
                    IdPublicacion = idPublicacion,
                    CodigoPrompt = codigoPrompt
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error actualizando contexto");
            throw;
        }
    }

    public async Task<IEnumerable<MensajeConversacionDto>> ObtenerHistorialIA(
     int idConversacion,
     int limite = 10)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            _logger.LogInformation(
                "Obteniendo historial IA para conversación {IdConversacion}",
                idConversacion);

            var mensajes = await conn.QueryAsync<MensajeConversacionDto>(@"
            SELECT TOP (@Limite)
                Emisor,
                Mensaje
            FROM MensajesConversacion
            WHERE IdConversacion = @Id
            ORDER BY Fecha DESC",
                new
                {
                    Id = idConversacion,
                    Limite = limite
                });

            return mensajes ?? Enumerable.Empty<MensajeConversacionDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error obteniendo historial IA de la conversación {IdConversacion}",
                idConversacion);

            throw;
        }
    }

}

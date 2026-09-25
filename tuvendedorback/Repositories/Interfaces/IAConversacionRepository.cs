using Dapper;
using tuvendedorback.Data;
using tuvendedorback.DTOs;
using tuvendedorback.Exceptions;

namespace tuvendedorback.Repositories.Interfaces;

public class IAConversacionRepository
    : IIAConversacionRepository
{
    private readonly DbConnections _conexion;

    private readonly ILogger<IAConversacionRepository>
        _logger;


    public IAConversacionRepository(
        DbConnections conexion,
        ILogger<IAConversacionRepository> logger)
    {
        _conexion = conexion;
        _logger = logger;
    }


    public async Task<int> ObtenerOCrearConversacion(
        string identificadorExterno)
    {
        using var conn =
            _conexion.CreateSqlConnection();

        try
        {
            const string sqlBuscar = @"
                SELECT TOP (1)
                    Id
                FROM dbo.Conversaciones
                WHERE Canal = 'WHATSAPP'
                  AND IdentificadorExterno = @IdentificadorExterno
                  AND Estado = 'ACTIVA'
                ORDER BY Id DESC;
                ";


            var idExistente =
                await conn.QueryFirstOrDefaultAsync<int?>(
                    sqlBuscar,
                    new
                    {
                        IdentificadorExterno =
                            identificadorExterno
                    });


            if (idExistente.HasValue)
            {
                await conn.ExecuteAsync(
                    @"
                        UPDATE dbo.Conversaciones
                        SET FechaUltimoMensaje = GETDATE()
                        WHERE Id = @IdConversacion;
                        ",
                    new
                    {
                        IdConversacion =
                            idExistente.Value
                    });


                return idExistente.Value;
            }


            const string sqlCrear = @"
                INSERT INTO dbo.Conversaciones
                (
                    Canal,
                    IdentificadorExterno,
                    Estado,
                    Modo,
                    FechaInicio,
                    FechaUltimoMensaje
                )
                OUTPUT INSERTED.Id
                VALUES
                (
                    'WHATSAPP',
                    @IdentificadorExterno,
                    'ACTIVA',
                    'IA',
                    GETDATE(),
                    GETDATE()
                );
                ";


            return await conn.ExecuteScalarAsync<int>(
                sqlCrear,
                new
                {
                    IdentificadorExterno =
                        identificadorExterno
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error obteniendo o creando conversación IA. Identificador={Identificador}",
                identificadorExterno);


            throw new RepositoryException(
                "Error obteniendo o creando conversación IA.",
                ex);
        }
    }


    public async Task<int?>
        ObtenerIdPublicacionContexto(
            int idConversacion)
    {
        using var conn =
            _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
                SELECT TOP (1)
                    IdPublicacion
                FROM dbo.ContextoConversacion
                WHERE IdConversacion = @IdConversacion
                ORDER BY Id DESC;
                ";


            return await conn
                .QueryFirstOrDefaultAsync<int?>(
                    sql,
                    new
                    {
                        IdConversacion =
                            idConversacion
                    });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error obteniendo contexto IA. IdConversacion={IdConversacion}",
                idConversacion);


            throw new RepositoryException(
                "Error obteniendo contexto de conversación.",
                ex);
        }
    }


    public async Task ActualizarContexto(
        int idConversacion,
        int idPublicacion,
        string codigoPrompt)
    {
        using var conn =
            _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
                DECLARE @IdContexto INT;


                SELECT TOP (1)
                    @IdContexto = Id
                FROM dbo.ContextoConversacion
                WHERE IdConversacion = @IdConversacion
                ORDER BY Id DESC;


                IF @IdContexto IS NULL
                BEGIN

                    INSERT INTO dbo.ContextoConversacion
                    (
                        IdConversacion,
                        PasoActual,
                        Intencion,
                        IdPublicacion,
                        FechaActualizacion,
                        CodigoPrompt
                    )
                    VALUES
                    (
                        @IdConversacion,
                        'VENTA_MOTO',
                        'CONSULTA_COMERCIAL',
                        @IdPublicacion,
                        GETDATE(),
                        @CodigoPrompt
                    );

                END
                ELSE
                BEGIN

                    UPDATE dbo.ContextoConversacion
                    SET
                        PasoActual =
                            'VENTA_MOTO',

                        Intencion =
                            'CONSULTA_COMERCIAL',

                        IdPublicacion =
                            @IdPublicacion,

                        FechaActualizacion =
                            GETDATE(),

                        CodigoPrompt =
                            @CodigoPrompt

                    WHERE Id =
                        @IdContexto;

                END
                ";


            await conn.ExecuteAsync(
                sql,
                new
                {
                    IdConversacion =
                        idConversacion,

                    IdPublicacion =
                        idPublicacion,

                    CodigoPrompt =
                        codigoPrompt
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error actualizando contexto IA. IdConversacion={IdConversacion}",
                idConversacion);


            throw new RepositoryException(
                "Error actualizando contexto de conversación.",
                ex);
        }
    }


    public async Task RegistrarMensaje(
        int idConversacion,
        string emisor,
        string mensaje)
    {
        using var conn =
            _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
                INSERT INTO dbo.MensajesConversacion
                (
                    IdConversacion,
                    Emisor,
                    Mensaje,
                    Fecha
                )
                VALUES
                (
                    @IdConversacion,
                    @Emisor,
                    @Mensaje,
                    GETDATE()
                );


                UPDATE dbo.Conversaciones
                SET FechaUltimoMensaje =
                    GETDATE()
                WHERE Id =
                    @IdConversacion;
                ";


            await conn.ExecuteAsync(
                sql,
                new
                {
                    IdConversacion =
                        idConversacion,

                    Emisor =
                        emisor,

                    Mensaje =
                        mensaje
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error registrando mensaje IA. IdConversacion={IdConversacion}",
                idConversacion);


            throw new RepositoryException(
                "Error registrando mensaje de conversación.",
                ex);
        }
    }


    public async Task<
        IReadOnlyList<MensajeConversacionHistorialDto>>
        ObtenerUltimosMensajes(
            int idConversacion,
            int limite)
    {
        using var conn =
            _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
                SELECT
                    Emisor,
                    Mensaje,
                    Fecha
                FROM
                (
                    SELECT TOP (@Limite)

                        Id,
                        Emisor,
                        Mensaje,
                        Fecha

                    FROM dbo.MensajesConversacion

                    WHERE IdConversacion =
                        @IdConversacion

                    ORDER BY Id DESC

                ) Historial

                ORDER BY Id ASC;
                ";


            var data =
                await conn.QueryAsync<
                    MensajeConversacionHistorialDto>(
                    sql,
                    new
                    {
                        IdConversacion =
                            idConversacion,

                        Limite =
                            limite
                    });


            return data.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error obteniendo historial IA. IdConversacion={IdConversacion}",
                idConversacion);


            throw new RepositoryException(
                "Error obteniendo historial de conversación.",
                ex);
        }
    }


    public async Task<string?> ObtenerPromptActivo(
        string codigo)
    {
        using var conn =
            _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
                SELECT TOP (1)
                    PromptBase
                FROM dbo.PromptsIA
                WHERE Codigo = @Codigo
                  AND Activo = 1
                ORDER BY Id DESC;
                ";


            return await conn
                .QueryFirstOrDefaultAsync<string?>(
                    sql,
                    new
                    {
                        Codigo =
                            codigo
                    });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error obteniendo prompt IA. Codigo={Codigo}",
                codigo);


            throw new RepositoryException(
                "Error obteniendo configuración de IA.",
                ex);
        }
    }
}
using Dapper;
using tuvendedorback.Data;
using tuvendedorback.DTOs;
using tuvendedorback.Exceptions;
using tuvendedorback.Repositories.Interfaces;

namespace tuvendedorback.Repositories;

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


    // =========================================================
    // CONVERSACION
    // =========================================================

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


    // =========================================================
    // PUBLICACION ACTUAL
    // =========================================================

    public async Task<int?> ObtenerIdPublicacionContexto(
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
                "Error obteniendo publicación del contexto. IdConversacion={IdConversacion}",
                idConversacion);


            throw new RepositoryException(
                "Error obteniendo publicación del contexto.",
                ex);
        }
    }


    // =========================================================
    // MODELO ACTUAL
    // =========================================================

    public async Task<int?> ObtenerIdModeloActual(
        int idConversacion)
    {
        using var conn =
            _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
SELECT TOP (1)
    IdModeloProductoActual
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
                "Error obteniendo modelo actual. IdConversacion={IdConversacion}",
                idConversacion);


            throw new RepositoryException(
                "Error obteniendo modelo actual de la conversación.",
                ex);
        }
    }


    // =========================================================
    // ACTUALIZAR CONTEXTO
    // =========================================================

    public async Task ActualizarContexto(
        int idConversacion,
        int? idPublicacion,
        int? idModeloProducto,
        string? codigoPrompt)
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
        IdModeloProductoActual,
        FechaActualizacion,
        CodigoPrompt
    )
    VALUES
    (
        @IdConversacion,
        'VENTA_MOTO',
        'CONSULTA_COMERCIAL',
        @IdPublicacion,
        @IdModeloProducto,
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

        IdModeloProductoActual =
            @IdModeloProducto,

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

                    IdModeloProducto =
                        idModeloProducto,

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


    // =========================================================
    // LIMPIAR PRODUCTO DEL CONTEXTO
    // =========================================================

    public async Task LimpiarProductoContexto(
        int idConversacion)
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
        IdModeloProductoActual,
        FechaActualizacion,
        CodigoPrompt
    )
    VALUES
    (
        @IdConversacion,
        'ESPERANDO_MODELO',
        'SELECCION_MODELO',
        NULL,
        NULL,
        GETDATE(),
        NULL
    );

END
ELSE
BEGIN

    UPDATE dbo.ContextoConversacion
    SET
        PasoActual =
            'ESPERANDO_MODELO',

        Intencion =
            'SELECCION_MODELO',

        IdPublicacion =
            NULL,

        IdModeloProductoActual =
            NULL,

        FechaActualizacion =
            GETDATE(),

        CodigoPrompt =
            NULL

    WHERE Id =
        @IdContexto;

END
";


            await conn.ExecuteAsync(
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
                "Error limpiando producto del contexto. IdConversacion={IdConversacion}",
                idConversacion);


            throw new RepositoryException(
                "Error limpiando producto de la conversación.",
                ex);
        }
    }


    // =========================================================
    // REGISTRAR MENSAJE
    // =========================================================

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
SET FechaUltimoMensaje = GETDATE()
WHERE Id = @IdConversacion;
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


    // =========================================================
    // HISTORIAL
    // =========================================================

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
    WHERE IdConversacion = @IdConversacion
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


    // =========================================================
    // PROMPTS
    // =========================================================

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


    // =========================================================
    // MODELOS DE MOTOS ACTIVOS
    // FUENTE: BASE DE DATOS
    // =========================================================

    public async Task<IReadOnlyList<MotoModeloCandidatoDto>>
        ObtenerModelosMotoActivos()
    {
        using var conn =
            _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
                SELECT
                    mp.Id AS IdModeloProducto,
                    m.Id AS IdMarca,
                    m.Nombre AS Marca,
                    mp.NombreModelo AS Modelo,
                    mp.CodigoReferencia,
                    mp.Cilindrada,
                    pub.IdPublicacion

                FROM dbo.ModelosProducto mp

                INNER JOIN dbo.Marcas m
                    ON m.Id = mp.IdMarca

                OUTER APPLY
                (
                    SELECT TOP (1)
                        p.Id AS IdPublicacion

                    FROM dbo.PublicacionModeloProducto pmp

                    INNER JOIN dbo.Publicaciones p
                        ON p.Id = pmp.IdPublicacion

                    WHERE pmp.IdModeloProducto = mp.Id
                      AND pmp.Estado = 'Activo'
                      AND p.Estado = 'Activo'

                    ORDER BY
                        pmp.EsPrincipal DESC,
                        p.Id DESC

                ) pub

                WHERE mp.Estado = 'Activo'
                  AND m.Estado = 'Activo'
                  AND UPPER(ISNULL(mp.Rubro, '')) LIKE '%MOTO%'

                ORDER BY
                    m.Nombre,
                    mp.NombreModelo;
                ";


            var data =
                await conn.QueryAsync<MotoModeloCandidatoDto>(
                    sql);


            return data.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error obteniendo modelos de motos desde BBDD.");


            throw new RepositoryException(
                "Error obteniendo catálogo de motos.",
                ex);
        }
    }


    // =========================================================
    // PUBLICACION ACTIVA POR MODELO
    // =========================================================

    public async Task<int?> ObtenerPublicacionActivaPorModelo(
        int idModeloProducto)
    {
        using var conn =
            _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
SELECT TOP (1)
    p.Id

FROM dbo.PublicacionModeloProducto pmp

INNER JOIN dbo.Publicaciones p
    ON p.Id = pmp.IdPublicacion

WHERE pmp.IdModeloProducto = @IdModeloProducto
  AND pmp.Estado = 'Activo'
  AND p.Estado = 'Activo'

ORDER BY
    pmp.EsPrincipal DESC,
    p.Id DESC;
";


            return await conn
                .QueryFirstOrDefaultAsync<int?>(
                    sql,
                    new
                    {
                        IdModeloProducto =
                            idModeloProducto
                    });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error obteniendo publicación del modelo. IdModeloProducto={IdModeloProducto}",
                idModeloProducto);


            throw new RepositoryException(
                "Error obteniendo publicación asociada al modelo.",
                ex);
        }
    }

    // =========================================================
    // OBTENER MODO CONVERSACION
    // =========================================================

    public async Task<string> ObtenerModoConversacion(
        int idConversacion)
    {
        using var conn =
            _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
SELECT TOP (1)
    Modo
FROM dbo.Conversaciones
WHERE Id = @IdConversacion;
";


            return
                await conn.QueryFirstOrDefaultAsync<string>(
                    sql,
                    new
                    {
                        IdConversacion =
                            idConversacion
                    })
                ??
                "IA";
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error obteniendo modo de conversación. Id={IdConversacion}",
                idConversacion);


            throw new RepositoryException(
                "Error obteniendo modo de conversación.",
                ex);
        }
    }


    // =========================================================
    // CAMBIAR MODO CONVERSACION
    // =========================================================

    public async Task CambiarModoConversacion(
        int idConversacion,
        string modo)
    {
        using var conn =
            _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
UPDATE dbo.Conversaciones
SET
    Modo = @Modo,
    FechaUltimoMensaje = GETDATE()
WHERE Id = @IdConversacion;
";


            await conn.ExecuteAsync(
                sql,
                new
                {
                    IdConversacion =
                        idConversacion,

                    Modo =
                        modo
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error cambiando modo conversación. Id={IdConversacion}, Modo={Modo}",
                idConversacion,
                modo);


            throw new RepositoryException(
                "Error cambiando modo de conversación.",
                ex);
        }
    }
}

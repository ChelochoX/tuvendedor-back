using Dapper;
using tuvendedorback.Data;
using tuvendedorback.DTOs;
using tuvendedorback.Exceptions;
using tuvendedorback.Repositories.Interfaces;
using tuvendedorback.Request;

namespace tuvendedorback.Repositories;

public sealed class SeguimientoWhatsappRepository : ISeguimientoWhatsappRepository
{
    private readonly DbConnections _conexion;
    private readonly ILogger<SeguimientoWhatsappRepository> _logger;

    public SeguimientoWhatsappRepository(
        DbConnections conexion,
        ILogger<SeguimientoWhatsappRepository> logger)
    {
        _conexion = conexion;
        _logger = logger;
    }

    public async Task<SeguimientoWhatsappConfiguracionDto> ObtenerConfiguracion()
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
SELECT
    Activo,
    ModoEnvio,
    SeparacionEnviosMinutos,
    HoraInicio,
    HoraFin,
    FechaDesdeElegibilidad,
    MinutosReintentoTecnico,
    MaximoReintentosTecnicos,
    FechaActualizacion
FROM dbo.SeguimientoWhatsappConfiguracion
WHERE Id = 1;

SELECT
    Id,
    Orden,
    DemoraValor,
    DemoraUnidad,
    Mensaje,
    Activo
FROM dbo.SeguimientoWhatsappReglas
ORDER BY Orden;";

            using var multi = await conn.QueryMultipleAsync(sql);

            var configuracion =
                await multi.ReadSingleAsync<SeguimientoWhatsappConfiguracionDto>();

            configuracion.Reglas =
                (await multi.ReadAsync<SeguimientoWhatsappReglaDto>())
                .ToList();

            return configuracion;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error obteniendo configuración de seguimiento WhatsApp.");
            throw new RepositoryException(
                "No se pudo obtener la configuración de seguimiento WhatsApp.",
                ex);
        }
    }

    public async Task GuardarConfiguracion(
        ActualizarSeguimientoWhatsappConfiguracionRequest request,
        int? idUsuario)
    {
        using var conn = _conexion.CreateSqlConnection();
        conn.Open();
        using var transaction = conn.BeginTransaction();

        try
        {
            const string sqlConfig = @"
UPDATE dbo.SeguimientoWhatsappConfiguracion
SET
    Activo = @Activo,
    ModoEnvio = @ModoEnvio,
    SeparacionEnviosMinutos = @SeparacionEnviosMinutos,
    HoraInicio = @HoraInicio,
    HoraFin = @HoraFin,
    FechaDesdeElegibilidad = @FechaDesdeElegibilidad,
    MinutosReintentoTecnico = @MinutosReintentoTecnico,
    MaximoReintentosTecnicos = @MaximoReintentosTecnicos,
    FechaActualizacion = GETDATE(),
    IdUsuarioActualizacion = @IdUsuario
WHERE Id = 1;

-- IMPORTANTE: guardar configuración o apagar el motor NO borra la cola.
-- Los pendientes se conservan para la prueba controlada y la auditoría.
-- Antes de activar envío real hay que revisar los pendientes existentes.

UPDATE dbo.SeguimientoWhatsappReglas
SET
    Activo = 0,
    FechaActualizacion = GETDATE();";

            await conn.ExecuteAsync(
                sqlConfig,
                new
                {
                    request.Activo,
                    ModoEnvio = request.ModoEnvio.Trim().ToUpperInvariant(),
                    request.SeparacionEnviosMinutos,
                    request.HoraInicio,
                    request.HoraFin,
                    request.FechaDesdeElegibilidad,
                    request.MinutosReintentoTecnico,
                    request.MaximoReintentosTecnicos,
                    IdUsuario = idUsuario
                },
                transaction);

            const string sqlRegla = @"
IF EXISTS
(
    SELECT 1
    FROM dbo.SeguimientoWhatsappReglas
    WHERE Orden = @Orden
)
BEGIN
    UPDATE dbo.SeguimientoWhatsappReglas
    SET
        DemoraValor = @DemoraValor,
        DemoraUnidad = @DemoraUnidad,
        Mensaje = @Mensaje,
        Activo = @Activo,
        FechaActualizacion = GETDATE()
    WHERE Orden = @Orden;
END
ELSE
BEGIN
    INSERT INTO dbo.SeguimientoWhatsappReglas
    (
        Orden,
        DemoraValor,
        DemoraUnidad,
        Mensaje,
        Activo,
        FechaCreacion,
        FechaActualizacion
    )
    VALUES
    (
        @Orden,
        @DemoraValor,
        @DemoraUnidad,
        @Mensaje,
        @Activo,
        GETDATE(),
        GETDATE()
    );
END;";

            foreach (var regla in request.Reglas)
            {
                await conn.ExecuteAsync(
                    sqlRegla,
                    new
                    {
                        regla.Orden,
                        regla.DemoraValor,
                        DemoraUnidad = regla.DemoraUnidad.Trim().ToUpperInvariant(),
                        Mensaje = regla.Mensaje.Trim(),
                        regla.Activo
                    },
                    transaction);
            }

            transaction.Commit();
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            _logger.LogError(ex, "Error guardando configuración de seguimiento WhatsApp.");
            throw new RepositoryException(
                "No se pudo guardar la configuración de seguimiento WhatsApp.",
                ex);
        }
    }

    public async Task<IReadOnlyList<SeguimientoWhatsappCandidatoDto>> ObtenerCandidatos(
        DateTime fechaDesdeElegibilidad,
        int limite)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
SELECT TOP (@Limite)
    i.Id AS IdInteresado,
    conv.IdConversacion,
    i.Nombre,
    i.Telefono,
    COALESCE(
        NULLIF(
            LTRIM(RTRIM(CONCAT(
                ISNULL(i.MarcaInteres, ''),
                ' ',
                ISNULL(i.ModeloInteres, '')
            ))),
            ''
        ),
        NULLIF(LTRIM(RTRIM(i.ProductoInteres)), '')
    ) AS ProductoInteres,
    CAST(cliente.Fecha AS DATETIME2(3)) AS CicloInicio,
    cliente.Fecha AS FechaUltimoMensajeCliente,
    cliente.Mensaje AS UltimoMensajeCliente,

    (
        SELECT COUNT(1)
        FROM dbo.SeguimientoWhatsappEnvios e
        WHERE e.IdInteresado = i.Id
          AND e.CicloInicio = CAST(cliente.Fecha AS DATETIME2(3))
          AND e.Estado = 'ENVIADO'
    ) AS CantidadSeguimientosEnviados,

    (
        SELECT MAX(e.NumeroSeguimiento)
        FROM dbo.SeguimientoWhatsappEnvios e
        WHERE e.IdInteresado = i.Id
          AND e.CicloInicio = CAST(cliente.Fecha AS DATETIME2(3))
          AND e.Estado = 'ENVIADO'
    ) AS UltimoNumeroSeguimientoEnviado,

    (
        SELECT MAX(e.FechaEnvio)
        FROM dbo.SeguimientoWhatsappEnvios e
        WHERE e.IdInteresado = i.Id
          AND e.CicloInicio = CAST(cliente.Fecha AS DATETIME2(3))
          AND e.Estado = 'ENVIADO'
    ) AS FechaUltimoSeguimientoEnviado

FROM dbo.Interesados i

OUTER APPLY
(
    SELECT TOP (1)
        c.Id AS IdConversacion,
        c.Modo,
        c.FechaUltimoMensaje
    FROM dbo.Conversaciones c
    LEFT JOIN dbo.Contactos ct ON ct.Id = c.IdContacto
    WHERE
        c.Id = i.IdConversacion
        OR
        (
            i.IdConversacion IS NULL
            AND UPPER(ISNULL(c.Canal, '')) = 'WHATSAPP'
            AND REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(ct.Telefono, ''), ' ', ''), '-', ''), '+', ''), '(', ''), ')', '')
              = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(i.Telefono, ''), ' ', ''), '-', ''), '+', ''), '(', ''), ')', '')
        )
    ORDER BY CASE WHEN c.Id = i.IdConversacion THEN 0 ELSE 1 END,
             c.FechaUltimoMensaje DESC, c.Id DESC
) conv

OUTER APPLY
(
    SELECT TOP (1)
        m.Fecha,
        m.Mensaje
    FROM dbo.MensajesConversacion m
    WHERE m.IdConversacion = conv.IdConversacion
      AND UPPER(m.Emisor) = 'CLIENTE'
    ORDER BY m.Fecha DESC, m.Id DESC
) cliente

OUTER APPLY
(
    SELECT TOP (1)
        m.Fecha,
        m.Emisor
    FROM dbo.MensajesConversacion m
    WHERE m.IdConversacion = conv.IdConversacion
    ORDER BY m.Fecha DESC, m.Id DESC
) ultimo

WHERE
    ISNULL(i.Estado, 'Activo') <> 'Inactivo'
    AND UPPER(ISNULL(i.Origen, '')) = 'WHATSAPP'
    AND ISNULL(i.NoContactarWhatsapp, 0) = 0
    AND ISNULL(i.RequiereSeguimiento, 0) = 1
    -- Seguimientos automáticos exclusivamente para motos con modelo identificado.
    AND (i.IdModeloProducto IS NOT NULL
         OR NULLIF(LTRIM(RTRIM(ISNULL(i.ModeloInteres, ''))), '') IS NOT NULL)
    AND NULLIF(LTRIM(RTRIM(ISNULL(i.Telefono, ''))), '') IS NOT NULL

    -- Si por datos históricos existen dos Interesados para el mismo teléfono,
    -- procesamos solamente el registro WhatsApp más reciente para no duplicar mensajes.
    AND i.Id =
    (
        SELECT TOP (1) i2.Id
        FROM dbo.Interesados i2
        WHERE UPPER(ISNULL(i2.Origen, '')) = 'WHATSAPP'
          AND ISNULL(i2.Estado, 'Activo') <> 'Inactivo'
          AND REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(i2.Telefono, ''), ' ', ''), '-', ''), '+', ''), '(', ''), ')', '')
              =
              REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(i.Telefono, ''), ' ', ''), '-', ''), '+', ''), '(', ''), ')', '')
        ORDER BY
            ISNULL(i2.FechaUltimaInteraccion, i2.FechaRegistro) DESC,
            i2.Id DESC
    )

    AND cliente.Fecha IS NOT NULL
    AND cliente.Fecha >= @FechaDesdeElegibilidad
    AND UPPER(ISNULL(ultimo.Emisor, '')) = 'IA'
    AND UPPER(ISNULL(conv.Modo, 'IA')) <> 'HUMANO'
    AND UPPER(ISNULL(i.EstadoConsulta, '')) NOT IN
        ('CERRADO', 'CREDITO_EN_PROCESO', 'CONTADO_EN_PROCESO', 'DERIVADO_HUMANO')

    AND NOT EXISTS
    (
        SELECT 1
        FROM dbo.SolicitudesCredito sc
        WHERE sc.IdConversacion = conv.IdConversacion
          AND sc.Estado IN ('EN_PROCESO', 'PRE_EVALUACION', 'DOCUMENTACION')
    )

    AND NOT EXISTS
    (
        SELECT 1
        FROM dbo.SolicitudesContadoMoto sc
        WHERE sc.IdConversacion = conv.IdConversacion
          AND sc.Estado IN ('EN_PROCESO', 'DOCUMENTACION')
    )

ORDER BY cliente.Fecha ASC, i.Id ASC;";

            var data = await conn.QueryAsync<SeguimientoWhatsappCandidatoDto>(
                sql,
                new
                {
                    FechaDesdeElegibilidad = fechaDesdeElegibilidad,
                    Limite = Math.Clamp(limite, 1, 500)
                });

            return data.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error obteniendo candidatos para seguimiento WhatsApp.");
            throw new RepositoryException(
                "No se pudieron obtener los clientes candidatos a seguimiento.",
                ex);
        }
    }

    public async Task<bool> CrearPendienteSiNoExiste(
        SeguimientoWhatsappCandidatoDto candidato,
        SeguimientoWhatsappReglaDto regla,
        DateTime programadoPara,
        string mensaje)
    {
        using var conn = _conexion.CreateSqlConnection();
        conn.Open();
        using var transaction = conn.BeginTransaction(System.Data.IsolationLevel.Serializable);

        try
        {
            const string sql = @"
IF EXISTS
(
    SELECT 1
    FROM dbo.SeguimientoWhatsappEnvios WITH (UPDLOCK, HOLDLOCK)
    WHERE IdInteresado = @IdInteresado
      AND CicloInicio = @CicloInicio
      AND NumeroSeguimiento = @NumeroSeguimiento
)
BEGIN
    SELECT CAST(0 AS BIT);
    RETURN;
END;

INSERT INTO dbo.SeguimientoWhatsappEnvios
(
    IdInteresado,
    IdConversacion,
    IdRegla,
    NumeroSeguimiento,
    CicloInicio,
    Telefono,
    Mensaje,
    ProgramadoPara,
    Estado,
    IntentosTecnicos,
    FechaCreacion
)
VALUES
(
    @IdInteresado,
    @IdConversacion,
    @IdRegla,
    @NumeroSeguimiento,
    @CicloInicio,
    @Telefono,
    @Mensaje,
    @ProgramadoPara,
    'PENDIENTE',
    0,
    GETDATE()
);

UPDATE dbo.Interesados
SET
    RequiereSeguimiento = 1,
    FechaProximoContacto = @ProgramadoPara,
    MotivoSeguimiento = CONCAT(N'Seguimiento automático WhatsApp #', @NumeroSeguimiento)
WHERE Id = @IdInteresado;

SELECT CAST(1 AS BIT);";

            var creado = await conn.ExecuteScalarAsync<bool>(
                sql,
                new
                {
                    candidato.IdInteresado,
                    candidato.IdConversacion,
                    IdRegla = regla.Id,
                    NumeroSeguimiento = regla.Orden,
                    candidato.CicloInicio,
                    candidato.Telefono,
                    Mensaje = mensaje,
                    ProgramadoPara = programadoPara
                },
                transaction);

            transaction.Commit();
            return creado;
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 2601 or 2627)
        {
            // Otro proceso registró el mismo ciclo/paso: duplicado esperado, no error 500.
            transaction.Rollback();
            _logger.LogInformation(
                "Seguimiento ya registrado por otro ciclo. IdInteresado={IdInteresado}, Orden={Orden}",
                candidato.IdInteresado, regla.Orden);
            return false;
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            _logger.LogError(
                ex,
                "Error creando seguimiento pendiente. IdInteresado={IdInteresado}, Orden={Orden}",
                candidato.IdInteresado,
                regla.Orden);

            throw new RepositoryException(
                "No se pudo programar el seguimiento WhatsApp.",
                ex);
        }
    }

    public async Task AplicarBajasAutomaticas()
    {
        using var conn = _conexion.CreateSqlConnection();
        conn.Open();
        using var transaction = conn.BeginTransaction();

        try
        {
            const string sql = @"
DECLARE @Bajas TABLE
(
    IdInteresado INT PRIMARY KEY
);

UPDATE i
SET
    NoContactarWhatsapp = 1,
    RequiereSeguimiento = 0,
    FechaProximoContacto = NULL,
    MotivoSeguimiento = N'Cliente solicitó no recibir seguimientos automáticos por WhatsApp.'
OUTPUT INSERTED.Id INTO @Bajas(IdInteresado)
FROM dbo.Interesados i
OUTER APPLY
(
    SELECT TOP (1)
        c.Id AS IdConversacion
    FROM dbo.Conversaciones c
    LEFT JOIN dbo.Contactos ct ON ct.Id = c.IdContacto
    WHERE
        c.Id = i.IdConversacion
        OR
        (
            i.IdConversacion IS NULL
            AND UPPER(ISNULL(c.Canal, '')) = 'WHATSAPP'
            AND REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(ct.Telefono, ''), ' ', ''), '-', ''), '+', ''), '(', ''), ')', '')
              = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(i.Telefono, ''), ' ', ''), '-', ''), '+', ''), '(', ''), ')', '')
        )
    ORDER BY CASE WHEN c.Id = i.IdConversacion THEN 0 ELSE 1 END,
             c.FechaUltimoMensaje DESC, c.Id DESC
) conv
OUTER APPLY
(
    SELECT TOP (1)
        LOWER(LTRIM(RTRIM(ISNULL(m.Mensaje, '')))) AS Mensaje
    FROM dbo.MensajesConversacion m
    WHERE m.IdConversacion = conv.IdConversacion
      AND UPPER(m.Emisor) = 'CLIENTE'
    ORDER BY m.Fecha DESC, m.Id DESC
) cliente
WHERE
    ISNULL(i.NoContactarWhatsapp, 0) = 0
    AND UPPER(ISNULL(i.Origen, '')) = 'WHATSAPP'
    AND
    (
        cliente.Mensaje LIKE N'%no me escrib%'
        OR cliente.Mensaje LIKE N'%no escribas más%'
        OR cliente.Mensaje LIKE N'%no escribas mas%'
        OR cliente.Mensaje LIKE N'%no me contact%'
        OR cliente.Mensaje LIKE N'%no quiero recibir mensajes%'
        OR cliente.Mensaje LIKE N'%no me mandes más%'
        OR cliente.Mensaje LIKE N'%no me mandes mas%'
        OR cliente.Mensaje LIKE N'%no mandar más%'
        OR cliente.Mensaje LIKE N'%no mandar mas%'
        OR cliente.Mensaje LIKE N'%basta de mensajes%'
    );

UPDATE e
SET
    Estado = 'CANCELADO',
    MotivoCancelacion = N'Cliente solicitó no recibir más seguimientos automáticos.',
    FechaInicioProceso = NULL
FROM dbo.SeguimientoWhatsappEnvios e
INNER JOIN @Bajas b
    ON b.IdInteresado = e.IdInteresado
WHERE e.Estado IN ('PENDIENTE', 'PROCESANDO');

INSERT INTO dbo.Seguimientos
(
    IdInteresado,
    Fecha,
    Comentario,
    Usuario
)
SELECT
    b.IdInteresado,
    GETDATE(),
    N'Cliente solicitó no recibir más seguimientos automáticos por WhatsApp. Se canceló la cola pendiente.',
    N'PANAMBI_AUTO'
FROM @Bajas b;";

            await conn.ExecuteAsync(sql, transaction: transaction);
            transaction.Commit();
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            _logger.LogError(ex, "Error aplicando bajas automáticas de seguimiento WhatsApp.");
            throw new RepositoryException(
                "No se pudieron procesar las solicitudes de no contacto por WhatsApp.",
                ex);
        }
    }

    public async Task RecuperarProcesandoVencidos(int minutos)
    {
        using var conn = _conexion.CreateSqlConnection();

        const string sql = @"
UPDATE dbo.SeguimientoWhatsappEnvios
SET
    Estado = 'PENDIENTE',
    FechaInicioProceso = NULL,
    UltimoError = COALESCE(
        UltimoError,
        N'Proceso recuperado automáticamente después de quedar bloqueado.'
    )
WHERE Estado = 'PROCESANDO'
  AND FechaInicioProceso < DATEADD(MINUTE, -@Minutos, GETDATE());";

        try
        {
            await conn.ExecuteAsync(sql, new { Minutos = Math.Clamp(minutos, 5, 120) });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recuperando seguimientos bloqueados.");
            throw new RepositoryException(
                "No se pudieron recuperar seguimientos WhatsApp bloqueados.",
                ex);
        }
    }

    public async Task<SeguimientoWhatsappEnvioPendienteDto?> TomarSiguientePendiente(
        int separacionEnviosMinutos)
    {
        using var conn = _conexion.CreateSqlConnection();
        conn.Open();
        using var transaction = conn.BeginTransaction(System.Data.IsolationLevel.Serializable);

        try
        {
            const string sqlUltimo = @"
SELECT MAX(FechaEnvio)
FROM dbo.SeguimientoWhatsappEnvios WITH (UPDLOCK, HOLDLOCK)
WHERE Estado = 'ENVIADO';";

            var ultimoEnvio =
                await conn.ExecuteScalarAsync<DateTime?>(sqlUltimo, transaction: transaction);

            if (
                ultimoEnvio.HasValue &&
                ultimoEnvio.Value.AddMinutes(separacionEnviosMinutos) > DateTime.Now)
            {
                transaction.Commit();
                return null;
            }

            const string sqlTomar = @"
SELECT TOP (1)
    Id,
    IdInteresado,
    IdConversacion,
    IdRegla,
    NumeroSeguimiento,
    CicloInicio,
    Telefono,
    Mensaje,
    ProgramadoPara,
    IntentosTecnicos
FROM dbo.SeguimientoWhatsappEnvios WITH (UPDLOCK, READPAST, ROWLOCK)
WHERE Estado = 'PENDIENTE'
  AND ProgramadoPara <= GETDATE()
ORDER BY ProgramadoPara ASC, Id ASC;";

            var item =
                await conn.QueryFirstOrDefaultAsync<SeguimientoWhatsappEnvioPendienteDto>(
                    sqlTomar,
                    transaction: transaction);

            if (item is null)
            {
                transaction.Commit();
                return null;
            }

            const string sqlMarcar = @"
UPDATE dbo.SeguimientoWhatsappEnvios
SET
    Estado = 'PROCESANDO',
    FechaInicioProceso = GETDATE()
WHERE Id = @Id
  AND Estado = 'PENDIENTE';";

            var filas = await conn.ExecuteAsync(
                sqlMarcar,
                new { item.Id },
                transaction);

            transaction.Commit();
            return filas == 1 ? item : null;
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            _logger.LogError(ex, "Error tomando siguiente seguimiento WhatsApp.");
            throw new RepositoryException(
                "No se pudo tomar el siguiente seguimiento WhatsApp.",
                ex);
        }
    }

    public async Task<SeguimientoWhatsappEstadoVigenciaDto> ValidarVigencia(long idEnvio)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
SELECT TOP (1)
    CASE
        WHEN e.Estado <> 'PROCESANDO'
            THEN CAST(0 AS BIT)
        WHEN ISNULL(i.NoContactarWhatsapp, 0) = 1
            THEN CAST(0 AS BIT)
        WHEN ISNULL(i.Estado, 'Activo') = 'Inactivo'
            THEN CAST(0 AS BIT)
        WHEN UPPER(ISNULL(i.EstadoConsulta, '')) IN
            ('CERRADO', 'CREDITO_EN_PROCESO', 'CONTADO_EN_PROCESO', 'DERIVADO_HUMANO')
            THEN CAST(0 AS BIT)
        WHEN UPPER(ISNULL(c.Modo, 'IA')) = 'HUMANO'
            THEN CAST(0 AS BIT)
        WHEN EXISTS
        (
            SELECT 1
            FROM dbo.MensajesConversacion m
            WHERE m.IdConversacion = e.IdConversacion
              AND UPPER(m.Emisor) = 'CLIENTE'
              AND m.Fecha > e.CicloInicio
        )
            THEN CAST(0 AS BIT)
        WHEN EXISTS
        (
            SELECT 1
            FROM dbo.SolicitudesCredito sc
            WHERE sc.IdConversacion = e.IdConversacion
              AND sc.Estado IN ('EN_PROCESO', 'PRE_EVALUACION', 'DOCUMENTACION')
        )
            THEN CAST(0 AS BIT)
        WHEN EXISTS
        (
            SELECT 1
            FROM dbo.SolicitudesContadoMoto sc
            WHERE sc.IdConversacion = e.IdConversacion
              AND sc.Estado IN ('EN_PROCESO', 'DOCUMENTACION')
        )
            THEN CAST(0 AS BIT)
        ELSE CAST(1 AS BIT)
    END AS Vigente,

    CASE
        WHEN e.Estado <> 'PROCESANDO'
            THEN N'El envío ya no está en proceso.'
        WHEN ISNULL(i.NoContactarWhatsapp, 0) = 1
            THEN N'El cliente está marcado como no contactar por WhatsApp.'
        WHEN ISNULL(i.Estado, 'Activo') = 'Inactivo'
            THEN N'El interesado está inactivo.'
        WHEN UPPER(ISNULL(i.EstadoConsulta, '')) IN
            ('CERRADO', 'CREDITO_EN_PROCESO', 'CONTADO_EN_PROCESO', 'DERIVADO_HUMANO')
            THEN N'La consulta cambió de etapa y ya no corresponde seguimiento automático.'
        WHEN UPPER(ISNULL(c.Modo, 'IA')) = 'HUMANO'
            THEN N'La conversación está siendo atendida por una persona.'
        WHEN EXISTS
        (
            SELECT 1
            FROM dbo.MensajesConversacion m
            WHERE m.IdConversacion = e.IdConversacion
              AND UPPER(m.Emisor) = 'CLIENTE'
              AND m.Fecha > e.CicloInicio
        )
            THEN N'El cliente volvió a escribir antes del seguimiento.'
        WHEN EXISTS
        (
            SELECT 1
            FROM dbo.SolicitudesCredito sc
            WHERE sc.IdConversacion = e.IdConversacion
              AND sc.Estado IN ('EN_PROCESO', 'PRE_EVALUACION', 'DOCUMENTACION')
        )
            THEN N'El cliente inició un proceso de crédito.'
        WHEN EXISTS
        (
            SELECT 1
            FROM dbo.SolicitudesContadoMoto sc
            WHERE sc.IdConversacion = e.IdConversacion
              AND sc.Estado IN ('EN_PROCESO', 'DOCUMENTACION')
        )
            THEN N'El cliente inició un proceso de contado.'
        ELSE NULL
    END AS MotivoCancelacion

FROM dbo.SeguimientoWhatsappEnvios e
INNER JOIN dbo.Interesados i
    ON i.Id = e.IdInteresado
LEFT JOIN dbo.Conversaciones c
    ON c.Id = e.IdConversacion
WHERE e.Id = @IdEnvio;";

            var resultado =
                await conn.QueryFirstOrDefaultAsync<SeguimientoWhatsappEstadoVigenciaDto>(
                    sql,
                    new { IdEnvio = idEnvio });

            return resultado ?? new SeguimientoWhatsappEstadoVigenciaDto
            {
                Vigente = false,
                MotivoCancelacion = "El envío no existe."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validando vigencia. IdEnvio={IdEnvio}", idEnvio);
            throw new RepositoryException(
                "No se pudo validar el seguimiento WhatsApp.",
                ex);
        }
    }

    public async Task MarcarCancelado(long idEnvio, string motivo)
    {
        using var conn = _conexion.CreateSqlConnection();

        const string sql = @"
DECLARE @IdInteresado INT;

SELECT @IdInteresado = IdInteresado
FROM dbo.SeguimientoWhatsappEnvios
WHERE Id = @IdEnvio;

UPDATE dbo.SeguimientoWhatsappEnvios
SET
    Estado = 'CANCELADO',
    MotivoCancelacion = @Motivo,
    FechaInicioProceso = NULL
WHERE Id = @IdEnvio
  AND Estado IN ('PENDIENTE', 'PROCESANDO');

UPDATE dbo.Interesados
SET
    RequiereSeguimiento = 0,
    FechaProximoContacto = NULL,
    MotivoSeguimiento = NULL
WHERE Id = @IdInteresado
  AND MotivoSeguimiento LIKE N'Seguimiento automático WhatsApp #%';";

        try
        {
            await conn.ExecuteAsync(
                sql,
                new
                {
                    IdEnvio = idEnvio,
                    Motivo = Limitar(motivo, 500)
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelando seguimiento. IdEnvio={IdEnvio}", idEnvio);
            throw new RepositoryException(
                "No se pudo cancelar el seguimiento WhatsApp.",
                ex);
        }
    }

    public async Task MarcarEnviado(
        long idEnvio,
        string? messageIdProveedor)
    {
        using var conn = _conexion.CreateSqlConnection();
        conn.Open();
        using var transaction = conn.BeginTransaction();

        try
        {
            const string sqlObtener = @"
SELECT TOP (1)
    Id,
    IdInteresado,
    IdConversacion,
    IdRegla,
    NumeroSeguimiento,
    CicloInicio,
    Telefono,
    Mensaje,
    ProgramadoPara,
    IntentosTecnicos
FROM dbo.SeguimientoWhatsappEnvios WITH (UPDLOCK, ROWLOCK)
WHERE Id = @IdEnvio;";

            var envio =
                await conn.QueryFirstOrDefaultAsync<SeguimientoWhatsappEnvioPendienteDto>(
                    sqlObtener,
                    new { IdEnvio = idEnvio },
                    transaction);

            if (envio is null)
            {
                transaction.Rollback();
                return;
            }

            const string sqlActualizar = @"
UPDATE dbo.SeguimientoWhatsappEnvios
SET
    Estado = 'ENVIADO',
    FechaEnvio = GETDATE(),
    FechaInicioProceso = NULL,
    MessageIdProveedor = @MessageIdProveedor,
    UltimoError = NULL
WHERE Id = @IdEnvio
  AND Estado = 'PROCESANDO';

IF @@ROWCOUNT = 0
BEGIN
    THROW 50020, 'El seguimiento ya no se encuentra en proceso.', 1;
END;

IF @IdConversacion IS NOT NULL
BEGIN
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
        'IA',
        @Mensaje,
        GETDATE()
    );

    UPDATE dbo.Conversaciones
    SET FechaUltimoMensaje = GETDATE()
    WHERE Id = @IdConversacion;
END;

INSERT INTO dbo.Seguimientos
(
    IdInteresado,
    Fecha,
    Comentario,
    Usuario
)
VALUES
(
    @IdInteresado,
    GETDATE(),
    CONCAT(
        N'Seguimiento automático WhatsApp #',
        @NumeroSeguimiento,
        N' enviado: ',
        @Mensaje
    ),
    N'PANAMBI_AUTO'
);

DECLARE @SiguienteOrden INT;
DECLARE @DemoraValor INT;
DECLARE @DemoraUnidad VARCHAR(10);
DECLARE @FechaProximo DATETIME2(0);

SELECT TOP (1)
    @SiguienteOrden = Orden,
    @DemoraValor = DemoraValor,
    @DemoraUnidad = DemoraUnidad
FROM dbo.SeguimientoWhatsappReglas
WHERE Activo = 1
  AND Orden > @NumeroSeguimiento
ORDER BY Orden;

IF @SiguienteOrden IS NOT NULL
BEGIN
    SET @FechaProximo =
        CASE @DemoraUnidad
            WHEN 'MINUTO' THEN DATEADD(MINUTE, @DemoraValor, GETDATE())
            WHEN 'HORA' THEN DATEADD(HOUR, @DemoraValor, GETDATE())
            ELSE DATEADD(DAY, @DemoraValor, GETDATE())
        END;

    UPDATE dbo.Interesados
    SET
        RequiereSeguimiento = 1,
        FechaProximoContacto = @FechaProximo,
        MotivoSeguimiento = CONCAT(N'Seguimiento automático WhatsApp #', @SiguienteOrden)
    WHERE Id = @IdInteresado;
END
ELSE
BEGIN
    UPDATE dbo.Interesados
    SET
        RequiereSeguimiento = 0,
        FechaProximoContacto = NULL,
        MotivoSeguimiento = N'Secuencia automática finalizada sin nueva respuesta.',
        EstadoConsulta = CASE
            WHEN UPPER(ISNULL(EstadoConsulta, '')) NOT IN
                ('CERRADO', 'CREDITO_EN_PROCESO', 'CONTADO_EN_PROCESO', 'DERIVADO_HUMANO')
                THEN 'SIN_RESPUESTA'
            ELSE EstadoConsulta
        END
    WHERE Id = @IdInteresado;
END;";

            await conn.ExecuteAsync(
                sqlActualizar,
                new
                {
                    IdEnvio = idEnvio,
                    envio.IdInteresado,
                    envio.IdConversacion,
                    envio.NumeroSeguimiento,
                    envio.Mensaje,
                    MessageIdProveedor = Limitar(messageIdProveedor, 200)
                },
                transaction);

            transaction.Commit();
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            _logger.LogError(ex, "Error cerrando seguimiento enviado. IdEnvio={IdEnvio}", idEnvio);
            throw new RepositoryException(
                "El mensaje salió, pero no se pudo cerrar su registro de seguimiento.",
                ex);
        }
    }

    public async Task MarcarError(
        long idEnvio,
        string error,
        int minutosReintento,
        int maximoReintentos)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
DECLARE @Intentos INT;

SELECT @Intentos = IntentosTecnicos + 1
FROM dbo.SeguimientoWhatsappEnvios
WHERE Id = @IdEnvio;

UPDATE dbo.SeguimientoWhatsappEnvios
SET
    IntentosTecnicos = @Intentos,
    Estado = CASE
        WHEN @Intentos >= @MaximoReintentos THEN 'ERROR'
        ELSE 'PENDIENTE'
    END,
    ProgramadoPara = CASE
        WHEN @Intentos >= @MaximoReintentos THEN ProgramadoPara
        ELSE DATEADD(MINUTE, @MinutosReintento, GETDATE())
    END,
    FechaInicioProceso = NULL,
    UltimoError = @Error
WHERE Id = @IdEnvio
  AND Estado = 'PROCESANDO';

IF @Intentos >= @MaximoReintentos
BEGIN
    UPDATE i
    SET
        RequiereSeguimiento = 1,
        FechaProximoContacto = NULL,
        MotivoSeguimiento = N'Error técnico en seguimiento automático WhatsApp. Requiere revisión manual.'
    FROM dbo.Interesados i
    INNER JOIN dbo.SeguimientoWhatsappEnvios e
        ON e.IdInteresado = i.Id
    WHERE e.Id = @IdEnvio;
END;";

            await conn.ExecuteAsync(
                sql,
                new
                {
                    IdEnvio = idEnvio,
                    Error = Limitar(error, 2000),
                    MinutosReintento = Math.Clamp(minutosReintento, 1, 1440),
                    MaximoReintentos = Math.Clamp(maximoReintentos, 1, 10)
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registrando fallo de envío. IdEnvio={IdEnvio}", idEnvio);
            throw new RepositoryException(
                "No se pudo registrar el error del seguimiento WhatsApp.",
                ex);
        }
    }

    public async Task<IReadOnlyList<SeguimientoWhatsappEnvioDto>> ListarEnvios(
        string? estado,
        int limite)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
SELECT TOP (@Limite)
    e.Id,
    e.IdInteresado,
    i.Nombre AS Cliente,
    e.Telefono,
    COALESCE(
        NULLIF(
            LTRIM(RTRIM(CONCAT(
                ISNULL(i.MarcaInteres, ''),
                ' ',
                ISNULL(i.ModeloInteres, '')
            ))),
            ''
        ),
        NULLIF(LTRIM(RTRIM(i.ProductoInteres)), '')
    ) AS ProductoInteres,
    e.NumeroSeguimiento,
    e.CicloInicio,
    e.ProgramadoPara,
    e.Estado,
    e.IntentosTecnicos,
    e.FechaCreacion,
    e.FechaEnvio,
    e.UltimoError,
    e.MotivoCancelacion,
    e.Mensaje
FROM dbo.SeguimientoWhatsappEnvios e
INNER JOIN dbo.Interesados i
    ON i.Id = e.IdInteresado
WHERE
    @Estado IS NULL
    OR e.Estado = @Estado
ORDER BY
    CASE e.Estado
        WHEN 'PROCESANDO' THEN 1
        WHEN 'PENDIENTE' THEN 2
        WHEN 'ERROR' THEN 3
        WHEN 'ENVIADO' THEN 4
        ELSE 5
    END,
    e.ProgramadoPara ASC,
    e.Id DESC;";

            var estadoNormalizado =
                string.IsNullOrWhiteSpace(estado)
                    ? null
                    : estado.Trim().ToUpperInvariant();

            var data = await conn.QueryAsync<SeguimientoWhatsappEnvioDto>(
                sql,
                new
                {
                    Estado = estadoNormalizado,
                    Limite = Math.Clamp(limite, 1, 500)
                });

            return data.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listando cola de seguimiento WhatsApp.");
            throw new RepositoryException(
                "No se pudo listar la cola de seguimiento WhatsApp.",
                ex);
        }
    }

    private static string? Limitar(string? valor, int maximo)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return null;
        }

        var limpio = valor.Trim();
        return limpio.Length <= maximo ? limpio : limpio[..maximo];
    }
}

using Dapper;
using System.Text;
using tuvendedorback.Data;
using tuvendedorback.DTOs;
using tuvendedorback.Exceptions;
using tuvendedorback.Repositories.Interfaces;
using tuvendedorback.Request;

namespace tuvendedorback.Repositories;

public class ClientesRepository : IClientesRepository
{
    private readonly DbConnections _conexion;
    private readonly ILogger<ClientesRepository> _logger;

    private static readonly TimeZoneInfo ZonaHorariaParaguay =
        ObtenerZonaHorariaParaguay();

    public ClientesRepository(
        ILogger<ClientesRepository> logger,
        DbConnections conexion)
    {
        _logger = logger;
        _conexion = conexion;
    }


    // =========================================================
    // REGISTRO MANUAL
    // =========================================================

    public async Task<int> InsertarInteresado(
        InteresadoDto interesado)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
INSERT INTO dbo.Interesados
(
    Nombre,
    Telefono,
    Email,
    Ciudad,
    ProductoInteres,
    AportaIPS,
    CantidadAportes,
    Estado,
    FechaRegistro,
    FechaProximoContacto,
    Descripcion,
    ArchivoUrl,
    UsuarioResponsable,

    Origen,
    EstadoConsulta,
    RequiereSeguimiento,
    MotivoSeguimiento,
    FechaUltimaInteraccion,
    CantidadInteracciones
)
VALUES
(
    @Nombre,
    @Telefono,
    @Email,
    @Ciudad,
    @ProductoInteres,
    @AportaIPS,
    @CantidadAportes,
    @Estado,
    @FechaRegistro,
    @FechaProximoContacto,
    @Descripcion,
    @ArchivoUrl,
    @UsuarioResponsable,

    COALESCE(NULLIF(@Origen, ''), 'MANUAL'),
    COALESCE(NULLIF(@EstadoConsulta, ''), 'REGISTRADO'),
    @RequiereSeguimiento,
    @MotivoSeguimiento,
    COALESCE(@FechaUltimaInteraccion, @FechaRegistro),
    @CantidadInteracciones
);

SELECT CAST(SCOPE_IDENTITY() AS INT);";

            var nuevoId =
                await conn.ExecuteScalarAsync<int>(
                    sql,
                    interesado);

            _logger.LogInformation(
                "Interesado {Nombre} registrado con Id {Id}",
                interesado.Nombre,
                nuevoId);

            return nuevoId;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al insertar interesado {@Interesado}",
                interesado);

            throw new RepositoryException(
                "Error al insertar interesado",
                ex);
        }
    }


    // =========================================================
    // REGISTRO / ACTUALIZACION AUTOMATICA DESDE WHATSAPP
    // =========================================================

    public async Task<int> RegistrarInteraccionWhatsApp(
        InteresadoWhatsAppEventoRequest request)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            conn.Open();

            using var transaction =
                conn.BeginTransaction();

            const string sql = @"
DECLARE @IdInteresado INT;
DECLARE @Identificador VARCHAR(150) = NULLIF(LTRIM(RTRIM(@IdentificadorExterno)), '');
DECLARE @TelefonoReal VARCHAR(100) = NULLIF(LTRIM(RTRIM(@NumeroWhatsapp)), '');
DECLARE @NombreWhatsapp NVARCHAR(150) = NULLIF(LTRIM(RTRIM(@NombreContacto)), '');

DECLARE @Marca VARCHAR(150);
DECLARE @Modelo VARCHAR(250);
DECLARE @CodigoReferencia VARCHAR(100);
DECLARE @ProductoInteres NVARCHAR(300);

-- Si el caller solamente mandó IdConversacion, recuperamos el identificador.
IF @Identificador IS NULL
BEGIN
    SELECT TOP (1)
        @Identificador = IdentificadorExterno
    FROM dbo.Conversaciones
    WHERE Id = @IdConversacion;
END;

-- Metadata segura del modelo.
IF @IdModeloProducto IS NOT NULL
BEGIN
    SELECT TOP (1)
        @Marca = m.Nombre,
        @Modelo = mp.NombreModelo,
        @CodigoReferencia = mp.CodigoReferencia
    FROM dbo.ModelosProducto mp
    INNER JOIN dbo.Marcas m
        ON m.Id = mp.IdMarca
    WHERE mp.Id = @IdModeloProducto;

    SET @ProductoInteres =
        NULLIF(
            LTRIM(
                RTRIM(
                    CONCAT(
                        ISNULL(@Marca, ''),
                        ' ',
                        ISNULL(@Modelo, '')
                    )
                )
            ),
            ''
        );
END;

-- Si todavía no tenemos modelo pero sí publicación, conservamos su título.
IF @ProductoInteres IS NULL
   AND @IdPublicacion IS NOT NULL
BEGIN
    SELECT TOP (1)
        @ProductoInteres = Titulo
    FROM dbo.Publicaciones
    WHERE Id = @IdPublicacion;
END;

-- Buscar primero por conversación, luego identificador y por último teléfono.
SELECT TOP (1)
    @IdInteresado = i.Id
FROM dbo.Interesados i
WHERE
    (
        @IdConversacion > 0
        AND i.IdConversacion = @IdConversacion
    )
    OR
    (
        @Identificador IS NOT NULL
        AND i.IdentificadorExterno = @Identificador
    )
    OR
    (
        @TelefonoReal IS NOT NULL
        AND
        REPLACE(REPLACE(REPLACE(ISNULL(i.Telefono, ''), ' ', ''), '-', ''), '+', '')
        =
        REPLACE(REPLACE(REPLACE(@TelefonoReal, ' ', ''), '-', ''), '+', '')
    )
ORDER BY
    CASE
        WHEN @IdConversacion > 0
         AND i.IdConversacion = @IdConversacion
            THEN 1
        WHEN @Identificador IS NOT NULL
         AND i.IdentificadorExterno = @Identificador
            THEN 2
        ELSE 3
    END,
    i.Id DESC;

IF @IdInteresado IS NULL
BEGIN
    INSERT INTO dbo.Interesados
    (
        Nombre,
        Telefono,
        Email,
        Ciudad,
        ProductoInteres,
        AportaIPS,
        CantidadAportes,
        Estado,
        FechaRegistro,
        FechaProximoContacto,
        Descripcion,
        ArchivoUrl,
        UsuarioResponsable,

        Origen,
        IdentificadorExterno,
        IdConversacion,
        IdModeloProducto,
        IdPublicacion,
        MarcaInteres,
        ModeloInteres,
        CodigoReferencia,
        EstadoConsulta,
        TipoOperacion,
        IdSolicitudOperacion,
        PasoOperacion,
        RequiereSeguimiento,
        MotivoSeguimiento,
        FechaUltimoMensajeCliente,
        FechaUltimaRespuesta,
        FechaUltimaInteraccion,
        UltimoMensajeCliente,
        UltimaRespuesta,
        CantidadInteracciones
    )
    VALUES
    (
        LEFT(COALESCE(@NombreWhatsapp, N'Cliente WhatsApp'), 100),
        LEFT(@TelefonoReal, 20),
        NULL,
        NULL,
        LEFT(COALESCE(@ProductoInteres, N'Consulta por WhatsApp'), 150),
        0,
        0,
        'Activo',
        GETDATE(),
        DATEADD(DAY, 1, GETDATE()),
        N'Registrado automáticamente desde la conversación de WhatsApp.',
        NULL,
        'PANAMBI',

        'WHATSAPP',
        @Identificador,
        @IdConversacion,
        @IdModeloProducto,
        @IdPublicacion,
        @Marca,
        @Modelo,
        @CodigoReferencia,
        COALESCE(NULLIF(@EstadoConsulta, ''), 'CONSULTANDO'),
        NULLIF(@TipoOperacion, ''),
        @IdSolicitudOperacion,
        NULLIF(@PasoOperacion, ''),
        1,
        COALESCE(
            NULLIF(@MotivoSeguimiento, ''),
            N'Seguimiento comercial de consulta por WhatsApp.'
        ),
        CASE WHEN @EsEntradaCliente = 1 THEN GETDATE() ELSE NULL END,
        CASE WHEN NULLIF(@Respuesta, '') IS NOT NULL THEN GETDATE() ELSE NULL END,
        GETDATE(),
        CASE WHEN @EsEntradaCliente = 1 THEN LEFT(@MensajeCliente, 1000) ELSE NULL END,
        CASE WHEN NULLIF(@Respuesta, '') IS NOT NULL THEN LEFT(@Respuesta, 1000) ELSE NULL END,
        CASE WHEN @EsEntradaCliente = 1 THEN 1 ELSE 0 END
    );

    SET @IdInteresado = CAST(SCOPE_IDENTITY() AS INT);
END
ELSE
BEGIN
    UPDATE dbo.Interesados
    SET
        Nombre =
            CASE
                WHEN @NombreWhatsapp IS NOT NULL
                 AND
                 (
                     Nombre IS NULL
                     OR LTRIM(RTRIM(Nombre)) = ''
                     OR Nombre = 'Cliente WhatsApp'
                 )
                    THEN LEFT(@NombreWhatsapp, 100)
                ELSE Nombre
            END,

        Telefono =
            CASE
                WHEN @TelefonoReal IS NOT NULL
                    THEN LEFT(@TelefonoReal, 20)
                ELSE Telefono
            END,

        ProductoInteres =
            COALESCE(LEFT(@ProductoInteres, 150), ProductoInteres),

        Estado = 'Activo',

        Origen =
            CASE
                WHEN Origen IS NULL OR Origen = ''
                    THEN 'WHATSAPP'
                WHEN Origen = 'MANUAL'
                    THEN 'MANUAL+WHATSAPP'
                ELSE Origen
            END,

        IdentificadorExterno =
            COALESCE(@Identificador, IdentificadorExterno),

        IdConversacion =
            CASE
                WHEN @IdConversacion > 0
                    THEN @IdConversacion
                ELSE IdConversacion
            END,

        IdModeloProducto =
            COALESCE(@IdModeloProducto, IdModeloProducto),

        IdPublicacion =
            COALESCE(@IdPublicacion, IdPublicacion),

        MarcaInteres =
            COALESCE(@Marca, MarcaInteres),

        ModeloInteres =
            COALESCE(@Modelo, ModeloInteres),

        CodigoReferencia =
            COALESCE(@CodigoReferencia, CodigoReferencia),

        EstadoConsulta =
            CASE
                WHEN ISNULL(EstadoConsulta, '') IN
                (
                    'CREDITO_EN_PROCESO',
                    'CONTADO_EN_PROCESO',
                    'DERIVADO_HUMANO',
                    'CERRADO'
                )
                AND NULLIF(@EstadoConsulta, '') IN
                (
                    'CONSULTANDO',
                    'ESPERANDO_MODELO',
                    'CONSULTA_PROMO',
                    'COTIZADO'
                )
                    THEN EstadoConsulta
                ELSE
                    COALESCE(
                        NULLIF(@EstadoConsulta, ''),
                        EstadoConsulta,
                        'CONSULTANDO'
                    )
            END,

        TipoOperacion =
            COALESCE(
                NULLIF(@TipoOperacion, ''),
                TipoOperacion
            ),

        IdSolicitudOperacion =
            COALESCE(
                @IdSolicitudOperacion,
                IdSolicitudOperacion
            ),

        PasoOperacion =
            COALESCE(
                NULLIF(@PasoOperacion, ''),
                PasoOperacion
            ),

        RequiereSeguimiento =
            CASE
                WHEN NULLIF(@EstadoConsulta, '') = 'CERRADO'
                    THEN 0
                WHEN Estado = 'Inactivo'
                    THEN RequiereSeguimiento
                ELSE 1
            END,

        -- Si se cierra porque el cliente desistió, guardamos ese motivo.
        -- En los demás casos conservamos primero cualquier motivo manual existente.
        MotivoSeguimiento =
            CASE
                WHEN NULLIF(@EstadoConsulta, '') = 'CERRADO'
                    THEN COALESCE(
                        NULLIF(@MotivoSeguimiento, ''),
                        N'Cliente desistió de la consulta.'
                    )
                ELSE COALESCE(
                    NULLIF(MotivoSeguimiento, ''),
                    NULLIF(@MotivoSeguimiento, ''),
                    N'Seguimiento comercial de consulta por WhatsApp.'
                )
            END,

        -- Una oportunidad cerrada no debe quedar agendada como seguimiento pendiente.
        -- Para conversaciones activas conservamos la agenda manual existente.
        FechaProximoContacto =
            CASE
                WHEN NULLIF(@EstadoConsulta, '') = 'CERRADO'
                    THEN NULL
                ELSE COALESCE(
                    FechaProximoContacto,
                    DATEADD(DAY, 1, GETDATE())
                )
            END,

        FechaUltimoMensajeCliente =
            CASE
                WHEN @EsEntradaCliente = 1
                    THEN GETDATE()
                ELSE FechaUltimoMensajeCliente
            END,

        FechaUltimaRespuesta =
            CASE
                WHEN NULLIF(@Respuesta, '') IS NOT NULL
                    THEN GETDATE()
                ELSE FechaUltimaRespuesta
            END,

        FechaUltimaInteraccion =
            GETDATE(),

        UltimoMensajeCliente =
            CASE
                WHEN @EsEntradaCliente = 1
                    THEN LEFT(@MensajeCliente, 1000)
                ELSE UltimoMensajeCliente
            END,

        UltimaRespuesta =
            CASE
                WHEN NULLIF(@Respuesta, '') IS NOT NULL
                    THEN LEFT(@Respuesta, 1000)
                ELSE UltimaRespuesta
            END,

        CantidadInteracciones =
            ISNULL(CantidadInteracciones, 0)
            +
            CASE
                WHEN @EsEntradaCliente = 1
                    THEN 1
                ELSE 0
            END

    WHERE Id = @IdInteresado;
END;


-- ============================================================
-- HISTORIAL POR MODELO
-- Se conserva una fila por interesado + conversación + modelo.
-- Si el cliente consulta otra moto se crea otra fila.
-- ============================================================

IF @IdModeloProducto IS NOT NULL
BEGIN
    DECLARE @IdConsulta INT;

    SELECT TOP (1)
        @IdConsulta = Id
    FROM dbo.InteresadoConsultasMoto
    WHERE IdInteresado = @IdInteresado
      AND IdConversacion = @IdConversacion
      AND IdModeloProducto = @IdModeloProducto
    ORDER BY Id DESC;

    IF @IdConsulta IS NULL
    BEGIN
        INSERT INTO dbo.InteresadoConsultasMoto
        (
            IdInteresado,
            IdConversacion,
            IdModeloProducto,
            IdPublicacion,
            Marca,
            Modelo,
            CodigoReferencia,
            TipoConsulta,
            EstadoConsulta,
            TipoOperacion,
            IdSolicitudOperacion,
            PasoOperacion,
            UltimoMensajeCliente,
            UltimaRespuesta,
            FechaPrimeraConsulta,
            FechaUltimaConsulta,
            CantidadInteracciones
        )
        VALUES
        (
            @IdInteresado,
            @IdConversacion,
            @IdModeloProducto,
            @IdPublicacion,
            @Marca,
            @Modelo,
            @CodigoReferencia,
            COALESCE(NULLIF(@TipoConsulta, ''), 'CONSULTA_MODELO'),
            COALESCE(NULLIF(@EstadoConsulta, ''), 'CONSULTANDO'),
            NULLIF(@TipoOperacion, ''),
            @IdSolicitudOperacion,
            NULLIF(@PasoOperacion, ''),
            LEFT(@MensajeCliente, 1500),
            LEFT(@Respuesta, 1500),
            GETDATE(),
            GETDATE(),
            1
        );
    END
    ELSE
    BEGIN
        UPDATE dbo.InteresadoConsultasMoto
        SET
            IdPublicacion =
                COALESCE(@IdPublicacion, IdPublicacion),

            Marca =
                COALESCE(@Marca, Marca),

            Modelo =
                COALESCE(@Modelo, Modelo),

            CodigoReferencia =
                COALESCE(@CodigoReferencia, CodigoReferencia),

            TipoConsulta =
                COALESCE(
                    NULLIF(@TipoConsulta, ''),
                    TipoConsulta
                ),

            EstadoConsulta =
                COALESCE(
                    NULLIF(@EstadoConsulta, ''),
                    EstadoConsulta
                ),

            TipoOperacion =
                COALESCE(
                    NULLIF(@TipoOperacion, ''),
                    TipoOperacion
                ),

            IdSolicitudOperacion =
                COALESCE(
                    @IdSolicitudOperacion,
                    IdSolicitudOperacion
                ),

            PasoOperacion =
                COALESCE(
                    NULLIF(@PasoOperacion, ''),
                    PasoOperacion
                ),

            UltimoMensajeCliente =
                COALESCE(
                    NULLIF(LEFT(@MensajeCliente, 1500), ''),
                    UltimoMensajeCliente
                ),

            UltimaRespuesta =
                COALESCE(
                    NULLIF(LEFT(@Respuesta, 1500), ''),
                    UltimaRespuesta
                ),

            FechaUltimaConsulta =
                GETDATE(),

            CantidadInteracciones =
                ISNULL(CantidadInteracciones, 0) + 1

        WHERE Id = @IdConsulta;
    END;
END;

SELECT @IdInteresado;";

            var id =
                await conn.ExecuteScalarAsync<int>(
                    sql,
                    request,
                    transaction);

            transaction.Commit();

            return id;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error registrando interacción WhatsApp. IdConversacion={IdConversacion}",
                request.IdConversacion);

            throw new RepositoryException(
                "Error registrando interacción comercial de WhatsApp.",
                ex);
        }
    }



    // =========================================================
    // CONVERSACIONES WHATSAPP DEL DIA DESDE NUESTRA BBDD
    // =========================================================

    public async Task<List<WhatsAppConversacionSincronizacionDto>>
        ObtenerConversacionesWhatsAppDia(
            DateTime fecha)
    {
        using var conn =
            _conexion.CreateSqlConnection();

        try
        {
            var (desdeUtc, hastaUtc) =
                ObtenerRangoUtcDiaParaguay(
                    fecha);

            const string sql = @"
WITH ConversacionesDia AS
(
    SELECT
        c.Id,
        c.IdentificadorExterno,
        MAX(m.Fecha) AS FechaUltimaInteraccion,
        COUNT(1) AS CantidadMensajesDia
    FROM dbo.Conversaciones c
    INNER JOIN dbo.MensajesConversacion m
        ON m.IdConversacion = c.Id
    WHERE c.Canal = 'WHATSAPP'
      AND m.Fecha >= @Desde
      AND m.Fecha < @Hasta
      AND NULLIF(
            LTRIM(RTRIM(c.IdentificadorExterno)),
            ''
          ) IS NOT NULL
    GROUP BY
        c.Id,
        c.IdentificadorExterno
)
SELECT
    cd.IdentificadorExterno,
    cli.Mensaje AS UltimoMensajeCliente,
    ia.Mensaje AS UltimaRespuesta,
    cli.Fecha AS FechaUltimoMensajeCliente,
    ia.Fecha AS FechaUltimaRespuesta,
    cd.FechaUltimaInteraccion,
    cd.CantidadMensajesDia
FROM ConversacionesDia cd
OUTER APPLY
(
    SELECT TOP (1)
        m.Mensaje,
        m.Fecha
    FROM dbo.MensajesConversacion m
    WHERE m.IdConversacion = cd.Id
      AND UPPER(LTRIM(RTRIM(m.Emisor))) = 'CLIENTE'
      AND m.Fecha >= @Desde
      AND m.Fecha < @Hasta
    ORDER BY
        m.Fecha DESC,
        m.Id DESC
) cli
OUTER APPLY
(
    SELECT TOP (1)
        m.Mensaje,
        m.Fecha
    FROM dbo.MensajesConversacion m
    WHERE m.IdConversacion = cd.Id
      AND UPPER(LTRIM(RTRIM(m.Emisor))) IN ('IA', 'PANAMBI')
      AND m.Fecha >= @Desde
      AND m.Fecha < @Hasta
    ORDER BY
        m.Fecha DESC,
        m.Id DESC
) ia
ORDER BY
    cd.FechaUltimaInteraccion DESC,
    cd.Id DESC;";

            var data =
                await conn.QueryAsync<
                    WhatsAppConversacionSincronizacionDto>(
                    sql,
                    new
                    {
                        Desde = desdeUtc,
                        Hasta = hastaUtc
                    });

            return data.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error obteniendo conversaciones WhatsApp del día {Fecha}",
                fecha.Date);

            throw new RepositoryException(
                "Error obteniendo conversaciones WhatsApp del día.",
                ex);
        }
    }


    // =========================================================
    // SINCRONIZACION DE CHATS DESDE WHATSAPP
    // Idempotente por conversación, identificador externo o teléfono.
    // No pisa la agenda/motivo de seguimiento manual del vendedor.
    // =========================================================

    public async Task<SincronizarContactoWhatsAppResultadoDto>
        SincronizarContactoWhatsApp(
            WhatsAppContactoSincronizacionRequest request)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            conn.Open();

            using var transaction =
                conn.BeginTransaction();

            const string sql = @"
DECLARE @Identificador VARCHAR(150) =
    NULLIF(LTRIM(RTRIM(@IdentificadorExterno)), '');

DECLARE @TelefonoReal VARCHAR(100) =
    NULLIF(LTRIM(RTRIM(@NumeroWhatsapp)), '');

DECLARE @NombreWhatsapp NVARCHAR(150) =
    NULLIF(LTRIM(RTRIM(@NombreContacto)), '');

DECLARE @IdConversacion INT;
DECLARE @IdInteresado INT;
DECLARE @EsNuevo BIT = 0;

IF @Identificador IS NULL
BEGIN
    THROW 57020, 'No se recibió identificador de WhatsApp.', 1;
END;

-- Conversación idempotente.
SELECT TOP (1)
    @IdConversacion = c.Id
FROM dbo.Conversaciones c WITH (UPDLOCK, HOLDLOCK)
WHERE c.Canal = 'WHATSAPP'
  AND c.IdentificadorExterno = @Identificador
ORDER BY c.Id DESC;

IF @IdConversacion IS NULL
BEGIN
    INSERT INTO dbo.Conversaciones
    (
        Canal,
        IdentificadorExterno,
        Estado,
        Modo,
        FechaInicio,
        FechaUltimoMensaje
    )
    VALUES
    (
        'WHATSAPP',
        @Identificador,
        'ACTIVA',
        'IA',
        COALESCE(@FechaUltimaInteraccion, GETDATE()),
        COALESCE(@FechaUltimaInteraccion, GETDATE())
    );

    SET @IdConversacion =
        CAST(SCOPE_IDENTITY() AS INT);
END
ELSE
BEGIN
    UPDATE dbo.Conversaciones
    SET
        FechaUltimoMensaje =
            CASE
                WHEN @FechaUltimaInteraccion IS NOT NULL
                 AND @FechaUltimaInteraccion > FechaUltimoMensaje
                    THEN @FechaUltimaInteraccion
                ELSE FechaUltimoMensaje
            END
    WHERE Id = @IdConversacion;
END;

-- Primero conversación/LID. Si todavía no estaba vinculado, buscamos
-- por teléfono para fusionar con un interesado cargado manualmente.
SELECT TOP (1)
    @IdInteresado = i.Id
FROM dbo.Interesados i WITH (UPDLOCK, HOLDLOCK)
WHERE
    i.IdConversacion = @IdConversacion
    OR i.IdentificadorExterno = @Identificador
    OR
    (
        @TelefonoReal IS NOT NULL
        AND
        REPLACE(REPLACE(REPLACE(ISNULL(i.Telefono, ''), ' ', ''), '-', ''), '+', '')
        =
        REPLACE(REPLACE(REPLACE(@TelefonoReal, ' ', ''), '-', ''), '+', '')
    )
ORDER BY
    CASE
        WHEN i.IdConversacion = @IdConversacion THEN 1
        WHEN i.IdentificadorExterno = @Identificador THEN 2
        ELSE 3
    END,
    i.Id DESC;

IF @IdInteresado IS NULL
BEGIN
    SET @EsNuevo = 1;

    INSERT INTO dbo.Interesados
    (
        Nombre,
        Telefono,
        Email,
        Ciudad,
        ProductoInteres,
        AportaIPS,
        CantidadAportes,
        Estado,
        FechaRegistro,
        FechaProximoContacto,
        Descripcion,
        ArchivoUrl,
        UsuarioResponsable,
        Origen,
        IdentificadorExterno,
        IdConversacion,
        EstadoConsulta,
        RequiereSeguimiento,
        MotivoSeguimiento,
        FechaUltimoMensajeCliente,
        FechaUltimaRespuesta,
        FechaUltimaInteraccion,
        UltimoMensajeCliente,
        UltimaRespuesta,
        CantidadInteracciones
    )
    VALUES
    (
        LEFT(COALESCE(@NombreWhatsapp, N'Cliente WhatsApp'), 100),
        LEFT(@TelefonoReal, 20),
        NULL,
        NULL,
        N'Consulta por WhatsApp',
        0,
        0,
        'Activo',
        COALESCE(@FechaUltimaInteraccion, GETDATE()),
        DATEADD(
            DAY,
            1,
            COALESCE(@FechaUltimaInteraccion, GETDATE())
        ),
        N'Registrado desde sincronización de chats de WhatsApp.',
        NULL,
        'PANAMBI',
        'WHATSAPP',
        @Identificador,
        @IdConversacion,
        'CONSULTANDO',
        1,
        N'Seguimiento comercial de consulta por WhatsApp.',
        @FechaUltimoMensajeCliente,
        @FechaUltimaRespuesta,
        COALESCE(@FechaUltimaInteraccion, GETDATE()),
        LEFT(@UltimoMensajeCliente, 1000),
        LEFT(@UltimaRespuesta, 1000),
        ISNULL(@CantidadMensajesDia, 0)
    );

    SET @IdInteresado =
        CAST(SCOPE_IDENTITY() AS INT);
END
ELSE
BEGIN
    UPDATE dbo.Interesados
    SET
        Nombre =
            CASE
                WHEN @NombreWhatsapp IS NOT NULL
                 AND
                 (
                     Nombre IS NULL
                     OR LTRIM(RTRIM(Nombre)) = ''
                     OR Nombre = 'Cliente WhatsApp'
                 )
                    THEN LEFT(@NombreWhatsapp, 100)
                ELSE Nombre
            END,

        Telefono =
            CASE
                WHEN @TelefonoReal IS NOT NULL
                    THEN LEFT(@TelefonoReal, 20)
                ELSE Telefono
            END,

        Origen =
            CASE
                WHEN ISNULL(Origen, '') = ''
                    THEN 'WHATSAPP'
                WHEN Origen = 'MANUAL'
                    THEN 'MANUAL+WHATSAPP'
                ELSE Origen
            END,

        IdentificadorExterno =
            COALESCE(NULLIF(IdentificadorExterno, ''), @Identificador),

        IdConversacion =
            COALESCE(IdConversacion, @IdConversacion),

        Estado =
            CASE
                WHEN @FechaUltimaInteraccion IS NOT NULL
                    THEN 'Activo'
                ELSE Estado
            END,

        EstadoConsulta =
            COALESCE(NULLIF(EstadoConsulta, ''), 'CONSULTANDO'),

        -- Agenda y motivo manual NO se pisan.
        FechaProximoContacto =
            COALESCE(
                FechaProximoContacto,
                DATEADD(
                    DAY,
                    1,
                    COALESCE(@FechaUltimaInteraccion, GETDATE())
                )
            ),

        MotivoSeguimiento =
            COALESCE(
                NULLIF(MotivoSeguimiento, ''),
                N'Seguimiento comercial de consulta por WhatsApp.'
            ),

        -- La sincronización masiva no cambia una decisión manual
        -- de seguimiento. Solamente los mensajes en vivo pueden reactivarla.
        RequiereSeguimiento =
            RequiereSeguimiento,

        FechaUltimoMensajeCliente =
            CASE
                WHEN @FechaUltimoMensajeCliente IS NOT NULL
                 AND
                 (
                     FechaUltimoMensajeCliente IS NULL
                     OR @FechaUltimoMensajeCliente > FechaUltimoMensajeCliente
                 )
                    THEN @FechaUltimoMensajeCliente
                ELSE FechaUltimoMensajeCliente
            END,

        FechaUltimaRespuesta =
            CASE
                WHEN @FechaUltimaRespuesta IS NOT NULL
                 AND
                 (
                     FechaUltimaRespuesta IS NULL
                     OR @FechaUltimaRespuesta > FechaUltimaRespuesta
                 )
                    THEN @FechaUltimaRespuesta
                ELSE FechaUltimaRespuesta
            END,

        FechaUltimaInteraccion =
            CASE
                WHEN @FechaUltimaInteraccion IS NOT NULL
                 AND
                 (
                     FechaUltimaInteraccion IS NULL
                     OR @FechaUltimaInteraccion > FechaUltimaInteraccion
                 )
                    THEN @FechaUltimaInteraccion
                ELSE FechaUltimaInteraccion
            END,

        UltimoMensajeCliente =
            CASE
                WHEN @FechaUltimoMensajeCliente IS NOT NULL
                 AND
                 (
                     FechaUltimoMensajeCliente IS NULL
                     OR @FechaUltimoMensajeCliente >= FechaUltimoMensajeCliente
                 )
                    THEN COALESCE(
                        NULLIF(LEFT(@UltimoMensajeCliente, 1000), ''),
                        UltimoMensajeCliente
                    )
                ELSE UltimoMensajeCliente
            END,

        UltimaRespuesta =
            CASE
                WHEN @FechaUltimaRespuesta IS NOT NULL
                 AND
                 (
                     FechaUltimaRespuesta IS NULL
                     OR @FechaUltimaRespuesta >= FechaUltimaRespuesta
                 )
                    THEN COALESCE(
                        NULLIF(LEFT(@UltimaRespuesta, 1000), ''),
                        UltimaRespuesta
                    )
                ELSE UltimaRespuesta
            END

    WHERE Id = @IdInteresado;
END;

SELECT
    @IdInteresado AS IdInteresado,
    @EsNuevo AS EsNuevo,
    CASE
        WHEN @TelefonoReal IS NOT NULL THEN CAST(1 AS BIT)
        ELSE CAST(0 AS BIT)
    END AS TieneTelefonoReal;
";

            var resultado =
                await conn.QuerySingleAsync<
                    SincronizarContactoWhatsAppResultadoDto>(
                    sql,
                    request,
                    transaction);

            transaction.Commit();

            return resultado;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error sincronizando contacto de WhatsApp. Identificador={Identificador}",
                request.IdentificadorExterno);

            throw new RepositoryException(
                "Error sincronizando contacto de WhatsApp.",
                ex);
        }
    }


    // =========================================================
    // SEGUIMIENTOS MANUALES
    // =========================================================

    public async Task<int> InsertarSeguimiento(
        SeguimientoDto seguimiento)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
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
    @Fecha,
    @Comentario,
    @Usuario
);

SELECT CAST(SCOPE_IDENTITY() AS INT);";

            var id =
                await conn.ExecuteScalarAsync<int>(
                    sql,
                    seguimiento);

            _logger.LogInformation(
                "Seguimiento agregado a interesado {IdInteresado} por {Usuario}",
                seguimiento.IdInteresado,
                seguimiento.Usuario);

            return id;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al insertar seguimiento {@Seguimiento}",
                seguimiento);

            throw new RepositoryException(
                "Error al insertar seguimiento",
                ex);
        }
    }


    // =========================================================
    // LISTADO DE INTERESADOS
    // =========================================================

    public async Task<(List<InteresadoDto> Items, int TotalRegistros)>
        ObtenerInteresados(
            FiltroInteresadosRequest filtro)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            var sqlSinRespuesta =
                SqlSinRespuesta(
                    "i");

            var where =
                new StringBuilder(@"
FROM dbo.Interesados i
WHERE 1 = 1
");

            if (!string.IsNullOrWhiteSpace(filtro.Nombre))
            {
                where.AppendLine(@"
AND
(
    i.Nombre LIKE '%' + @Nombre + '%'
    OR i.Telefono LIKE '%' + @Nombre + '%'
    OR i.ProductoInteres LIKE '%' + @Nombre + '%'
    OR i.MarcaInteres LIKE '%' + @Nombre + '%'
    OR i.ModeloInteres LIKE '%' + @Nombre + '%'
    OR i.CodigoReferencia LIKE '%' + @Nombre + '%'
)");
            }

            if (!string.IsNullOrWhiteSpace(filtro.Estado)
                && !filtro.Estado.Equals(
                    "Todos",
                    StringComparison.OrdinalIgnoreCase))
            {
                where.AppendLine(
                    "AND i.Estado = @Estado");
            }

            if (!string.IsNullOrWhiteSpace(filtro.Origen)
                && !filtro.Origen.Equals(
                    "Todos",
                    StringComparison.OrdinalIgnoreCase))
            {
                where.AppendLine(
                    "AND i.Origen = @Origen");
            }

            if (!string.IsNullOrWhiteSpace(filtro.EstadoConsulta)
                && !filtro.EstadoConsulta.Equals(
                    "Todos",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (filtro.EstadoConsulta.Equals(
                    "SIN_RESPUESTA",
                    StringComparison.OrdinalIgnoreCase))
                {
                    where.AppendLine($@"
AND {sqlSinRespuesta} = 1");
                }
                else
                {
                    where.AppendLine(
                        "AND i.EstadoConsulta = @EstadoConsulta");
                }
            }

            if (filtro.SoloSeguimiento)
            {
                where.AppendLine(
                    "AND i.RequiereSeguimiento = 1");
            }

            if (filtro.SoloSinRespuesta)
            {
                where.AppendLine($@"
AND {sqlSinRespuesta} = 1");
            }

            if (filtro.SoloSeguimientoVencido)
            {
                where.AppendLine(@"
AND i.RequiereSeguimiento = 1
AND i.FechaProximoContacto IS NOT NULL
AND i.FechaProximoContacto <= GETDATE()");
            }

            if (filtro.FechaRegistroDesde.HasValue)
            {
                where.AppendLine(@"
AND CONVERT(date, i.FechaRegistro)
    >= CONVERT(date, @FechaRegistroDesde)");
            }

            if (filtro.FechaRegistroHasta.HasValue)
            {
                where.AppendLine(@"
AND CONVERT(date, i.FechaRegistro)
    <= CONVERT(date, @FechaRegistroHasta)");
            }

            if (filtro.FechaProximoContactoDesde.HasValue)
            {
                where.AppendLine(@"
AND CONVERT(date, i.FechaProximoContacto)
    >= CONVERT(date, @FechaProximoContactoDesde)");
            }

            if (filtro.FechaProximoContactoHasta.HasValue)
            {
                where.AppendLine(@"
AND CONVERT(date, i.FechaProximoContacto)
    <= CONVERT(date, @FechaProximoContactoHasta)");
            }

            var sqlCount =
                "SELECT COUNT(1) " + where;

            var sqlData =
                new StringBuilder($@"
SELECT
    i.Id,
    i.Nombre,
    i.Telefono,
    i.Email,
    i.Ciudad,
    i.ProductoInteres,
    i.AportaIPS,
    i.CantidadAportes,
    i.Estado,
    i.FechaRegistro,
    i.FechaProximoContacto,
    i.Descripcion,
    i.ArchivoUrl,
    i.UsuarioResponsable,

    i.Origen,
    i.IdentificadorExterno,
    i.IdConversacion,
    i.IdModeloProducto,
    i.IdPublicacion,
    i.MarcaInteres,
    i.ModeloInteres,
    i.CodigoReferencia,
    i.EstadoConsulta,

    CASE
        WHEN {sqlSinRespuesta} = 1
            THEN 'SIN_RESPUESTA'
        ELSE ISNULL(i.EstadoConsulta, 'REGISTRADO')
    END AS EstadoGestion,

    i.TipoOperacion,
    i.IdSolicitudOperacion,
    i.PasoOperacion,
    i.RequiereSeguimiento,
    i.MotivoSeguimiento,
    i.FechaUltimoMensajeCliente,
    i.FechaUltimaRespuesta,
    i.FechaUltimaInteraccion,
    i.UltimoMensajeCliente,
    i.UltimaRespuesta,
    ISNULL(i.CantidadInteracciones, 0)
        AS CantidadInteracciones,

    CAST(
        {sqlSinRespuesta}
        AS BIT
    ) AS SinRespuesta,

    CAST(
        CASE
            WHEN i.RequiereSeguimiento = 1
             AND i.FechaProximoContacto IS NOT NULL
             AND i.FechaProximoContacto <= GETDATE()
                THEN 1
            ELSE 0
        END
        AS BIT
    ) AS SeguimientoVencido
");

            sqlData.Append(where);

            sqlData.AppendLine(@"
ORDER BY
    CASE
        WHEN i.FechaUltimoMensajeCliente IS NOT NULL
         AND CAST(i.FechaUltimoMensajeCliente AS DATE) = CAST(GETDATE() AS DATE)
            THEN 0
        ELSE 1
    END,
    CASE
        WHEN i.FechaUltimoMensajeCliente IS NOT NULL
         AND CAST(i.FechaUltimoMensajeCliente AS DATE) = CAST(GETDATE() AS DATE)
            THEN i.FechaUltimoMensajeCliente
        ELSE NULL
    END DESC,
    CASE
        WHEN i.FechaUltimoMensajeCliente IS NULL
          OR CAST(i.FechaUltimoMensajeCliente AS DATE) <> CAST(GETDATE() AS DATE)
        THEN
            CASE
                WHEN i.RequiereSeguimiento = 1
                 AND i.FechaProximoContacto IS NOT NULL
                 AND i.FechaProximoContacto <= GETDATE()
                    THEN 0
                ELSE 1
            END
        ELSE 0
    END,
    COALESCE(
        i.FechaUltimaInteraccion,
        i.FechaRegistro
    ) DESC,
    i.Id DESC

OFFSET (@Offset) ROWS
FETCH NEXT (@Limit) ROWS ONLY;");

            var numeroPagina =
                Math.Max(1, filtro.NumeroPagina);

            var registrosPorPagina =
                Math.Clamp(
                    filtro.RegistrosPorPagina,
                    1,
                    100);

            var parametros =
                new
                {
                    filtro.Nombre,
                    filtro.Estado,
                    filtro.Origen,
                    filtro.EstadoConsulta,
                    filtro.FechaRegistroDesde,
                    filtro.FechaRegistroHasta,
                    filtro.FechaProximoContactoDesde,
                    filtro.FechaProximoContactoHasta,
                    Offset =
                        (numeroPagina - 1)
                        *
                        registrosPorPagina,
                    Limit =
                        registrosPorPagina
                };

            var total =
                await conn.ExecuteScalarAsync<int>(
                    sqlCount,
                    parametros);

            var items =
                await conn.QueryAsync<InteresadoDto>(
                    sqlData.ToString(),
                    parametros);

            return (
                items.ToList(),
                total
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al obtener interesados filtrados");

            throw new RepositoryException(
                "Error al obtener interesados filtrados",
                ex);
        }
    }


    // =========================================================
    // RESUMEN PARA DASHBOARD
    // =========================================================

    public async Task<InteresadosResumenDto>
        ObtenerResumenInteresados(
            DateTime? fecha)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
SELECT
    SUM(
        CASE
            WHEN i.Estado = 'Activo'
                THEN 1
            ELSE 0
        END
    ) AS TotalActivos,

    SUM(
        CASE
            WHEN i.FechaRegistro >= @DesdeUtc
             AND i.FechaRegistro < @HastaUtc
                THEN 1
            ELSE 0
        END
    ) AS NuevosDelDia,

    SUM(
        CASE
            WHEN i.FechaUltimoMensajeCliente >= @DesdeUtc
             AND i.FechaUltimoMensajeCliente < @HastaUtc
                THEN 1
            ELSE 0
        END
    ) AS InteraccionesDelDia,

    SUM(
        CASE
            WHEN i.Estado = 'Activo'
             AND i.RequiereSeguimiento = 1
                THEN 1
            ELSE 0
        END
    ) AS PendientesSeguimiento,

    SUM(
        CASE
            WHEN i.Estado = 'Activo'
             AND i.RequiereSeguimiento = 1
             AND i.FechaProximoContacto IS NOT NULL
             AND i.FechaProximoContacto <= GETDATE()
                THEN 1
            ELSE 0
        END
    ) AS SeguimientosVencidos,

    SUM(
        CASE
            WHEN i.Estado = 'Activo'
             AND
             i.RequiereSeguimiento = 1
             AND i.FechaUltimaRespuesta IS NOT NULL
             AND
             (
                 i.FechaUltimoMensajeCliente IS NULL
                 OR i.FechaUltimaRespuesta
                    >= i.FechaUltimoMensajeCliente
             )
             AND i.FechaUltimaRespuesta
                 <= DATEADD(HOUR, -24, GETDATE())
             AND ISNULL(i.EstadoConsulta, '')
                 NOT IN
                 (
                     'CREDITO_EN_PROCESO',
                     'CONTADO_EN_PROCESO',
                     'DERIVADO_HUMANO',
                     'CERRADO'
                 )
                THEN 1
            ELSE 0
        END
    ) AS SinRespuesta,

    SUM(
        CASE
            WHEN i.Estado = 'Activo'
             AND ISNULL(i.EstadoConsulta, '')
                 IN
                 (
                     'CONSULTANDO',
                     'ESPERANDO_MODELO',
                     'CONSULTA_PROMO'
                 )
                THEN 1
            ELSE 0
        END
    ) AS Consultando,

    SUM(
        CASE
            WHEN i.Estado = 'Activo'
             AND i.EstadoConsulta = 'COTIZADO'
                THEN 1
            ELSE 0
        END
    ) AS Cotizados,

    SUM(
        CASE
            WHEN i.Estado = 'Activo'
             AND i.EstadoConsulta = 'CREDITO_EN_PROCESO'
                THEN 1
            ELSE 0
        END
    ) AS CreditoEnProceso,

    SUM(
        CASE
            WHEN i.Estado = 'Activo'
             AND i.EstadoConsulta = 'CONTADO_EN_PROCESO'
                THEN 1
            ELSE 0
        END
    ) AS ContadoEnProceso,

    SUM(
        CASE
            WHEN i.Estado = 'Activo'
             AND i.EstadoConsulta = 'DERIVADO_HUMANO'
                THEN 1
            ELSE 0
        END
    ) AS DerivadosHumano

FROM dbo.Interesados i;";

            var fechaReferencia =
                fecha ??
                TimeZoneInfo.ConvertTimeFromUtc(
                    DateTime.UtcNow,
                    ZonaHorariaParaguay);

            var (desdeUtc, hastaUtc) =
                ObtenerRangoUtcDiaParaguay(
                    fechaReferencia);

            return
                await conn.QueryFirstAsync<InteresadosResumenDto>(
                    sql,
                    new
                    {
                        DesdeUtc = desdeUtc,
                        HastaUtc = hastaUtc
                    });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error obteniendo resumen de interesados");

            throw new RepositoryException(
                "Error obteniendo resumen de interesados",
                ex);
        }
    }


    // =========================================================
    // SEGUIMIENTOS
    // =========================================================

    public async Task<List<SeguimientoDto>>
        ObtenerSeguimientosPorInteresado(
            int idInteresado)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
SELECT
    s.Id,
    s.IdInteresado,
    s.Fecha,
    s.Comentario,

    COALESCE(
        u.NombreUsuario,
        s.Usuario
    ) AS Usuario

FROM dbo.Seguimientos s

LEFT JOIN dbo.Usuarios u
    ON u.Id = TRY_CONVERT(INT, s.Usuario)

WHERE s.IdInteresado = @Id

ORDER BY
    s.Fecha DESC,
    s.Id DESC;";

            var result =
                await conn.QueryAsync<SeguimientoDto>(
                    sql,
                    new
                    {
                        Id = idInteresado
                    });

            return result.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al obtener seguimientos del interesado {Id}",
                idInteresado);

            throw new RepositoryException(
                "Error al obtener seguimientos",
                ex);
        }
    }


    // =========================================================
    // OBTENER INTERESADO
    // =========================================================

    public async Task<InteresadoDto?>
        ObtenerInteresadoPorId(
            int id)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            var sqlSinRespuesta =
                SqlSinRespuesta(
                    "i");

            var sql = $@"
SELECT TOP (1)
    i.Id,
    i.Nombre,
    i.Telefono,
    i.Email,
    i.Ciudad,
    i.ProductoInteres,
    i.AportaIPS,
    i.CantidadAportes,
    i.Estado,
    i.FechaRegistro,
    i.FechaProximoContacto,
    i.Descripcion,
    i.ArchivoUrl,
    i.UsuarioResponsable,

    i.Origen,
    i.IdentificadorExterno,
    i.IdConversacion,
    i.IdModeloProducto,
    i.IdPublicacion,
    i.MarcaInteres,
    i.ModeloInteres,
    i.CodigoReferencia,
    i.EstadoConsulta,

    CASE
        WHEN {sqlSinRespuesta} = 1
            THEN 'SIN_RESPUESTA'
        ELSE ISNULL(i.EstadoConsulta, 'REGISTRADO')
    END AS EstadoGestion,

    i.TipoOperacion,
    i.IdSolicitudOperacion,
    i.PasoOperacion,
    i.RequiereSeguimiento,
    i.MotivoSeguimiento,
    i.FechaUltimoMensajeCliente,
    i.FechaUltimaRespuesta,
    i.FechaUltimaInteraccion,
    i.UltimoMensajeCliente,
    i.UltimaRespuesta,
    ISNULL(i.CantidadInteracciones, 0)
        AS CantidadInteracciones,

    CAST(
        {sqlSinRespuesta}
        AS BIT
    ) AS SinRespuesta,

    CAST(
        CASE
            WHEN i.RequiereSeguimiento = 1
             AND i.FechaProximoContacto IS NOT NULL
             AND i.FechaProximoContacto <= GETDATE()
                THEN 1
            ELSE 0
        END
        AS BIT
    ) AS SeguimientoVencido

FROM dbo.Interesados i

WHERE i.Id = @Id;";

            return
                await conn.QueryFirstOrDefaultAsync<InteresadoDto>(
                    sql,
                    new
                    {
                        Id = id
                    });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error obteniendo interesado {Id}",
                id);

            throw new RepositoryException(
                "Error obteniendo interesado",
                ex);
        }
    }


    // =========================================================
    // DETALLE COMPLETO
    // =========================================================

    public async Task<InteresadoDetalleDto?>
        ObtenerDetalleInteresado(
            int id)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            var interesado =
                await ObtenerInteresadoPorId(id);

            if (interesado is null)
            {
                return null;
            }

            const string sql = @"
SELECT
    q.Id,
    q.IdInteresado,
    q.IdConversacion,
    q.IdModeloProducto,
    q.IdPublicacion,
    q.Marca,
    q.Modelo,
    q.CodigoReferencia,
    q.TipoConsulta,
    q.EstadoConsulta,
    q.TipoOperacion,
    q.IdSolicitudOperacion,
    q.PasoOperacion,
    q.UltimoMensajeCliente,
    q.UltimaRespuesta,
    q.FechaPrimeraConsulta,
    q.FechaUltimaConsulta,
    q.CantidadInteracciones
FROM dbo.InteresadoConsultasMoto q
WHERE q.IdInteresado = @IdInteresado
ORDER BY
    q.FechaUltimaConsulta DESC,
    q.Id DESC;


SELECT TOP (50)
    m.Emisor,
    m.Mensaje,
    m.Fecha
FROM dbo.MensajesConversacion m
INNER JOIN dbo.Interesados i
    ON i.IdConversacion = m.IdConversacion
WHERE i.Id = @IdInteresado
ORDER BY
    m.Id DESC;


SELECT
    s.Id,
    s.IdInteresado,
    s.Fecha,
    s.Comentario,
    COALESCE(
        u.NombreUsuario,
        s.Usuario
    ) AS Usuario
FROM dbo.Seguimientos s
LEFT JOIN dbo.Usuarios u
    ON u.Id = TRY_CONVERT(INT, s.Usuario)
WHERE s.IdInteresado = @IdInteresado
ORDER BY
    s.Fecha DESC,
    s.Id DESC;";

            using var multi =
                await conn.QueryMultipleAsync(
                    sql,
                    new
                    {
                        IdInteresado = id
                    });

            var consultas =
                (await multi.ReadAsync<InteresadoConsultaMotoDto>())
                .ToList();

            var mensajes =
                (await multi.ReadAsync<MensajeConversacionHistorialDto>())
                .Reverse()
                .ToList();

            var seguimientos =
                (await multi.ReadAsync<SeguimientoDto>())
                .ToList();

            return new InteresadoDetalleDto
            {
                Interesado = interesado,
                ConsultasMoto = consultas,
                UltimosMensajes = mensajes,
                Seguimientos = seguimientos
            };
        }
        catch (RepositoryException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error obteniendo detalle del interesado {Id}",
                id);

            throw new RepositoryException(
                "Error obteniendo detalle del interesado",
                ex);
        }
    }


    // =========================================================
    // ACTUALIZAR REGISTRO MANUAL
    // =========================================================

    public async Task ActualizarInteresado(
        InteresadoDto interesado)
    {
        using var conn = _conexion.CreateSqlConnection();

        const string sql = @"
UPDATE dbo.Interesados
SET
    Nombre = @Nombre,
    Telefono = @Telefono,
    Email = @Email,
    Ciudad = @Ciudad,
    ProductoInteres = @ProductoInteres,
    AportaIPS = @AportaIPS,
    CantidadAportes = @CantidadAportes,
    Estado = @Estado,
    FechaProximoContacto = @FechaProximoContacto,
    Descripcion = @Descripcion,
    ArchivoUrl = @ArchivoUrl
WHERE Id = @Id;";

        try
        {
            await conn.ExecuteAsync(
                sql,
                interesado);

            _logger.LogInformation(
                "Interesado {Id} actualizado correctamente",
                interesado.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al actualizar interesado {@Interesado}",
                interesado);

            throw new RepositoryException(
                "Error al actualizar interesado",
                ex);
        }
    }


    // =========================================================
    // ACTUALIZAR SEGUIMIENTO SIN OBLIGAR A EDITAR TODO EL CLIENTE
    // =========================================================

    public async Task ActualizarSeguimientoInteresado(
        int idInteresado,
        ActualizarSeguimientoInteresadoRequest request)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
UPDATE dbo.Interesados
SET
    FechaProximoContacto =
        CASE
            WHEN @FechaProximoContacto IS NOT NULL
                THEN @FechaProximoContacto
            ELSE FechaProximoContacto
        END,

    RequiereSeguimiento =
        COALESCE(
            @RequiereSeguimiento,
            RequiereSeguimiento
        ),

    MotivoSeguimiento =
        COALESCE(
            NULLIF(@MotivoSeguimiento, ''),
            MotivoSeguimiento
        ),

    EstadoConsulta =
        COALESCE(
            NULLIF(@EstadoConsulta, ''),
            EstadoConsulta
        ),

    FechaUltimaInteraccion =
        GETDATE()

WHERE Id = @IdInteresado;

IF @@ROWCOUNT = 0
BEGIN
    THROW 51001, 'No se encontró el interesado.', 1;
END;";

            await conn.ExecuteAsync(
                sql,
                new
                {
                    IdInteresado = idInteresado,
                    request.FechaProximoContacto,
                    request.RequiereSeguimiento,
                    request.MotivoSeguimiento,
                    request.EstadoConsulta
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error actualizando seguimiento del interesado {Id}",
                idInteresado);

            throw new RepositoryException(
                "Error actualizando seguimiento del interesado",
                ex);
        }
    }


    private static string SqlSinRespuesta(
        string alias)
    {
        return $@"
CASE
    WHEN {alias}.Estado = 'Activo'
     AND {alias}.RequiereSeguimiento = 1
     AND {alias}.FechaUltimaRespuesta IS NOT NULL
     AND
     (
         {alias}.FechaUltimoMensajeCliente IS NULL
         OR {alias}.FechaUltimaRespuesta
            >= {alias}.FechaUltimoMensajeCliente
     )
     AND {alias}.FechaUltimaRespuesta
         <= DATEADD(HOUR, -24, GETDATE())
     AND ISNULL({alias}.EstadoConsulta, '')
         NOT IN
         (
             'CREDITO_EN_PROCESO',
             'CONTADO_EN_PROCESO',
             'DERIVADO_HUMANO',
             'CERRADO'
         )
        THEN 1
    ELSE 0
END";
    }

    // =========================================================
    // FECHAS OPERATIVAS - PARAGUAY / UTC
    // =========================================================

    private static (DateTime DesdeUtc, DateTime HastaUtc)
        ObtenerRangoUtcDiaParaguay(
            DateTime fechaParaguay)
    {
        var inicioLocal =
            DateTime.SpecifyKind(
                fechaParaguay.Date,
                DateTimeKind.Unspecified);

        var finLocal =
            inicioLocal.AddDays(1);

        return
        (
            TimeZoneInfo.ConvertTimeToUtc(
                inicioLocal,
                ZonaHorariaParaguay),
            TimeZoneInfo.ConvertTimeToUtc(
                finLocal,
                ZonaHorariaParaguay)
        );
    }


    private static TimeZoneInfo
        ObtenerZonaHorariaParaguay()
    {
        try
        {
            // Linux / contenedores Docker.
            return TimeZoneInfo.FindSystemTimeZoneById(
                "America/Asuncion");
        }
        catch (TimeZoneNotFoundException)
        {
            // Windows / desarrollo local.
            return TimeZoneInfo.FindSystemTimeZoneById(
                "Paraguay Standard Time");
        }
    }

}

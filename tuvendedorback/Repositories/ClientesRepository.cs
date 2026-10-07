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
        LEFT(COALESCE(@TelefonoReal, @Identificador), 20),
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
                WHEN NULLIF(Telefono, '') IS NULL
                    THEN LEFT(@Identificador, 20)
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
            COALESCE(
                NULLIF(@EstadoConsulta, ''),
                EstadoConsulta,
                'CONSULTANDO'
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

        RequiereSeguimiento =
            CASE
                WHEN Estado = 'Inactivo'
                    THEN RequiereSeguimiento
                ELSE 1
            END,

        MotivoSeguimiento =
            COALESCE(
                NULLIF(@MotivoSeguimiento, ''),
                MotivoSeguimiento,
                N'Seguimiento comercial de consulta por WhatsApp.'
            ),

        FechaProximoContacto =
            CASE
                WHEN Estado = 'Inactivo'
                    THEN FechaProximoContacto
                WHEN FechaProximoContacto IS NULL
                  OR FechaProximoContacto < GETDATE()
                    THEN DATEADD(DAY, 1, GETDATE())
                ELSE FechaProximoContacto
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
        WHEN i.RequiereSeguimiento = 1
         AND i.FechaProximoContacto IS NOT NULL
         AND i.FechaProximoContacto <= GETDATE()
            THEN 0
        ELSE 1
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
DECLARE @Dia DATE =
    COALESCE(CAST(@Fecha AS DATE), CAST(GETDATE() AS DATE));

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
            WHEN CAST(i.FechaRegistro AS DATE) = @Dia
                THEN 1
            ELSE 0
        END
    ) AS NuevosDelDia,

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

            return
                await conn.QueryFirstAsync<InteresadosResumenDto>(
                    sql,
                    new
                    {
                        Fecha = fecha?.Date
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
}

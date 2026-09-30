using Dapper;
using tuvendedorback.Data;
using tuvendedorback.DTOs;
using tuvendedorback.Exceptions;
using tuvendedorback.Repositories.Interfaces;

namespace tuvendedorback.Repositories;

public class ContadoMotoGestionRepository
    : IContadoMotoGestionRepository
{
    private readonly DbConnections _conexion;
    private readonly ILogger<ContadoMotoGestionRepository> _logger;

    public ContadoMotoGestionRepository(
        DbConnections conexion,
        ILogger<ContadoMotoGestionRepository> logger)
    {
        _conexion = conexion;
        _logger = logger;
    }


    // =========================================================
    // LISTAR
    // =========================================================

    public async Task<IReadOnlyList<ContadoMotoGestionListaDto>> Listar(
        string? estado,
        string? buscar,
        DateTime? fecha)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
SELECT
    g.Id AS IdGestion,
    g.IdSolicitudContado,
    sc.IdConversacion,
    sc.IdContacto,
    sc.IdModeloProducto,

    m.Nombre AS Marca,
    mp.NombreModelo AS Modelo,
    mp.CodigoReferencia,

    LTRIM(RTRIM(
        CONCAT(
            ISNULL(c.Nombre, ''),
            CASE
                WHEN NULLIF(LTRIM(RTRIM(ISNULL(c.Apellido, ''))), '') IS NULL
                    THEN ''
                ELSE ' ' + c.Apellido
            END
        )
    )) AS NombreCompleto,

    c.Cedula,
    c.Telefono,

    sc.Estado AS EstadoSolicitud,
    sc.PasoActual,
    sc.EstadoCedula,

    g.EstadoControl,
    g.Prioridad,
    g.IdUsuarioAsignado,
    g.ObservacionInterna,
    g.FechaRecepcion,
    g.FechaUltimaGestion,
    g.FechaCierreControl

FROM dbo.SolicitudContadoMotoGestion g

INNER JOIN dbo.SolicitudesContadoMoto sc
    ON sc.Id = g.IdSolicitudContado

INNER JOIN dbo.Contactos c
    ON c.Id = sc.IdContacto

LEFT JOIN dbo.ModelosProducto mp
    ON mp.Id = sc.IdModeloProducto

LEFT JOIN dbo.Marcas m
    ON m.Id = mp.IdMarca

WHERE
    (
        @Estado IS NULL
        OR @Estado = ''
        OR @Estado = 'TODOS'
        OR g.EstadoControl = @Estado
    )
    AND
    (
        @Buscar IS NULL
        OR @Buscar = ''
        OR c.Nombre LIKE '%' + @Buscar + '%'
        OR c.Apellido LIKE '%' + @Buscar + '%'
        OR c.Cedula LIKE '%' + @Buscar + '%'
        OR c.Telefono LIKE '%' + @Buscar + '%'
        OR m.Nombre LIKE '%' + @Buscar + '%'
        OR mp.NombreModelo LIKE '%' + @Buscar + '%'
        OR mp.CodigoReferencia LIKE '%' + @Buscar + '%'
    )
    AND
    (
        @Fecha IS NULL
        OR
        (
            g.FechaRecepcion >= @Fecha
            AND g.FechaRecepcion < DATEADD(DAY, 1, @Fecha)
        )
    )

ORDER BY
    CASE
        WHEN g.EstadoControl = 'PENDIENTE_CONTACTO' THEN 0
        WHEN g.EstadoControl = 'CONTACTADO' THEN 1
        ELSE 2
    END,
    CASE
        WHEN g.Prioridad = 'URGENTE' THEN 0
        ELSE 1
    END,
    g.FechaRecepcion DESC;
";

            var data = await conn.QueryAsync<ContadoMotoGestionListaDto>(
                sql,
                new
                {
                    Estado = string.IsNullOrWhiteSpace(estado)
                        ? null
                        : estado.Trim().ToUpperInvariant(),

                    Buscar = string.IsNullOrWhiteSpace(buscar)
                        ? null
                        : buscar.Trim(),

                    Fecha = fecha?.Date
                });

            return data.ToList();
        }
        catch (Exception ex)
        {
            throw Error(
                ex,
                "Error listando solicitudes al contado.");
        }
    }


    // =========================================================
    // DETALLE
    // =========================================================

    public async Task<ContadoMotoGestionDetalleDto?> ObtenerDetalle(
        int idSolicitudContado)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
SELECT
    g.Id AS IdGestion,
    g.IdSolicitudContado,
    sc.IdConversacion,
    sc.IdContacto,
    sc.IdModeloProducto,

    m.Nombre AS Marca,
    mp.NombreModelo AS Modelo,
    mp.CodigoReferencia,

    LTRIM(RTRIM(
        CONCAT(
            ISNULL(c.Nombre, ''),
            CASE
                WHEN NULLIF(LTRIM(RTRIM(ISNULL(c.Apellido, ''))), '') IS NULL
                    THEN ''
                ELSE ' ' + c.Apellido
            END
        )
    )) AS NombreCompleto,

    c.Cedula,
    c.Telefono,
    c.Direccion,
    c.Barrio,
    c.Ciudad,

    sc.Estado AS EstadoSolicitud,
    sc.PasoActual,
    sc.EstadoCedula,

    g.EstadoControl,
    g.Prioridad,
    g.IdUsuarioAsignado,
    g.ObservacionInterna,
    g.FechaRecepcion,
    g.FechaUltimaGestion,
    g.FechaCierreControl

FROM dbo.SolicitudContadoMotoGestion g

INNER JOIN dbo.SolicitudesContadoMoto sc
    ON sc.Id = g.IdSolicitudContado

INNER JOIN dbo.Contactos c
    ON c.Id = sc.IdContacto

LEFT JOIN dbo.ModelosProducto mp
    ON mp.Id = sc.IdModeloProducto

LEFT JOIN dbo.Marcas m
    ON m.Id = mp.IdMarca

WHERE g.IdSolicitudContado = @IdSolicitudContado;


SELECT
    d.Id,
    d.TipoDocumento,
    d.NombreArchivo,
    d.MimeType,
    d.EstadoRevision,
    d.FechaRecepcion

FROM dbo.SolicitudDocumentosMoto d

WHERE d.IdSolicitudContado = @IdSolicitudContado
  AND d.TipoOperacion = 'CONTADO'

ORDER BY d.Id;


SELECT
    h.Id,
    h.IdSolicitudContado,
    h.Accion,
    h.EstadoAnterior,
    h.EstadoNuevo,
    h.Observacion,
    h.IdUsuario,
    h.Fecha

FROM dbo.SolicitudContadoMotoGestionHistorial h

WHERE h.IdSolicitudContado = @IdSolicitudContado

ORDER BY h.Fecha DESC, h.Id DESC;
";

            using var multi = await conn.QueryMultipleAsync(
                sql,
                new
                {
                    IdSolicitudContado = idSolicitudContado
                });

            var detalle =
                await multi.ReadFirstOrDefaultAsync<
                    ContadoMotoGestionDetalleDto>();

            if (detalle == null)
            {
                return null;
            }

            detalle.Documentos =
                (await multi.ReadAsync<ContadoMotoDocumentoDto>())
                .ToList();

            detalle.Historial =
                (await multi.ReadAsync<ContadoMotoGestionHistorialDto>())
                .ToList();

            return detalle;
        }
        catch (Exception ex)
        {
            throw Error(
                ex,
                "Error obteniendo detalle de solicitud al contado.");
        }
    }


    // =========================================================
    // CONTACTAR
    // =========================================================

    public async Task Contactar(
        int idSolicitudContado,
        int? idUsuario)
    {
        using var conn = _conexion.CreateSqlConnection();

        conn.Open();

        using var transaction = conn.BeginTransaction();

        try
        {
            const string sql = @"
DECLARE @EstadoAnterior NVARCHAR(30);
DECLARE @IdConversacion INT;

SELECT
    @EstadoAnterior = g.EstadoControl,
    @IdConversacion = sc.IdConversacion

FROM dbo.SolicitudContadoMotoGestion g

INNER JOIN dbo.SolicitudesContadoMoto sc
    ON sc.Id = g.IdSolicitudContado

WHERE g.IdSolicitudContado = @IdSolicitudContado;


IF @EstadoAnterior IS NULL
BEGIN
    THROW 50020,
    'No se encontró la solicitud al contado en gestión.',
    1;
END;


IF @EstadoAnterior IN ('CONCRETADA', 'NO_CONCRETADA')
BEGIN
    THROW 50021,
    'La gestión de esta solicitud ya se encuentra cerrada.',
    1;
END;


UPDATE dbo.SolicitudContadoMotoGestion
SET
    EstadoControl = 'CONTACTADO',

    -- IMPORTANTE:
    -- Si el JWT no trae un IdUsuario numérico, NO bloqueamos la operación.
    -- La columna es nullable y dejamos la gestión sin asignación exclusiva.
    IdUsuarioAsignado =
        COALESCE(@IdUsuario, IdUsuarioAsignado),

    ObservacionInterna =
        N'La oportunidad pasó a contacto comercial humano.',

    FechaUltimaGestion = SYSDATETIME()

WHERE IdSolicitudContado = @IdSolicitudContado;


INSERT INTO dbo.SolicitudContadoMotoGestionHistorial
(
    IdSolicitudContado,
    Accion,
    EstadoAnterior,
    EstadoNuevo,
    Observacion,
    IdUsuario,
    Fecha
)
VALUES
(
    @IdSolicitudContado,
    'CONTACTO',
    @EstadoAnterior,
    'CONTACTADO',
    N'La oportunidad pasó a contacto comercial humano.',
    @IdUsuario,
    SYSDATETIME()
);


UPDATE dbo.Conversaciones
SET Modo = 'HUMANO'
WHERE Id = @IdConversacion;
";

            await conn.ExecuteAsync(
                sql,
                new
                {
                    IdSolicitudContado = idSolicitudContado,
                    IdUsuario = idUsuario
                },
                transaction);

            transaction.Commit();
        }
        catch (Exception ex)
        {
            transaction.Rollback();

            throw Error(
                ex,
                "Error marcando solicitud al contado como contactada.");
        }
    }


    // =========================================================
    // CAMBIAR ESTADO
    // =========================================================

    public async Task CambiarEstado(
        int idSolicitudContado,
        int? idUsuario,
        string nuevoEstado,
        string? observacion)
    {
        using var conn = _conexion.CreateSqlConnection();

        conn.Open();

        using var transaction = conn.BeginTransaction();

        try
        {
            const string sql = @"
DECLARE @EstadoAnterior NVARCHAR(30);
DECLARE @IdConversacion INT;

SELECT
    @EstadoAnterior = g.EstadoControl,
    @IdConversacion = sc.IdConversacion

FROM dbo.SolicitudContadoMotoGestion g

INNER JOIN dbo.SolicitudesContadoMoto sc
    ON sc.Id = g.IdSolicitudContado

WHERE g.IdSolicitudContado = @IdSolicitudContado;


IF @EstadoAnterior IS NULL
BEGIN
    THROW 50022,
    'No se encontró la solicitud al contado en gestión.',
    1;
END;


UPDATE dbo.SolicitudContadoMotoGestion
SET
    EstadoControl = @NuevoEstado,

    IdUsuarioAsignado =
        COALESCE(IdUsuarioAsignado, @IdUsuario),

    ObservacionInterna =
        COALESCE(NULLIF(@Observacion, ''), ObservacionInterna),

    FechaUltimaGestion =
        SYSDATETIME(),

    FechaCierreControl =
        CASE
            WHEN @NuevoEstado IN ('CONCRETADA', 'NO_CONCRETADA')
                THEN SYSDATETIME()
            ELSE NULL
        END

WHERE IdSolicitudContado = @IdSolicitudContado;


INSERT INTO dbo.SolicitudContadoMotoGestionHistorial
(
    IdSolicitudContado,
    Accion,
    EstadoAnterior,
    EstadoNuevo,
    Observacion,
    IdUsuario,
    Fecha
)
VALUES
(
    @IdSolicitudContado,

    CASE
        WHEN @NuevoEstado = 'CONTACTADO'
            THEN 'CONTACTO'
        WHEN @NuevoEstado = 'CONCRETADA'
            THEN 'CIERRE_CONCRETADO'
        WHEN @NuevoEstado = 'NO_CONCRETADA'
            THEN 'CIERRE_NO_CONCRETADO'
        ELSE 'CAMBIO_ESTADO'
    END,

    @EstadoAnterior,
    @NuevoEstado,
    @Observacion,
    @IdUsuario,
    SYSDATETIME()
);


IF @NuevoEstado IN ('CONCRETADA', 'NO_CONCRETADA')
BEGIN
    UPDATE dbo.Conversaciones
    SET Modo = 'IA'
    WHERE Id = @IdConversacion;
END
ELSE
BEGIN
    UPDATE dbo.Conversaciones
    SET Modo = 'HUMANO'
    WHERE Id = @IdConversacion;
END;
";

            await conn.ExecuteAsync(
                sql,
                new
                {
                    IdSolicitudContado = idSolicitudContado,
                    IdUsuario = idUsuario,
                    NuevoEstado = nuevoEstado,
                    Observacion = string.IsNullOrWhiteSpace(observacion)
                        ? null
                        : observacion.Trim()
                },
                transaction);

            transaction.Commit();
        }
        catch (Exception ex)
        {
            transaction.Rollback();

            throw Error(
                ex,
                "Error cambiando estado de solicitud al contado.");
        }
    }


    // =========================================================
    // DOCUMENTO
    // =========================================================

    public async Task<ContadoMotoDocumentoArchivoDto?> ObtenerDocumento(
        int idSolicitudContado,
        int idDocumento)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
SELECT TOP (1)
    d.NombreArchivo,
    d.MimeType,
    d.RutaPrivada

FROM dbo.SolicitudDocumentosMoto d

WHERE d.Id = @IdDocumento
  AND d.IdSolicitudContado = @IdSolicitudContado
  AND d.TipoOperacion = 'CONTADO';
";

            return await conn.QueryFirstOrDefaultAsync<
                ContadoMotoDocumentoArchivoDto>(
                    sql,
                    new
                    {
                        IdSolicitudContado = idSolicitudContado,
                        IdDocumento = idDocumento
                    });
        }
        catch (Exception ex)
        {
            throw Error(
                ex,
                "Error obteniendo documento de solicitud al contado.");
        }
    }


    private RepositoryException Error(
        Exception ex,
        string mensaje)
    {
        _logger.LogError(
            ex,
            mensaje);

        return new RepositoryException(
            mensaje,
            ex);
    }
}

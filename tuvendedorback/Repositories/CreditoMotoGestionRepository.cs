using Dapper;
using tuvendedorback.Data;
using tuvendedorback.DTOs;
using tuvendedorback.Exceptions;
using tuvendedorback.Repositories.Interfaces;

namespace tuvendedorback.Repositories;

public class CreditoMotoGestionRepository
    : ICreditoMotoGestionRepository
{
    private readonly DbConnections _conexion;
    private readonly ILogger<CreditoMotoGestionRepository> _logger;

    public CreditoMotoGestionRepository(
        DbConnections conexion,
        ILogger<CreditoMotoGestionRepository> logger)
    {
        _conexion = conexion;
        _logger = logger;
    }


    // =========================================================
    // LISTAR BANDEJA
    // =========================================================

    public async Task<IReadOnlyList<CreditoMotoGestionListaDto>> Listar(
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
    g.IdSolicitudCredito,

    s.IdConversacion,
    s.IdContacto,
    s.IdModeloProducto,

    ma.Nombre AS Marca,
    mp.NombreModelo AS Modelo,
    mp.CodigoReferencia,

    LTRIM(
        RTRIM(
            CONCAT(
                ISNULL(c.Nombre, ''),
                ' ',
                ISNULL(c.Apellido, '')
            )
        )
    ) AS NombreCompleto,

    c.Cedula,
    c.Telefono,

    s.Estado AS EstadoSolicitud,
    s.ResultadoPreEvaluacion,

    g.EstadoControl,

    g.IdUsuarioAsignado,
    u.NombreUsuario AS UsuarioAsignado,

    g.FechaRecepcion,
    g.FechaUltimaGestion,
    g.FechaCierreControl

FROM dbo.SolicitudCreditoMotoGestion g

INNER JOIN dbo.SolicitudesCredito s
    ON s.Id = g.IdSolicitudCredito

INNER JOIN dbo.Contactos c
    ON c.Id = s.IdContacto

LEFT JOIN dbo.ModelosProducto mp
    ON mp.Id = s.IdModeloProducto

LEFT JOIN dbo.Marcas ma
    ON ma.Id = mp.IdMarca

LEFT JOIN dbo.Usuarios u
    ON u.Id = g.IdUsuarioAsignado

WHERE
    (
        @Estado IS NULL
        OR g.EstadoControl = @Estado
    )
    AND
    (
        @Buscar IS NULL
        OR c.Cedula LIKE @Buscar
        OR c.Telefono LIKE @Buscar
        OR c.Nombre LIKE @Buscar
        OR c.Apellido LIKE @Buscar
        OR mp.NombreModelo LIKE @Buscar
        OR mp.CodigoReferencia LIKE @Buscar
        OR ma.Nombre LIKE @Buscar
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
    CASE g.EstadoControl
        WHEN 'PENDIENTE_ENVIO' THEN 1
        WHEN 'ENVIADA_EMPRESA' THEN 2
        ELSE 9
    END,
    g.FechaRecepcion DESC,
    g.Id DESC;
";

            var patronBuscar =
                string.IsNullOrWhiteSpace(buscar)
                    ? null
                    : $"%{buscar.Trim()}%";

            var data =
                await conn.QueryAsync<CreditoMotoGestionListaDto>(
                    sql,
                    new
                    {
                        Estado =
                            string.IsNullOrWhiteSpace(estado)
                                ? null
                                : estado.Trim().ToUpperInvariant(),

                        Buscar = patronBuscar,
                        Fecha = fecha?.Date
                    });

            return data.ToList();
        }
        catch (Exception ex)
        {
            throw Error(
                ex,
                "Error listando solicitudes de crédito de motos.");
        }
    }


    // =========================================================
    // OBTENER DETALLE COMPLETO
    // =========================================================

    public async Task<CreditoMotoGestionDetalleDto?> ObtenerDetalle(
        int idSolicitudCredito)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
-- ============================================================
-- 1) CABECERA
-- ============================================================

SELECT TOP (1)
    g.Id AS IdGestion,
    g.IdSolicitudCredito,

    s.IdConversacion,
    s.IdContacto,
    s.IdModeloProducto,
    s.IdPublicacion,

    ma.Nombre AS Marca,
    mp.NombreModelo AS Modelo,
    mp.CodigoReferencia,
    mp.Cilindrada,

    LTRIM(
        RTRIM(
            CONCAT(
                ISNULL(c.Nombre, ''),
                ' ',
                ISNULL(c.Apellido, '')
            )
        )
    ) AS NombreCompleto,

    c.Cedula,
    c.FechaNacimiento,
    c.Telefono,

    c.Ciudad,
    c.Barrio,
    c.Direccion,

    s.Estado AS EstadoSolicitud,
    s.PasoActual,

    s.ResultadoPreEvaluacion,
    s.MotivoPreEvaluacion,
    s.ViaEvaluacion,
    s.EstadoCedula,

    s.Observacion AS ObservacionSolicitud,

    s.FechaCreacion,
    s.FechaActualizacion,
    s.FechaCierre,

    g.EstadoControl,

    g.IdUsuarioAsignado,
    u.NombreUsuario AS UsuarioAsignado,

    g.ObservacionInterna,

    g.FechaRecepcion,
    g.FechaUltimaGestion,
    g.FechaCierreControl

FROM dbo.SolicitudCreditoMotoGestion g

INNER JOIN dbo.SolicitudesCredito s
    ON s.Id = g.IdSolicitudCredito

INNER JOIN dbo.Contactos c
    ON c.Id = s.IdContacto

LEFT JOIN dbo.ModelosProducto mp
    ON mp.Id = s.IdModeloProducto

LEFT JOIN dbo.Marcas ma
    ON ma.Id = mp.IdMarca

LEFT JOIN dbo.Usuarios u
    ON u.Id = g.IdUsuarioAsignado

WHERE g.IdSolicitudCredito = @IdSolicitud;


-- ============================================================
-- 2) DATOS LABORALES
-- ============================================================

SELECT TOP (1)
    Empresa,
    AntiguedadMeses,
    AportaIPS,
    CantidadAportesIPS,
    Cargo,
    Salario,
    TipoPago,
    DireccionEmpresa,
    TelefonoEmpresa,
    TelefonoEmpresaEsMovil,
    NombreJefeEncargado

FROM dbo.SolicitudDatosLaborales

WHERE IdSolicitud = @IdSolicitud;


-- ============================================================
-- 3) REFERENCIAS
-- ============================================================

SELECT
    Id,
    Tipo,
    Nombre,
    Telefono,
    Parentesco,
    Observacion

FROM dbo.SolicitudReferencias

WHERE IdSolicitud = @IdSolicitud

ORDER BY Id;


-- ============================================================
-- 4) DOCUMENTOS
-- ============================================================

SELECT
    Id,
    TipoDocumento,
    NombreArchivo,
    MimeType,
    EstadoRevision,
    FechaRecepcion

FROM dbo.SolicitudDocumentosMoto

WHERE IdSolicitudCredito = @IdSolicitud

ORDER BY Id;


-- ============================================================
-- 5) AUTORIZACION
-- ============================================================

SELECT TOP (1)
    Id,
    VersionAutorizacion,
    TextoAutorizacion,
    MensajeOriginal,
    NombreCompleto,
    NumeroCedula,
    Canal,
    FechaAutorizacion

FROM dbo.SolicitudAutorizacionCredito

WHERE IdSolicitudCredito = @IdSolicitud

ORDER BY Id DESC;


-- ============================================================
-- 6) HISTORIAL
-- ============================================================

SELECT
    h.Id,
    h.Accion,
    h.EstadoAnterior,
    h.EstadoNuevo,
    h.Observacion,
    h.IdUsuario,
    u.NombreUsuario AS Usuario,
    h.Fecha

FROM dbo.SolicitudCreditoMotoGestionHistorial h

LEFT JOIN dbo.Usuarios u
    ON u.Id = h.IdUsuario

WHERE h.IdSolicitudCredito = @IdSolicitud

ORDER BY
    h.Fecha ASC,
    h.Id ASC;
";

            using var multi =
                await conn.QueryMultipleAsync(
                    sql,
                    new
                    {
                        IdSolicitud = idSolicitudCredito
                    });

            var detalle =
                await multi.ReadFirstOrDefaultAsync<
                    CreditoMotoGestionDetalleDto>();

            if (detalle == null)
            {
                return null;
            }

            detalle.Laboral =
                await multi.ReadFirstOrDefaultAsync<
                    CreditoMotoLaboralDto>();

            detalle.Referencias =
                (await multi.ReadAsync<
                    CreditoMotoReferenciaDto>())
                .ToList();

            detalle.Documentos =
                (await multi.ReadAsync<
                    CreditoMotoDocumentoDto>())
                .ToList();

            detalle.Autorizacion =
                await multi.ReadFirstOrDefaultAsync<
                    CreditoMotoAutorizacionDto>();

            detalle.Historial =
                (await multi.ReadAsync<
                    CreditoMotoHistorialDto>())
                .ToList();

            return detalle;
        }
        catch (Exception ex)
        {
            throw Error(
                ex,
                "Error obteniendo detalle de solicitud de crédito de moto.");
        }
    }


    // =========================================================
    // CAMBIAR ESTADO DE CONTROL
    // =========================================================

    public async Task<bool> CambiarEstado(
        int idSolicitudCredito,
        int idUsuario,
        string estadoAnterior,
        string estadoNuevo,
        string accion,
        string? observacion)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            conn.Open();

            using var transaction =
                conn.BeginTransaction();

            const string sqlActualizar = @"
UPDATE dbo.SolicitudCreditoMotoGestion
SET
    EstadoControl = @EstadoNuevo,
    FechaUltimaGestion = GETDATE(),

    ObservacionInterna =
        CASE
            WHEN NULLIF(LTRIM(RTRIM(@Observacion)), '') IS NOT NULL
                THEN @Observacion
            ELSE ObservacionInterna
        END,

    FechaCierreControl =
        CASE
            WHEN @EstadoNuevo = 'ENVIADA_EMPRESA'
                THEN GETDATE()
            ELSE FechaCierreControl
        END

WHERE IdSolicitudCredito = @IdSolicitud
  AND EstadoControl = @EstadoAnterior;
";

            var filas =
                await conn.ExecuteAsync(
                    sqlActualizar,
                    new
                    {
                        IdSolicitud = idSolicitudCredito,
                        EstadoAnterior = estadoAnterior,
                        EstadoNuevo = estadoNuevo,
                        Observacion = observacion
                    },
                    transaction);

            if (filas == 0)
            {
                transaction.Rollback();
                return false;
            }

            const string sqlHistorial = @"
INSERT INTO dbo.SolicitudCreditoMotoGestionHistorial
(
    IdSolicitudCredito,
    Accion,
    EstadoAnterior,
    EstadoNuevo,
    Observacion,
    IdUsuario,
    Fecha
)
VALUES
(
    @IdSolicitud,
    @Accion,
    @EstadoAnterior,
    @EstadoNuevo,
    @Observacion,
    @IdUsuario,
    GETDATE()
);
";

            await conn.ExecuteAsync(
                sqlHistorial,
                new
                {
                    IdSolicitud = idSolicitudCredito,
                    IdUsuario = idUsuario,
                    Accion = accion,
                    EstadoAnterior = estadoAnterior,
                    EstadoNuevo = estadoNuevo,
                    Observacion = observacion
                },
                transaction);

            transaction.Commit();

            return true;
        }
        catch (Exception ex)
        {
            throw Error(
                ex,
                "Error cambiando estado de solicitud de crédito de moto.");
        }
    }


    // =========================================================
    // OBTENER ARCHIVO PRIVADO
    // =========================================================

    public async Task<CreditoMotoDocumentoArchivoDataDto?>
        ObtenerDocumentoArchivo(
            int idSolicitudCredito,
            int idDocumento)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
SELECT TOP (1)
    NombreArchivo,
    MimeType,
    RutaPrivada

FROM dbo.SolicitudDocumentosMoto

WHERE Id = @IdDocumento
  AND IdSolicitudCredito = @IdSolicitud;
";

            return await conn.QueryFirstOrDefaultAsync<
                CreditoMotoDocumentoArchivoDataDto>(
                    sql,
                    new
                    {
                        IdSolicitud = idSolicitudCredito,
                        IdDocumento = idDocumento
                    });
        }
        catch (Exception ex)
        {
            throw Error(
                ex,
                "Error obteniendo archivo de documento de crédito.");
        }
    }


    // =========================================================
    // ERROR
    // =========================================================

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

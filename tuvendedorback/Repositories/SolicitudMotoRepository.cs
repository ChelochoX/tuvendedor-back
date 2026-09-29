using Dapper;
using tuvendedorback.Data;
using tuvendedorback.DTOs;
using tuvendedorback.Exceptions;
using tuvendedorback.Repositories.Interfaces;

namespace tuvendedorback.Repositories;

public class SolicitudMotoRepository : ISolicitudMotoRepository
{
    private readonly DbConnections _conexion;
    private readonly ILogger<SolicitudMotoRepository> _logger;

    public SolicitudMotoRepository(
        DbConnections conexion,
        ILogger<SolicitudMotoRepository> logger)
    {
        _conexion = conexion;
        _logger = logger;
    }

    public async Task<SolicitudMotoProcesoDto?> ObtenerActivaPorConversacion(
        int idConversacion)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
                SELECT TOP (1) *
                FROM
                (
                    SELECT
                        'CREDITO' AS TipoOperacion,
                        sc.Id AS IdSolicitud,
                        sc.IdConversacion,
                        sc.IdModeloProducto,
                        sc.IdPublicacion,
                        sc.IdContacto,
                        sc.Estado,
                        ISNULL(sc.PasoActual, 'PRECALIFICACION_EDAD') AS PasoActual,
                        sc.ResultadoPreEvaluacion,
                        sc.MotivoPreEvaluacion,
                        sc.ViaEvaluacion,
                        sc.EstadoCedula,
                        c.Nombre,
                        c.Apellido,
                        c.Cedula,
                        c.FechaNacimiento,
                        c.Telefono,
                        c.Direccion,
                        c.Barrio,
                        c.Ciudad,
                        dl.Empresa,
                        dl.AntiguedadMeses,
                        dl.AportaIPS,
                        dl.CantidadAportesIPS,
                        dl.DireccionEmpresa,
                        dl.TelefonoEmpresa,
                        dl.TelefonoEmpresaEsMovil,
                        dl.NombreJefeEncargado,
                        ISNULL(sc.FechaActualizacion, sc.FechaCreacion) AS FechaOrden
                    FROM dbo.SolicitudesCredito sc
                    INNER JOIN dbo.Contactos c
                        ON c.Id = sc.IdContacto
                    LEFT JOIN dbo.SolicitudDatosLaborales dl
                        ON dl.IdSolicitud = sc.Id
                    WHERE sc.IdConversacion = @IdConversacion
                      AND sc.Estado IN ('EN_PROCESO','PRE_EVALUACION','DOCUMENTACION')

                    UNION ALL

                    SELECT
                        'CONTADO' AS TipoOperacion,
                        sc.Id AS IdSolicitud,
                        sc.IdConversacion,
                        sc.IdModeloProducto,
                        sc.IdPublicacion,
                        sc.IdContacto,
                        sc.Estado,
                        sc.PasoActual,
                        CAST(NULL AS NVARCHAR(30)) AS ResultadoPreEvaluacion,
                        CAST(NULL AS NVARCHAR(500)) AS MotivoPreEvaluacion,
                        CAST(NULL AS NVARCHAR(40)) AS ViaEvaluacion,
                        sc.EstadoCedula,
                        c.Nombre,
                        c.Apellido,
                        c.Cedula,
                        c.FechaNacimiento,
                        c.Telefono,
                        c.Direccion,
                        c.Barrio,
                        c.Ciudad,
                        CAST(NULL AS NVARCHAR(150)) AS Empresa,
                        CAST(NULL AS INT) AS AntiguedadMeses,
                        CAST(NULL AS BIT) AS AportaIPS,
                        CAST(NULL AS INT) AS CantidadAportesIPS,
                        CAST(NULL AS NVARCHAR(250)) AS DireccionEmpresa,
                        CAST(NULL AS NVARCHAR(30)) AS TelefonoEmpresa,
                        CAST(NULL AS BIT) AS TelefonoEmpresaEsMovil,
                        CAST(NULL AS NVARCHAR(150)) AS NombreJefeEncargado,
                        ISNULL(sc.FechaActualizacion, sc.FechaCreacion) AS FechaOrden
                    FROM dbo.SolicitudesContadoMoto sc
                    INNER JOIN dbo.Contactos c
                        ON c.Id = sc.IdContacto
                    WHERE sc.IdConversacion = @IdConversacion
                      AND sc.Estado = 'EN_PROCESO'
                ) x
                ORDER BY x.FechaOrden DESC;";

            return await conn.QueryFirstOrDefaultAsync<SolicitudMotoProcesoDto>(
                sql,
                new { IdConversacion = idConversacion });
        }
        catch (Exception ex)
        {
            throw Error(ex,
                "Error obteniendo solicitud de moto activa. IdConversacion={IdConversacion}",
                idConversacion);
        }
    }

    public async Task<ReglaCreditoMotoDto> ObtenerReglaCreditoActiva()
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
                SELECT TOP (1)
                    Id,
                    EdadMinima,
                    AntiguedadLaboralMinMeses,
                    AportesIPSMinimos,
                    ReferenciasFamiliaresMinimas,
                    ReferenciasAmigosMinimas,
                    ReferenciasComercialesMinimasSinIps
                FROM dbo.ReglasCreditoMoto
                WHERE Estado = 'Activo'
                  AND FechaDesde <= CAST(GETDATE() AS DATE)
                  AND (FechaHasta IS NULL OR FechaHasta >= CAST(GETDATE() AS DATE))
                ORDER BY FechaDesde DESC, Id DESC;";

            var regla = await conn.QueryFirstOrDefaultAsync<ReglaCreditoMotoDto>(sql);

            if (regla == null)
            {
                throw new ReglasdeNegocioException(
                    "No existe una regla activa de crédito para motos.");
            }

            return regla;
        }
        catch (ReglasdeNegocioException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw Error(ex, "Error obteniendo regla de crédito de motos.");
        }
    }

    public async Task<int> ObtenerOCrearContacto(string telefono)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
                DECLARE @IdContacto INT;

                SELECT TOP (1)
                    @IdContacto = Id
                FROM dbo.Contactos
                WHERE REPLACE(REPLACE(REPLACE(ISNULL(Telefono,''), ' ', ''), '-', ''), '+', '') = @Telefono
                ORDER BY Id DESC;

                IF @IdContacto IS NULL
                BEGIN
                    INSERT INTO dbo.Contactos
                    (
                        Telefono,
                        FechaCreacion
                    )
                    VALUES
                    (
                        @Telefono,
                        GETDATE()
                    );

                    SET @IdContacto = SCOPE_IDENTITY();
                END;

                SELECT @IdContacto;";

            return await conn.ExecuteScalarAsync<int>(
                sql,
                new { Telefono = SoloDigitos(telefono) });
        }
        catch (Exception ex)
        {
            throw Error(ex, "Error obteniendo o creando contacto.");
        }
    }

    public async Task<int> CrearCredito(
        int idConversacion,
        int idModeloProducto,
        int? idPublicacion,
        int idContacto)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
                INSERT INTO dbo.SolicitudesCredito
                (
                    IdConversacion,
                    IdPublicacion,
                    IdModeloProducto,
                    IdContacto,
                    Estado,
                    PasoActual,
                    ResultadoPreEvaluacion,
                    FechaCreacion,
                    FechaActualizacion
                )
                OUTPUT INSERTED.Id
                VALUES
                (
                    @IdConversacion,
                    @IdPublicacion,
                    @IdModeloProducto,
                    @IdContacto,
                    'PRE_EVALUACION',
                    'PRECALIFICACION_EDAD',
                    'PENDIENTE',
                    GETDATE(),
                    GETDATE()
                );";

            return await conn.ExecuteScalarAsync<int>(
                sql,
                new
                {
                    IdConversacion = idConversacion,
                    IdPublicacion = idPublicacion,
                    IdModeloProducto = idModeloProducto,
                    IdContacto = idContacto
                });
        }
        catch (Exception ex)
        {
            throw Error(ex, "Error creando solicitud de crédito de moto.");
        }
    }

    public async Task<int> CrearContado(
        int idConversacion,
        int idModeloProducto,
        int? idPublicacion,
        int idContacto)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
                INSERT INTO dbo.SolicitudesContadoMoto
                (
                    IdConversacion,
                    IdModeloProducto,
                    IdPublicacion,
                    IdContacto,
                    Estado,
                    PasoActual,
                    FechaCreacion,
                    FechaActualizacion
                )
                OUTPUT INSERTED.Id
                VALUES
                (
                    @IdConversacion,
                    @IdModeloProducto,
                    @IdPublicacion,
                    @IdContacto,
                    'EN_PROCESO',
                    'NOMBRE_COMPLETO',
                    GETDATE(),
                    GETDATE()
                );";

            return await conn.ExecuteScalarAsync<int>(
                sql,
                new
                {
                    IdConversacion = idConversacion,
                    IdModeloProducto = idModeloProducto,
                    IdPublicacion = idPublicacion,
                    IdContacto = idContacto
                });
        }
        catch (Exception ex)
        {
            throw Error(ex, "Error creando solicitud al contado de moto.");
        }
    }

    public Task ActualizarPaso(
        string tipoOperacion,
        int idSolicitud,
        string pasoActual)
        => EjecutarSolicitudUpdate(
            tipoOperacion,
            idSolicitud,
            pasoActual,
            null,
            null,
            null);

    public Task GuardarFechaNacimiento(
        int idContacto,
        DateTime fechaNacimiento)
        => Ejecutar(
            @"UPDATE dbo.Contactos
              SET FechaNacimiento = @FechaNacimiento
              WHERE Id = @IdContacto;",
            new
            {
                IdContacto = idContacto,
                FechaNacimiento = fechaNacimiento.Date
            },
            "guardando fecha de nacimiento");

    public Task GuardarDatosContactoParciales(
        int idContacto,
        string? nombreCompleto = null,
        string? numeroCedula = null,
        string? ciudad = null,
        string? barrio = null,
        string? direccion = null)
    {
        string? nombre = null;
        string? apellido = null;

        if (!string.IsNullOrWhiteSpace(nombreCompleto))
        {
            SepararNombre(nombreCompleto, out var nombreSeparado, out var apellidoSeparado);
            nombre = nombreSeparado;
            apellido = apellidoSeparado;
        }

        return Ejecutar(
            @"UPDATE dbo.Contactos
              SET Nombre = COALESCE(@Nombre, Nombre),
                  Apellido = CASE WHEN @ActualizarNombre = 1 THEN @Apellido ELSE Apellido END,
                  Cedula = COALESCE(@Cedula, Cedula),
                  Ciudad = COALESCE(@Ciudad, Ciudad),
                  Barrio = COALESCE(@Barrio, Barrio),
                  Direccion = COALESCE(@Direccion, Direccion)
              WHERE Id = @IdContacto;",
            new
            {
                IdContacto = idContacto,
                Nombre = nombre,
                Apellido = apellido,
                ActualizarNombre = !string.IsNullOrWhiteSpace(nombreCompleto),
                Cedula = string.IsNullOrWhiteSpace(numeroCedula) ? null : numeroCedula.Trim(),
                Ciudad = string.IsNullOrWhiteSpace(ciudad) ? null : ciudad.Trim(),
                Barrio = string.IsNullOrWhiteSpace(barrio) ? null : barrio.Trim(),
                Direccion = string.IsNullOrWhiteSpace(direccion) ? null : direccion.Trim()
            },
            "guardando datos parciales del contacto");
    }

    public async Task GuardarDatosLaboralesParciales(
        int idSolicitudCredito,
        string? empresa = null,
        int? antiguedadMeses = null,
        bool? aportaIps = null,
        int? cantidadAportesIps = null,
        string? direccionEmpresa = null,
        string? telefonoEmpresa = null,
        bool? telefonoEsMovil = null,
        string? nombreJefeEncargado = null)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
                IF EXISTS
                (
                    SELECT 1
                    FROM dbo.SolicitudDatosLaborales
                    WHERE IdSolicitud = @IdSolicitud
                )
                BEGIN
                    UPDATE dbo.SolicitudDatosLaborales
                    SET Empresa = COALESCE(@Empresa, Empresa),
                        AntiguedadMeses = COALESCE(@AntiguedadMeses, AntiguedadMeses),
                        AportaIPS = COALESCE(@AportaIPS, AportaIPS),
                        CantidadAportesIPS = COALESCE(@CantidadAportesIPS, CantidadAportesIPS),
                        DireccionEmpresa = COALESCE(@DireccionEmpresa, DireccionEmpresa),
                        TelefonoEmpresa = COALESCE(@TelefonoEmpresa, TelefonoEmpresa),
                        TelefonoEmpresaEsMovil = COALESCE(@TelefonoEmpresaEsMovil, TelefonoEmpresaEsMovil),
                        NombreJefeEncargado = COALESCE(@NombreJefeEncargado, NombreJefeEncargado)
                    WHERE IdSolicitud = @IdSolicitud;
                END
                ELSE
                BEGIN
                    INSERT INTO dbo.SolicitudDatosLaborales
                    (
                        IdSolicitud,
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
                    )
                    VALUES
                    (
                        @IdSolicitud,
                        COALESCE(@Empresa, N''),
                        COALESCE(@AntiguedadMeses, 0),
                        COALESCE(@AportaIPS, 0),
                        COALESCE(@CantidadAportesIPS, 0),
                        N'',
                        0,
                        N'',
                        @DireccionEmpresa,
                        @TelefonoEmpresa,
                        @TelefonoEmpresaEsMovil,
                        @NombreJefeEncargado
                    );
                END;";

            await conn.ExecuteAsync(
                sql,
                new
                {
                    IdSolicitud = idSolicitudCredito,
                    Empresa = string.IsNullOrWhiteSpace(empresa) ? null : empresa.Trim(),
                    AntiguedadMeses = antiguedadMeses,
                    AportaIPS = aportaIps,
                    CantidadAportesIPS = cantidadAportesIps,
                    DireccionEmpresa = string.IsNullOrWhiteSpace(direccionEmpresa) ? null : direccionEmpresa.Trim(),
                    TelefonoEmpresa = string.IsNullOrWhiteSpace(telefonoEmpresa) ? null : telefonoEmpresa.Trim(),
                    TelefonoEmpresaEsMovil = telefonoEsMovil,
                    NombreJefeEncargado = string.IsNullOrWhiteSpace(nombreJefeEncargado) ? null : nombreJefeEncargado.Trim()
                });
        }
        catch (Exception ex)
        {
            throw Error(ex, "Error guardando datos laborales parciales.");
        }
    }

    public Task GuardarIdentidadContacto(
        int idContacto,
        string nombreCompleto,
        string numeroCedula)
    {
        SepararNombre(nombreCompleto, out var nombre, out var apellido);

        return Ejecutar(
            @"UPDATE dbo.Contactos
              SET Nombre = @Nombre,
                  Apellido = @Apellido,
                  Cedula = @Cedula
              WHERE Id = @IdContacto;",
            new
            {
                IdContacto = idContacto,
                Nombre = nombre,
                Apellido = apellido,
                Cedula = numeroCedula
            },
            "guardando identidad del contacto");
    }

    public Task GuardarDomicilioContacto(
        int idContacto,
        string ciudad,
        string barrio,
        string direccion)
        => Ejecutar(
            @"UPDATE dbo.Contactos
              SET Ciudad = @Ciudad,
                  Barrio = @Barrio,
                  Direccion = @Direccion
              WHERE Id = @IdContacto;",
            new
            {
                IdContacto = idContacto,
                Ciudad = ciudad,
                Barrio = barrio,
                Direccion = direccion
            },
            "guardando domicilio del contacto");

    public async Task GuardarPrecalificacionLaboral(
        int idSolicitudCredito,
        string empresa,
        int antiguedadMeses,
        bool aportaIps,
        int cantidadAportesIps)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
                IF EXISTS
                (
                    SELECT 1
                    FROM dbo.SolicitudDatosLaborales
                    WHERE IdSolicitud = @IdSolicitud
                )
                BEGIN
                    UPDATE dbo.SolicitudDatosLaborales
                    SET Empresa = @Empresa,
                        AntiguedadMeses = @AntiguedadMeses,
                        AportaIPS = @AportaIPS,
                        CantidadAportesIPS = @CantidadAportesIPS
                    WHERE IdSolicitud = @IdSolicitud;
                END
                ELSE
                BEGIN
                    INSERT INTO dbo.SolicitudDatosLaborales
                    (
                        IdSolicitud,
                        Empresa,
                        AntiguedadMeses,
                        AportaIPS,
                        CantidadAportesIPS,
                        Cargo,
                        Salario,
                        TipoPago
                    )
                    VALUES
                    (
                        @IdSolicitud,
                        @Empresa,
                        @AntiguedadMeses,
                        @AportaIPS,
                        @CantidadAportesIPS,
                        NULL,
                        NULL,
                        NULL
                    );
                END;";

            await conn.ExecuteAsync(
                sql,
                new
                {
                    IdSolicitud = idSolicitudCredito,
                    Empresa = empresa,
                    AntiguedadMeses = antiguedadMeses,
                    AportaIPS = aportaIps,
                    CantidadAportesIPS = cantidadAportesIps
                });
        }
        catch (Exception ex)
        {
            throw Error(ex, "Error guardando preevaluación laboral.");
        }
    }

    public Task GuardarDatosLaboralesCompletos(
        int idSolicitudCredito,
        string direccionEmpresa,
        string telefonoEmpresa,
        bool telefonoEsMovil,
        string? nombreJefeEncargado)
        => Ejecutar(
            @"UPDATE dbo.SolicitudDatosLaborales
              SET DireccionEmpresa = @DireccionEmpresa,
                  TelefonoEmpresa = @TelefonoEmpresa,
                  TelefonoEmpresaEsMovil = @TelefonoEmpresaEsMovil,
                  NombreJefeEncargado = @NombreJefeEncargado
              WHERE IdSolicitud = @IdSolicitud;",
            new
            {
                IdSolicitud = idSolicitudCredito,
                DireccionEmpresa = direccionEmpresa,
                TelefonoEmpresa = telefonoEmpresa,
                TelefonoEmpresaEsMovil = telefonoEsMovil,
                NombreJefeEncargado = nombreJefeEncargado
            },
            "guardando datos laborales completos");

    public Task GuardarResultadoPreEvaluacion(
        int idSolicitudCredito,
        string resultado,
        string? motivo,
        string? viaEvaluacion,
        string? siguientePaso)
        => Ejecutar(
            @"UPDATE dbo.SolicitudesCredito
              SET ResultadoPreEvaluacion = @Resultado,
                  MotivoPreEvaluacion = @Motivo,
                  ViaEvaluacion = @ViaEvaluacion,
                  PasoActual = COALESCE(@PasoActual, PasoActual),
                  Estado = CASE
                              WHEN @Resultado = 'NO_VIABLE' THEN 'NO_VIABLE'
                              WHEN @Resultado = 'VIABLE' THEN 'DOCUMENTACION'
                              ELSE 'PRE_EVALUACION'
                           END,
                  FechaActualizacion = GETDATE(),
                  FechaCierre = CASE WHEN @Resultado = 'NO_VIABLE' THEN GETDATE() ELSE FechaCierre END
              WHERE Id = @IdSolicitud;",
            new
            {
                IdSolicitud = idSolicitudCredito,
                Resultado = resultado,
                Motivo = motivo,
                ViaEvaluacion = viaEvaluacion,
                PasoActual = siguientePaso
            },
            "guardando resultado de preevaluación");

    public async Task AgregarReferencia(
        int idSolicitudCredito,
        string tipo,
        string nombre,
        string telefono,
        string? parentesco,
        string? observacion)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
                INSERT INTO dbo.SolicitudReferencias
                (
                    IdSolicitud,
                    Tipo,
                    Nombre,
                    Telefono,
                    Parentesco,
                    Observacion
                )
                VALUES
                (
                    @IdSolicitud,
                    @Tipo,
                    @Nombre,
                    @Telefono,
                    @Parentesco,
                    @Observacion
                );";

            await conn.ExecuteAsync(
                sql,
                new
                {
                    IdSolicitud = idSolicitudCredito,
                    Tipo = tipo,
                    Nombre = nombre,
                    Telefono = telefono,
                    Parentesco = parentesco,
                    Observacion = observacion
                });
        }
        catch (Exception ex)
        {
            throw Error(ex, "Error guardando referencia de crédito.");
        }
    }

    public async Task<IReadOnlyList<SolicitudMotoReferenciaDto>> ObtenerReferencias(
        int idSolicitudCredito)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
                SELECT
                    Id,
                    IdSolicitud,
                    Tipo,
                    Nombre,
                    Telefono,
                    Parentesco,
                    Observacion
                FROM dbo.SolicitudReferencias
                WHERE IdSolicitud = @IdSolicitud
                ORDER BY Id;";

            var data = await conn.QueryAsync<SolicitudMotoReferenciaDto>(
                sql,
                new { IdSolicitud = idSolicitudCredito });

            return data.ToList();
        }
        catch (Exception ex)
        {
            throw Error(ex, "Error obteniendo referencias de crédito.");
        }
    }

    public Task GuardarEstadoCedula(
        string tipoOperacion,
        int idSolicitud,
        string estadoCedula)
    {
        var tabla = EsCredito(tipoOperacion)
            ? "dbo.SolicitudesCredito"
            : "dbo.SolicitudesContadoMoto";

        return Ejecutar(
            $@"UPDATE {tabla}
               SET EstadoCedula = @EstadoCedula,
                   FechaActualizacion = GETDATE()
               WHERE Id = @IdSolicitud;",
            new
            {
                IdSolicitud = idSolicitud,
                EstadoCedula = estadoCedula
            },
            "guardando estado de cédula");
    }

    public async Task GuardarDocumento(
        string tipoOperacion,
        int idSolicitud,
        int idConversacion,
        int? idModeloProducto,
        string tipoDocumento,
        string nombreArchivo,
        string mimeType,
        string rutaPrivada,
        string hashSha256)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
                DELETE FROM dbo.SolicitudDocumentosMoto
                WHERE TipoDocumento = @TipoDocumento
                  AND
                  (
                      (@TipoOperacion = 'CREDITO' AND IdSolicitudCredito = @IdSolicitud)
                      OR
                      (@TipoOperacion = 'CONTADO' AND IdSolicitudContado = @IdSolicitud)
                  );

                INSERT INTO dbo.SolicitudDocumentosMoto
                (
                    IdSolicitudCredito,
                    IdSolicitudContado,
                    IdConversacion,
                    IdModeloProducto,
                    TipoOperacion,
                    TipoDocumento,
                    NombreArchivo,
                    MimeType,
                    RutaPrivada,
                    HashSha256,
                    EstadoRevision,
                    FechaRecepcion
                )
                VALUES
                (
                    CASE WHEN @TipoOperacion = 'CREDITO' THEN @IdSolicitud ELSE NULL END,
                    CASE WHEN @TipoOperacion = 'CONTADO' THEN @IdSolicitud ELSE NULL END,
                    @IdConversacion,
                    @IdModeloProducto,
                    @TipoOperacion,
                    @TipoDocumento,
                    @NombreArchivo,
                    @MimeType,
                    @RutaPrivada,
                    @HashSha256,
                    'PENDIENTE',
                    GETDATE()
                );";

            await conn.ExecuteAsync(
                sql,
                new
                {
                    IdSolicitud = idSolicitud,
                    IdConversacion = idConversacion,
                    IdModeloProducto = idModeloProducto,
                    TipoOperacion = tipoOperacion.ToUpperInvariant(),
                    TipoDocumento = tipoDocumento,
                    NombreArchivo = nombreArchivo,
                    MimeType = mimeType,
                    RutaPrivada = rutaPrivada,
                    HashSha256 = hashSha256
                });
        }
        catch (Exception ex)
        {
            throw Error(ex, "Error guardando documento de solicitud.");
        }
    }

    public async Task<bool> TieneDocumento(
        string tipoOperacion,
        int idSolicitud,
        string tipoDocumento)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
                SELECT CASE WHEN EXISTS
                (
                    SELECT 1
                    FROM dbo.SolicitudDocumentosMoto
                    WHERE TipoDocumento = @TipoDocumento
                      AND
                      (
                          (@TipoOperacion = 'CREDITO' AND IdSolicitudCredito = @IdSolicitud)
                          OR
                          (@TipoOperacion = 'CONTADO' AND IdSolicitudContado = @IdSolicitud)
                      )
                )
                THEN CAST(1 AS BIT)
                ELSE CAST(0 AS BIT)
                END;";

            return await conn.ExecuteScalarAsync<bool>(
                sql,
                new
                {
                    TipoOperacion = tipoOperacion.ToUpperInvariant(),
                    IdSolicitud = idSolicitud,
                    TipoDocumento = tipoDocumento
                });
        }
        catch (Exception ex)
        {
            throw Error(ex, "Error verificando documento de solicitud.");
        }
    }

    public async Task<string?> ObtenerTextoAutorizacion()
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
                SELECT TOP (1) PromptBase
                FROM dbo.PromptsIA
                WHERE Codigo = 'MOTO_CREDITO_AUTORIZACION'
                  AND Activo = 1
                ORDER BY Id DESC;";

            return await conn.QueryFirstOrDefaultAsync<string?>(sql);
        }
        catch (Exception ex)
        {
            throw Error(ex, "Error obteniendo texto de autorización de crédito.");
        }
    }

    public async Task GuardarAutorizacion(
        int idSolicitudCredito,
        string version,
        string textoAutorizacion,
        string mensajeOriginal,
        string nombreCompleto,
        string numeroCedula)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
                DELETE FROM dbo.SolicitudAutorizacionCredito
                WHERE IdSolicitudCredito = @IdSolicitud;

                INSERT INTO dbo.SolicitudAutorizacionCredito
                (
                    IdSolicitudCredito,
                    VersionAutorizacion,
                    TextoAutorizacion,
                    MensajeOriginal,
                    NombreCompleto,
                    NumeroCedula,
                    Canal,
                    FechaAutorizacion
                )
                VALUES
                (
                    @IdSolicitud,
                    @Version,
                    @TextoAutorizacion,
                    @MensajeOriginal,
                    @NombreCompleto,
                    @NumeroCedula,
                    'WHATSAPP',
                    GETDATE()
                );";

            await conn.ExecuteAsync(
                sql,
                new
                {
                    IdSolicitud = idSolicitudCredito,
                    Version = version,
                    TextoAutorizacion = textoAutorizacion,
                    MensajeOriginal = mensajeOriginal,
                    NombreCompleto = nombreCompleto,
                    NumeroCedula = numeroCedula
                });
        }
        catch (Exception ex)
        {
            throw Error(ex, "Error guardando autorización de crédito.");
        }
    }

    public async Task<bool> TieneAutorizacion(int idSolicitudCredito)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
                SELECT CASE WHEN EXISTS
                (
                    SELECT 1
                    FROM dbo.SolicitudAutorizacionCredito
                    WHERE IdSolicitudCredito = @IdSolicitud
                )
                THEN CAST(1 AS BIT)
                ELSE CAST(0 AS BIT)
                END;";

            return await conn.ExecuteScalarAsync<bool>(
                sql,
                new { IdSolicitud = idSolicitudCredito });
        }
        catch (Exception ex)
        {
            throw Error(ex, "Error verificando autorización de crédito.");
        }
    }

    public async Task MarcarListaRevision(
        string tipoOperacion,
        int idSolicitud)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            conn.Open();

            using var transaction = conn.BeginTransaction();

            if (EsCredito(tipoOperacion))
            {
                const string sql = @"
                    UPDATE dbo.SolicitudesCredito
                    SET
                        Estado = 'LISTA_REVISION',
                        PasoActual = 'LISTA_REVISION',
                        FechaActualizacion = GETDATE(),
                        FechaCierre = GETDATE()
                    WHERE Id = @IdSolicitud;

                    IF @@ROWCOUNT = 0
                    BEGIN
                        THROW 50001, 'No se encontró la solicitud de crédito.', 1;
                    END;


                    IF NOT EXISTS
                    (
                        SELECT 1
                        FROM dbo.SolicitudCreditoMotoGestion
                        WHERE IdSolicitudCredito = @IdSolicitud
                    )
                    BEGIN
                        INSERT INTO dbo.SolicitudCreditoMotoGestion
                        (
                            IdSolicitudCredito,
                            EstadoControl,
                            IdUsuarioAsignado,
                            ObservacionInterna,
                            FechaRecepcion,
                            FechaUltimaGestion,
                            FechaCierreControl
                        )
                        VALUES
                        (
                            @IdSolicitud,
                            'PENDIENTE_ENVIO',
                            NULL,
                            N'Solicitud recibida automáticamente desde el flujo de crédito.',
                            GETDATE(),
                            NULL,
                            NULL
                        );
                    END;


                    IF NOT EXISTS
                    (
                        SELECT 1
                        FROM dbo.SolicitudCreditoMotoGestionHistorial
                        WHERE IdSolicitudCredito = @IdSolicitud
                          AND Accion = 'RECEPCION'
                    )
                    BEGIN
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
                            'RECEPCION',
                            NULL,
                            'PENDIENTE_ENVIO',
                            N'Solicitud recibida automáticamente en la bandeja interna de TuVendedor.',
                            NULL,
                            GETDATE()
                        );
                    END;
                    ";

                    await conn.ExecuteAsync(
                        sql,
                        new
                        {
                            IdSolicitud = idSolicitud
                        },
                        transaction);

                    transaction.Commit();
                    return;
            }

            const string sqlContado = @"
                UPDATE dbo.SolicitudesContadoMoto

                SET
                    Estado = 'LISTA_REVISION',

                    PasoActual = 'LISTA_REVISION',

                    FechaActualizacion = GETDATE(),

                    FechaCierre = GETDATE()

                WHERE Id = @IdSolicitud;


                IF @@ROWCOUNT = 0
                BEGIN

                    THROW 50002,
                    'No se encontró la solicitud al contado.',
                    1;

                END;


                -- ============================================================
                -- BANDEJA COMERCIAL
                -- ============================================================

                IF NOT EXISTS
                (
                    SELECT 1

                    FROM dbo.SolicitudContadoMotoGestion

                    WHERE IdSolicitudContado =
                          @IdSolicitud
                )
                BEGIN

                    INSERT INTO dbo.SolicitudContadoMotoGestion
                    (
                        IdSolicitudContado,
                        EstadoControl,
                        Prioridad,
                        IdUsuarioAsignado,
                        ObservacionInterna,
                        FechaRecepcion,
                        FechaUltimaGestion,
                        FechaCierreControl
                    )
                    VALUES
                    (
                        @IdSolicitud,

                        'PENDIENTE_CONTACTO',

                        'URGENTE',

                        NULL,

                        N'Compra al contado lista. Contactar al cliente con prioridad.',

                        SYSDATETIME(),

                        NULL,

                        NULL
                    );

                END;


                -- ============================================================
                -- HISTORIAL
                -- ============================================================

                IF NOT EXISTS
                (
                    SELECT 1

                    FROM dbo.SolicitudContadoMotoGestionHistorial

                    WHERE IdSolicitudContado =
                          @IdSolicitud

                      AND Accion =
                          'RECEPCION'
                )
                BEGIN

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
                        @IdSolicitud,

                        'RECEPCION',

                        NULL,

                        'PENDIENTE_CONTACTO',

                        N'Compra al contado recibida. Requiere contacto comercial inmediato.',

                        NULL,

                        SYSDATETIME()
                    );

                END;


                -- ============================================================
                -- PANAMBI ENTREGA LA CONVERSACION AL HUMANO
                -- ============================================================

                UPDATE conv

                SET
                    conv.Modo =
                        'HUMANO'

                FROM dbo.Conversaciones conv

                INNER JOIN dbo.SolicitudesContadoMoto sc
                    ON sc.IdConversacion =
                       conv.Id

                WHERE sc.Id =
                      @IdSolicitud;
                ";


            await conn.ExecuteAsync(
                sqlContado,
                new
                {
                    IdSolicitud =
                        idSolicitud
                },
                transaction);

            transaction.Commit();
        }
        catch (Exception ex)
        {
            throw Error(
                ex,
                "Error marcando solicitud lista para revisión.");
        }
    }

    public Task Cancelar(
        string tipoOperacion,
        int idSolicitud,
        string motivo)
    {
        var tabla = EsCredito(tipoOperacion)
            ? "dbo.SolicitudesCredito"
            : "dbo.SolicitudesContadoMoto";

        return Ejecutar(
            $@"UPDATE {tabla}
               SET Estado = 'CANCELADA',
                   PasoActual = 'CANCELADA',
                   Observacion = @Motivo,
                   FechaActualizacion = GETDATE(),
                   FechaCierre = GETDATE()
               WHERE Id = @IdSolicitud;",
            new
            {
                IdSolicitud = idSolicitud,
                Motivo = motivo
            },
            "cancelando solicitud de moto");
    }

    private Task EjecutarSolicitudUpdate(
        string tipoOperacion,
        int idSolicitud,
        string pasoActual,
        string? estado,
        string? observacion,
        DateTime? fechaCierre)
    {
        var tabla = EsCredito(tipoOperacion)
            ? "dbo.SolicitudesCredito"
            : "dbo.SolicitudesContadoMoto";

        return Ejecutar(
            $@"UPDATE {tabla}
               SET PasoActual = @PasoActual,
                   Estado = COALESCE(@Estado, Estado),
                   Observacion = COALESCE(@Observacion, Observacion),
                   FechaActualizacion = GETDATE(),
                   FechaCierre = COALESCE(@FechaCierre, FechaCierre)
               WHERE Id = @IdSolicitud;",
            new
            {
                IdSolicitud = idSolicitud,
                PasoActual = pasoActual,
                Estado = estado,
                Observacion = observacion,
                FechaCierre = fechaCierre
            },
            "actualizando paso de solicitud");
    }

    private async Task Ejecutar(
        string sql,
        object parametros,
        string operacion)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            await conn.ExecuteAsync(sql, parametros);
        }
        catch (Exception ex)
        {
            throw Error(ex, $"Error {operacion}.");
        }
    }

    private RepositoryException Error(
        Exception ex,
        string mensaje,
        params object[] args)
    {
        _logger.LogError(ex, mensaje, args);
        return new RepositoryException(mensaje, ex);
    }

    private static bool EsCredito(string tipoOperacion)
        => string.Equals(
            tipoOperacion,
            "CREDITO",
            StringComparison.OrdinalIgnoreCase);

    private static string SoloDigitos(string texto)
        => new(texto.Where(char.IsDigit).ToArray());

    private static void SepararNombre(
        string nombreCompleto,
        out string nombre,
        out string? apellido)
    {
        var partes = nombreCompleto
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (partes.Length <= 1)
        {
            nombre = nombreCompleto.Trim();
            apellido = null;
            return;
        }

        apellido = partes[^1];
        nombre = string.Join(' ', partes.Take(partes.Length - 1));
    }
}
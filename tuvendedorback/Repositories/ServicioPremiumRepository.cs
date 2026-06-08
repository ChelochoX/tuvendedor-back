using Dapper;
using tuvendedorback.Data;
using tuvendedorback.DTOs;
using tuvendedorback.Exceptions;
using tuvendedorback.Repositories.Interfaces;
using tuvendedorback.Request;
using tuvendedorback.Wrappers;
using static tuvendedorback.Common.ServiciosPremiumConstants;

namespace tuvendedorback.Repositories;

public class ServicioPremiumRepository :
    IServicioPremiumRepository
{
    private readonly DbConnections _conexion;

    private readonly ILogger<ServicioPremiumRepository>
        _logger;

    public ServicioPremiumRepository(
        DbConnections conexion,
        ILogger<ServicioPremiumRepository> logger)
    {
        _conexion = conexion;
        _logger = logger;
    }

    public async Task<int?> ObtenerIdVendedorPorUsuario(
        int idUsuario)
    {
        using var conn =
            _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
                SELECT TOP 1
                    Id
                FROM dbo.Vendedores
                WHERE IdUsuario = @IdUsuario;";

            return await conn
                .QueryFirstOrDefaultAsync<int?>(
                    sql,
                    new
                    {
                        IdUsuario =
                            idUsuario
                    });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al obtener vendedor por usuario. IdUsuario={IdUsuario}",
                idUsuario);

            throw new RepositoryException(
                "Error al obtener el perfil vendedor.",
                ex);
        }
    }

    public async Task<bool> EsAdministrador(
        int idUsuario)
    {
        using var conn =
            _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
                SELECT COUNT(1)
                FROM dbo.UsuarioRoles ur
                INNER JOIN dbo.Roles r
                    ON r.Id = ur.IdRol
                WHERE ur.IdUsuario = @IdUsuario
                  AND r.NombreRol = 'Administrador';";

            var cantidad =
                await conn.ExecuteScalarAsync<int>(
                    sql,
                    new
                    {
                        IdUsuario =
                            idUsuario
                    });

            return cantidad > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al validar administrador. IdUsuario={IdUsuario}",
                idUsuario);

            throw new RepositoryException(
                "Error al validar el rol administrador.",
                ex);
        }
    }

    public async Task<bool> EsPublicacionDelVendedor(
        int idPublicacion,
        int idVendedor)
    {
        using var conn =
            _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
                SELECT COUNT(1)
                FROM dbo.Publicaciones p
                INNER JOIN dbo.Vendedores v
                    ON v.IdUsuario = p.IdUsuario
                WHERE p.Id = @IdPublicacion
                  AND v.Id = @IdVendedor;";

            var cantidad =
                await conn.ExecuteScalarAsync<int>(
                    sql,
                    new
                    {
                        IdPublicacion =
                            idPublicacion,

                        IdVendedor =
                            idVendedor
                    });

            return cantidad > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al validar propiedad de publicación. IdPublicacion={IdPublicacion}, IdVendedor={IdVendedor}",
                idPublicacion,
                idVendedor);

            throw new RepositoryException(
                "Error al validar la publicación del vendedor.",
                ex);
        }
    }

    public async Task<bool> ExisteSolicitudPendiente(
        int idVendedor,
        string tipoServicio,
        int? idPublicacion)
    {
        using var conn =
            _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
                SELECT COUNT(1)
                FROM dbo.ServiciosPremium
                WHERE IdVendedor = @IdVendedor
                  AND TipoServicio = @TipoServicio
                  AND Estado IN
                  (
                      'SOLICITADO',
                      'PENDIENTE_PAGO'
                  )
                  AND
                  (
                      (
                          @IdPublicacion IS NULL
                          AND IdPublicacion IS NULL
                      )
                      OR
                      IdPublicacion =
                          @IdPublicacion
                  );";

            var cantidad =
                await conn.ExecuteScalarAsync<int>(
                    sql,
                    new
                    {
                        IdVendedor =
                            idVendedor,

                        TipoServicio =
                            tipoServicio,

                        IdPublicacion =
                            idPublicacion
                    });

            return cantidad > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al validar solicitud pendiente. IdVendedor={IdVendedor}, TipoServicio={TipoServicio}",
                idVendedor,
                tipoServicio);

            throw new RepositoryException(
                "Error al validar solicitudes Premium pendientes.",
                ex);
        }
    }

    public async Task<int> CrearSolicitud(
        int idVendedor,
        CrearSolicitudServicioPremiumRequest request)
    {
        using var conn =
            _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
                INSERT INTO dbo.ServiciosPremium
                (
                    IdVendedor,
                    IdPublicacion,
                    TipoServicio,
                    Estado,
                    FechaSolicitud,
                    Observacion
                )
                VALUES
                (
                    @IdVendedor,
                    @IdPublicacion,
                    @TipoServicio,
                    'SOLICITADO',
                    GETDATE(),
                    @Observacion
                );

                SELECT CAST(
                    SCOPE_IDENTITY()
                    AS INT
                );";

            var id =
                await conn.ExecuteScalarAsync<int>(
                    sql,
                    new
                    {
                        IdVendedor =
                            idVendedor,

                        request
                            .IdPublicacion,

                        request
                            .TipoServicio,

                        request
                            .Observacion
                    });

            _logger.LogInformation(
                "Solicitud Premium registrada. IdServicio={IdServicio}, IdVendedor={IdVendedor}, TipoServicio={TipoServicio}",
                id,
                idVendedor,
                request.TipoServicio);

            return id;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al registrar solicitud Premium. IdVendedor={IdVendedor}, TipoServicio={TipoServicio}",
                idVendedor,
                request.TipoServicio);

            throw new RepositoryException(
                "Error al registrar la solicitud Premium.",
                ex);
        }
    }

    public async Task<
        Datos<List<ServicioPremiumDto>>
    > ObtenerServicios(
        FiltrosServiciosPremiumRequest filtros)
    {
        using var conn =
            _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
                SELECT COUNT(1)
                FROM dbo.ServiciosPremium sp
                INNER JOIN dbo.Vendedores v
                    ON v.Id = sp.IdVendedor
                INNER JOIN dbo.Usuarios uv
                    ON uv.Id = v.IdUsuario
                WHERE
                    (
                        @Estado IS NULL
                        OR sp.Estado =
                            @Estado
                    )
                    AND
                    (
                        @TipoServicio IS NULL
                        OR sp.TipoServicio =
                            @TipoServicio
                    )
                    AND
                    (
                        @Cliente IS NULL
                        OR v.NombreNegocio LIKE
                            '%' + @Cliente + '%'
                        OR uv.NombreUsuario LIKE
                            '%' + @Cliente + '%'
                    )
                    AND
                    (
                        @FechaDesde IS NULL
                        OR sp.FechaSolicitud >=
                            @FechaDesde
                    )
                    AND
                    (
                        @FechaHasta IS NULL
                        OR sp.FechaSolicitud <
                            DATEADD(
                                DAY,
                                1,
                                @FechaHasta
                            )
                    );

                SELECT
                    sp.Id
                        AS Id,

                    sp.IdVendedor
                        AS IdVendedor,

                    v.IdUsuario
                        AS IdUsuarioVendedor,

                    v.NombreNegocio
                        AS NombreNegocio,

                    uv.NombreUsuario
                        AS NombreUsuarioVendedor,

                    sp.IdPublicacion
                        AS IdPublicacion,

                    p.Titulo
                        AS TituloPublicacion,

                    sp.IdTemporada
                        AS IdTemporada,

                    t.Nombre
                        AS NombreTemporada,

                    sp.TipoServicio
                        AS TipoServicio,

                    sp.Estado
                        AS Estado,

                    sp.FechaSolicitud
                        AS FechaSolicitud,

                    sp.FechaInicio
                        AS FechaInicio,

                    sp.FechaFin
                        AS FechaFin,

                    sp.FechaPago
                        AS FechaPago,

                    sp.Monto
                        AS Monto,

                    sp.MedioPago
                        AS MedioPago,

                    sp.ReferenciaPago
                        AS ReferenciaPago,

                    sp.Observacion
                        AS Observacion,

                    sp.IdUsuarioAdmin
                        AS IdUsuarioAdmin,

                    ua.NombreUsuario
                        AS NombreUsuarioAdmin

                FROM dbo.ServiciosPremium sp
                INNER JOIN dbo.Vendedores v
                    ON v.Id = sp.IdVendedor
                INNER JOIN dbo.Usuarios uv
                    ON uv.Id = v.IdUsuario
                LEFT JOIN dbo.Publicaciones p
                    ON p.Id =
                        sp.IdPublicacion
                LEFT JOIN dbo.Temporadas t
                    ON t.Id =
                        sp.IdTemporada
                LEFT JOIN dbo.Usuarios ua
                    ON ua.Id =
                        sp.IdUsuarioAdmin

                WHERE
                    (
                        @Estado IS NULL
                        OR sp.Estado =
                            @Estado
                    )
                    AND
                    (
                        @TipoServicio IS NULL
                        OR sp.TipoServicio =
                            @TipoServicio
                    )
                    AND
                    (
                        @Cliente IS NULL
                        OR v.NombreNegocio LIKE
                            '%' + @Cliente + '%'
                        OR uv.NombreUsuario LIKE
                            '%' + @Cliente + '%'
                    )
                    AND
                    (
                        @FechaDesde IS NULL
                        OR sp.FechaSolicitud >=
                            @FechaDesde
                    )
                    AND
                    (
                        @FechaHasta IS NULL
                        OR sp.FechaSolicitud <
                            DATEADD(
                                DAY,
                                1,
                                @FechaHasta
                            )
                    )

                ORDER BY
                    sp.FechaSolicitud DESC,
                    sp.Id DESC

                OFFSET @Offset ROWS

                FETCH NEXT
                    @TamanioPagina
                ROWS ONLY;";

            var parametros =
                new
                {
                    Estado =
                        string.IsNullOrWhiteSpace(
                            filtros.Estado)
                            ? null
                            : filtros.Estado,

                    TipoServicio =
                        string.IsNullOrWhiteSpace(
                            filtros.TipoServicio)
                            ? null
                            : filtros.TipoServicio,

                    Cliente =
                        string.IsNullOrWhiteSpace(
                            filtros.Cliente)
                            ? null
                            : filtros
                                .Cliente
                                .Trim(),

                    filtros
                        .FechaDesde,

                    filtros
                        .FechaHasta,

                    Offset =
                        (
                            filtros.Pagina -
                            1
                        )
                        *
                        filtros
                            .TamanioPagina,

                    filtros
                        .TamanioPagina
                };

            using var multi =
                await conn.QueryMultipleAsync(
                    sql,
                    parametros);

            var totalRegistros =
                await multi.ReadSingleAsync<int>();

            var items =
                (
                    await multi
                        .ReadAsync<
                            ServicioPremiumDto
                        >()
                )
                .ToList();

            return new Datos<
                List<ServicioPremiumDto>
            >
            {
                Items =
                    items,

                TotalRegistros =
                    totalRegistros
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al obtener servicios Premium paginados.");

            throw new RepositoryException(
                "Error al obtener los servicios Premium.",
                ex);
        }
    }

    public async Task<ServicioPremiumDto?>
        ObtenerServicioPorId(
            int idServicio)
    {
        using var conn =
            _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
                SELECT
                    sp.Id
                        AS Id,

                    sp.IdVendedor
                        AS IdVendedor,

                    v.IdUsuario
                        AS IdUsuarioVendedor,

                    v.NombreNegocio
                        AS NombreNegocio,

                    uv.NombreUsuario
                        AS NombreUsuarioVendedor,

                    sp.IdPublicacion
                        AS IdPublicacion,

                    p.Titulo
                        AS TituloPublicacion,

                    sp.IdTemporada
                        AS IdTemporada,

                    t.Nombre
                        AS NombreTemporada,

                    sp.TipoServicio
                        AS TipoServicio,

                    sp.Estado
                        AS Estado,

                    sp.FechaSolicitud
                        AS FechaSolicitud,

                    sp.FechaInicio
                        AS FechaInicio,

                    sp.FechaFin
                        AS FechaFin,

                    sp.FechaPago
                        AS FechaPago,

                    sp.Monto
                        AS Monto,

                    sp.MedioPago
                        AS MedioPago,

                    sp.ReferenciaPago
                        AS ReferenciaPago,

                    sp.Observacion
                        AS Observacion,

                    sp.IdUsuarioAdmin
                        AS IdUsuarioAdmin,

                    ua.NombreUsuario
                        AS NombreUsuarioAdmin

                FROM dbo.ServiciosPremium sp
                INNER JOIN dbo.Vendedores v
                    ON v.Id =
                        sp.IdVendedor
                INNER JOIN dbo.Usuarios uv
                    ON uv.Id =
                        v.IdUsuario
                LEFT JOIN dbo.Publicaciones p
                    ON p.Id =
                        sp.IdPublicacion
                LEFT JOIN dbo.Temporadas t
                    ON t.Id =
                        sp.IdTemporada
                LEFT JOIN dbo.Usuarios ua
                    ON ua.Id =
                        sp.IdUsuarioAdmin

                WHERE sp.Id =
                    @IdServicio;";

            return await conn
                .QueryFirstOrDefaultAsync<
                    ServicioPremiumDto
                >(
                    sql,
                    new
                    {
                        IdServicio =
                            idServicio
                    });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al obtener servicio Premium. IdServicio={IdServicio}",
                idServicio);

            throw new RepositoryException(
                "Error al obtener el servicio Premium.",
                ex);
        }
    }

    public async Task<
        ResumenServiciosPremiumDto
    > ObtenerResumen()
    {
        using var conn =
            _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
                SELECT
                    CAST(
                        COALESCE(
                            SUM(
                                CASE
                                    WHEN Estado IN
                                    (
                                        'SOLICITADO',
                                        'PENDIENTE_PAGO'
                                    )
                                    THEN 1
                                    ELSE 0
                                END
                            ),
                            0
                        )
                        AS INT
                    )
                    AS SolicitudesPendientes,

                    CAST(
                        COALESCE(
                            SUM(
                                CASE
                                    WHEN Estado =
                                        'ACTIVO'
                                    THEN 1
                                    ELSE 0
                                END
                            ),
                            0
                        )
                        AS INT
                    )
                    AS ServiciosActivos,

                    CAST(
                        COALESCE(
                            SUM(
                                CASE
                                    WHEN Estado =
                                        'ACTIVO'
                                     AND FechaFin >=
                                        GETDATE()
                                     AND FechaFin <=
                                        DATEADD(
                                            DAY,
                                            7,
                                            GETDATE()
                                        )
                                    THEN 1
                                    ELSE 0
                                END
                            ),
                            0
                        )
                        AS INT
                    )
                    AS ProximosAVencer,

                    CAST(
                        COALESCE(
                            SUM(
                                CASE
                                    WHEN FechaPago
                                        IS NOT NULL
                                    THEN COALESCE(
                                        Monto,
                                        0
                                    )
                                    ELSE 0
                                END
                            ),
                            0
                        )
                        AS DECIMAL(18, 2)
                    )
                    AS MontoCobrado

                FROM dbo.ServiciosPremium;";

            return await conn
                .QuerySingleAsync<
                    ResumenServiciosPremiumDto
                >(sql);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al obtener resumen de servicios Premium.");

            throw new RepositoryException(
                "Error al obtener el resumen de servicios Premium.",
                ex);
        }
    }

    public async Task ActivarServicio(
        int idServicio,
        ActivarServicioPremiumRequest request,
        int idUsuarioAdmin)
    {
        using var conn =
            _conexion.CreateSqlConnection();

        conn.Open();

        using var tran =
            conn.BeginTransaction();

        try
        {
            const string servicioSql = @"
                SELECT
                    Id,
                    IdVendedor,
                    IdPublicacion,
                    IdTemporada,
                    TipoServicio,
                    Estado
                FROM dbo.ServiciosPremium
                WHERE Id =
                    @IdServicio;";

            var servicio =
                await conn
                    .QueryFirstOrDefaultAsync<
                        ServicioPremiumDto
                    >(
                        servicioSql,
                        new
                        {
                            IdServicio =
                                idServicio
                        },
                        tran);

            if (servicio == null)
            {
                throw new RepositoryException(
                    "No se encontró el servicio Premium.");
            }

            DateTime fechaInicio;

            DateTime fechaFin;

            int? idTemporadaEfectiva =
                request.IdTemporada;

            switch (
                servicio.TipoServicio)
            {
                case TiposServicioPremium
                    .VitrinaProfesional:
                    {
                        ValidarFechas(
                            request);

                        fechaInicio =
                            request
                                .FechaInicio!
                                .Value;

                        fechaFin =
                            request
                                .FechaFin!
                                .Value;

                        const string sql = @"
                        UPDATE dbo.Vendedores
                        SET EsPremium = 1
                        WHERE Id =
                            @IdVendedor;";

                        await conn.ExecuteAsync(
                            sql,
                            new
                            {
                                servicio
                                    .IdVendedor
                            },
                            tran);

                        break;
                    }

                case TiposServicioPremium
                    .PublicacionDestacada:
                    {
                        ValidarFechas(
                            request);

                        if (
                            !servicio
                                .IdPublicacion
                                .HasValue)
                        {
                            throw new RepositoryException(
                                "El servicio no tiene una publicación asociada.");
                        }

                        fechaInicio =
                            request
                                .FechaInicio!
                                .Value;

                        fechaFin =
                            request
                                .FechaFin!
                                .Value;

                        await conn.ExecuteAsync(
                            @"
                            UPDATE dbo.PublicacionesDestacadas
                            SET Estado =
                                'Inactivo'
                            WHERE IdPublicacion =
                                @IdPublicacion
                              AND Estado =
                                'Activo';",
                            new
                            {
                                servicio
                                    .IdPublicacion
                            },
                            tran);

                        await conn.ExecuteAsync(
                            @"
                            INSERT INTO dbo.PublicacionesDestacadas
                            (
                                IdPublicacion,
                                FechaInicio,
                                FechaFin,
                                Estado
                            )
                            VALUES
                            (
                                @IdPublicacion,
                                @FechaInicio,
                                @FechaFin,
                                'Activo'
                            );",
                            new
                            {
                                servicio
                                    .IdPublicacion,

                                FechaInicio =
                                    fechaInicio,

                                FechaFin =
                                    fechaFin
                            },
                            tran);

                        break;
                    }

                case TiposServicioPremium
                    .PublicacionEspecial:
                    {
                        if (
                            !servicio
                                .IdPublicacion
                                .HasValue)
                        {
                            throw new RepositoryException(
                                "El servicio no tiene una publicación asociada.");
                        }

                        if (
                            !request
                                .IdTemporada
                                .HasValue)
                        {
                            throw new RepositoryException(
                                "Debe seleccionar una temporada.");
                        }

                        const string temporadaSql = @"
                        SELECT TOP 1
                            Id,
                            FechaInicio,
                            FechaFin,
                            BadgeTexto,
                            BadgeColor
                        FROM dbo.Temporadas
                        WHERE Id =
                            @IdTemporada
                          AND Estado =
                            'Activo'
                          AND GETDATE()
                              BETWEEN
                                  FechaInicio
                                  AND FechaFin;";

                        var temporada =
                            await conn
                                .QueryFirstOrDefaultAsync<
                                    TemporadaActivacionDto
                                >(
                                    temporadaSql,
                                    new
                                    {
                                        request
                                            .IdTemporada
                                    },
                                    tran);

                        if (temporada == null)
                        {
                            throw new RepositoryException(
                                "La temporada seleccionada no existe o no se encuentra vigente.");
                        }

                        fechaInicio =
                            temporada.FechaInicio;

                        fechaFin =
                            temporada.FechaFin;

                        idTemporadaEfectiva =
                            temporada.Id;

                        await conn.ExecuteAsync(
                            @"
                            UPDATE dbo.PublicacionesTemporada
                            SET Estado =
                                'Inactivo'
                            WHERE IdPublicacion =
                                @IdPublicacion
                              AND Estado =
                                'Activo';",
                            new
                            {
                                servicio
                                    .IdPublicacion
                            },
                            tran);

                        await conn.ExecuteAsync(
                            @"
                            INSERT INTO dbo.PublicacionesTemporada
                            (
                                IdPublicacion,
                                IdTemporada,
                                FechaInicio,
                                FechaFin,
                                BadgeTexto,
                                BadgeColor,
                                Estado
                            )
                            VALUES
                            (
                                @IdPublicacion,
                                @IdTemporada,
                                @FechaInicio,
                                @FechaFin,
                                @BadgeTexto,
                                @BadgeColor,
                                'Activo'
                            );",
                            new
                            {
                                servicio
                                    .IdPublicacion,

                                IdTemporada =
                                    temporada.Id,

                                temporada
                                    .FechaInicio,

                                temporada
                                    .FechaFin,

                                temporada
                                    .BadgeTexto,

                                temporada
                                    .BadgeColor
                            },
                            tran);

                        break;
                    }

                default:
                    {
                        throw new RepositoryException(
                            "El tipo de servicio Premium no está soportado.");
                    }
            }

            const string actualizarServicioSql = @"
                UPDATE dbo.ServiciosPremium
                SET
                    Estado =
                        'ACTIVO',

                    FechaInicio =
                        @FechaInicio,

                    FechaFin =
                        @FechaFin,

                    FechaPago =
                        GETDATE(),

                    IdTemporada =
                        @IdTemporada,

                    Monto =
                        @Monto,

                    MedioPago =
                        @MedioPago,

                    ReferenciaPago =
                        @ReferenciaPago,

                    Observacion =
                        COALESCE(
                            NULLIF(
                                @Observacion,
                                ''
                            ),
                            Observacion
                        ),

                    IdUsuarioAdmin =
                        @IdUsuarioAdmin,

                    FechaActualizacion =
                        GETDATE()

                WHERE Id =
                    @IdServicio;";

            await conn.ExecuteAsync(
                actualizarServicioSql,
                new
                {
                    IdServicio =
                        idServicio,

                    FechaInicio =
                        fechaInicio,

                    FechaFin =
                        fechaFin,

                    IdTemporada =
                        idTemporadaEfectiva,

                    request
                        .Monto,

                    request
                        .MedioPago,

                    request
                        .ReferenciaPago,

                    request
                        .Observacion,

                    IdUsuarioAdmin =
                        idUsuarioAdmin
                },
                tran);

            tran.Commit();

            _logger.LogInformation(
                "Servicio Premium activado. IdServicio={IdServicio}, IdUsuarioAdmin={IdUsuarioAdmin}",
                idServicio,
                idUsuarioAdmin);
        }
        catch (Exception ex)
        {
            tran.Rollback();

            _logger.LogError(
                ex,
                "Error al activar servicio Premium. IdServicio={IdServicio}",
                idServicio);

            throw new RepositoryException(
                "Error al activar el servicio Premium.",
                ex);
        }
    }

    public async Task CancelarServicio(
        int idServicio,
        string? observacion,
        int idUsuarioAdmin)
    {
        using var conn =
            _conexion.CreateSqlConnection();

        conn.Open();

        using var tran =
            conn.BeginTransaction();

        try
        {
            const string servicioSql = @"
                SELECT
                    Id,
                    IdVendedor,
                    IdPublicacion,
                    IdTemporada,
                    TipoServicio,
                    Estado
                FROM dbo.ServiciosPremium
                WHERE Id =
                    @IdServicio;";

            var servicio =
                await conn
                    .QueryFirstOrDefaultAsync<
                        ServicioPremiumDto
                    >(
                        servicioSql,
                        new
                        {
                            IdServicio =
                                idServicio
                        },
                        tran);

            if (servicio == null)
            {
                throw new RepositoryException(
                    "No se encontró el servicio Premium.");
            }

            await conn.ExecuteAsync(
                @"
                    UPDATE dbo.ServiciosPremium
                    SET
                        Estado =
                            'CANCELADO',

                        Observacion =
                            COALESCE(
                                NULLIF(
                                    @Observacion,
                                    ''
                                ),
                                Observacion
                            ),

                        IdUsuarioAdmin =
                            @IdUsuarioAdmin,

                        FechaActualizacion =
                            GETDATE()

                    WHERE Id =
                        @IdServicio;",
                new
                {
                    IdServicio =
                        idServicio,

                    Observacion =
                        observacion,

                    IdUsuarioAdmin =
                        idUsuarioAdmin
                },
                tran);

            switch (
                servicio.TipoServicio)
            {
                case TiposServicioPremium
                    .VitrinaProfesional:
                    {
                        var otrasVitrinasActivas =
                            await conn.ExecuteScalarAsync<int>(
                                @"
                                SELECT COUNT(1)
                                FROM dbo.ServiciosPremium
                                WHERE IdVendedor =
                                    @IdVendedor
                                  AND TipoServicio =
                                    'VITRINA_PROFESIONAL'
                                  AND Estado =
                                    'ACTIVO'
                                  AND Id <>
                                    @IdServicio
                                  AND GETDATE()
                                      BETWEEN
                                          FechaInicio
                                          AND FechaFin;",
                                new
                                {
                                    servicio
                                        .IdVendedor,

                                    IdServicio =
                                        idServicio
                                },
                                tran);

                        if (
                            otrasVitrinasActivas ==
                            0)
                        {
                            await conn.ExecuteAsync(
                                @"
                                UPDATE dbo.Vendedores
                                SET
                                    EsPremium = 0,
                                    EsPerfilPublico = 0
                                WHERE Id =
                                    @IdVendedor;",
                                new
                                {
                                    servicio
                                        .IdVendedor
                                },
                                tran);
                        }

                        break;
                    }

                case TiposServicioPremium
                    .PublicacionDestacada:
                    {
                        if (
                            servicio
                                .IdPublicacion
                                .HasValue)
                        {
                            var otrosActivos =
                                await conn.ExecuteScalarAsync<int>(
                                    @"
                                    SELECT COUNT(1)
                                    FROM dbo.ServiciosPremium
                                    WHERE IdPublicacion =
                                        @IdPublicacion
                                      AND TipoServicio =
                                        'PUBLICACION_DESTACADA'
                                      AND Estado =
                                        'ACTIVO'
                                      AND Id <>
                                        @IdServicio
                                      AND GETDATE()
                                          BETWEEN
                                              FechaInicio
                                              AND FechaFin;",
                                    new
                                    {
                                        servicio
                                            .IdPublicacion,

                                        IdServicio =
                                            idServicio
                                    },
                                    tran);

                            if (
                                otrosActivos ==
                                0)
                            {
                                await conn.ExecuteAsync(
                                    @"
                                    UPDATE dbo.PublicacionesDestacadas
                                    SET Estado =
                                        'Inactivo'
                                    WHERE IdPublicacion =
                                        @IdPublicacion
                                      AND Estado =
                                        'Activo';",
                                    new
                                    {
                                        servicio
                                            .IdPublicacion
                                    },
                                    tran);
                            }
                        }

                        break;
                    }

                case TiposServicioPremium
                    .PublicacionEspecial:
                    {
                        if (
                            servicio
                                .IdPublicacion
                                .HasValue)
                        {
                            var otrosActivos =
                                await conn.ExecuteScalarAsync<int>(
                                    @"
                                    SELECT COUNT(1)
                                    FROM dbo.ServiciosPremium
                                    WHERE IdPublicacion =
                                        @IdPublicacion
                                      AND TipoServicio =
                                        'PUBLICACION_ESPECIAL'
                                      AND Estado =
                                        'ACTIVO'
                                      AND Id <>
                                        @IdServicio
                                      AND GETDATE()
                                          BETWEEN
                                              FechaInicio
                                              AND FechaFin;",
                                    new
                                    {
                                        servicio
                                            .IdPublicacion,

                                        IdServicio =
                                            idServicio
                                    },
                                    tran);

                            if (
                                otrosActivos ==
                                0)
                            {
                                await conn.ExecuteAsync(
                                    @"
                                    UPDATE dbo.PublicacionesTemporada
                                    SET Estado =
                                        'Inactivo'
                                    WHERE IdPublicacion =
                                        @IdPublicacion
                                      AND Estado =
                                        'Activo';",
                                    new
                                    {
                                        servicio
                                            .IdPublicacion
                                    },
                                    tran);
                            }
                        }

                        break;
                    }
            }

            tran.Commit();

            _logger.LogInformation(
                "Servicio Premium cancelado. IdServicio={IdServicio}, IdUsuarioAdmin={IdUsuarioAdmin}",
                idServicio,
                idUsuarioAdmin);
        }
        catch (Exception ex)
        {
            tran.Rollback();

            _logger.LogError(
                ex,
                "Error al cancelar servicio Premium. IdServicio={IdServicio}",
                idServicio);

            throw new RepositoryException(
                "Error al cancelar el servicio Premium.",
                ex);
        }
    }

    public async Task SincronizarVencimientos()
    {
        using var conn =
            _conexion.CreateSqlConnection();

        conn.Open();

        using var tran =
            conn.BeginTransaction();

        try
        {
            await conn.ExecuteAsync(
                @"
                    UPDATE dbo.ServiciosPremium
                    SET
                        Estado =
                            'VENCIDO',

                        FechaActualizacion =
                            GETDATE()

                    WHERE Estado =
                        'ACTIVO'
                      AND FechaFin
                        IS NOT NULL
                      AND FechaFin <
                        GETDATE();",
                transaction:
                    tran);

            await conn.ExecuteAsync(
                @"
                    UPDATE dbo.PublicacionesDestacadas
                    SET Estado =
                        'Inactivo'
                    WHERE Estado =
                        'Activo'
                      AND FechaFin <
                        GETDATE();",
                transaction:
                    tran);

            await conn.ExecuteAsync(
                @"
                    UPDATE dbo.PublicacionesTemporada
                    SET Estado =
                        'Inactivo'
                    WHERE Estado =
                        'Activo'
                      AND FechaFin <
                        GETDATE();",
                transaction:
                    tran);

            /*
             * Importante:
             * solo desactivamos vitrinas con historial dentro de
             * ServiciosPremium.
             *
             * Esto evita apagar perfiles Premium antiguos que
             * todavía fueron habilitados manualmente antes de
             * incorporar este módulo.
             */
            await conn.ExecuteAsync(
                @"
                    UPDATE v
                    SET
                        v.EsPremium = 0,
                        v.EsPerfilPublico = 0

                    FROM dbo.Vendedores v

                    WHERE v.EsPremium = 1

                      AND EXISTS
                      (
                          SELECT 1
                          FROM dbo.ServiciosPremium historial
                          WHERE historial.IdVendedor =
                                v.Id
                            AND historial.TipoServicio =
                                'VITRINA_PROFESIONAL'
                      )

                      AND NOT EXISTS
                      (
                          SELECT 1
                          FROM dbo.ServiciosPremium activo
                          WHERE activo.IdVendedor =
                                v.Id
                            AND activo.TipoServicio =
                                'VITRINA_PROFESIONAL'
                            AND activo.Estado =
                                'ACTIVO'
                            AND GETDATE()
                                BETWEEN
                                    activo.FechaInicio
                                    AND activo.FechaFin
                      );",
                transaction:
                    tran);

            tran.Commit();
        }
        catch (Exception ex)
        {
            tran.Rollback();

            _logger.LogError(
                ex,
                "Error al sincronizar vencimientos Premium.");

            throw new RepositoryException(
                "Error al sincronizar vencimientos Premium.",
                ex);
        }
    }

    private static void ValidarFechas(
        ActivarServicioPremiumRequest request)
    {
        if (
            !request
                .FechaInicio
                .HasValue
            ||
            !request
                .FechaFin
                .HasValue)
        {
            throw new RepositoryException(
                "Debe indicar la fecha de inicio y la fecha de finalización.");
        }

        if (
            request
                .FechaFin
                .Value
            <=
            request
                .FechaInicio
                .Value)
        {
            throw new RepositoryException(
                "La fecha final debe ser posterior a la fecha inicial.");
        }
    }

    private sealed class TemporadaActivacionDto
    {
        public int Id { get; set; }

        public DateTime FechaInicio { get; set; }

        public DateTime FechaFin { get; set; }

        public string BadgeTexto { get; set; } =
            string.Empty;

        public string BadgeColor { get; set; } =
            string.Empty;
    }
}

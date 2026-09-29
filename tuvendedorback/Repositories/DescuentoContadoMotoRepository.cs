using Dapper;
using tuvendedorback.Data;
using tuvendedorback.DTOs;
using tuvendedorback.Exceptions;
using tuvendedorback.Repositories.Interfaces;

namespace tuvendedorback.Repositories;

public class DescuentoContadoMotoRepository
    : IDescuentoContadoMotoRepository
{
    private readonly DbConnections _conexion;
    private readonly ILogger<DescuentoContadoMotoRepository> _logger;

    public DescuentoContadoMotoRepository(
        DbConnections conexion,
        ILogger<DescuentoContadoMotoRepository> logger)
    {
        _conexion = conexion;
        _logger = logger;
    }


    // =========================================================
    // MARCAS DISPONIBLES
    // =========================================================

    public async Task<IReadOnlyList<DescuentoContadoMarcaDto>> ListarMarcas()
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
SELECT DISTINCT
    m.Id AS IdMarca,
    m.Nombre AS Marca

FROM dbo.Marcas m

INNER JOIN dbo.ModelosProducto mp
    ON mp.IdMarca = m.Id

WHERE m.Estado = 'Activo'
  AND mp.Estado = 'Activo'

ORDER BY m.Nombre;
";

            var data =
                await conn.QueryAsync<DescuentoContadoMarcaDto>(
                    sql);

            return data.ToList();
        }
        catch (Exception ex)
        {
            throw Error(
                ex,
                "Error listando marcas para descuentos de contado.");
        }
    }


    // =========================================================
    // CONFIGURACION DEL MES
    // =========================================================

    public async Task<DescuentoContadoConfiguracionDto?>
        ObtenerConfiguracion(
            int idMarca,
            int anio,
            int mes,
            DateTime fechaDesde,
            DateTime fechaHasta)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
-- ============================================================
-- 1) MARCA
-- ============================================================

SELECT TOP (1)
    m.Id AS IdMarca,
    m.Nombre AS Marca

FROM dbo.Marcas m

WHERE m.Id = @IdMarca
  AND m.Estado = 'Activo';


-- ============================================================
-- 2) REGLA GENERAL EFECTIVA
-- ============================================================

SELECT TOP (1)
    r.Id AS IdReglaGeneral,
    r.PorcentajeDescuento AS PorcentajeGeneral,
    r.FechaDesde AS FechaDesdeReglaGeneral,
    r.FechaHasta AS FechaHastaReglaGeneral

FROM dbo.ReglasDescuentoContadoMoto r

WHERE r.IdMarca = @IdMarca
  AND r.IdModeloProducto IS NULL
  AND r.TipoRegla = 'PORCENTAJE'
  AND r.Estado = 'Activo'
  AND r.FechaDesde <= @FechaDesde
  AND
  (
      r.FechaHasta IS NULL
      OR r.FechaHasta >= @FechaDesde
  )

ORDER BY
    r.Prioridad DESC,
    r.FechaDesde DESC,
    r.Id DESC;


-- ============================================================
-- 3) MODELOS + EXCEPCION EFECTIVA
-- ============================================================

SELECT
    mp.Id AS IdModeloProducto,

    mp.CodigoReferencia,

    mp.NombreModelo,

    mp.Cilindrada,

    especifica.Id AS IdReglaEspecifica,

    especifica.PorcentajeDescuento AS PorcentajeExcepcion,

    COALESCE(
        especifica.PorcentajeDescuento,
        general.PorcentajeDescuento
    ) AS PorcentajeEfectivo,

    CAST(
        CASE
            WHEN especifica.Id IS NOT NULL THEN 1
            ELSE 0
        END
        AS BIT
    ) AS TieneExcepcion,

    especifica.FechaDesde AS FechaDesdeReglaEspecifica,

    especifica.FechaHasta AS FechaHastaReglaEspecifica

FROM dbo.ModelosProducto mp


OUTER APPLY
(
    SELECT TOP (1)
        r.Id,
        r.PorcentajeDescuento,
        r.FechaDesde,
        r.FechaHasta

    FROM dbo.ReglasDescuentoContadoMoto r

    WHERE r.IdMarca = @IdMarca
      AND r.IdModeloProducto IS NULL
      AND r.TipoRegla = 'PORCENTAJE'
      AND r.Estado = 'Activo'
      AND r.FechaDesde <= @FechaDesde
      AND
      (
          r.FechaHasta IS NULL
          OR r.FechaHasta >= @FechaDesde
      )

    ORDER BY
        r.Prioridad DESC,
        r.FechaDesde DESC,
        r.Id DESC
) general


OUTER APPLY
(
    SELECT TOP (1)
        r.Id,
        r.PorcentajeDescuento,
        r.FechaDesde,
        r.FechaHasta

    FROM dbo.ReglasDescuentoContadoMoto r

    WHERE r.IdMarca = @IdMarca
      AND r.IdModeloProducto = mp.Id
      AND r.TipoRegla = 'PORCENTAJE'
      AND r.Estado = 'Activo'
      AND r.FechaDesde <= @FechaDesde
      AND
      (
          r.FechaHasta IS NULL
          OR r.FechaHasta >= @FechaDesde
      )

    ORDER BY
        r.Prioridad DESC,
        r.FechaDesde DESC,
        r.Id DESC
) especifica


WHERE mp.IdMarca = @IdMarca
  AND mp.Estado = 'Activo'

ORDER BY mp.NombreModelo;
";

            using var multi =
                await conn.QueryMultipleAsync(
                    sql,
                    new
                    {
                        IdMarca = idMarca,
                        FechaDesde = fechaDesde.Date,
                        FechaHasta = fechaHasta.Date
                    });


            var marca =
                await multi.ReadFirstOrDefaultAsync<
                    DescuentoContadoMarcaDto>();

            if (marca == null)
            {
                return null;
            }


            var reglaGeneral =
                await multi.ReadFirstOrDefaultAsync<
                    DescuentoContadoReglaGeneralDto>();


            var modelos =
                (
                    await multi.ReadAsync<
                        DescuentoContadoModeloDto>()
                )
                .ToList();


            var configuracion =
                new DescuentoContadoConfiguracionDto
                {
                    IdMarca =
                        marca.IdMarca,

                    Marca =
                        marca.Marca,

                    Anio =
                        anio,

                    Mes =
                        mes,

                    FechaDesde =
                        fechaDesde.Date,

                    FechaHasta =
                        fechaHasta.Date,

                    IdReglaGeneral =
                        reglaGeneral?.IdReglaGeneral,

                    PorcentajeGeneral =
                        reglaGeneral?.PorcentajeGeneral,

                    FechaDesdeReglaGeneral =
                        reglaGeneral?.FechaDesdeReglaGeneral,

                    FechaHastaReglaGeneral =
                        reglaGeneral?.FechaHastaReglaGeneral,

                    Modelos =
                        modelos
                };


            configuracion.ReglaGeneralEsDelMes =
                reglaGeneral != null
                &&
                reglaGeneral.FechaDesdeReglaGeneral.Date
                    == fechaDesde.Date
                &&
                reglaGeneral.FechaHastaReglaGeneral.HasValue
                &&
                reglaGeneral.FechaHastaReglaGeneral.Value.Date
                    == fechaHasta.Date;


            foreach (var modelo in configuracion.Modelos)
            {
                modelo.ReglaEspecificaEsDelMes =
                    modelo.IdReglaEspecifica.HasValue
                    &&
                    modelo.FechaDesdeReglaEspecifica.HasValue
                    &&
                    modelo.FechaDesdeReglaEspecifica.Value.Date
                        == fechaDesde.Date
                    &&
                    modelo.FechaHastaReglaEspecifica.HasValue
                    &&
                    modelo.FechaHastaReglaEspecifica.Value.Date
                        == fechaHasta.Date;
            }


            return configuracion;
        }
        catch (Exception ex)
        {
            throw Error(
                ex,
                "Error obteniendo configuración mensual de descuentos de contado.");
        }
    }


    // =========================================================
    // GUARDAR CONFIGURACION DEL MES
    // =========================================================

    public async Task GuardarConfiguracionMes(
        int idMarca,
        DateTime fechaDesde,
        DateTime fechaHasta,
        decimal porcentajeGeneral,
        IReadOnlyList<DescuentoContadoExcepcionGuardarDto> excepciones)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            conn.Open();

            using var transaction =
                conn.BeginTransaction();


            // =====================================================
            // 1) REGLA GENERAL DEL MES
            // =====================================================

            const string sqlGeneral = @"
UPDATE dbo.ReglasDescuentoContadoMoto
SET
    TipoRegla = 'PORCENTAJE',
    PorcentajeDescuento = @PorcentajeGeneral,
    PrecioContadoFijo = NULL,
    Prioridad = 100,
    Estado = 'Activo'

WHERE IdMarca = @IdMarca
  AND IdModeloProducto IS NULL
  AND FechaDesde = @FechaDesde
  AND FechaHasta = @FechaHasta;


IF @@ROWCOUNT = 0
BEGIN

    INSERT INTO dbo.ReglasDescuentoContadoMoto
    (
        IdMarca,
        IdModeloProducto,
        TipoRegla,
        PorcentajeDescuento,
        PrecioContadoFijo,
        FechaDesde,
        FechaHasta,
        Prioridad,
        Estado
    )
    VALUES
    (
        @IdMarca,
        NULL,
        'PORCENTAJE',
        @PorcentajeGeneral,
        NULL,
        @FechaDesde,
        @FechaHasta,
        100,
        'Activo'
    );

END;
";

            await conn.ExecuteAsync(
                sqlGeneral,
                new
                {
                    IdMarca = idMarca,
                    FechaDesde = fechaDesde.Date,
                    FechaHasta = fechaHasta.Date,
                    PorcentajeGeneral = porcentajeGeneral
                },
                transaction);


            // =====================================================
            // 2) DESACTIVAR EXCEPCIONES DEL MISMO MES
            //
            // Luego activamos nuevamente solamente las recibidas.
            // =====================================================

            const string sqlDesactivar = @"
UPDATE dbo.ReglasDescuentoContadoMoto
SET
    Estado = 'Inactivo'

WHERE IdMarca = @IdMarca
  AND IdModeloProducto IS NOT NULL
  AND TipoRegla = 'PORCENTAJE'
  AND FechaDesde = @FechaDesde
  AND FechaHasta = @FechaHasta;
";

            await conn.ExecuteAsync(
                sqlDesactivar,
                new
                {
                    IdMarca = idMarca,
                    FechaDesde = fechaDesde.Date,
                    FechaHasta = fechaHasta.Date
                },
                transaction);


            // =====================================================
            // 3) EXCEPCIONES DEL MES
            // =====================================================

            const string sqlExcepcion = @"
UPDATE dbo.ReglasDescuentoContadoMoto
SET
    TipoRegla = 'PORCENTAJE',
    PorcentajeDescuento = @PorcentajeDescuento,
    PrecioContadoFijo = NULL,
    Prioridad = 200,
    Estado = 'Activo'

WHERE IdMarca = @IdMarca
  AND IdModeloProducto = @IdModeloProducto
  AND FechaDesde = @FechaDesde
  AND FechaHasta = @FechaHasta;


IF @@ROWCOUNT = 0
BEGIN

    INSERT INTO dbo.ReglasDescuentoContadoMoto
    (
        IdMarca,
        IdModeloProducto,
        TipoRegla,
        PorcentajeDescuento,
        PrecioContadoFijo,
        FechaDesde,
        FechaHasta,
        Prioridad,
        Estado
    )
    VALUES
    (
        @IdMarca,
        @IdModeloProducto,
        'PORCENTAJE',
        @PorcentajeDescuento,
        NULL,
        @FechaDesde,
        @FechaHasta,
        200,
        'Activo'
    );

END;
";

            foreach (var excepcion in excepciones)
            {
                await conn.ExecuteAsync(
                    sqlExcepcion,
                    new
                    {
                        IdMarca = idMarca,

                        IdModeloProducto =
                            excepcion.IdModeloProducto,

                        PorcentajeDescuento =
                            excepcion.PorcentajeDescuento,

                        FechaDesde =
                            fechaDesde.Date,

                        FechaHasta =
                            fechaHasta.Date
                    },
                    transaction);
            }


            transaction.Commit();
        }
        catch (Exception ex)
        {
            throw Error(
                ex,
                "Error guardando configuración mensual de descuentos de contado.");
        }
    }

    // =========================================================
    // CREAR MODELO + EXCEPCION DE DESCUENTO
    // =========================================================

    public async Task<int> CrearModeloConExcepcion(
        int idMarca,
        string codigoReferencia,
        string nombreModelo,
        int? cilindrada,
        string? categoria,
        DateTime fechaDesde,
        DateTime fechaHasta,
        decimal porcentajeDescuento)
    {
        using var conn =
            _conexion.CreateSqlConnection();

        try
        {
            conn.Open();

            using var transaction =
                conn.BeginTransaction();


            // =====================================================
            // 1) VALIDAR MARCA
            // =====================================================

            const string sqlMarca = @"
SELECT COUNT(1)
FROM dbo.Marcas
WHERE Id = @IdMarca
  AND Estado = 'Activo';
";

            var marcaExiste =
                await conn.ExecuteScalarAsync<int>(
                    sqlMarca,
                    new
                    {
                        IdMarca = idMarca
                    },
                    transaction);


            if (marcaExiste == 0)
            {
                throw new ReglasdeNegocioException(
                    "La marca seleccionada no existe o no está activa.");
            }


            // =====================================================
            // 2) EVITAR CODIGO DUPLICADO
            // =====================================================

            const string sqlCodigo = @"
SELECT COUNT(1)
FROM dbo.ModelosProducto
WHERE LTRIM(RTRIM(CodigoReferencia))
      = LTRIM(RTRIM(@CodigoReferencia));
";

            var codigoExiste =
                await conn.ExecuteScalarAsync<int>(
                    sqlCodigo,
                    new
                    {
                        CodigoReferencia =
                            codigoReferencia
                    },
                    transaction);


            if (codigoExiste > 0)
            {
                throw new ReglasdeNegocioException(
                    $"Ya existe un modelo con el código {codigoReferencia}.");
            }


            // =====================================================
            // 3) EVITAR MODELO DUPLICADO EN LA MISMA MARCA
            // =====================================================

            const string sqlNombre = @"
SELECT COUNT(1)
FROM dbo.ModelosProducto
WHERE IdMarca = @IdMarca
  AND LTRIM(RTRIM(NombreModelo))
      = LTRIM(RTRIM(@NombreModelo));
";

            var nombreExiste =
                await conn.ExecuteScalarAsync<int>(
                    sqlNombre,
                    new
                    {
                        IdMarca = idMarca,
                        NombreModelo =
                            nombreModelo
                    },
                    transaction);


            if (nombreExiste > 0)
            {
                throw new ReglasdeNegocioException(
                    $"Ya existe el modelo {nombreModelo} para la marca seleccionada.");
            }


            // =====================================================
            // 4) CREAR MODELO
            // =====================================================

            const string sqlModelo = @"
INSERT INTO dbo.ModelosProducto
(
    IdMarca,
    Rubro,
    CodigoReferencia,
    NombreModelo,
    Cilindrada,
    Categoria,
    Estado,
    FechaCreacion
)
VALUES
(
    @IdMarca,
    'MOTO',
    @CodigoReferencia,
    @NombreModelo,
    @Cilindrada,
    @Categoria,
    'Activo',
    GETDATE()
);

SELECT CAST(SCOPE_IDENTITY() AS INT);
";


            var idModeloProducto =
                await conn.ExecuteScalarAsync<int>(
                    sqlModelo,
                    new
                    {
                        IdMarca =
                            idMarca,

                        CodigoReferencia =
                            codigoReferencia,

                        NombreModelo =
                            nombreModelo,

                        Cilindrada =
                            cilindrada,

                        Categoria =
                            string.IsNullOrWhiteSpace(
                                categoria)
                                ? null
                                : categoria.Trim()
                    },
                    transaction);


            // =====================================================
            // 5) CREAR EXCEPCION PARA EL MES
            // =====================================================

            const string sqlExcepcion = @"
INSERT INTO dbo.ReglasDescuentoContadoMoto
(
    IdMarca,
    IdModeloProducto,
    TipoRegla,
    PorcentajeDescuento,
    PrecioContadoFijo,
    FechaDesde,
    FechaHasta,
    Prioridad,
    Estado
)
VALUES
(
    @IdMarca,
    @IdModeloProducto,
    'PORCENTAJE',
    @PorcentajeDescuento,
    NULL,
    @FechaDesde,
    @FechaHasta,
    200,
    'Activo'
);
";


            await conn.ExecuteAsync(
                sqlExcepcion,
                new
                {
                    IdMarca =
                        idMarca,

                    IdModeloProducto =
                        idModeloProducto,

                    PorcentajeDescuento =
                        porcentajeDescuento,

                    FechaDesde =
                        fechaDesde.Date,

                    FechaHasta =
                        fechaHasta.Date
                },
                transaction);


            transaction.Commit();


            return idModeloProducto;
        }
        catch (ReglasdeNegocioException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw Error(
                ex,
                "Error creando modelo con excepción de descuento.");
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
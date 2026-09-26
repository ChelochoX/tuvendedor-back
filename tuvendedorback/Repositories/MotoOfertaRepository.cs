using Dapper;
using tuvendedorback.Data;
using tuvendedorback.DTOs;
using tuvendedorback.Exceptions;
using tuvendedorback.Repositories.Interfaces;

namespace tuvendedorback.Repositories;

public class MotoOfertaRepository : IMotoOfertaRepository
{
    private readonly DbConnections _conexion;
    private readonly ILogger<MotoOfertaRepository> _logger;

    public MotoOfertaRepository(
        DbConnections conexion,
        ILogger<MotoOfertaRepository> logger)
    {
        _conexion = conexion;
        _logger = logger;
    }


    // =========================================================
    // OFERTA POR PUBLICACION
    // =========================================================

    public async Task<MotoOfertaDataDto?> ObtenerOfertaPorPublicacion(
        int idPublicacion,
        DateTime fechaActual)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sqlModelo = @"
SELECT TOP (1)
    pmp.IdModeloProducto
FROM dbo.PublicacionModeloProducto pmp
INNER JOIN dbo.Publicaciones p
    ON p.Id = pmp.IdPublicacion
INNER JOIN dbo.ModelosProducto mp
    ON mp.Id = pmp.IdModeloProducto
INNER JOIN dbo.Marcas m
    ON m.Id = mp.IdMarca
WHERE pmp.IdPublicacion = @IdPublicacion
  AND pmp.Estado = 'Activo'
  AND p.Estado = 'Activo'
  AND mp.Estado = 'Activo'
  AND m.Estado = 'Activo'
ORDER BY
    pmp.EsPrincipal DESC,
    pmp.Id ASC;
";

            var idModeloProducto =
                await conn.QueryFirstOrDefaultAsync<int?>(
                    sqlModelo,
                    new
                    {
                        IdPublicacion = idPublicacion
                    });

            if (!idModeloProducto.HasValue)
            {
                return null;
            }

            return await ObtenerOfertaPorModeloInterno(
                idModeloProducto.Value,
                fechaActual,
                idPublicacion);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error obteniendo oferta comercial de moto. IdPublicacion={IdPublicacion}",
                idPublicacion);

            throw new RepositoryException(
                "Error obteniendo oferta comercial de moto.",
                ex);
        }
    }


    // =========================================================
    // OFERTA POR MODELO
    //
    // Esta es la fuente principal para WhatsApp/IA.
    // NO requiere que el modelo tenga una publicacion activa.
    // =========================================================

    public async Task<MotoOfertaDataDto?> ObtenerOfertaPorModelo(
        int idModeloProducto,
        DateTime fechaActual)
    {
        try
        {
            return await ObtenerOfertaPorModeloInterno(
                idModeloProducto,
                fechaActual,
                null);
        }
        catch (Exception ex) when (ex is not RepositoryException)
        {
            _logger.LogError(
                ex,
                "Error obteniendo oferta comercial por modelo. IdModeloProducto={IdModeloProducto}",
                idModeloProducto);

            throw new RepositoryException(
                "Error obteniendo oferta comercial de la moto.",
                ex);
        }
    }


    // =========================================================
    // OFERTA BASE POR MODELO
    //
    // REGLAS:
    // 1. Debe existir una lista NORMAL activa.
    // 2. El precio contado sale de la lista NORMAL.
    // 3. Si hay regla de contado, se aplica.
    // 4. Si NO hay regla de contado, el contado es PrecioPublico.
    // 5. Para credito se usa PROMO vigente si existe y tiene plan.
    // 6. Si no hay PROMO, se usa la lista NORMAL.
    // 7. La publicacion es opcional.
    // =========================================================

    private async Task<MotoOfertaDataDto?> ObtenerOfertaPorModeloInterno(
        int idModeloProducto,
        DateTime fechaActual,
        int? idPublicacionPreferida)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
DECLARE @IdModeloProducto INT = @Modelo;
DECLARE @FechaActual DATE = @Fecha;
DECLARE @IdPublicacionPreferida INT = @PublicacionPreferida;

DECLARE @IdMarca INT;

SELECT
    @IdMarca = mp.IdMarca
FROM dbo.ModelosProducto mp
INNER JOIN dbo.Marcas m
    ON m.Id = mp.IdMarca
WHERE mp.Id = @IdModeloProducto
  AND mp.Estado = 'Activo'
  AND m.Estado = 'Activo';

IF @IdMarca IS NULL
BEGIN
    RETURN;
END;


/* ============================================================
   REGLA DE DESCUENTO CONTADO
   ============================================================ */

DECLARE @PorcentajeDescuento DECIMAL(5,2);
DECLARE @PrecioContadoFijo DECIMAL(18,2);
DECLARE @TipoRegla VARCHAR(30);

SELECT TOP (1)
    @PorcentajeDescuento = r.PorcentajeDescuento,
    @PrecioContadoFijo = r.PrecioContadoFijo,
    @TipoRegla = r.TipoRegla
FROM dbo.ReglasDescuentoContadoMoto r
WHERE r.IdMarca = @IdMarca
  AND
  (
      r.IdModeloProducto = @IdModeloProducto
      OR r.IdModeloProducto IS NULL
  )
  AND r.Estado = 'Activo'
  AND r.FechaDesde <= @FechaActual
  AND
  (
      r.FechaHasta IS NULL
      OR r.FechaHasta >= @FechaActual
  )
ORDER BY
    CASE
        WHEN r.IdModeloProducto = @IdModeloProducto THEN 1
        ELSE 0
    END DESC,
    r.Prioridad DESC,
    r.Id DESC;


/* ============================================================
   LISTA NORMAL

   SIEMPRE es la base comercial del modelo.
   No depende de que exista una promo.
   ============================================================ */

DECLARE @IdListaNormal INT;
DECLARE @PrecioPublico DECIMAL(18,2);

SELECT TOP (1)
    @IdListaNormal = lp.Id,
    @PrecioPublico = lp.PrecioPublico
FROM dbo.ListasPreciosProducto lp
WHERE lp.IdModeloProducto = @IdModeloProducto
  AND lp.Estado = 'Activo'
  AND lp.EsPromo = 0
  AND lp.FechaDesde <= @FechaActual
  AND
  (
      lp.FechaHasta IS NULL
      OR lp.FechaHasta >= @FechaActual
  )
ORDER BY
    lp.FechaDesde DESC,
    lp.Id DESC;

/*
 * Sin lista NORMAL no existe un precio comercial base confiable.
 */
IF @IdListaNormal IS NULL
BEGIN
    RETURN;
END;


/* ============================================================
   PROMO DE CREDITO VIGENTE

   Es opcional.
   Solo se considera promo si tiene al menos un plan activo.
   ============================================================ */

DECLARE @IdListaPromoCredito INT;

SELECT TOP (1)
    @IdListaPromoCredito = lp.Id
FROM dbo.ListasPreciosProducto lp
WHERE lp.IdModeloProducto = @IdModeloProducto
  AND lp.Estado = 'Activo'
  AND lp.EsPromo = 1
  AND lp.FechaDesde <= @FechaActual
  AND
  (
      lp.FechaHasta IS NULL
      OR lp.FechaHasta >= @FechaActual
  )
  AND EXISTS
  (
      SELECT 1
      FROM dbo.PlanesFinanciacionProducto pf
      WHERE pf.IdListaPrecio = lp.Id
        AND pf.Estado = 'Activo'
  )
ORDER BY
    lp.FechaDesde DESC,
    lp.Id DESC;


/* ============================================================
   LISTA DE CREDITO

   PROMO vigente -> si no -> NORMAL
   ============================================================ */

DECLARE @IdListaCredito INT;

SET @IdListaCredito =
    COALESCE(
        @IdListaPromoCredito,
        @IdListaNormal
    );


/* ============================================================
   PUBLICACION OPCIONAL

   Para cotizar por modelo NO exigimos publicacion.
   Si existe una activa, la asociamos al DTO.
   ============================================================ */

DECLARE @IdPublicacion INT = @IdPublicacionPreferida;

IF @IdPublicacion IS NULL
BEGIN
    SELECT TOP (1)
        @IdPublicacion = p.Id
    FROM dbo.PublicacionModeloProducto pmp
    INNER JOIN dbo.Publicaciones p
        ON p.Id = pmp.IdPublicacion
    WHERE pmp.IdModeloProducto = @IdModeloProducto
      AND pmp.Estado = 'Activo'
      AND p.Estado = 'Activo'
    ORDER BY
        pmp.EsPrincipal DESC,
        p.Id DESC;
END;


/* ============================================================
   RESULTADO
   ============================================================ */

SELECT TOP (1)
    @IdPublicacion AS PublicacionId,

    mp.Id AS ModeloId,
    ma.Nombre AS Marca,
    mp.NombreModelo,
    mp.CodigoReferencia,
    mp.Cilindrada,

    @IdListaCredito AS IdListaCredito,

    CAST(
        @PrecioPublico
        AS DECIMAL(18,2)
    ) AS PrecioPublico,

    @PorcentajeDescuento AS PorcentajeDescuento,

    CAST(
        CASE
            WHEN @TipoRegla = 'PRECIO_FIJO'
                 AND @PrecioContadoFijo IS NOT NULL
                THEN @PrecioContadoFijo

            WHEN @TipoRegla = 'PORCENTAJE'
                 AND @PorcentajeDescuento IS NOT NULL
                THEN ROUND(
                    @PrecioPublico
                    -
                    (
                        @PrecioPublico
                        *
                        (@PorcentajeDescuento / 100.0)
                    ),
                    0
                )

            /*
             * IMPORTANTE:
             * si no existe regla de descuento,
             * igual existe precio contado:
             * usamos el PrecioPublico NORMAL.
             */
            ELSE @PrecioPublico
        END
        AS DECIMAL(18,2)
    ) AS PrecioContadoFinal,

    CASE
        WHEN @IdListaPromoCredito IS NOT NULL
            THEN CAST(1 AS BIT)
        ELSE CAST(0 AS BIT)
    END AS TienePromoCredito,

    CASE
        WHEN @IdListaPromoCredito IS NOT NULL
            THEN 'PROMO'
        ELSE 'NORMAL'
    END AS TipoCredito,

    lp.FechaDesde,
    lp.FechaHasta

FROM dbo.ModelosProducto mp
INNER JOIN dbo.Marcas ma
    ON ma.Id = mp.IdMarca
INNER JOIN dbo.ListasPreciosProducto lp
    ON lp.Id = @IdListaCredito
WHERE mp.Id = @IdModeloProducto
  AND mp.Estado = 'Activo'
  AND ma.Estado = 'Activo';
";

            return await conn
                .QueryFirstOrDefaultAsync<MotoOfertaDataDto>(
                    sql,
                    new
                    {
                        Modelo = idModeloProducto,
                        Fecha = fechaActual.Date,
                        PublicacionPreferida = idPublicacionPreferida
                    });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error obteniendo oferta comercial por modelo. IdModeloProducto={IdModeloProducto}",
                idModeloProducto);

            throw new RepositoryException(
                "Error obteniendo oferta comercial de la moto.",
                ex);
        }
    }


    // =========================================================
    // PLANES DE FINANCIACION
    // =========================================================

    public async Task<List<MotoPlanOfertaDataDto>>
        ObtenerPlanesPorListaPrecio(
            int idListaPrecio)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
SELECT
    pf.EntregaInicial,
    pf.CantidadCuotas,
    pf.ImporteCuota,
    pf.Interes,
    pf.CodigoPlan
FROM dbo.PlanesFinanciacionProducto pf
WHERE pf.IdListaPrecio = @IdListaPrecio
  AND pf.Estado = 'Activo'
ORDER BY
    pf.CantidadCuotas ASC,
    pf.ImporteCuota ASC;
";

            var data =
                await conn.QueryAsync<MotoPlanOfertaDataDto>(
                    sql,
                    new
                    {
                        IdListaPrecio = idListaPrecio
                    });

            return data.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error obteniendo planes de financiación. IdListaPrecio={IdListaPrecio}",
                idListaPrecio);

            throw new RepositoryException(
                "Error obteniendo planes de financiación de la moto.",
                ex);
        }
    }
}

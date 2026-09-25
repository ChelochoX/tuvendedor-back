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


    public async Task<MotoOfertaDataDto?> ObtenerOfertaPorPublicacion(
        int idPublicacion,
        DateTime fechaActual)
    {
        using var conn = _conexion.CreateSqlConnection();

        try
        {
            const string sql = @"
                DECLARE @IdPublicacion INT = @Publicacion;
                DECLARE @FechaActual DATE = @Fecha;

                DECLARE @IdModeloProducto INT;
                DECLARE @IdMarca INT;


                /* ============================================================
                    MODELO PRINCIPAL DE LA PUBLICACION
                    ============================================================ */

                SELECT TOP (1)
                    @IdModeloProducto = pm.IdModeloProducto,
                    @IdMarca = mp.IdMarca
                FROM dbo.PublicacionModeloProducto pm
                INNER JOIN dbo.ModelosProducto mp
                    ON mp.Id = pm.IdModeloProducto
                WHERE pm.IdPublicacion = @IdPublicacion
                    AND pm.Estado = 'Activo'
                ORDER BY
                    pm.EsPrincipal DESC,
                    pm.Id ASC;


                /*
                    * Si la publicación no está relacionada a un modelo,
                    * no retornamos oferta.
                    */

                IF @IdModeloProducto IS NULL
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

                    @PorcentajeDescuento =
                        r.PorcentajeDescuento,

                    @PrecioContadoFijo =
                        r.PrecioContadoFijo,

                    @TipoRegla =
                        r.TipoRegla

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
                        WHEN r.IdModeloProducto = @IdModeloProducto
                            THEN 1
                        ELSE 0
                    END DESC,

                    r.Prioridad DESC,

                    r.Id DESC;


                /* ============================================================
                    LISTA NORMAL
                    ============================================================ */

                DECLARE @IdListaNormal INT;
                DECLARE @PrecioPublico DECIMAL(18,2);


                SELECT TOP (1)

                    @IdListaNormal =
                        lp.Id,

                    @PrecioPublico =
                        lp.PrecioPublico

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


                /* ============================================================
                    PROMO DE CREDITO VIGENTE
                    Solo consideramos una promo de crédito si tiene
                    al menos un plan de financiación activo.
                    ============================================================ */

                DECLARE @IdListaPromoCredito INT;


                SELECT TOP (1)

                    @IdListaPromoCredito =
                        lp.Id

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
                    LISTA DE CREDITO A UTILIZAR

                    PROMO vigente
                        ↓
                    si no existe
                        ↓
                    NORMAL
                    ============================================================ */

                DECLARE @IdListaCredito INT;


                SET @IdListaCredito =
                    COALESCE(
                        @IdListaPromoCredito,
                        @IdListaNormal
                    );


                IF @IdListaCredito IS NULL
                BEGIN
                    RETURN;
                END;


                /* ============================================================
                    RESULTADO
                    ============================================================ */

                SELECT TOP (1)

                    p.Id AS PublicacionId,

                    mp.Id AS ModeloId,

                    ma.Nombre AS Marca,

                    mp.NombreModelo,

                    mp.CodigoReferencia,

                    mp.Cilindrada,

                    @IdListaCredito AS IdListaCredito,


                    CAST(
                        COALESCE(
                            @PrecioPublico,
                            lp.PrecioPublico
                        )
                        AS DECIMAL(18,2)
                    ) AS PrecioPublico,


                    @PorcentajeDescuento
                        AS PorcentajeDescuento,


                    CASE

                        WHEN @TipoRegla = 'PRECIO_FIJO'
                        THEN
                            CAST(
                                @PrecioContadoFijo
                                AS DECIMAL(18,2)
                            )


                        WHEN @TipoRegla = 'PORCENTAJE'
                        THEN
                            CAST(
                                ROUND(
                                    COALESCE(
                                        @PrecioPublico,
                                        lp.PrecioPublico
                                    )
                                    -
                                    (
                                        COALESCE(
                                            @PrecioPublico,
                                            lp.PrecioPublico
                                        )
                                        *
                                        (
                                            @PorcentajeDescuento
                                            /
                                            100.0
                                        )
                                    ),
                                    0
                                )
                                AS DECIMAL(18,2)
                            )


                        ELSE NULL

                    END AS PrecioContadoFinal,


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


                FROM dbo.Publicaciones p

                INNER JOIN dbo.PublicacionModeloProducto pm
                    ON pm.IdPublicacion = p.Id

                INNER JOIN dbo.ModelosProducto mp
                    ON mp.Id = pm.IdModeloProducto

                INNER JOIN dbo.Marcas ma
                    ON ma.Id = mp.IdMarca

                INNER JOIN dbo.ListasPreciosProducto lp
                    ON lp.Id = @IdListaCredito


                WHERE p.Id = @IdPublicacion

                    AND p.Estado = 'Activo'

                    AND pm.Estado = 'Activo'

                    AND pm.IdModeloProducto =
                        @IdModeloProducto;
                ";


            return await conn
                .QueryFirstOrDefaultAsync<MotoOfertaDataDto>(
                    sql,
                    new
                    {
                        Publicacion = idPublicacion,
                        Fecha = fechaActual.Date
                    });
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
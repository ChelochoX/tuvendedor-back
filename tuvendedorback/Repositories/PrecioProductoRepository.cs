using Dapper;
using tuvendedorback.Data;
using tuvendedorback.DTOs;
using tuvendedorback.Exceptions;
using tuvendedorback.Repositories.Interfaces;
using tuvendedorback.Request;

namespace tuvendedorback.Repositories;

public class PrecioProductoRepository : IPrecioProductoRepository
{
    private readonly DbConnections _conexion;
    private readonly ILogger<PrecioProductoRepository> _logger;

    public PrecioProductoRepository(DbConnections conexion, ILogger<PrecioProductoRepository> logger)
    {
        _conexion = conexion;
        _logger = logger;
    }

    public async Task<int> CrearModeloProducto(CrearModeloProductoRequest request)
    {
        using var conn = _conexion.CreateSqlConnection();
        try
        {
            const string sql = @"
            INSERT INTO ModelosProducto (IdMarca, Rubro, CodigoReferencia, NombreModelo, Cilindrada, Categoria, Estado, FechaCreacion)
            VALUES (@IdMarca, @Rubro, @CodigoReferencia, @NombreModelo, @Cilindrada, @Categoria, 'Activo', GETDATE());
            SELECT SCOPE_IDENTITY();";

            return await conn.ExecuteScalarAsync<int>(sql, request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al crear modelo producto. Rubro={Rubro}, Codigo={Codigo}", request.Rubro, request.CodigoReferencia);
            throw new RepositoryException("Error al crear modelo producto", ex);
        }
    }

    public async Task<int> CrearListaPrecioProducto(CrearListaPrecioProductoRequest request)
    {
        using var conn = _conexion.CreateSqlConnection();
        try
        {
            const string sql = @"
            INSERT INTO ListasPreciosProducto
            (IdModeloProducto, PrecioPublico, PrecioDistribuidor, PrecioBase, FechaDesde, FechaHasta, EsPromo, Estado)
            VALUES
            (@IdModeloProducto, @PrecioPublico, @PrecioDistribuidor, @PrecioBase, @FechaDesde, @FechaHasta, @EsPromo, 'Activo');
            SELECT SCOPE_IDENTITY();";

            return await conn.ExecuteScalarAsync<int>(sql, request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al crear lista de precios. IdModeloProducto={IdModeloProducto}", request.IdModeloProducto);
            throw new RepositoryException("Error al crear lista de precios", ex);
        }
    }

    public async Task<int> CrearPlanFinanciacionProducto(CrearPlanFinanciacionProductoRequest request)
    {
        using var conn = _conexion.CreateSqlConnection();
        try
        {
            const string sql = @"
            INSERT INTO PlanesFinanciacionProducto
            (IdListaPrecio, EntregaInicial, CantidadCuotas, ImporteCuota, Interes, CodigoPlan, Estado)
            VALUES
            (@IdListaPrecio, @EntregaInicial, @CantidadCuotas, @ImporteCuota, @Interes, @CodigoPlan, 'Activo');
            SELECT SCOPE_IDENTITY();";

            return await conn.ExecuteScalarAsync<int>(sql, request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al crear plan financiación. IdListaPrecio={IdListaPrecio}", request.IdListaPrecio);
            throw new RepositoryException("Error al crear plan de financiación", ex);
        }
    }

    public async Task<bool> ExisteModeloPorCodigo(string rubro, string codigoReferencia)
    {
        using var conn = _conexion.CreateSqlConnection();
        try
        {
            const string sql = @"
            SELECT COUNT(1)
            FROM ModelosProducto
            WHERE Rubro = @Rubro AND CodigoReferencia = @Codigo AND Estado = 'Activo';";

            var count = await conn.ExecuteScalarAsync<int>(sql, new { Rubro = rubro, Codigo = codigoReferencia });
            return count > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validando existencia de modelo. Rubro={Rubro}, Codigo={Codigo}", rubro, codigoReferencia);
            throw new RepositoryException("Error validando existencia de modelo", ex);
        }
    }

    public async Task<int?> ObtenerIdModeloPorCodigo(string rubro, string codigoReferencia)
    {
        using var conn = _conexion.CreateSqlConnection();
        try
        {
            const string sql = @"
            SELECT TOP 1 Id
            FROM ModelosProducto
            WHERE Rubro = @Rubro AND CodigoReferencia = @Codigo AND Estado = 'Activo'
            ORDER BY Id DESC;";

            return await conn.ExecuteScalarAsync<int?>(sql, new { Rubro = rubro, Codigo = codigoReferencia });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error obteniendo IdModelo por código. Rubro={Rubro}, Codigo={Codigo}", rubro, codigoReferencia);
            throw new RepositoryException("Error obteniendo IdModelo por código", ex);
        }
    }

    // ✅ Detecta solapamiento de fechas para evitar 2 listas vigentes al mismo tiempo
    public async Task<bool> ExisteSolapamientoListaPrecio(
        int idModeloProducto,
        DateTime fechaDesde,
        DateTime? fechaHasta,
        bool esPromo   // 👈 NUEVO
    )
    {
        using var conn = _conexion.CreateSqlConnection();
        try
        {
            const string sql = @"
            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM ListasPreciosProducto lp
                WHERE lp.IdModeloProducto = @IdModelo
                  AND lp.Estado = 'Activo'
                  AND lp.EsPromo = @EsPromo   -- 👈 CLAVE
                  AND (
                       @Desde <= ISNULL(lp.FechaHasta, '9999-12-31')
                   AND ISNULL(@Hasta, '9999-12-31') >= lp.FechaDesde
                  )
            ) THEN 1 ELSE 0 END;";

            return await conn.ExecuteScalarAsync<bool>(sql, new
            {
                IdModelo = idModeloProducto,
                Desde = fechaDesde.Date,
                Hasta = fechaHasta?.Date,
                EsPromo = esPromo
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verificando solapamiento. IdModeloProducto={IdModelo}", idModeloProducto);
            throw new RepositoryException("Error verificando solapamiento de listas de precios", ex);
        }
    }

    public async Task<PrecioVigenteProductoDto?> ObtenerPrecioVigentePorCodigo(string rubro, string codigoReferencia, DateTime fechaActual)
    {
        using var conn = _conexion.CreateSqlConnection();
        try
        {
            const string sql = @"
            SELECT TOP 1
                mp.Id                 AS IdModeloProducto,
                mp.Rubro              AS Rubro,
                mp.CodigoReferencia   AS CodigoReferencia,
                mp.NombreModelo       AS NombreModelo,
                m.Nombre              AS Marca,

                lp.Id                 AS IdListaPrecio,
                lp.PrecioPublico      AS PrecioPublico,
                lp.PrecioDistribuidor AS PrecioDistribuidor,
                lp.PrecioBase         AS PrecioBase,
                lp.FechaDesde         AS FechaDesde,
                lp.FechaHasta         AS FechaHasta
            FROM ModelosProducto mp
            INNER JOIN Marcas m ON m.Id = mp.IdMarca
            INNER JOIN ListasPreciosProducto lp ON lp.IdModeloProducto = mp.Id
            WHERE mp.Rubro = @Rubro
              AND mp.CodigoReferencia = @Codigo
              AND mp.Estado = 'Activo'
              AND lp.Estado = 'Activo'
              AND @Fecha BETWEEN lp.FechaDesde AND ISNULL(lp.FechaHasta, '9999-12-31')
            ORDER BY lp.FechaDesde DESC;";

            return await conn.QueryFirstOrDefaultAsync<PrecioVigenteProductoDto>(sql, new
            {
                Rubro = rubro,
                Codigo = codigoReferencia,
                Fecha = fechaActual.Date
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error obteniendo precio vigente. Rubro={Rubro}, Codigo={Codigo}", rubro, codigoReferencia);
            throw new RepositoryException("Error obteniendo precio vigente", ex);
        }
    }

    public async Task<List<PlanFinanciacionDto>> ObtenerPlanesPorListaPrecio(int idListaPrecio)
    {
        using var conn = _conexion.CreateSqlConnection();
        try
        {
            const string sql = @"
            SELECT
                Id,
                EntregaInicial,
                CantidadCuotas,
                ImporteCuota,
                Interes,
                CodigoPlan
            FROM PlanesFinanciacionProducto
            WHERE IdListaPrecio = @IdLista
              AND Estado = 'Activo'
            ORDER BY CantidadCuotas ASC;";

            return (await conn.QueryAsync<PlanFinanciacionDto>(sql, new { IdLista = idListaPrecio })).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error obteniendo planes. IdListaPrecio={IdListaPrecio}", idListaPrecio);
            throw new RepositoryException("Error obteniendo planes de financiación", ex);
        }
    }

    public async Task<IEnumerable<ModeloProductoDto>> ListarModelos()
    {
        using var conn = _conexion.CreateSqlConnection();
        try
        {
            const string sql = @"
        SELECT
            mp.Id                 AS Id,
            m.Nombre              AS Marca,
            mp.NombreModelo       AS Modelo,
            mp.CodigoReferencia   AS Codigo,
            mp.Rubro              AS Rubro
        FROM ModelosProducto mp
        INNER JOIN Marcas m ON m.Id = mp.IdMarca
        WHERE mp.Estado = 'Activo'
        ORDER BY m.Nombre, mp.NombreModelo;";

            return await conn.QueryAsync<ModeloProductoDto>(sql);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al listar modelos de producto");
            throw new RepositoryException("Error al listar modelos de producto", ex);
        }
    }



}

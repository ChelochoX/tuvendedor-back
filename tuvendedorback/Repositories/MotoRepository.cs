using Dapper;
using System.Data;
using tuvendedorback.Data;
using tuvendedorback.DTOs;
using tuvendedorback.Exceptions;
using tuvendedorback.Repositories.Interfaces;

namespace tuvendedorback.Repositories;

public class MotoRepository: IMotoRepository
{
    private readonly DbConnections _conexion;
    private readonly ILogger<MotoRepository> _logger;

    public MotoRepository(ILogger<MotoRepository> logger, DbConnections conexion)
    {
        _logger = logger;
        _conexion = conexion;
    }

    public async Task<ProductoDTO> ObtenerProductoConPlanes(string modelo)
    {
        _logger.LogInformation("Inicio del proceso para obtener el producto con sus planes");

        string queryProducto = @"select 
                                    p.IdProducto, 
                                    p.Articulo, 
                                    p.Modelo, 
                                    p.PrecioPublico, 
                                    p.PrecioMayorista, 
                                    p.PrecioBase
                                 from 
                                    Productos p
                                 where 
                                    p.Modelo like @modelo";

        string queryPlanes = @"select 
                                    pl.IdPrecioPlan, 
                                    pl.IdPlan, 
                                    pl.Entrega, 
                                    pl.Cuotas, 
                                    pl.Importe,
                                    p.NombrePlan
                               from 
                                    PreciosPlan pl 
                                    inner join Planes p on pl.IdPlan = p.IdPlan
                               where 
                                    pl.IdProducto = @idProducto";

        try
        {
            using (var connection = _conexion.CreateSqlConnection())
            {
                string descripcionNormalizada = modelo.Replace("×", "x");

                // Obtener los detalles del producto
                var parametros = new DynamicParameters();
                parametros.Add("@modelo", descripcionNormalizada);

                var producto = await connection.QueryFirstOrDefaultAsync<ProductoDTO>(queryProducto, parametros);

                if (producto == null)
                {
                    throw new NoDataFoundException("No se encontró el producto con el modelo proporcionado");
                }

                // Obtener los planes asociados al producto
                parametros = new DynamicParameters();
                parametros.Add("@idProducto", producto.IdProducto);

                var planes = await connection.QueryAsync<Plan>(queryPlanes, parametros);

                // Asignar los planes al producto
                producto.Planes = planes.ToList();

                _logger.LogInformation("Fin del proceso para obtener el producto con sus planes");

                return producto;
            }
        }
        catch (NoDataFoundException ex)
        {
            _logger.LogWarning(ex, "No se encontraron datos para el modelo proporcionado");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ocurrió un error inesperado al obtener los datos del producto con sus planes");
            throw new RepositoryException("Ocurrió un error inesperado al obtener los datos del producto con sus planes", ex);
        }
    }

    public async Task<int> ObtenerPrecioBase(string modelo)
    {
        _logger.LogInformation("Inicio del proceso para obtener el producto con sus planes");

        string queryProducto = @"select                                 
                                    p.PrecioBase
                                 from 
                                    Productos p
                                 where 
                                    p.Modelo like @modelo";      
        try
        {
            using (var connection = _conexion.CreateSqlConnection())
            {
                string descripcionNormalizada = modelo.Replace("×", "x");

                // Obtener los detalles del producto
                var parametros = new DynamicParameters();
                parametros.Add("@modelo", descripcionNormalizada);

                var precioBase = await connection.QueryFirstOrDefaultAsync<int>(queryProducto, parametros);

                if (precioBase == null)
                {
                    throw new NoDataFoundException("No se encontró el producto con el modelo proporcionado");
                }                

                _logger.LogInformation("Fin del proceso para obtener el monto del precio base");

                return precioBase;
            }
        }
        catch (NoDataFoundException ex)
        {
            _logger.LogWarning(ex, "No se encontraron datos para el modelo proporcionado");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ocurrió un error inesperado al obtener el monto del precio base");
            throw new RepositoryException("Ocurrió un error inesperado al obtener el monto del precio base", ex);
        }
    }
}

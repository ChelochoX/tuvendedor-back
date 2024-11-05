using Dapper;
using tuvendedorback.Data;
using tuvendedorback.DTOs;
using tuvendedorback.Exceptions;
using tuvendedorback.Models;
using tuvendedorback.Repositories.Interfaces;
using tuvendedorback.Request;
using tuvendedorback.Wrappers;

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

    public async Task<int> GuardarSolicitudCredito(SolicitudCredito solicitud)
    {
        _logger.LogInformation("Inicio del proceso para guardar la solicitud de crédito");

        string queryInsertarSolicitud = @"
            INSERT INTO CreditoSolicitud 
            (ModeloSolicitado, EntregaInicial, CantidadCuotas, MontoPorCuota, CedulaIdentidad, TelefonoMovil, FechaNacimiento, Barrio, Ciudad, DireccionParticular,NombresApellidos)
            VALUES 
            (@ModeloSolicitado, @EntregaInicial, @CantidadCuotas, @MontoPorCuota, @Cedula, @TelefonoMovil, @FechaNacimiento, @Barrio, @Ciudad, @DireccionParticular,@NombresApellidos);
            SELECT CAST(SCOPE_IDENTITY() as int);";

        string queryInsertarDatosLaborales = @"
            INSERT INTO DatosLaborales 
            (CreditoSolicitudId, Empresa, DireccionLaboral, TelefonoLaboral, AntiguedadAnios, AportaIPS, CantidadAportes, Salario)
            VALUES 
            (@CreditoSolicitudId, @Empresa, @DireccionLaboral, @TelefonoLaboral, @AntiguedadAnios, @AportaIPS, @CantidadAportes, @Salario);";

        string queryInsertarReferenciaComercial = @"
            INSERT INTO ReferenciasComerciales (CreditoSolicitudId, NombreLocal, Telefono)
            VALUES (@CreditoSolicitudId, @NombreLocal, @Telefono);";

        string queryInsertarReferenciaPersonal = @"
            INSERT INTO ReferenciasPersonales (CreditoSolicitudId, Nombre, Telefono)
            VALUES (@CreditoSolicitudId, @Nombre, @Telefono);";

        try
        {
            using (var connection = _conexion.CreateSqlConnection())
            {
                connection.Open();

                using (var transaction = connection.BeginTransaction())
                {
                    var parametrosSolicitud = new DynamicParameters();
                    parametrosSolicitud.Add("@ModeloSolicitado", solicitud.ModeloSolicitado);
                    parametrosSolicitud.Add("@EntregaInicial", solicitud.EntregaInicial);
                    parametrosSolicitud.Add("@CantidadCuotas", solicitud.CantidadCuotas);
                    parametrosSolicitud.Add("@MontoPorCuota", solicitud.MontoPorCuota);
                    parametrosSolicitud.Add("@Cedula", solicitud.CedulaIdentidad);
                    parametrosSolicitud.Add("@TelefonoMovil", solicitud.TelefonoMovil);
                    parametrosSolicitud.Add("@FechaNacimiento", solicitud.FechaNacimiento);
                    parametrosSolicitud.Add("@Barrio", solicitud.Barrio);
                    parametrosSolicitud.Add("@Ciudad", solicitud.Ciudad);
                    parametrosSolicitud.Add("@DireccionParticular", solicitud.DireccionParticular);
                    parametrosSolicitud.Add("@NombresApellidos", solicitud.NombresApellidos);

                    // Insertar los datos en CreditoSolicitud
                    var solicitudId = await connection.QuerySingleAsync<int>(queryInsertarSolicitud, parametrosSolicitud, transaction);

                    // Insertar Datos Laborales
                    var parametrosLaborales = new DynamicParameters();
                    parametrosLaborales.Add("@CreditoSolicitudId", solicitudId);
                    parametrosLaborales.Add("@Empresa", solicitud.Empresa);
                    parametrosLaborales.Add("@DireccionLaboral", solicitud.DireccionLaboral);
                    parametrosLaborales.Add("@TelefonoLaboral", solicitud.TelefonoLaboral);
                    parametrosLaborales.Add("@AntiguedadAnios", solicitud.AntiguedadAnios);
                    parametrosLaborales.Add("@AportaIPS", solicitud.AportaIPS);
                    parametrosLaborales.Add("@CantidadAportes", solicitud.CantidadAportes);
                    parametrosLaborales.Add("@Salario", solicitud.Salario);

                    await connection.ExecuteAsync(queryInsertarDatosLaborales, parametrosLaborales, transaction);

                    // Insertar Referencias Comerciales
                    foreach (var referenciaComercial in solicitud.ReferenciasComerciales)
                    {
                        var parametrosReferenciaComercial = new DynamicParameters();
                        parametrosReferenciaComercial.Add("@CreditoSolicitudId", solicitudId);
                        parametrosReferenciaComercial.Add("@NombreLocal", referenciaComercial.NombreLocal);
                        parametrosReferenciaComercial.Add("@Telefono", referenciaComercial.Telefono);
                        await connection.ExecuteAsync(queryInsertarReferenciaComercial, parametrosReferenciaComercial, transaction);
                    }

                    // Insertar Referencias Personales
                    foreach (var referenciaPersonal in solicitud.ReferenciasPersonales)
                    {
                        var parametrosReferenciaPersonal = new DynamicParameters();
                        parametrosReferenciaPersonal.Add("@CreditoSolicitudId", solicitudId);
                        parametrosReferenciaPersonal.Add("@Nombre", referenciaPersonal.Nombre);
                        parametrosReferenciaPersonal.Add("@Telefono", referenciaPersonal.Telefono);
                        await connection.ExecuteAsync(queryInsertarReferenciaPersonal, parametrosReferenciaPersonal, transaction);
                    }

                    transaction.Commit();
                    _logger.LogInformation("Fin del proceso para guardar la solicitud de crédito");

                    return solicitudId;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ocurrió un error inesperado al guardar la solicitud de crédito");
            throw new RepositoryException("Ocurrió un error inesperado al guardar la solicitud de crédito", ex);
        }
    }

    public async Task<List<ProductosDTOPromo>> ListarProductosConPlanesPromo()
    {
        _logger.LogInformation("Inicio del proceso para obtener todos los productos con promociones y sus planes promocionales");

        string queryProductos = @"
                    SELECT 
                        p.IdProducto,
                        p.Articulo,
                        p.Modelo,
                        p.PrecioPublicoPromo,
                        p.PrecioMayoristaPromo,
                        p.PrecioBasePromo
                    FROM 
                        Productos p
                    WHERE 
                        p.TienePromocion = 1                      
                        AND p.FechaInicioPromo IS NOT NULL
                        AND p.FechaFinPromo IS NOT NULL
                        AND p.FechaInicioPromo <= GETDATE()
                        AND p.FechaFinPromo >= GETDATE()";

        string queryPlanes = @"
                    SELECT 
                        pl.IdPrecioPlan,
                        pl.IdPlan,
                        pl.EntregaPromo,
                        pl.CuotasPromo,
                        pl.ImportePromo,
                        p.NombrePlan,
                        pl.FechaInicioPromo,
                        pl.FechaFinPromo
                    FROM 
                        PreciosPlan pl 
                        INNER JOIN Planes p ON pl.IdPlan = p.IdPlan
                    WHERE 
                        pl.IdProducto = @idProducto";

        try
        {
            using (var connection = _conexion.CreateSqlConnection())
            {
                // Obtener todos los productos con promociones
                var productos = await connection.QueryAsync<ProductosDTOPromo>(queryProductos);

                if (productos == null || !productos.Any())
                {
                    throw new NoDataFoundException("No se encontraron productos con promociones");
                }

                foreach (var producto in productos)
                {
                    // Obtener los planes promocionales asociados a cada producto
                    var parametros = new DynamicParameters();
                    parametros.Add("@idProducto", producto.IdProducto);

                    var planesPromo = await connection.QueryAsync<PlanesPromo>(queryPlanes, parametros);

                    // Asignar los planes promocionales al producto
                    producto.Planes = planesPromo.ToList();
                }

                _logger.LogInformation("Fin del proceso para obtener todos los productos con sus planes promocionales");

                return productos.ToList();
            }
        }
        catch (NoDataFoundException ex)
        {
            _logger.LogWarning(ex, "No se encontraron productos con promociones");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ocurrió un error inesperado al obtener los productos con sus planes promocionales");
            throw new RepositoryException("Ocurrió un error inesperado al obtener los productos con sus planes promocionales", ex);
        }
    }

    public async Task<ProductoDTOPromo> ObtenerProductoConPlanesPromo(string modelo)
    {
        _logger.LogInformation("Inicio del proceso para obtener el producto con promocion y sus plan promocional");

        string queryProducto = @"
                SELECT 
                    p.IdProducto,
                    p.Articulo,
                    p.Modelo,
                    p.PrecioPublicoPromo,
                    p.PrecioMayoristaPromo,
                    p.PrecioBasePromo,
                    p.PrecioPublico, 
                    p.PrecioMayorista, 
                    p.PrecioBase,
                    p.TienePromocion
                FROM 
                    Productos p
                WHERE 
                    p.Modelo LIKE @modelo
                    AND p.TienePromocion = 1
                    AND p.FechaInicioPromo IS NOT NULL
                    AND p.FechaFinPromo IS NOT NULL
                    AND p.FechaInicioPromo <= GETDATE()
                    AND p.FechaFinPromo >= GETDATE()";

        string queryPlanes = @"
                SELECT 
                    pl.IdPrecioPlan,
                    pl.IdPlan,
                    pl.EntregaPromo,
                    pl.CuotasPromo,
                    pl.ImportePromo,                  
                    p.NombrePlan,
                    pl.FechaInicioPromo,
                    pl.FechaFinPromo
                FROM 
                    PreciosPlan pl 
                    INNER JOIN Planes p ON pl.IdPlan = p.IdPlan
                WHERE 
                    pl.IdProducto = @idProducto
                    AND pl.ImportePromo IS NOT NULL
                    AND pl.ImportePromo > 0";

        try
        {
            using (var connection = _conexion.CreateSqlConnection())
            {
                string descripcionNormalizada = modelo.Replace("×", "x");

                // Obtener los detalles del producto con promoción
                var parametros = new DynamicParameters();
                parametros.Add("@modelo", descripcionNormalizada);

                var producto = await connection.QueryFirstOrDefaultAsync<ProductoDTOPromo>(queryProducto, parametros);

                if (producto == null)
                {
                    throw new NoDataFoundException("No se encontró un producto con promoción para el modelo proporcionado");
                }

                // Obtener los planes promocionales asociados al producto
                parametros = new DynamicParameters();
                parametros.Add("@idProducto", producto.IdProducto);

                var planes = await connection.QueryAsync<PlanPromo>(queryPlanes, parametros);

                // Asignar los planes promocionales al producto
                producto.Planes = planes.ToList();

                _logger.LogInformation("Fin del proceso para obtener el producto con promocion y sus planes promocionales");

                return producto;
            }
        }
        catch (NoDataFoundException ex)
        {
            _logger.LogWarning(ex, "No se encontro el producto con promocion para el modelo proporcionado");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ocurrió un error inesperado al obtener el producto con plan promocional");
            throw new RepositoryException("Ocurrió un error inesperado al obtener el producto con plan promocional", ex);
        }
    }

    public async Task RegistrarVisitaAsync(string page)
    {
        _logger.LogInformation("Inicio del proceso para registrar la visita para la página: {Page}", page);

        string queryBuscarVisita = @"
                SELECT Id, Count, LastVisited 
                FROM Visitas 
                WHERE Page = @page";

        string queryActualizarVisita = @"
                UPDATE Visitas 
                SET Count = Count + 1, LastVisited = @lastVisited 
                WHERE Id = @id";

        string queryInsertarVisita = @"
                INSERT INTO Visitas (Page, Count, LastVisited) 
                VALUES (@page, @count, @lastVisited)";

        try
        {
            using (var connection = _conexion.CreateSqlConnection())
            {              
                // Busca si ya existe un registro de visitas para la página
                var parametrosBuscar = new DynamicParameters();
                parametrosBuscar.Add("@page", page);

                var visitaExistente = await connection.QueryFirstOrDefaultAsync<Visita>(queryBuscarVisita, parametrosBuscar);

                if (visitaExistente != null)
                {
                    // Si existe, incrementa el contador y actualiza la fecha de la última visita
                    var parametrosActualizar = new DynamicParameters();
                    parametrosActualizar.Add("@id", visitaExistente.Id);
                    parametrosActualizar.Add("@lastVisited", DateTime.Now);

                    await connection.ExecuteAsync(queryActualizarVisita, parametrosActualizar);
                }
                else
                {
                    // Si no existe, inserta una nueva visita
                    var parametrosInsertar = new DynamicParameters();
                    parametrosInsertar.Add("@page", page);
                    parametrosInsertar.Add("@count", 1);
                    parametrosInsertar.Add("@lastVisited", DateTime.Now);

                    await connection.ExecuteAsync(queryInsertarVisita, parametrosInsertar);
                }

                _logger.LogInformation("Fin del proceso para registrar la visita para la página: {Page}", page);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ocurrió un error inesperado al registrar la visita para la página: {Page}", page);
            throw new RepositoryException("Ocurrió un error inesperado al registrar la visita", ex);
        }
    }

    public async Task<Datos<IEnumerable<SolicitudesdeCreditoDTO>>> ObtenerSolicitudesCredito(SolicitudCreditoRequest request)
    {
        _logger.LogInformation("Inicio de Proceso de obtener lista de solicitudes de crédito");

        int saltarRegistros = (request.Pagina - 1) * request.CantidadRegistros;
        string filtro = string.Empty;

        // Filtro de término de búsqueda o modelo solicitado
        if (!string.IsNullOrEmpty(request.TerminoDeBusqueda))
        {
            filtro += @"
                AND (@terminoBusqueda IS NULL OR @terminoBusqueda = '' OR                            
                     cs.ModeloSolicitado LIKE '%' + @terminoBusqueda + '%' OR
                     CONVERT(varchar, cs.FechaCreacion, 23) LIKE '%' + @terminoBusqueda + '%')";
        }
        else
        {
            if (!string.IsNullOrEmpty(request.ModeloSolicitado))
            {
                filtro += " AND (@modeloSolicitado IS NULL OR cs.ModeloSolicitado LIKE '%' + @modeloSolicitado + '%')";
            }

            // Filtro por una fecha específica
            if (request.FechaCreacion.HasValue)
            {
                filtro += " AND (@fechaCreacion IS NULL OR CONVERT(date, cs.FechaCreacion) = @fechaCreacion)";
            }

            // Filtro por rango de fechas
            if (request.FechaInicio.HasValue && request.FechaFin.HasValue)
            {
                filtro += " AND (cs.FechaCreacion BETWEEN @fechaInicio AND @fechaFin)";
            }
        }

        string query = $@"
                    SELECT 
                        cs.Id AS Id, 
                        cs.CedulaIdentidad AS Cedula, 
                        cs.ModeloSolicitado AS ModeloSolicitado, 
                        cs.EntregaInicial AS EntregaInicial, 
                        cs.CantidadCuotas AS Cuotas, 
                        cs.MontoPorCuota AS MontoPorCuota, 
                        cs.TelefonoMovil AS Telefono,
                        cs.FechaCreacion AS FechaCreacion
                    FROM 
                        CreditoSolicitud cs
                    WHERE 
                        1=1
                        {filtro}
                    ORDER BY cs.FechaCreacion DESC
                    OFFSET @saltarRegistros ROWS
                    FETCH NEXT @cantidadRegistros ROWS ONLY";

        string queryCantidad = $@"
                            SELECT COUNT(*) 
                            FROM 
                                CreditoSolicitud cs
                            WHERE 
                                1=1
                                {filtro}";

        try
        {
            using (var connection = this._conexion.CreateSqlConnection())
            {
                var parametros = new DynamicParameters();
                parametros.Add("@modeloSolicitado", request.ModeloSolicitado);
                parametros.Add("@fechaCreacion", request.FechaCreacion);
                parametros.Add("@fechaInicio", request.FechaInicio);
                parametros.Add("@fechaFin", request.FechaFin);
                parametros.Add("@terminoBusqueda", request.TerminoDeBusqueda);
                parametros.Add("@saltarRegistros", saltarRegistros);
                parametros.Add("@cantidadRegistros", request.CantidadRegistros);

                var totalRegistros = await connection.ExecuteScalarAsync<int>(queryCantidad, parametros);

                var resultado = await connection.QueryAsync<SolicitudesdeCreditoDTO>(query, parametros);

                if (!resultado.Any())
                {
                    throw new NoDataFoundException("No se encontraron solicitudes de crédito con los criterios proporcionados.");
                }

                var listado = new Datos<IEnumerable<SolicitudesdeCreditoDTO>>
                {
                    Items = resultado,
                    TotalRegistros = totalRegistros
                };

                _logger.LogInformation("Fin de Proceso de obtener lista de solicitudes de crédito");

                return listado;
            }
        }
        catch (NoDataFoundException ex)
        {
            _logger.LogWarning(ex, "No se encontraron solicitudes de crédito con los criterios proporcionados.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ocurrió un error inesperado al obtener lista de solicitudes de crédito");
            throw new RepositoryException("Ocurrió un error inesperado al obtener lista de solicitudes de crédito", ex);
        }
    }




}

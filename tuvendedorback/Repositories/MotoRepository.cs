﻿using Dapper;
using tuvendedorback.Data;
using tuvendedorback.DTOs;
using tuvendedorback.Exceptions;
using tuvendedorback.Models;
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

    public async Task<int> GuardarSolicitudCredito(SolicitudCredito solicitud)
    {
        _logger.LogInformation("Inicio del proceso para guardar la solicitud de crédito");

        string queryInsertarSolicitud = @"
            INSERT INTO CreditoSolicitud 
            (ModeloSolicitado, EntregaInicial, CantidadCuotas, MontoPorCuota, CedulaIdentidad, TelefonoMovil, FechaNacimiento, Barrio, Ciudad, DireccionParticular)
            VALUES 
            (@ModeloSolicitado, @EntregaInicial, @CantidadCuotas, @MontoPorCuota, @Cedula, @TelefonoMovil, @FechaNacimiento, @Barrio, @Ciudad, @DireccionParticular);
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


}

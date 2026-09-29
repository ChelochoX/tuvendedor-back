using tuvendedorback.DTOs;
using tuvendedorback.Exceptions;
using tuvendedorback.Repositories.Interfaces;
using tuvendedorback.Request;
using tuvendedorback.Services.Interfaces;

namespace tuvendedorback.Services;

public class DescuentoContadoMotoService
    : IDescuentoContadoMotoService
{
    private readonly IDescuentoContadoMotoRepository _repository;

    public DescuentoContadoMotoService(
        IDescuentoContadoMotoRepository repository)
    {
        _repository = repository;
    }


    // =========================================================
    // MARCAS
    // =========================================================

    public Task<IReadOnlyList<DescuentoContadoMarcaDto>> ListarMarcas()
    {
        return _repository.ListarMarcas();
    }


    // =========================================================
    // OBTENER CONFIGURACION
    // =========================================================

    public async Task<DescuentoContadoConfiguracionDto> ObtenerConfiguracion(
        int idMarca,
        int anio,
        int mes)
    {
        ValidarMarca(
            idMarca);

        ValidarPeriodo(
            anio,
            mes);


        var fechaDesde =
            new DateTime(
                anio,
                mes,
                1);


        var fechaHasta =
            fechaDesde
                .AddMonths(1)
                .AddDays(-1);


        var data =
            await _repository.ObtenerConfiguracion(
                idMarca,
                anio,
                mes,
                fechaDesde,
                fechaHasta);


        if (data == null)
        {
            throw new ReglasdeNegocioException(
                "No se encontró la marca indicada o no se encuentra activa.");
        }


        return data;
    }


    // =========================================================
    // GUARDAR CONFIGURACION
    // =========================================================

    public async Task<DescuentoContadoConfiguracionDto>
        GuardarConfiguracion(
            GuardarDescuentoContadoMesRequest request)
    {
        if (request == null)
        {
            throw new ReglasdeNegocioException(
                "Debe indicar la configuración de descuentos.");
        }


        ValidarMarca(
            request.IdMarca);


        ValidarPeriodo(
            request.Anio,
            request.Mes);


        ValidarPorcentaje(
            request.PorcentajeGeneral,
            "El descuento general");


        request.Excepciones ??=
            new List<GuardarDescuentoContadoModeloRequest>();


        // =====================================================
        // DUPLICADOS
        // =====================================================

        var tieneDuplicados =
            request.Excepciones
                .GroupBy(
                    x =>
                        x.IdModeloProducto)
                .Any(
                    g =>
                        g.Count() > 1);


        if (tieneDuplicados)
        {
            throw new ReglasdeNegocioException(
                "No se puede cargar más de una excepción para el mismo modelo.");
        }


        var configuracionActual =
            await ObtenerConfiguracion(
                request.IdMarca,
                request.Anio,
                request.Mes);


        var modelosValidos =
            configuracionActual.Modelos
                .Select(
                    x =>
                        x.IdModeloProducto)
                .ToHashSet();


        var excepcionesGuardar =
            new List<DescuentoContadoExcepcionGuardarDto>();


        foreach (var excepcion in request.Excepciones)
        {
            if (excepcion.IdModeloProducto <= 0)
            {
                throw new ReglasdeNegocioException(
                    "Existe una excepción con un modelo inválido.");
            }


            if (!modelosValidos.Contains(
                    excepcion.IdModeloProducto))
            {
                throw new ReglasdeNegocioException(
                    $"El modelo {excepcion.IdModeloProducto} no pertenece a la marca seleccionada o no está activo.");
            }


            ValidarPorcentaje(
                excepcion.PorcentajeDescuento,
                "El descuento de la excepción");


            // Si tiene el mismo porcentaje que la regla general,
            // no hace falta guardar una excepción.
            if (excepcion.PorcentajeDescuento
                == request.PorcentajeGeneral)
            {
                continue;
            }


            excepcionesGuardar.Add(
                new DescuentoContadoExcepcionGuardarDto
                {
                    IdModeloProducto =
                        excepcion.IdModeloProducto,

                    PorcentajeDescuento =
                        excepcion.PorcentajeDescuento
                });
        }


        var fechaDesde =
            new DateTime(
                request.Anio,
                request.Mes,
                1);


        var fechaHasta =
            fechaDesde
                .AddMonths(1)
                .AddDays(-1);


        await _repository.GuardarConfiguracionMes(
            request.IdMarca,
            fechaDesde,
            fechaHasta,
            request.PorcentajeGeneral,
            excepcionesGuardar);


        var resultado =
            await _repository.ObtenerConfiguracion(
                request.IdMarca,
                request.Anio,
                request.Mes,
                fechaDesde,
                fechaHasta);


        if (resultado == null)
        {
            throw new ReglasdeNegocioException(
                "La configuración fue guardada, pero no pudo recuperarse.");
        }


        return resultado;
    }

    // =========================================================
    // CREAR MODELO CON EXCEPCION
    // =========================================================

    public async Task<DescuentoContadoConfiguracionDto>
        CrearModeloConExcepcion(
            CrearModeloConExcepcionDescuentoRequest request)
    {
        if (request == null)
        {
            throw new ReglasdeNegocioException(
                "Debe indicar los datos del modelo.");
        }


        ValidarMarca(
            request.IdMarca);


        ValidarPeriodo(
            request.Anio,
            request.Mes);


        if (string.IsNullOrWhiteSpace(
                request.CodigoReferencia))
        {
            throw new ReglasdeNegocioException(
                "El código de referencia es obligatorio.");
        }


        request.CodigoReferencia =
            request.CodigoReferencia
                .Trim()
                .ToUpperInvariant();


        if (request.CodigoReferencia.Length > 50)
        {
            throw new ReglasdeNegocioException(
                "El código de referencia no puede superar 50 caracteres.");
        }


        if (string.IsNullOrWhiteSpace(
                request.NombreModelo))
        {
            throw new ReglasdeNegocioException(
                "El nombre del modelo es obligatorio.");
        }


        request.NombreModelo =
            request.NombreModelo
                .Trim()
                .ToUpperInvariant();


        if (request.NombreModelo.Length > 150)
        {
            throw new ReglasdeNegocioException(
                "El nombre del modelo no puede superar 150 caracteres.");
        }


        if (request.Cilindrada.HasValue
            &&
            request.Cilindrada.Value <= 0)
        {
            throw new ReglasdeNegocioException(
                "La cilindrada debe ser mayor a cero.");
        }


        if (!string.IsNullOrWhiteSpace(
                request.Categoria))
        {
            request.Categoria =
                request.Categoria
                    .Trim()
                    .ToUpperInvariant();


            if (request.Categoria.Length > 50)
            {
                throw new ReglasdeNegocioException(
                    "La categoría no puede superar 50 caracteres.");
            }
        }


        ValidarPorcentaje(
            request.PorcentajeDescuento,
            "El descuento de la excepción");


        // =====================================================
        // CONTROLAR QUE EXISTA DESCUENTO GENERAL
        // =====================================================

        var configuracionActual =
            await ObtenerConfiguracion(
                request.IdMarca,
                request.Anio,
                request.Mes);


        if (!configuracionActual
                .PorcentajeGeneral
                .HasValue)
        {
            throw new ReglasdeNegocioException(
                "Primero debe existir un descuento general para el período seleccionado.");
        }


        if (request.PorcentajeDescuento
            ==
            configuracionActual
                .PorcentajeGeneral
                .Value)
        {
            throw new ReglasdeNegocioException(
                "El porcentaje de excepción debe ser diferente al descuento general.");
        }


        var fechaDesde =
            new DateTime(
                request.Anio,
                request.Mes,
                1);


        var fechaHasta =
            fechaDesde
                .AddMonths(1)
                .AddDays(-1);


        await _repository
            .CrearModeloConExcepcion(
                request.IdMarca,
                request.CodigoReferencia,
                request.NombreModelo,
                request.Cilindrada,
                request.Categoria,
                fechaDesde,
                fechaHasta,
                request.PorcentajeDescuento);


        var resultado =
            await _repository
                .ObtenerConfiguracion(
                    request.IdMarca,
                    request.Anio,
                    request.Mes,
                    fechaDesde,
                    fechaHasta);


        if (resultado == null)
        {
            throw new ReglasdeNegocioException(
                "El modelo fue creado pero no pudo recuperarse la configuración.");
        }


        return resultado;
    }


    // =========================================================
    // VALIDACIONES
    // =========================================================

    private static void ValidarMarca(
        int idMarca)
    {
        if (idMarca <= 0)
        {
            throw new ReglasdeNegocioException(
                "La marca indicada no es válida.");
        }
    }


    private static void ValidarPeriodo(
        int anio,
        int mes)
    {
        if (anio < 2020
            || anio > 2100)
        {
            throw new ReglasdeNegocioException(
                "El año indicado no es válido.");
        }


        if (mes < 1
            || mes > 12)
        {
            throw new ReglasdeNegocioException(
                "El mes indicado no es válido.");
        }
    }


    private static void ValidarPorcentaje(
        decimal porcentaje,
        string nombre)
    {
        if (porcentaje < 0
            || porcentaje > 100)
        {
            throw new ReglasdeNegocioException(
                $"{nombre} debe estar entre 0 y 100.");
        }
    }
}
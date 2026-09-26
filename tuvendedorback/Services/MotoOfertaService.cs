using System.Globalization;
using tuvendedorback.Common;
using tuvendedorback.DTOs;
using tuvendedorback.Exceptions;
using tuvendedorback.Repositories.Interfaces;
using tuvendedorback.Services.Interfaces;

namespace tuvendedorback.Services;

public class MotoOfertaService : IMotoOfertaService
{
    private readonly IMotoOfertaRepository _repository;

    public MotoOfertaService(
        IMotoOfertaRepository repository)
    {
        _repository = repository;
    }


    // =========================================================
    // OFERTA POR PUBLICACION
    // =========================================================

    public async Task<MotoOfertaDto> ObtenerOfertaPorPublicacion(
        int idPublicacion)
    {
        if (idPublicacion <= 0)
        {
            throw new ReglasdeNegocioException(
                "El identificador de la publicación no es válido.");
        }

        var fechaActual =
            DateTime.Today;

        var data =
            await _repository.ObtenerOfertaPorPublicacion(
                idPublicacion,
                fechaActual);

        if (data == null)
        {
            throw new ReglasdeNegocioException(
                "No se encontró un precio normal activo para el modelo asociado a esta publicación.");
        }

        return await ConstruirOferta(
            data);
    }


    // =========================================================
    // OFERTA POR MODELO
    //
    // Esta es la ruta principal para WhatsApp/IA.
    // La cotización NO depende de que exista publicación.
    // =========================================================

    public async Task<MotoOfertaDto> ObtenerOfertaPorModelo(
        int idModeloProducto)
    {
        if (idModeloProducto <= 0)
        {
            throw new ReglasdeNegocioException(
                "El identificador del modelo no es válido.");
        }

        var fechaActual =
            DateTime.Today;

        var data =
            await _repository.ObtenerOfertaPorModelo(
                idModeloProducto,
                fechaActual);

        if (data == null)
        {
            throw new ReglasdeNegocioException(
                "No se encontró un precio normal activo para este modelo.");
        }

        return await ConstruirOferta(
            data);
    }


    // =========================================================
    // CONSTRUIR OFERTA
    //
    // IMPORTANTE:
    // - Un modelo puede tener precio normal SIN promo.
    // - La promo es opcional.
    // - Un modelo puede tener precio contado aunque no tenga
    //   ningún plan de financiación cargado.
    // =========================================================

    private async Task<MotoOfertaDto> ConstruirOferta(
        MotoOfertaDataDto data)
    {
        var planes =
            await _repository.ObtenerPlanesPorListaPrecio(
                data.IdListaCredito);

        return new MotoOfertaDto
        {
            PublicacionId =
                data.PublicacionId,

            Modelo =
                new MotoModeloOfertaDto
                {
                    Id =
                        data.ModeloId,

                    Marca =
                        data.Marca,

                    Nombre =
                        data.NombreModelo,

                    CodigoReferencia =
                        data.CodigoReferencia,

                    Cilindrada =
                        data.Cilindrada
                },

            Contado =
                new MotoContadoOfertaDto
                {
                    PrecioLista =
                        data.PrecioPublico,

                    PrecioListaFormateado =
                        FormatearGuaranies(
                            data.PrecioPublico),

                    PorcentajeDescuento =
                        data.PorcentajeDescuento,

                    PorcentajeDescuentoFormateado =
                        data.PorcentajeDescuento.HasValue
                            ? FormatearPorcentaje(
                                data.PorcentajeDescuento.Value)
                            : null,

                    PrecioFinal =
                        data.PrecioContadoFinal,

                    PrecioFinalFormateado =
                        data.PrecioContadoFinal.HasValue
                            ? FormatearGuaranies(
                                data.PrecioContadoFinal.Value)
                            : FormatearGuaranies(
                                data.PrecioPublico)
                },

            Credito =
                new MotoCreditoOfertaDto
                {
                    TienePromo =
                        data.TienePromoCredito,

                    Tipo =
                        data.TipoCredito,

                    FechaDesde =
                        FechaHelper.Formatear(
                            data.FechaDesde),

                    FechaHasta =
                        FechaHelper.Formatear(
                            data.FechaHasta),

                    Planes =
                        planes
                            .Select(
                                plan =>
                                    new MotoPlanOfertaDto
                                    {
                                        EntregaInicial =
                                            plan.EntregaInicial,

                                        EntregaInicialFormateada =
                                            FormatearGuaranies(
                                                plan.EntregaInicial),

                                        CantidadCuotas =
                                            plan.CantidadCuotas,

                                        ImporteCuota =
                                            plan.ImporteCuota,

                                        ImporteCuotaFormateado =
                                            FormatearGuaranies(
                                                plan.ImporteCuota),

                                        Interes =
                                            plan.Interes,

                                        CodigoPlan =
                                            plan.CodigoPlan,

                                        Resumen =
                                            $"{plan.CantidadCuotas} cuotas de " +
                                            $"{FormatearGuaranies(plan.ImporteCuota)}"
                                    })
                            .ToList()
                }
        };
    }


    private static string FormatearGuaranies(
        decimal importe)
    {
        var culture =
            new CultureInfo("es-PY");

        return
            $"Gs. {importe.ToString("N0", culture)}";
    }


    private static string FormatearPorcentaje(
        decimal porcentaje)
    {
        if (porcentaje ==
            decimal.Truncate(porcentaje))
        {
            return
                $"{decimal.Truncate(porcentaje):0}%";
        }

        return
            $"{porcentaje:0.##}%";
    }
}

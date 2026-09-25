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
                "No se encontró una oferta comercial activa para esta publicación.");
        }


        var planes =
            await _repository.ObtenerPlanesPorListaPrecio(
                data.IdListaCredito);


        if (planes.Count == 0)
        {
            throw new ReglasdeNegocioException(
                "No se encontraron planes de financiación activos para este modelo.");
        }


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
                            : null
                },


            Credito =
                new MotoCreditoOfertaDto
                {
                    TienePromo =
                        data.TienePromoCredito,

                    Tipo =
                        data.TipoCredito,


                    /*
                     * Formato visible:
                     * dd/MM/yyyy
                     */
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
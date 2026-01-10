using FluentValidation;

namespace tuvendedorback.Request;

public class CrearListaPrecioProductoRequest
{
    public int IdModeloProducto { get; set; }
    public decimal PrecioPublico { get; set; }
    public decimal PrecioDistribuidor { get; set; }
    public decimal PrecioBase { get; set; }
    public DateTime? FechaDesde { get; set; }
    public DateTime? FechaHasta { get; set; }
    public bool EsPromo { get; set; }
}
public class CrearListaPrecioProductoRequestValidator : AbstractValidator<CrearListaPrecioProductoRequest>
{
    public CrearListaPrecioProductoRequestValidator()
    {
        RuleFor(x => x.IdModeloProducto).GreaterThan(0);

        RuleFor(x => x.PrecioPublico).GreaterThan(0);
        RuleFor(x => x.PrecioDistribuidor).GreaterThan(0);
        RuleFor(x => x.PrecioBase).GreaterThan(0);

        RuleFor(x => x.FechaDesde)
        .NotNull()
        .When(x => x.EsPromo)
        .WithMessage("FechaDesde es obligatoria para promociones.");

        RuleFor(x => x)
        .Must(x =>
            !x.EsPromo ||
            x.FechaHasta == null ||
            x.FechaHasta.Value.Date >= x.FechaDesde!.Value.Date
        )
        .WithMessage("FechaHasta no puede ser menor a FechaDesde.");
    }
}
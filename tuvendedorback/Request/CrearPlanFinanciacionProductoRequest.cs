using FluentValidation;

namespace tuvendedorback.Request;

public class CrearPlanFinanciacionProductoRequest
{
    public int IdListaPrecio { get; set; }

    public decimal EntregaInicial { get; set; }
    public int CantidadCuotas { get; set; }
    public decimal ImporteCuota { get; set; }

    public decimal? Interes { get; set; }
    public string? CodigoPlan { get; set; } // C30, C29PROMO
}
public class CrearPlanFinanciacionProductoRequestValidator : AbstractValidator<CrearPlanFinanciacionProductoRequest>
{
    public CrearPlanFinanciacionProductoRequestValidator()
    {
        RuleFor(x => x.IdListaPrecio).GreaterThan(0);
        RuleFor(x => x.EntregaInicial).GreaterThanOrEqualTo(0);
        RuleFor(x => x.CantidadCuotas).GreaterThan(0);
        RuleFor(x => x.ImporteCuota).GreaterThan(0);
        RuleFor(x => x.CodigoPlan).MaximumLength(20);
    }
}
using FluentValidation;

namespace tuvendedorback.Request;

public class EditarPlanFinanciacionProductoRequest
{
    public int Id { get; set; }
    public decimal EntregaInicial { get; set; }
    public int CantidadCuotas { get; set; }
    public decimal ImporteCuota { get; set; }
    public string? Interes { get; set; }
    public decimal? InteresParam { get; set; }
    public string? CodigoPlan { get; set; }
}

public class EditarPlanFinanciacionProductoRequestValidator
    : AbstractValidator<EditarPlanFinanciacionProductoRequest>
{
    public EditarPlanFinanciacionProductoRequestValidator()
    {
        RuleFor(x => x.Id)
            .GreaterThan(0)
            .WithMessage("El Id del plan es obligatorio.");

        RuleFor(x => x.CantidadCuotas)
            .GreaterThan(0)
            .WithMessage("La cantidad de cuotas debe ser mayor a cero.");

        RuleFor(x => x.ImporteCuota)
            .GreaterThan(0)
            .WithMessage("El importe de la cuota debe ser mayor a cero.");

        RuleFor(x => x.CodigoPlan)
            .MaximumLength(20)
            .WithMessage("El código del plan no puede superar los 20 caracteres.")
            .When(x => !string.IsNullOrWhiteSpace(x.CodigoPlan));
    }
}
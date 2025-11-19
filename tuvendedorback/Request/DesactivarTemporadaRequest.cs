using FluentValidation;

namespace tuvendedorback.Request;

public class DesactivarTemporadaRequest
{
    public int IdPublicacion { get; set; }
}
public class DesactivarTemporadaValidator : AbstractValidator<DesactivarTemporadaRequest>
{
    public DesactivarTemporadaValidator()
    {
        RuleFor(x => x.IdPublicacion)
            .GreaterThan(0)
            .WithMessage("El IdPublicacion es obligatorio.");
    }
}

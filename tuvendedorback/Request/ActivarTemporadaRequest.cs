using FluentValidation;

namespace tuvendedorback.Request;

public class ActivarTemporadaRequest
{
    public int IdPublicacion { get; set; }
    public int IdTemporada { get; set; }
}
public class ActivarTemporadaValidator : AbstractValidator<ActivarTemporadaRequest>
{
    public ActivarTemporadaValidator()
    {
        RuleFor(x => x.IdPublicacion)
        .GreaterThan(0).WithMessage("Id de publicación inválido.");

        RuleFor(x => x.IdTemporada)
            .GreaterThan(0).WithMessage("Id de temporada inválido.");
    }
}
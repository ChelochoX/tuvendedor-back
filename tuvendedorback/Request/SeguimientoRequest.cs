using FluentValidation;

namespace tuvendedorback.Request;

public class SeguimientoRequest
{
    public int IdInteresado { get; set; }
    public string Comentario { get; set; } = null!;
}

public class SeguimientoRequestValidator : AbstractValidator<SeguimientoRequest>
{
    public SeguimientoRequestValidator()
    {
        RuleFor(x => x.IdInteresado)
            .GreaterThan(0).WithMessage("Debe especificar un interesado válido para el seguimiento.");

        RuleFor(x => x.Comentario)
            .NotEmpty().WithMessage("Debe ingresar una descripción o comentario del seguimiento.");
    }
}
using FluentValidation;

namespace tuvendedorback.Request;

public class MarcarVendidoRequest
{
    public int IdPublicacion { get; set; }
}
public class MarcarVendidoRequestValidator : AbstractValidator<MarcarVendidoRequest>
{
    public MarcarVendidoRequestValidator()
    {
        RuleFor(x => x.IdPublicacion)
            .GreaterThan(0).WithMessage("El Id de la publicación es obligatorio.");
    }
}

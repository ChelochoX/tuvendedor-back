using FluentValidation;

namespace tuvendedorback.Request;

public class CrearMarcaRequest
{
    public string Nombre { get; set; } = "";
}
public class CrearMarcaRequestValidator : AbstractValidator<CrearMarcaRequest>
{
    public CrearMarcaRequestValidator()
    {
        RuleFor(x => x.Nombre)
            .NotEmpty()
            .MaximumLength(100);
    }
}

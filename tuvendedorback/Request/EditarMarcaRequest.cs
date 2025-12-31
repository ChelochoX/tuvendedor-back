using FluentValidation;

namespace tuvendedorback.Request;

public class EditarMarcaRequest
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
}

public class EditarMarcaRequestValidator : AbstractValidator<EditarMarcaRequest>
{
    public EditarMarcaRequestValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Nombre)
            .NotEmpty()
            .MaximumLength(100);
    }
}
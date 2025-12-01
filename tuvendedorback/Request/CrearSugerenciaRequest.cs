using FluentValidation;

namespace tuvendedorback.Request;

public class CrearSugerenciaRequest
{
    public string Comentario { get; set; }
}
public class CrearSugerenciaValidator : AbstractValidator<CrearSugerenciaRequest>
{
    public CrearSugerenciaValidator()
    {
        RuleFor(x => x.Comentario)
            .NotEmpty().WithMessage("El comentario es obligatorio")
            .MinimumLength(5).WithMessage("La sugerencia debe tener al menos 5 caracteres.")
            .MaximumLength(2000).WithMessage("La sugerencia no puede superar 2000 caracteres.");
    }
}

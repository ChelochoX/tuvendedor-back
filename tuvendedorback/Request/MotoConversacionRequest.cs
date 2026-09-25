using FluentValidation;

namespace tuvendedorback.Request;

public class MotoConversacionRequest
{
    public string Telefono { get; set; } = string.Empty;

    public string Mensaje { get; set; } = string.Empty;

    public int? IdPublicacion { get; set; }
}


public class MotoConversacionRequestValidator
    : AbstractValidator<MotoConversacionRequest>
{
    public MotoConversacionRequestValidator()
    {
        RuleFor(x => x.Telefono)
            .NotEmpty()
            .WithMessage(
                "El identificador del contacto es obligatorio.")
            .MaximumLength(100)
            .WithMessage(
                "El identificador del contacto no es válido.");


        RuleFor(x => x.Mensaje)
            .NotEmpty()
            .WithMessage(
                "El mensaje es obligatorio.")
            .MaximumLength(6000)
            .WithMessage(
                "El mensaje supera la longitud permitida.");


        When(
            x => x.IdPublicacion.HasValue,
            () =>
            {
                RuleFor(x => x.IdPublicacion!.Value)
                    .GreaterThan(0)
                    .WithMessage(
                        "El identificador de publicación no es válido.");
            });
    }
}

using FluentValidation;

namespace tuvendedorback.Request;

public class CambiarClaveRequest
{
    public string? Email { get; set; }
    public string? UsuarioLogin { get; set; }
    public string NuevaClave { get; set; }
    public string ConfirmarClave { get; set; }
}

public class CambiarClaveRequestValidator : AbstractValidator<CambiarClaveRequest>
{
    public CambiarClaveRequestValidator()
    {
        RuleFor(x => x)
            .Must(x => !string.IsNullOrEmpty(x.Email) || !string.IsNullOrEmpty(x.UsuarioLogin))
            .WithMessage("Debe especificar el correo electrónico o el nombre de usuario.");

        When(x => !string.IsNullOrEmpty(x.Email), () =>
        {
            RuleFor(x => x.Email)
                .EmailAddress().WithMessage("Debe ingresar un email válido.");
        });

        RuleFor(x => x.NuevaClave)
            .NotEmpty().MinimumLength(6)
            .WithMessage("La nueva clave debe tener al menos 6 caracteres.");

        RuleFor(x => x.ConfirmarClave)
            .Equal(x => x.NuevaClave)
            .WithMessage("Las contraseñas no coinciden.");
    }
}
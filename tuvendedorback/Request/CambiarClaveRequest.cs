using FluentValidation;

namespace tuvendedorback.Request;

public class CambiarClaveRequest
{
    public string Email { get; set; }
    public string ClaveActual { get; set; }
    public string NuevaClave { get; set; }
    public string ConfirmarClave { get; set; }
}

public class CambiarClaveRequestValidator : AbstractValidator<CambiarClaveRequest>
{
    public CambiarClaveRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.ClaveActual).NotEmpty().WithMessage("Debe ingresar su clave actual.");
        RuleFor(x => x.NuevaClave).MinimumLength(6).WithMessage("La nueva clave debe tener al menos 6 caracteres.");
        RuleFor(x => x.ConfirmarClave)
            .Equal(x => x.NuevaClave).WithMessage("Las contraseñas no coinciden.");
    }
}
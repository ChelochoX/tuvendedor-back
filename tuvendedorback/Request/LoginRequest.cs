using FluentValidation;

namespace tuvendedorback.Request;

public class LoginRequest
{
    public string Email { get; set; }
    public string Clave { get; set; }
}
public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("El correo electrónico es obligatorio.")
            .EmailAddress().WithMessage("El correo electrónico no tiene un formato válido.");

        RuleFor(x => x.Clave)
            .NotEmpty().WithMessage("La contraseña es obligatoria.");
    }
}
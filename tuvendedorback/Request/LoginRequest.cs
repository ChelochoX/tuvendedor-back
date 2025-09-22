using FluentValidation;

namespace tuvendedorback.Request;

public class LoginRequest
{
    public string Email { get; set; }
    public string Clave { get; set; }
    public string TipoLogin { get; set; }

    // Datos opcionales desde proveedor
    public string? Nombre { get; set; }
    public string? FotoUrl { get; set; }
    public string? ProveedorId { get; set; }
}
public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .When(x => x.TipoLogin != "clasico")
            .WithMessage("El correo electrónico es obligatorio.")
            .EmailAddress().When(x => x.TipoLogin != "clasico")
            .WithMessage("El correo electrónico no tiene un formato válido.");

        RuleFor(x => x.Clave)
            .NotEmpty()
            .When(x => x.TipoLogin == "clasico")
            .WithMessage("La contraseña es obligatoria para login clásico.");

        RuleFor(x => x.ProveedorId)
            .NotEmpty()
            .When(x => x.TipoLogin == "google" || x.TipoLogin == "facebook")
            .WithMessage("El identificador del proveedor es obligatorio para login externo.");
    }
}
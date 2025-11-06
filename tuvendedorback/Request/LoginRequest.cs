using FluentValidation;

namespace tuvendedorback.Request;

public class LoginRequest
{
    public string? Email { get; set; }
    public string? Clave { get; set; }
    public string? TipoLogin { get; set; }
    public string? UsuarioLogin { get; set; }

    // Datos opcionales desde proveedor
    public string? Nombre { get; set; }
    public string? FotoUrl { get; set; }
    public string? ProveedorId { get; set; }
}
public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        // Para login clásico: debe venir Email o UsuarioLogin
        When(x => x.TipoLogin == "clasico", () =>
        {
            RuleFor(x => x.Clave)
                .NotEmpty().WithMessage("La contraseña es obligatoria para login clásico.");

            RuleFor(x => x)
                .Must(x => !string.IsNullOrEmpty(x.Email) || !string.IsNullOrEmpty(x.UsuarioLogin))
                .WithMessage("Debe ingresar correo o usuario.");
        });

        // Para login externo: Email obligatorio y válido
        When(x => x.TipoLogin != "clasico", () =>
        {
            RuleFor(x => x.Email)
                .NotEmpty().WithMessage("El correo electrónico es obligatorio.")
                .EmailAddress().WithMessage("El correo electrónico no tiene un formato válido.");

            RuleFor(x => x.ProveedorId)
                .NotEmpty().WithMessage("El identificador del proveedor es obligatorio para login externo.");
        });
    }
}
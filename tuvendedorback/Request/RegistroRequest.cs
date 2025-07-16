using FluentValidation;

namespace tuvendedorback.Request;

public class RegistroRequest
{
    public string NombreUsuario { get; set; }
    public string Email { get; set; }
    public string Clave { get; set; }
    public int IdRol { get; set; }
}

public class RegistroRequestValidator : AbstractValidator<RegistroRequest>
{
    public RegistroRequestValidator()
    {
        RuleFor(x => x.NombreUsuario).NotEmpty().WithMessage("El nombre de usuario es obligatorio.");
        RuleFor(x => x.Email).NotEmpty().EmailAddress().WithMessage("El correo electrónico no es válido.");
        RuleFor(x => x.Clave).MinimumLength(6).WithMessage("La clave debe tener al menos 6 caracteres.");
        RuleFor(x => x.IdRol).GreaterThan(0).WithMessage("Debe seleccionar un rol válido.");
    }
}
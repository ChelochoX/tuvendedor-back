using FluentValidation;

namespace tuvendedorback.Request;

public class RegistroRequest
{
    public string NombreUsuario { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string? Clave { get; set; } = null!;
    public string? Proveedor { get; set; }
    public string? ProveedorId { get; set; }
    public string? FotoPerfil { get; set; }

    public string? Telefono { get; set; }
    public string? Ciudad { get; set; }
    public string? Direccion { get; set; }

    public bool EsVendedor { get; set; }
    public string? NombreNegocio { get; set; }
    public string? Ruc { get; set; }
    public string? Rubro { get; set; }
}

public class RegistroRequestValidator : AbstractValidator<RegistroRequest>
{
    public RegistroRequestValidator()
    {
        RuleFor(x => x.NombreUsuario)
            .NotEmpty().WithMessage("El nombre de usuario es obligatorio.");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("El correo electrónico es obligatorio.")
            .EmailAddress().WithMessage("Debe ingresar un correo electrónico válido.");

        RuleFor(x => x.Clave)
            .NotEmpty().WithMessage("La contraseña es obligatoria.")
            .MinimumLength(6).WithMessage("La contraseña debe tener al menos 6 caracteres.")
            .When(x => string.IsNullOrWhiteSpace(x.Proveedor));

        RuleFor(x => x.ProveedorId)
            .MaximumLength(255)
            .When(x => !string.IsNullOrEmpty(x.Proveedor));

        RuleFor(x => x.Proveedor)
            .MaximumLength(50)
            .When(x => !string.IsNullOrEmpty(x.Proveedor));

        RuleFor(x => x.Telefono)
            .MaximumLength(20).WithMessage("El teléfono no puede tener más de 20 caracteres.")
            .When(x => !string.IsNullOrWhiteSpace(x.Telefono));

        RuleFor(x => x.Ciudad)
            .MaximumLength(100).WithMessage("La ciudad no puede tener más de 100 caracteres.")
            .When(x => !string.IsNullOrWhiteSpace(x.Ciudad));

        RuleFor(x => x.Direccion)
            .MaximumLength(200).WithMessage("La dirección no puede tener más de 200 caracteres.")
            .When(x => !string.IsNullOrWhiteSpace(x.Direccion));

        When(x => x.EsVendedor, () =>
        {
            RuleFor(x => x.NombreNegocio)
                .NotEmpty().WithMessage("El nombre del negocio es obligatorio para vendedores.");

            RuleFor(x => x.Ruc)
                .NotEmpty().WithMessage("El RUC es obligatorio para vendedores.");

            RuleFor(x => x.Rubro)
                .NotEmpty().WithMessage("El rubro es obligatorio para vendedores.");
        });
    }
}
using FluentValidation;

namespace tuvendedorback.Request;

public class InteresadoRequest
{
    public string Nombre { get; set; } = null!;
    public string? Telefono { get; set; }
    public string? Email { get; set; }
    public string? Ciudad { get; set; }
    public string? ProductoInteres { get; set; }
    public DateTime? FechaProximoContacto { get; set; }
    public string? Descripcion { get; set; }
    public bool AportaIPS { get; set; }
    public int CantidadAportes { get; set; }
    public IFormFile? ArchivoConversacion { get; set; }
}

public class InteresadoRequestValidator : AbstractValidator<InteresadoRequest>
{
    public InteresadoRequestValidator()
    {
        RuleFor(x => x.Nombre)
            .NotEmpty().WithMessage("El nombre del interesado es obligatorio.")
            .MaximumLength(100).WithMessage("El nombre no puede superar los 100 caracteres.");

        RuleFor(x => x.Telefono)
            .NotEmpty().WithMessage("El teléfono es obligatorio para poder contactar al cliente.");

        RuleFor(x => x.Email)
            .EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email))
            .WithMessage("El email ingresado no es válido.");

        RuleFor(x => x.ProductoInteres)
            .NotEmpty().WithMessage("Debe especificar el producto de interés.")
            .MaximumLength(200).WithMessage("El nombre del producto es demasiado largo.");

        RuleFor(x => x.AportaIPS)
            .NotNull().WithMessage("Debe indicar si el interesado aporta a IPS.");

        RuleFor(x => x.CantidadAportes)
            .GreaterThanOrEqualTo(0).WithMessage("La cantidad de aportes no puede ser negativa.");

        RuleFor(x => x.FechaProximoContacto)
            .GreaterThan(DateTime.Now.Date).When(x => x.FechaProximoContacto.HasValue)
            .WithMessage("La fecha de próximo contacto debe ser futura.");

        RuleFor(x => x.Descripcion)
            .MaximumLength(500).WithMessage("La descripción no puede superar los 500 caracteres.");
    }
}
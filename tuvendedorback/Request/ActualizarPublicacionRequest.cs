using FluentValidation;

namespace tuvendedorback.Request;

public class ActualizarPublicacionRequest
{
    public string Titulo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public decimal Precio { get; set; }
    public string Moneda { get; set; } = "PYG";
    public string Categoria { get; set; } = string.Empty;
    public string? Ubicacion { get; set; } = string.Empty;
    public bool MostrarBotonesCompra { get; set; }

    public decimal? Latitud { get; set; }
    public decimal? Longitud { get; set; }
    public string? GoogleMapsUrl { get; set; } = string.Empty;

    public List<IFormFile>? Imagenes { get; set; }
    public List<PlanCreditoDto>? PlanCredito { get; set; }
}

public class ActualizarPublicacionRequestValidator : AbstractValidator<ActualizarPublicacionRequest>
{
    public ActualizarPublicacionRequestValidator()
    {
        RuleFor(x => x.Titulo)
            .NotEmpty()
            .MaximumLength(10000);

        RuleFor(x => x.Descripcion)
            .NotEmpty()
            .MaximumLength(10000);

        RuleFor(x => x.Precio)
            .GreaterThan(0);

        RuleFor(x => x.Categoria)
            .NotEmpty()
            .MaximumLength(250);

        RuleFor(x => x.Ubicacion)
            .MaximumLength(500)
            .When(x => !string.IsNullOrWhiteSpace(x.Ubicacion));

        RuleFor(x => x.GoogleMapsUrl)
            .MaximumLength(1000)
            .When(x => !string.IsNullOrWhiteSpace(x.GoogleMapsUrl));

        RuleFor(x => x.Latitud)
            .InclusiveBetween(-90, 90)
            .When(x => x.Latitud.HasValue);

        RuleFor(x => x.Longitud)
            .InclusiveBetween(-180, 180)
            .When(x => x.Longitud.HasValue);

        RuleFor(x => x.Imagenes)
            .Must(i => i == null || i.Count <= 10)
            .WithMessage("Máximo 10 imágenes permitidas.");

        RuleForEach(x => x.PlanCredito)
            .SetValidator(new PlanCreditoDtoValidator())
            .When(x => x.MostrarBotonesCompra && x.PlanCredito != null);
    }
}

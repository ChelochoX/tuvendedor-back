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
    public bool PermiteDelivery { get; set; }

    public decimal? Latitud { get; set; }
    public decimal? Longitud { get; set; }
    public string? GoogleMapsUrl { get; set; } = string.Empty;

    /*
      Cuando GestionarImagenes viene en true, el backend sincroniza las imágenes:
      - Conserva solo las URLs enviadas en ImagenesConservar.
      - Borra de la base las imágenes que ya no estén en esa lista.
      - Agrega las nuevas imágenes enviadas en Imagenes.
    */
    public bool GestionarImagenes { get; set; } = false;

    public List<string>? ImagenesConservar { get; set; }

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

        RuleFor(x => x.ImagenesConservar)
            .Must(i => i == null || i.Count <= 10)
            .WithMessage("Máximo 10 imágenes actuales permitidas.");

        RuleForEach(x => x.ImagenesConservar)
            .MaximumLength(1000)
            .When(x => x.ImagenesConservar != null);

        RuleFor(x => x)
            .Must(x => ((x.Imagenes?.Count ?? 0) + (x.ImagenesConservar?.Count ?? 0)) <= 10)
            .WithMessage("Máximo 10 imágenes permitidas por publicación.");

        RuleForEach(x => x.PlanCredito)
            .SetValidator(new PlanCreditoDtoValidator())
            .When(x => x.MostrarBotonesCompra && x.PlanCredito != null);
    }
}

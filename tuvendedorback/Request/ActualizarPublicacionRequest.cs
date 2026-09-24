using FluentValidation;
using Microsoft.Extensions.Options;
using tuvendedorback.Configurations;

namespace tuvendedorback.Request;

public class ActualizarPublicacionRequest
{
    public string Titulo { get; set; } =
        string.Empty;

    public string Descripcion { get; set; } =
        string.Empty;

    public decimal Precio { get; set; }

    public string Moneda { get; set; } =
        "PYG";

    public string Categoria { get; set; } =
        string.Empty;

    /*
     * Si viene null/vacío:
     * conservaremos el canal actual de la publicación.
     */
    public string? CanalPublicacion { get; set; }

    public string? Ubicacion { get; set; } =
        string.Empty;

    public bool MostrarBotonesCompra { get; set; }

    public bool PermiteDelivery { get; set; }

    public decimal? Latitud { get; set; }

    public decimal? Longitud { get; set; }

    public string? GoogleMapsUrl { get; set; } =
        string.Empty;

    /*
      Cuando GestionarImagenes viene en true,
      el backend sincroniza las imágenes:

      - Conserva las URLs enviadas en ImagenesConservar.
      - Borra las imágenes que ya no estén en esa lista.
      - Agrega los archivos enviados en Imagenes.
    */
    public bool GestionarImagenes { get; set; } =
        false;

    public List<string>? ImagenesConservar
    {
        get;
        set;
    }

    public List<IFormFile>? Imagenes
    {
        get;
        set;
    }

    public List<PlanCreditoDto>? PlanCredito
    {
        get;
        set;
    }
}

public class ActualizarPublicacionRequestValidator
    : AbstractValidator<ActualizarPublicacionRequest>
{
    public ActualizarPublicacionRequestValidator(
        IOptions<UploadOptions> uploadOptions)
    {
        var upload =
            uploadOptions.Value;

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

        RuleFor(x => x.CanalPublicacion)
            .Must(canal =>
                string.IsNullOrWhiteSpace(canal)
                ||
                canal.Equals(
                    "MARKETPLACE",
                    StringComparison.OrdinalIgnoreCase)
                ||
                canal.Equals(
                    "VITRINA",
                    StringComparison.OrdinalIgnoreCase))
            .WithMessage(
                "CanalPublicacion debe ser MARKETPLACE o VITRINA.");

        RuleFor(x => x.Ubicacion)
            .MaximumLength(500)
            .When(x =>
                !string.IsNullOrWhiteSpace(
                    x.Ubicacion));

        RuleFor(x => x.GoogleMapsUrl)
            .MaximumLength(1000)
            .When(x =>
                !string.IsNullOrWhiteSpace(
                    x.GoogleMapsUrl));

        RuleFor(x => x.Latitud)
            .InclusiveBetween(-90, 90)
            .When(x =>
                x.Latitud.HasValue);

        RuleFor(x => x.Longitud)
            .InclusiveBetween(-180, 180)
            .When(x =>
                x.Longitud.HasValue);

        RuleFor(x => x.Imagenes)
            .Must(imagenes =>
                imagenes == null ||
                imagenes.Count <=
                upload.MaxFiles)
            .WithMessage(
                $"Máximo {upload.MaxFiles} imágenes permitidas.");

        RuleForEach(x => x.Imagenes)
            .Must(archivo =>
                archivo.Length <=
                upload.MaxFileSize)
            .WithMessage(
                $"Cada archivo puede pesar como máximo " +
                $"{upload.MaxFileSize / 1024 / 1024} MB.")
            .When(x =>
                x.Imagenes != null);

        RuleFor(x => x.ImagenesConservar)
            .Must(imagenes =>
                imagenes == null ||
                imagenes.Count <=
                upload.MaxFiles)
            .WithMessage(
                $"Máximo {upload.MaxFiles} imágenes actuales permitidas.");

        RuleForEach(x => x.ImagenesConservar)
            .MaximumLength(1000)
            .When(x =>
                x.ImagenesConservar != null);

        RuleFor(x => x)
            .Must(x =>
                (x.Imagenes?.Count ?? 0)
                +
                (x.ImagenesConservar?.Count ?? 0)
                <= upload.MaxFiles)
            .WithMessage(
                $"Máximo {upload.MaxFiles} imágenes permitidas por publicación.");

        RuleForEach(x => x.PlanCredito)
            .SetValidator(
                new PlanCreditoDtoValidator())
            .When(x =>
                x.MostrarBotonesCompra &&
                x.PlanCredito != null);
    }
}
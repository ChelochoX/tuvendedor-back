using FluentValidation;
using Microsoft.Extensions.Options;
using tuvendedorback.Configurations;

namespace tuvendedorback.Request;

public class CrearPublicacionRequest
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

    public string? Ubicacion { get; set; } =
        string.Empty;

    public bool MostrarBotonesCompra { get; set; }

    public bool PermiteDelivery { get; set; }

    public decimal? Latitud { get; set; }

    public decimal? Longitud { get; set; }

    public string? GoogleMapsUrl { get; set; } =
        string.Empty;

    public List<IFormFile> Imagenes { get; set; } =
        new();

    public List<PlanCreditoDto>? PlanCredito
    {
        get;
        set;
    }
}

public class PlanCreditoDto
{
    public int Cuotas { get; set; }

    public decimal ValorCuota { get; set; }
}

public class CrearPublicacionRequestValidator
    : AbstractValidator<CrearPublicacionRequest>
{
    public CrearPublicacionRequestValidator(
        IOptions<UploadOptions> uploadOptions)
    {
        var upload = uploadOptions.Value;

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
            .When(x =>
                !string.IsNullOrWhiteSpace(
                    x.Ubicacion));

        RuleFor(x => x.GoogleMapsUrl)
            .MaximumLength(1000)
            .When(x =>
                !string.IsNullOrWhiteSpace(
                    x.GoogleMapsUrl));

        RuleFor(x => x.Imagenes)
            .NotEmpty()
            .WithMessage(
                "Debe adjuntar al menos una imagen.")

            .Must(imagenes =>
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
                $"{upload.MaxFileSize / 1024 / 1024} MB.");

        RuleForEach(x => x.PlanCredito)
            .SetValidator(
                new PlanCreditoDtoValidator())
            .When(x =>
                x.MostrarBotonesCompra);
    }
}

public class PlanCreditoDtoValidator
    : AbstractValidator<PlanCreditoDto>
{
    public PlanCreditoDtoValidator()
    {
        RuleFor(x => x.Cuotas)
            .GreaterThan(0);

        RuleFor(x => x.ValorCuota)
            .GreaterThan(0);
    }
}
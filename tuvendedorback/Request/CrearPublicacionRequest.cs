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

    /*
     * Para publicaciones de motos este valor es OBLIGATORIO.
     * Debe ser el Id real de ModelosProducto seleccionado por el usuario.
     * Nunca se infiere por título, descripción ni imagen.
     */
    public int? IdModeloProducto { get; set; }

    /*
     * MARKETPLACE o VITRINA.
     *
     * Por ahora se deja nullable para mantener compatibilidad
     * con el front actual.
     *
     * Si no viene informado, el backend utilizará MARKETPLACE.
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

        RuleFor(x => x.IdModeloProducto)
            .Must(id => id.HasValue && id.Value > 0)
            .WithMessage(
                "Para publicar una moto debe seleccionar el modelo exacto.")
            .When(x =>
                !string.IsNullOrWhiteSpace(x.Categoria)
                &&
                x.Categoria.Contains(
                    "moto",
                    StringComparison.OrdinalIgnoreCase));

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
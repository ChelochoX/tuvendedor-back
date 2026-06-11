using FluentValidation;
using tuvendedorback.Common;

namespace tuvendedorback.Request;

public class CrearBannerPublicitarioRequest : IBannerPublicitarioRequestBase
{
    public string NombreCliente { get; set; } = string.Empty;

    public string Ubicacion { get; set; } = string.Empty;

    public string Titulo { get; set; } = string.Empty;

    public string? Subtitulo { get; set; }

    public string? Descripcion { get; set; }

    public string Etiqueta { get; set; } = "Publicidad";

    public string? TextoBoton { get; set; }

    public string? UrlDestino { get; set; }

    public string? WhatsappUrl { get; set; }

    public DateTime FechaInicio { get; set; }

    public DateTime FechaFin { get; set; }

    public string Estado { get; set; } =
        BannerPublicitarioConstantes.EstadoBorrador;

    public int Orden { get; set; }

    public int Prioridad { get; set; }

    public bool EsExclusivo { get; set; }

    public bool AbrirNuevaPestana { get; set; } = true;

    public IFormFile ImagenDesktop { get; set; } = default!;

    public IFormFile? ImagenMobile { get; set; }
}

public class CrearBannerPublicitarioRequestValidator
    : AbstractValidator<CrearBannerPublicitarioRequest>
{
    public CrearBannerPublicitarioRequestValidator()
    {
        BannerPublicitarioValidatorHelper.AgregarReglasComunes(this);

        RuleFor(x => x.ImagenDesktop)
            .NotNull()
            .WithMessage("Debe adjuntar la imagen desktop del banner.")
            .Must(BannerPublicitarioValidatorHelper.EsImagenPermitida)
            .WithMessage("La imagen desktop debe ser JPG, PNG o WEBP.")
            .Must(x => BannerPublicitarioValidatorHelper.NoSuperaPeso(x, 10))
            .WithMessage("La imagen desktop no puede superar 10 MB.");

        RuleFor(x => x.ImagenMobile)
            .Must(BannerPublicitarioValidatorHelper.EsImagenPermitida)
            .WithMessage("La imagen mobile debe ser JPG, PNG o WEBP.")
            .Must(x => BannerPublicitarioValidatorHelper.NoSuperaPeso(x, 6))
            .WithMessage("La imagen mobile no puede superar 6 MB.")
            .When(x => x.ImagenMobile != null);
    }
}

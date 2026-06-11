using FluentValidation;
using tuvendedorback.Common;

namespace tuvendedorback.Request;

public class ActualizarBannerPublicitarioRequest : IBannerPublicitarioRequestBase
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

    public IFormFile? ImagenDesktop { get; set; }

    public IFormFile? ImagenMobile { get; set; }

    public bool EliminarImagenMobile { get; set; }
}

public class ActualizarBannerPublicitarioRequestValidator
    : AbstractValidator<ActualizarBannerPublicitarioRequest>
{
    public ActualizarBannerPublicitarioRequestValidator()
    {
        BannerPublicitarioValidatorHelper.AgregarReglasComunes(this);

        RuleFor(x => x.ImagenDesktop)
            .Must(BannerPublicitarioValidatorHelper.EsImagenPermitida)
            .WithMessage("La imagen desktop debe ser JPG, PNG o WEBP.")
            .Must(x => BannerPublicitarioValidatorHelper.NoSuperaPeso(x, 10))
            .WithMessage("La imagen desktop no puede superar 10 MB.")
            .When(x => x.ImagenDesktop != null);

        RuleFor(x => x.ImagenMobile)
            .Must(BannerPublicitarioValidatorHelper.EsImagenPermitida)
            .WithMessage("La imagen mobile debe ser JPG, PNG o WEBP.")
            .Must(x => BannerPublicitarioValidatorHelper.NoSuperaPeso(x, 6))
            .WithMessage("La imagen mobile no puede superar 6 MB.")
            .When(x => x.ImagenMobile != null);
    }
}

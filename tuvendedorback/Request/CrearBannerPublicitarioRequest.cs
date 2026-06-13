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

    public string TipoDestino { get; set; } =
        BannerPublicitarioConstantes.TipoDestinoWeb;

    public string? UrlDestino { get; set; }

    public bool MostrarBotonWhatsapp { get; set; }

    public string? WhatsappUrl { get; set; }

    public string TextoBotonWhatsapp { get; set; } =
        "Escribir por WhatsApp";

    public DateTime FechaInicio { get; set; }

    public DateTime FechaFin { get; set; }

    public string Estado { get; set; } =
        BannerPublicitarioConstantes.EstadoBorrador;

    public int Orden { get; set; }

    public int Prioridad { get; set; }

    public bool EsExclusivo { get; set; }

    public bool AbrirNuevaPestana { get; set; } = true;

    public IFormFile ImagenDesktop { get; set; } = default!;

    public IFormFile ImagenMobile { get; set; } = default!;
}

public class CrearBannerPublicitarioRequestValidator
    : AbstractValidator<CrearBannerPublicitarioRequest>
{
    public CrearBannerPublicitarioRequestValidator()
    {
        BannerPublicitarioValidatorHelper
            .AgregarReglasComunes(this);

        RuleFor(x => x.ImagenDesktop)
            .NotNull()
            .WithMessage(
                "Debe adjuntar la imagen desktop del banner.")
            .Must(
                BannerPublicitarioValidatorHelper
                    .EsImagenPermitida)
            .WithMessage(
                "La imagen desktop debe ser JPG, PNG o WEBP.")
            .Must(
                x => BannerPublicitarioValidatorHelper
                    .NoSuperaPeso(x, 3))
            .WithMessage(
                "La imagen desktop no puede superar 3 MB.");

        RuleFor(x => x.ImagenMobile)
            .NotNull()
            .WithMessage(
                "Debe adjuntar la imagen mobile del banner.")
            .Must(
                BannerPublicitarioValidatorHelper
                    .EsImagenPermitida)
            .WithMessage(
                "La imagen mobile debe ser JPG, PNG o WEBP.")
            .Must(
                x => BannerPublicitarioValidatorHelper
                    .NoSuperaPeso(x, 3))
            .WithMessage(
                "La imagen mobile no puede superar 3 MB.");
    }
}

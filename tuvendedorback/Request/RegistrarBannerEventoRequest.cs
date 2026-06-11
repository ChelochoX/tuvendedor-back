using FluentValidation;
using tuvendedorback.Common;

namespace tuvendedorback.Request;

public class RegistrarBannerEventoRequest
{
    public int IdBanner { get; set; }

    public string TipoEvento { get; set; } = string.Empty;

    public string Ubicacion { get; set; } = string.Empty;

    public string? Dispositivo { get; set; }

    public string? Pagina { get; set; }

    public string? VisitorId { get; set; }
}

public class RegistrarBannerEventoRequestValidator
    : AbstractValidator<RegistrarBannerEventoRequest>
{
    public RegistrarBannerEventoRequestValidator()
    {
        RuleFor(x => x.IdBanner)
            .GreaterThan(0);

        RuleFor(x => x.TipoEvento)
            .NotEmpty()
            .Must(tipo =>
                BannerPublicitarioConstantes.TiposEventosPermitidos.Contains(
                    tipo.Trim().ToUpperInvariant()))
            .WithMessage(
                "El tipo de evento debe ser IMPRESION, CLICK o WHATSAPP.");

        RuleFor(x => x.Ubicacion)
            .NotEmpty()
            .Must(BannerPublicitarioValidatorHelper.EsUbicacionPermitida)
            .WithMessage(
                "La ubicación debe ser HOME_TOP o HOME_INLINE.");

        RuleFor(x => x.Dispositivo)
            .Must(dispositivo =>
                BannerPublicitarioConstantes.DispositivosPermitidos.Contains(
                    dispositivo!.Trim().ToUpperInvariant()))
            .WithMessage(
                "El dispositivo debe ser DESKTOP, MOBILE o TABLET.")
            .When(x => !string.IsNullOrWhiteSpace(x.Dispositivo));

        RuleFor(x => x.Pagina)
            .MaximumLength(250)
            .When(x => !string.IsNullOrWhiteSpace(x.Pagina));

        RuleFor(x => x.VisitorId)
            .MaximumLength(150)
            .When(x => !string.IsNullOrWhiteSpace(x.VisitorId));
    }
}

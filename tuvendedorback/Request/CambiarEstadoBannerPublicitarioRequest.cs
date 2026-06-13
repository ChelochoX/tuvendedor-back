using FluentValidation;
using tuvendedorback.Common;

namespace tuvendedorback.Request;

public class CambiarEstadoBannerPublicitarioRequest
{
    public string Estado { get; set; } = string.Empty;
}

public class CambiarEstadoBannerPublicitarioRequestValidator
    : AbstractValidator<CambiarEstadoBannerPublicitarioRequest>
{
    public CambiarEstadoBannerPublicitarioRequestValidator()
    {
        RuleFor(x => x.Estado)
            .NotEmpty()
            .Must(
                BannerPublicitarioValidatorHelper
                    .EsEstadoEditable)
            .WithMessage(
                "El estado debe ser BORRADOR, ACTIVO, " +
                "PAUSADO o FINALIZADO.");
    }
}
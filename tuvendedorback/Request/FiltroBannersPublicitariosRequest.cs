using FluentValidation;
using tuvendedorback.Common;

namespace tuvendedorback.Request;

public class FiltroBannersPublicitariosRequest
{
    public string? Busqueda { get; set; }

    public string? Ubicacion { get; set; }

    public string? Estado { get; set; }

    public DateTime? FechaDesde { get; set; }

    public DateTime? FechaHasta { get; set; }

    public int Pagina { get; set; } = 1;

    public int RegistrosPorPagina { get; set; } = 20;
}
public class FiltroBannersPublicitariosRequestValidator
    : AbstractValidator<FiltroBannersPublicitariosRequest>
{
    public FiltroBannersPublicitariosRequestValidator()
    {
        RuleFor(x => x.Busqueda)
            .MaximumLength(150)
            .When(x => !string.IsNullOrWhiteSpace(x.Busqueda));

        RuleFor(x => x.Ubicacion)
            .Must(BannerPublicitarioValidatorHelper.EsUbicacionPermitida)
            .WithMessage(
                "La ubicación debe ser HOME_TOP o HOME_INLINE.")
            .When(x => !string.IsNullOrWhiteSpace(x.Ubicacion));

        RuleFor(x => x.Estado)
            .Must(BannerPublicitarioValidatorHelper.EsEstadoFiltroPermitido)
            .WithMessage(
                "El estado debe ser BORRADOR, ACTIVO, PAUSADO, PROGRAMADO o VENCIDO.")
            .When(x => !string.IsNullOrWhiteSpace(x.Estado));

        RuleFor(x => x.Pagina)
            .GreaterThan(0);

        RuleFor(x => x.RegistrosPorPagina)
            .InclusiveBetween(1, 100);

        RuleFor(x => x.FechaHasta)
            .GreaterThanOrEqualTo(x => x.FechaDesde)
            .When(x => x.FechaDesde.HasValue && x.FechaHasta.HasValue)
            .WithMessage(
                "La fecha hasta debe ser mayor o igual que la fecha desde.");
    }
}
using FluentValidation;
using tuvendedorback.Request;

namespace tuvendedorback.Common;

internal static class BannerPublicitarioValidatorHelper
{
    private static readonly string[] TiposImagenPermitidos =
    {
        "image/jpeg",
        "image/png",
        "image/webp"
    };

    public static void AgregarReglasComunes<T>(
        AbstractValidator<T> validator)
        where T : class, IBannerPublicitarioRequestBase
    {
        validator.RuleFor(x => x.NombreCliente)
            .NotEmpty()
            .MaximumLength(150);

        validator.RuleFor(x => x.Ubicacion)
            .NotEmpty()
            .Must(EsUbicacionPermitida)
            .WithMessage(
                "La ubicación debe ser HOME_TOP o HOME_INLINE.");

        validator.RuleFor(x => x.Titulo)
            .NotEmpty()
            .MaximumLength(180);

        validator.RuleFor(x => x.Subtitulo)
            .MaximumLength(250)
            .When(
                x => !string.IsNullOrWhiteSpace(x.Subtitulo));

        validator.RuleFor(x => x.Descripcion)
            .MaximumLength(600)
            .When(
                x => !string.IsNullOrWhiteSpace(x.Descripcion));

        validator.RuleFor(x => x.Etiqueta)
            .NotEmpty()
            .MaximumLength(50);

        validator.RuleFor(x => x.TextoBoton)
            .MaximumLength(80)
            .When(
                x => !string.IsNullOrWhiteSpace(x.TextoBoton));

        validator.RuleFor(x => x.TipoDestino)
            .NotEmpty()
            .Must(EsTipoDestinoPermitido)
            .WithMessage(
                "El tipo de destino debe ser WEB, FACEBOOK, " +
                "INSTAGRAM, WHATSAPP, VITRINA_INTERNA u OTRO.");

        validator.RuleFor(x => x.UrlDestino)
            .NotEmpty()
            .MaximumLength(1000)
            .Must(
                (request, url) =>
                    EsDestinoValido(
                        request.TipoDestino,
                        url))
            .WithMessage(
                "La URL de destino no coincide con " +
                "el tipo de destino seleccionado.");

        validator.RuleFor(x => x.WhatsappUrl)
            .MaximumLength(1000)
            .Must(EsWhatsappValido)
            .WithMessage(
                "Ingresá un número o una URL válida de WhatsApp.")
            .When(
                x => !string.IsNullOrWhiteSpace(x.WhatsappUrl));

        validator.RuleFor(x => x.WhatsappUrl)
            .NotEmpty()
            .WithMessage(
                "Ingresá el WhatsApp secundario o desactivá " +
                "el botón de WhatsApp.")
            .When(x => x.MostrarBotonWhatsapp);

        validator.RuleFor(x => x.TextoBotonWhatsapp)
            .NotEmpty()
            .MaximumLength(80)
            .When(x => x.MostrarBotonWhatsapp);

        validator.RuleFor(x => x.FechaInicio)
            .NotEmpty();

        validator.RuleFor(x => x.FechaFin)
            .NotEmpty()
            .GreaterThan(x => x.FechaInicio)
            .WithMessage(
                "La fecha fin debe ser posterior " +
                "a la fecha inicio.");

        validator.RuleFor(x => x.Estado)
            .NotEmpty()
            .Must(EsEstadoEditable)
            .WithMessage(
                "El estado debe ser BORRADOR, ACTIVO, " +
                "PAUSADO o FINALIZADO.");

        validator.RuleFor(x => x.Orden)
            .InclusiveBetween(0, 999);

        validator.RuleFor(x => x.Prioridad)
            .InclusiveBetween(0, 999);
    }

    public static bool EsImagenPermitida(IFormFile? archivo)
    {
        if (archivo == null)
            return false;

        return archivo.Length > 0
            && TiposImagenPermitidos.Contains(
                archivo.ContentType,
                StringComparer.OrdinalIgnoreCase);
    }

    public static bool NoSuperaPeso(
        IFormFile? archivo,
        int megabytes)
    {
        if (archivo == null)
            return false;

        return archivo.Length
            <= megabytes * 1024L * 1024L;
    }

    public static bool EsUbicacionPermitida(string? ubicacion)
    {
        if (string.IsNullOrWhiteSpace(ubicacion))
            return false;

        return BannerPublicitarioConstantes
            .UbicacionesPermitidas
            .Contains(
                ubicacion.Trim().ToUpperInvariant());
    }

    public static bool EsTipoDestinoPermitido(
        string? tipoDestino)
    {
        if (string.IsNullOrWhiteSpace(tipoDestino))
            return false;

        return BannerPublicitarioConstantes
            .TiposDestinoPermitidos
            .Contains(
                tipoDestino.Trim().ToUpperInvariant());
    }

    public static bool EsEstadoEditable(string? estado)
    {
        if (string.IsNullOrWhiteSpace(estado))
            return false;

        return BannerPublicitarioConstantes
            .EstadosEditables
            .Contains(
                estado.Trim().ToUpperInvariant());
    }

    public static bool EsEstadoFiltroPermitido(string? estado)
    {
        if (string.IsNullOrWhiteSpace(estado))
            return false;

        return BannerPublicitarioConstantes
            .EstadosFiltro
            .Contains(
                estado.Trim().ToUpperInvariant());
    }

    private static bool EsDestinoValido(
        string? tipoDestino,
        string? valor)
    {
        if (
            string.IsNullOrWhiteSpace(tipoDestino)
            || string.IsNullOrWhiteSpace(valor)
        )
        {
            return false;
        }

        var tipo =
            tipoDestino.Trim().ToUpperInvariant();

        var destino =
            valor.Trim();

        if (
            tipo ==
            BannerPublicitarioConstantes
                .TipoDestinoVitrinaInterna
        )
        {
            return destino.StartsWith('/');
        }

        if (
            tipo ==
            BannerPublicitarioConstantes
                .TipoDestinoWhatsapp
        )
        {
            return EsWhatsappValido(destino);
        }

        return EsUrlHttpValida(destino);
    }

    private static bool EsWhatsappValido(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return true;

        var texto =
            valor.Trim();

        if (EsUrlHttpValida(texto))
            return true;

        var numero =
            new string(
                texto
                    .Where(char.IsDigit)
                    .ToArray());

        return numero.Length >= 8;
    }

    private static bool EsUrlHttpValida(string valor)
    {
        return Uri.TryCreate(
                valor,
                UriKind.Absolute,
                out var uri)
            && (
                uri.Scheme == Uri.UriSchemeHttp
                || uri.Scheme == Uri.UriSchemeHttps
            );
    }
}

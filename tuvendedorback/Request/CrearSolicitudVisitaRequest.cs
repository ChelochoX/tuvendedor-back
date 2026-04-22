using FluentValidation;

namespace tuvendedorback.Request;

public class CrearSolicitudVisitaRequest
{
    public int IdPublicacion { get; set; }

    public string NombreInteresado { get; set; } = string.Empty;
    public string TelefonoInteresado { get; set; } = string.Empty;

    public DateTime FechaVisita { get; set; }
    public TimeSpan HoraVisita { get; set; }

    public string? Mensaje { get; set; }
}

public class CrearSolicitudVisitaRequestValidator : AbstractValidator<CrearSolicitudVisitaRequest>
{
    public CrearSolicitudVisitaRequestValidator()
    {
        RuleFor(x => x.IdPublicacion)
            .GreaterThan(0)
            .WithMessage("La publicación es obligatoria.");

        RuleFor(x => x.NombreInteresado)
            .NotEmpty()
            .WithMessage("El nombre del interesado es obligatorio.")
            .MaximumLength(200);

        RuleFor(x => x.TelefonoInteresado)
            .NotEmpty()
            .WithMessage("El teléfono o WhatsApp del interesado es obligatorio.")
            .MaximumLength(50);

        RuleFor(x => x.FechaVisita)
            .Must(fecha => fecha.Date >= DateTime.Today)
            .WithMessage("La fecha de visita no puede ser anterior a hoy.");

        RuleFor(x => x.HoraVisita)
            .NotEmpty()
            .WithMessage("La hora de visita es obligatoria.");

        RuleFor(x => x.Mensaje)
            .MaximumLength(1000)
            .When(x => !string.IsNullOrWhiteSpace(x.Mensaje));
    }
}

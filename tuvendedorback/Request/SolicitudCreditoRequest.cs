using FluentValidation;

namespace tuvendedorback.Request;

public class SolicitudCreditoRequest
{
    public string? ModeloSolicitado { get; set; }
    public DateTime? FechaCreacion { get; set; } // Para una sola fecha
    public DateTime? FechaInicio { get; set; }    // Para el rango de fechas
    public DateTime? FechaFin { get; set; }

    // Propiedades para búsqueda y paginación
    public string? TerminoDeBusqueda { get; set; }
    public int Pagina { get; set; }
    public int CantidadRegistros { get; set; }
}
public class SolicitudCreditoRequestValidator : AbstractValidator<SolicitudCreditoRequest>
{
    public SolicitudCreditoRequestValidator()
    {
        RuleFor(x => x.Pagina)
            .GreaterThan(0)
            .WithMessage("El número de página debe ser mayor a cero.");

        RuleFor(x => x.CantidadRegistros)
            .GreaterThan(0)
            .WithMessage("La cantidad de registros debe ser mayor a cero.");
    }
}
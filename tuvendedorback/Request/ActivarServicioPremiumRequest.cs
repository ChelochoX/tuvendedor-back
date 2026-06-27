namespace tuvendedorback.Request;

public class ActivarServicioPremiumRequest
{
    public DateTime? FechaInicio { get; set; }

    public DateTime? FechaFin { get; set; }

    public int? DuracionDias { get; set; }

    public int? IdTemporada { get; set; }

    public string? ModoActivacionEspecial { get; set; }

    public decimal? Monto { get; set; }

    public string? MedioPago { get; set; }

    public string? ReferenciaPago { get; set; }

    public string? Observacion { get; set; }

    public string? BadgeTexto { get; set; }

    public string? BadgeColor { get; set; }
}

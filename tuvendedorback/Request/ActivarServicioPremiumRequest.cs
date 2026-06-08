namespace tuvendedorback.Request;

public class ActivarServicioPremiumRequest
{
    public DateTime? FechaInicio { get; set; }

    public DateTime? FechaFin { get; set; }

    public int? IdTemporada { get; set; }

    public decimal? Monto { get; set; }

    public string? MedioPago { get; set; }

    public string? ReferenciaPago { get; set; }

    public string? Observacion { get; set; }
}

namespace tuvendedorback.Request;

public class FiltrosServiciosPremiumRequest
{
    public string? Estado { get; set; }

    public string? TipoServicio { get; set; }

    public string? Cliente { get; set; }

    public DateTime? FechaDesde { get; set; }

    public DateTime? FechaHasta { get; set; }

    public int Pagina { get; set; } = 1;

    public int TamanioPagina { get; set; } = 10;
}

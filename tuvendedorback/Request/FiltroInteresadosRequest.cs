namespace tuvendedorback.Request;

public class FiltroInteresadosRequest
{
    public DateTime? FechaDesde { get; set; }
    public DateTime? FechaHasta { get; set; }
    public string? Nombre { get; set; }
    public string? Estado { get; set; }

    // 🔹 Paginación
    public int NumeroPagina { get; set; } = 1;
    public int RegistrosPorPagina { get; set; } = 10;
}

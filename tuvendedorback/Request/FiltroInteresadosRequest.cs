namespace tuvendedorback.Request;

public class FiltroInteresadosRequest
{
    public string? Nombre { get; set; }
    public string? Estado { get; set; }

    // 🔹 Filtros por fecha de registro
    public DateTime? FechaRegistroDesde { get; set; }
    public DateTime? FechaRegistroHasta { get; set; }

    // 🔹 Filtros por fecha del próximo contacto (seguimiento)
    public DateTime? FechaProximoContactoDesde { get; set; }
    public DateTime? FechaProximoContactoHasta { get; set; }

    // 🔹 Paginación
    public int NumeroPagina { get; set; } = 1;
    public int RegistrosPorPagina { get; set; } = 10;
}

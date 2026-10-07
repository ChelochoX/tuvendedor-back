namespace tuvendedorback.Request;

public class FiltroInteresadosRequest
{
    public string? Nombre { get; set; }

    public string? Estado { get; set; }

    public string? Origen { get; set; }

    public string? EstadoConsulta { get; set; }

    public bool SoloSeguimiento { get; set; }

    public bool SoloSinRespuesta { get; set; }

    public bool SoloSeguimientoVencido { get; set; }

    // Filtros por fecha de registro
    public DateTime? FechaRegistroDesde { get; set; }

    public DateTime? FechaRegistroHasta { get; set; }

    // Filtros por fecha del próximo contacto
    public DateTime? FechaProximoContactoDesde { get; set; }

    public DateTime? FechaProximoContactoHasta { get; set; }

    // Paginación
    public int NumeroPagina { get; set; } = 1;

    public int RegistrosPorPagina { get; set; } = 10;
}

namespace tuvendedorback.DTOs;

public class ServicioPremiumDto
{
    public int Id { get; set; }

    public int IdVendedor { get; set; }

    public int IdUsuarioVendedor { get; set; }

    public string NombreNegocio { get; set; } =
        string.Empty;

    public string? NombreUsuarioVendedor { get; set; }

    public int? IdPublicacion { get; set; }

    public string? TituloPublicacion { get; set; }

    public int? IdTemporada { get; set; }

    public string? NombreTemporada { get; set; }

    public string TipoServicio { get; set; } =
        string.Empty;

    public string Estado { get; set; } =
        string.Empty;

    public DateTime FechaSolicitud { get; set; }

    public DateTime? FechaInicio { get; set; }

    public DateTime? FechaFin { get; set; }

    public DateTime? FechaPago { get; set; }

    public decimal? Monto { get; set; }

    public string? MedioPago { get; set; }

    public string? ReferenciaPago { get; set; }

    public string? Observacion { get; set; }

    public int? IdUsuarioAdmin { get; set; }

    public string? NombreUsuarioAdmin { get; set; }
}

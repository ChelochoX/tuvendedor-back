namespace tuvendedorback.DTOs;

public class SolicitudVisitaDto
{
    public int Id { get; set; }
    public int IdPublicacion { get; set; }
    public int IdUsuarioVendedor { get; set; }

    public string TituloPublicacion { get; set; } = string.Empty;
    public string? UbicacionPublicacion { get; set; }

    public string NombreInteresado { get; set; } = string.Empty;
    public string TelefonoInteresado { get; set; } = string.Empty;

    public DateTime FechaVisita { get; set; }
    public TimeSpan HoraVisita { get; set; }

    public string? Mensaje { get; set; }
    public string Estado { get; set; } = "Pendiente";

    public bool NotificadoVendedor { get; set; }
    public DateTime? FechaNotificacion { get; set; }
    public string? ErrorNotificacion { get; set; }

    public DateTime FechaSolicitud { get; set; }
}

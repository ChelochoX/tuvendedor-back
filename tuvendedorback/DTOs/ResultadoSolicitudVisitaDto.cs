namespace tuvendedorback.DTOs;

public class ResultadoSolicitudVisitaDto
{
    public int IdSolicitudVisita { get; set; }
    public bool NotificadoVendedor { get; set; }
    public string Estado { get; set; } = "Pendiente";
}

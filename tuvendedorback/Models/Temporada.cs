namespace tuvendedorback.Models;

public class Temporada
{
    public int Id { get; set; }
    public string Nombre { get; set; }
    public string BadgeTexto { get; set; }
    public string BadgeColor { get; set; }
    public DateTime? FechaInicio { get; set; }
    public DateTime? FechaFin { get; set; }
    public string Estado { get; set; }
}

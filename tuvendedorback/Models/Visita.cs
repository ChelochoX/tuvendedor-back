namespace tuvendedorback.Models;

public class Visita
{
    public int Id { get; set; }
    public string Page { get; set; } = string.Empty;
    public int Count { get; set; }
    public DateTime LastVisited { get; set; } = DateTime.Now;
}

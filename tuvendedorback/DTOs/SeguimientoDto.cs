namespace tuvendedorback.DTOs;

public class SeguimientoDto
{
    public int Id { get; set; }
    public int IdInteresado { get; set; }
    public string Comentario { get; set; } = null!;
    public DateTime Fecha { get; set; }
    public string? Usuario { get; set; }
}

namespace tuvendedorback.DTOs;

public class InteresadoDto
{
    public int Id { get; set; }
    public string Nombre { get; set; } = null!;
    public string? Telefono { get; set; }
    public string? Email { get; set; }
    public string? Ciudad { get; set; }
    public string? ProductoInteres { get; set; }
    public bool AportaIPS { get; set; }
    public int CantidadAportes { get; set; }
    public string? Estado { get; set; }
    public DateTime FechaRegistro { get; set; }
    public DateTime? FechaProximoContacto { get; set; }
    public string? Descripcion { get; set; }
    public string? ArchivoUrl { get; set; }
    public string? UsuarioResponsable { get; set; }
}

namespace tuvendedorback.Request;

public class Class
{
    public string Nombre { get; set; } = null!;
    public string? Telefono { get; set; }
    public string? Email { get; set; }
    public string? Ciudad { get; set; }
    public string? ProductoInteres { get; set; }
    public DateTime? FechaProximoContacto { get; set; }
    public string? Descripcion { get; set; }
    public bool AportaIPS { get; set; }
    public int CantidadAportes { get; set; }
    public IFormFile? ArchivoConversacion { get; set; }
}

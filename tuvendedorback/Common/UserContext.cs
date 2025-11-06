namespace tuvendedorback.Common;

public class UserContext
{
    public int? IdUsuario { get; set; }
    public int? IdRol { get; set; }
    public string? NombreUsuario { get; set; } = string.Empty;
    public string? Ubicacion { get; set; }
    public string? Vendedor { get; set; }
}

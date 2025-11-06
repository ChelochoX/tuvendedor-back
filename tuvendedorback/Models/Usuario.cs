namespace tuvendedorback.Models;

public class Usuario
{
    public int Id { get; set; }
    public string NombreUsuario { get; set; }
    public string Email { get; set; }
    public string ClaveHash { get; set; }
    public string Estado { get; set; }
    public DateTime FechaRegistro { get; set; }

    public string? Proveedor { get; set; }
    public string? ProveedorId { get; set; }
    public string? FotoPerfil { get; set; }
    public string? Telefono { get; set; }
    public string? Ciudad { get; set; }
    public string? Direccion { get; set; }
}

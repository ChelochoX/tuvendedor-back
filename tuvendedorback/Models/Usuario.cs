namespace tuvendedorback.Models;

public class Usuario
{
    public int Id { get; set; }
    public string NombreUsuario { get; set; }
    public string Email { get; set; }
    public string ClaveHash { get; set; }
    public string Estado { get; set; }
    public DateTime FechaRegistro { get; set; }
}

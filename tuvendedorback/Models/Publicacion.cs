namespace tuvendedorback.Models;

public class Publicacion
{
    public int Id { get; set; }
    public string Titulo { get; set; }
    public string Descripcion { get; set; }
    public decimal Precio { get; set; }
    public string Categoria { get; set; }
    public string Ubicacion { get; set; }
    public bool MostrarBotonesCompra { get; set; }
    public List<PlanCredito>? PlanCredito { get; set; }
    public List<ImagenPublicacion> Imagenes { get; set; } = new();
    public string VendedorNombre { get; set; }
    public string VendedorAvatar { get; set; }
    public string VendedorTelefono { get; set; }
    public bool EsDestacada { get; set; }
    public DateTime? FechaFinDestacado { get; set; }

}

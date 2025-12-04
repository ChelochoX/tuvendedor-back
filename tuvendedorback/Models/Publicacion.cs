using tuvendedorback.Models;

public class Publicacion
{
    public int Id { get; set; }
    public string Titulo { get; set; } = "";
    public string? Descripcion { get; set; } = "";
    public decimal Precio { get; set; }
    public string Categoria { get; set; } = "";
    public string Ubicacion { get; set; } = "";
    public string Estado { get; set; }
    public bool MostrarBotonesCompra { get; set; }

    // Vendedor
    public string? VendedorNombre { get; set; }
    public string? VendedorTelefono { get; set; }

    // Destacado
    public bool EsDestacada { get; set; }
    public DateTime? FechaFinDestacado { get; set; }

    // Temporada
    public bool EsTemporada { get; set; }
    public DateTime? FechaFinTemporada { get; set; }
    public string? BadgeTexto { get; set; }
    public string? BadgeColor { get; set; }

    // Relacionados
    public List<string> Imagenes { get; set; } = new();
    public List<PlanCredito> PlanCredito { get; set; } = new();
}

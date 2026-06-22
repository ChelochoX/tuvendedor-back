using tuvendedorback.Models;

public class Publicacion
{
    public int Id { get; set; }
    public string Titulo { get; set; } = "";
    public string? Descripcion { get; set; } = "";
    public decimal Precio { get; set; }
    public string? Moneda { get; set; }
    public string Categoria { get; set; } = "";
    public string Ubicacion { get; set; } = "";
    public string Estado { get; set; }
    public bool MostrarBotonesCompra { get; set; }
    public bool PermiteDelivery { get; set; }

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

    //GPS
    public decimal? Latitud { get; set; }
    public decimal? Longitud { get; set; }
    public string? GoogleMapsUrl { get; set; }

    //ME GUSTA/VISTOS/WA
    public int CantidadFavoritos { get; set; }
    public int CantidadVistas { get; set; }
    public int CantidadClicksWhatsapp { get; set; }
    public bool EsFavorito { get; set; }

    // Relacionados
    public List<string> Imagenes { get; set; } = new();
    public List<PlanCredito> PlanCredito { get; set; } = new();
}

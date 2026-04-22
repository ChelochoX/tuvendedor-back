namespace tuvendedorback.DTOs;

public class PerfilPublicoPublicacionDto
{
    public int Id { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public decimal? Precio { get; set; }
    public string? Categoria { get; set; }
    public string? Ubicacion { get; set; }

    public decimal? Latitud { get; set; }
    public decimal? Longitud { get; set; }
    public string? GoogleMapsUrl { get; set; }

    public string? Estado { get; set; }
    public string? ImagenPrincipal { get; set; }
    public string? ThumbUrl { get; set; }
    public bool EsDestacada { get; set; }
}

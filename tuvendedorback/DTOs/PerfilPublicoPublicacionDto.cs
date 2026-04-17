namespace tuvendedorback.DTOs;

public class PerfilPublicoPublicacionDto
{
    public int Id { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public decimal Precio { get; set; }
    public string Categoria { get; set; } = string.Empty;
    public string? Ubicacion { get; set; }
    public string Estado { get; set; } = string.Empty;
    public string? ImagenPrincipal { get; set; }
    public string? ThumbUrl { get; set; }
    public bool EsDestacada { get; set; }
}

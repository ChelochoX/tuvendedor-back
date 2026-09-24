namespace tuvendedorback.DTOs;

public class ProductoCompartirDto
{
    public int Id { get; set; }

    public string Titulo { get; set; } =
        string.Empty;

    public string? Descripcion { get; set; }

    public decimal Precio { get; set; }

    public string? Categoria { get; set; }

    public string? Ubicacion { get; set; }

    public string? ImagenUrl { get; set; }

    public string CanalPublicacion { get; set; } =
        "MARKETPLACE";

    public string? SlugVendedor { get; set; }
}
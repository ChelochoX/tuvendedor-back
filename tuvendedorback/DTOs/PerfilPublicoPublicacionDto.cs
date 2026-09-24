namespace tuvendedorback.DTOs;

public class PerfilPublicoPublicacionDto
{
    public int Id { get; set; }

    public string Titulo { get; set; } =
        string.Empty;

    public string? Descripcion { get; set; }

    public decimal? Precio { get; set; }

    public string? Moneda { get; set; }

    public string? Categoria { get; set; }

    public string? Ubicacion { get; set; }

    public decimal? Latitud { get; set; }

    public decimal? Longitud { get; set; }

    public string? GoogleMapsUrl { get; set; }

    public string? Estado { get; set; }

    public string CanalPublicacion { get; set; } =
        "VITRINA";

    public bool MostrarBotonesCompra { get; set; }

    public bool PermiteDelivery { get; set; }

    public string? ImagenPrincipal { get; set; }

    public string? ThumbUrl { get; set; }

    public List<string> Imagenes { get; set; } =
        new();

    public bool EsDestacada { get; set; }

    public DateTime? FechaFinDestacado { get; set; }

    public bool EsTemporada { get; set; }

    public DateTime? FechaFinTemporada { get; set; }

    public string? BadgeTexto { get; set; }

    public string? BadgeColor { get; set; }

    public PlanCreditoDto? PlanCredito { get; set; }

    public int CantidadFavoritos { get; set; }

    public int CantidadVistas { get; set; }

    public int CantidadClicksWhatsapp { get; set; }
}
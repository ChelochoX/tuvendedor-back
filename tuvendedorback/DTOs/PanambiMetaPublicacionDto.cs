namespace tuvendedorback.DTOs;

public sealed class PanambiMetaPublicacionRequest
{
    public string NumeroWhatsapp { get; set; } = string.Empty;
    public string ImagenUrl { get; set; } = string.Empty;
    public string Texto { get; set; } = string.Empty;
    public bool Facebook { get; set; }
    public bool Instagram { get; set; }
    // Por defecto jamás modifica las redes sociales.
    public bool Simulacion { get; set; } = true;
    // Confirmación adicional obligatoria para una publicación real.
    public string Confirmacion { get; set; } = string.Empty;
}

public sealed class PanambiMetaDestinoResultado
{
    public string Red { get; set; } = string.Empty;
    public bool Exito { get; set; }
    public string Estado { get; set; } = string.Empty;
    public string? IdPublicacion { get; set; }
    public string? Error { get; set; }
}

namespace tuvendedorback.DTOs;

public class BannerPublicitarioArchivoDto
{
    public long Id { get; set; }

    public int BannerPublicitarioId { get; set; }

    public string TipoDispositivo { get; set; } = string.Empty;

    public int Revision { get; set; }

    public string? CloudinaryAssetFolder { get; set; }

    public string? CloudinaryPublicId { get; set; }

    public string? CloudinaryAssetId { get; set; }

    public string SecureUrl { get; set; } = string.Empty;

    public string Estado { get; set; } = string.Empty;

    public DateTime FechaCreacion { get; set; }

    public DateTime? FechaEliminacionProgramada { get; set; }

    public DateTime? FechaEliminacion { get; set; }

    public int IntentosEliminacion { get; set; }

    public string? UltimoErrorEliminacion { get; set; }
}

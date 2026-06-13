namespace tuvendedorback.DTOs;

public class BannerArchivoUploadResultDto
{
    public string TipoDispositivo { get; set; } = string.Empty;

    public int Revision { get; set; }

    public string AssetFolder { get; set; } = string.Empty;

    public string PublicId { get; set; } = string.Empty;

    public string AssetId { get; set; } = string.Empty;

    public string SecureUrl { get; set; } = string.Empty;

    public int Width { get; set; }

    public int Height { get; set; }

    public long Bytes { get; set; }

    public string Format { get; set; } = string.Empty;
}

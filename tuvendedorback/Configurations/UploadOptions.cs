namespace tuvendedorback.Configurations;

public sealed class UploadOptions
{
    public const string SectionName = "Upload";

    public long MaxRequestBodySize { get; set; }

    public long MaxFileSize { get; set; }

    public int MaxFiles { get; set; }
}

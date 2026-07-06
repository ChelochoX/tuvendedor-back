namespace tuvendedorback.Configurations;

public class R2Options
{
    public string AccountId { get; set; } = string.Empty;

    public string AccessKeyId { get; set; } = string.Empty;

    public string SecretAccessKey { get; set; } = string.Empty;

    public string BucketName { get; set; } = string.Empty;

    public string PublicBaseUrl { get; set; } = string.Empty;

    public string RootFolder { get; set; } = "tuvendedor";

    public string EnvironmentFolder { get; set; } = "prod";

    public string BannerRootFolder { get; set; } = "tuvendedor/banners";

    public string BannerEnvironmentFolder { get; set; } = "prod";

    public int ImageQuality { get; set; } = 84;

    public int ThumbnailQuality { get; set; } = 80;

    public int BannerWebpQuality { get; set; } = 84;

    public void Validar()
    {
        if (string.IsNullOrWhiteSpace(AccountId))
        {
            throw new InvalidOperationException(
                "Falta configurar R2:AccountId.");
        }

        if (string.IsNullOrWhiteSpace(AccessKeyId))
        {
            throw new InvalidOperationException(
                "Falta configurar R2:AccessKeyId.");
        }

        if (string.IsNullOrWhiteSpace(SecretAccessKey))
        {
            throw new InvalidOperationException(
                "Falta configurar R2:SecretAccessKey.");
        }

        if (string.IsNullOrWhiteSpace(BucketName))
        {
            throw new InvalidOperationException(
                "Falta configurar R2:BucketName.");
        }

        if (string.IsNullOrWhiteSpace(PublicBaseUrl))
        {
            throw new InvalidOperationException(
                "Falta configurar R2:PublicBaseUrl.");
        }
    }
}

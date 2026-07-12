using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Net;
using System.Text;
using tuvendedorback.Common;
using tuvendedorback.Configurations;
using tuvendedorback.DTOs;
using tuvendedorback.Exceptions;
using tuvendedorback.Services.Interfaces;

namespace tuvendedorback.Services.Storage;

public class R2BannerImageStorageService : IBannerImageStorageService
{
    private readonly AmazonS3Client _s3;
    private readonly R2Options _options;
    private readonly ILogger<R2BannerImageStorageService> _logger;
    private readonly long _maxFileSize;
    private readonly string _rootFolder;
    private readonly string _environmentFolder;
    private readonly int _quality;

    public R2BannerImageStorageService(
        IOptions<R2Options> options,
        IOptions<UploadOptions> uploadOptions,
        IHostEnvironment environment,
        ILogger<R2BannerImageStorageService> logger)
    {
        _options = options.Value ?? new R2Options();
        _options.Validar();

        _logger = logger;

        _maxFileSize =
         uploadOptions.Value.MaxFileSize;

        _rootFolder = NormalizarCarpeta(
            string.IsNullOrWhiteSpace(_options.BannerRootFolder)
                ? "tuvendedor/banners"
                : _options.BannerRootFolder);

        _environmentFolder = NormalizarSegmento(
            string.IsNullOrWhiteSpace(_options.BannerEnvironmentFolder)
                ? (environment.IsProduction() ? "prod" : "dev")
                : _options.BannerEnvironmentFolder);

        _quality = Math.Clamp(_options.BannerWebpQuality, 70, 95);

        var credentials = new BasicAWSCredentials(
            _options.AccessKeyId,
            _options.SecretAccessKey);

        var s3Config = new AmazonS3Config
        {
            ServiceURL = $"https://{_options.AccountId}.r2.cloudflarestorage.com",
            AuthenticationRegion = "auto",
            ForcePathStyle = true
        };

        _s3 = new AmazonS3Client(credentials, s3Config);
    }

    public string ConstruirAssetFolder(
        Guid storageKey,
        string nombreCliente,
        string ubicacion)
    {
        var clienteSlug = CrearSlug(nombreCliente);

        var ubicacionSlug = CrearSlug(
            ubicacion.Replace('_', '-'));

        return
            $"{_rootFolder}/" +
            $"{_environmentFolder}/" +
            $"{clienteSlug}/" +
            $"{ubicacionSlug}/" +
            $"{storageKey:N}";
    }

    public async Task<BannerArchivoUploadResultDto> SubirImagen(
        IFormFile archivo,
        string assetFolder,
        Guid storageKey,
        string tipoDispositivo,
        int revision,
        BannerDimensionDto dimensionEsperada)
    {
        ValidarArchivo(archivo);

        if (revision <= 0)
        {
            throw new ReglasdeNegocioException(
                "La revisión del archivo debe ser mayor a cero.");
        }

        var dispositivo = tipoDispositivo
            .Trim()
            .ToUpperInvariant();

        await ValidarDimensionExacta(
            archivo,
            dimensionEsperada,
            dispositivo);

        var webpBytes = await ImagenHelper.GenerarWebPAsync(
            archivo,
            width: dimensionEsperada.Width,
            height: dimensionEsperada.Height,
            calidad: _quality,
            crop: false);

        var fileName =
            $"tv-banner-{storageKey:N}-" +
            $"{dispositivo.ToLowerInvariant()}-" +
            $"r{revision:000}.webp";

        var folder = NormalizarCarpeta(assetFolder);
        var objectKey = $"{folder}/{fileName}";

        try
        {
            var response = await SubirBytesAsync(
                objectKey,
                webpBytes,
                "image/webp");

            return new BannerArchivoUploadResultDto
            {
                TipoDispositivo = dispositivo,
                Revision = revision,
                AssetFolder = folder,

                // Por compatibilidad con tus tablas actuales:
                // CloudinaryPublicId guardará el object key de R2.
                PublicId = objectKey,

                // Guardamos el ETag como identificador técnico.
                AssetId = response.ETag?.Trim('"') ?? objectKey,

                SecureUrl = ConstruirUrlPublica(objectKey),
                Width = dimensionEsperada.Width,
                Height = dimensionEsperada.Height,
                Bytes = webpBytes.LongLength,
                Format = "webp"
            };
        }
        catch (RepositoryException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al subir imagen de banner a R2. Dispositivo={Dispositivo}, Carpeta={Carpeta}",
                dispositivo,
                assetFolder);

            throw new RepositoryException(
                "Error al subir imagen de banner a R2.",
                ex);
        }
    }

    public async Task EliminarImagen(string? publicId)
    {
        if (string.IsNullOrWhiteSpace(publicId))
        {
            return;
        }

        var objectKey = publicId.Trim().TrimStart('/');

        try
        {
            await _s3.DeleteObjectAsync(
                new DeleteObjectRequest
                {
                    BucketName = _options.BucketName,
                    Key = objectKey
                });

            _logger.LogInformation(
                "Imagen de banner eliminada de R2. Key={ObjectKey}",
                objectKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al eliminar imagen de banner en R2. Key={ObjectKey}",
                objectKey);

            throw new RepositoryException(
                "Error al eliminar imagen de banner en R2.",
                ex);
        }
    }

    private async Task<PutObjectResponse> SubirBytesAsync(
        string objectKey,
        byte[] bytes,
        string contentType)
    {
        await using var stream = new MemoryStream(bytes, writable: false);

        var request = new PutObjectRequest
        {
            BucketName = _options.BucketName,
            Key = objectKey,
            InputStream = stream,
            ContentType = contentType,
            AutoCloseStream = true,
            UseChunkEncoding = false
        };

        request.Headers.CacheControl = "public, max-age=31536000, immutable";

        var response = await _s3.PutObjectAsync(request);

        if (response.HttpStatusCode is not HttpStatusCode.OK)
        {
            throw new RepositoryException(
                $"R2 no confirmó la subida del banner. Key={objectKey}, StatusCode={response.HttpStatusCode}");
        }

        return response;
    }

    private string ConstruirUrlPublica(string objectKey)
    {
        var segmentos = objectKey
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.EscapeDataString);

        return $"{_options.PublicBaseUrl.TrimEnd('/')}/{string.Join('/', segmentos)}";
    }

    private void ValidarArchivo(IFormFile archivo)
    {
        if (archivo == null || archivo.Length <= 0)
        {
            throw new ReglasdeNegocioException(
                "La imagen del banner no puede estar vacía.");
        }

        if (archivo.Length > _maxFileSize)
        {
            throw new ReglasdeNegocioException(
                $"El archivo supera el tamaño máximo permitido de {_maxFileSize / 1024 / 1024} MB.");
        }

        var permitidos = new[]
        {
            "image/jpeg",
            "image/png",
            "image/webp"
        };

        if (!permitidos.Contains(
                archivo.ContentType,
                StringComparer.OrdinalIgnoreCase))
        {
            throw new ReglasdeNegocioException(
                "La imagen del banner debe ser JPG, PNG o WEBP.");
        }
    }

    private static async Task ValidarDimensionExacta(
        IFormFile archivo,
        BannerDimensionDto dimensionEsperada,
        string dispositivo)
    {
        await using var stream = archivo.OpenReadStream();

        var info = await SixLabors.ImageSharp.Image.IdentifyAsync(stream);

        if (info == null)
        {
            throw new ReglasdeNegocioException(
                "No fue posible leer la imagen del banner.");
        }

        if (info.Width != dimensionEsperada.Width
            || info.Height != dimensionEsperada.Height)
        {
            throw new ReglasdeNegocioException(
                $"La imagen {dispositivo} debe medir " +
                $"exactamente {dimensionEsperada.Width} × " +
                $"{dimensionEsperada.Height} px. " +
                $"El archivo enviado mide " +
                $"{info.Width} × {info.Height} px.");
        }
    }

    private static string NormalizarCarpeta(string valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return string.Empty;
        }

        return valor
            .Trim()
            .Replace("\\", "/")
            .Trim('/');
    }

    private static string NormalizarSegmento(string valor)
    {
        return CrearSlug(valor);
    }

    private static string CrearSlug(string valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return "cliente";
        }

        var texto = valor
            .Trim()
            .ToLowerInvariant()
            .Normalize(NormalizationForm.FormD);

        var builder = new StringBuilder();
        var anteriorFueGuion = false;

        foreach (var caracter in texto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(caracter)
                == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(caracter))
            {
                builder.Append(caracter);
                anteriorFueGuion = false;
            }
            else if (!anteriorFueGuion)
            {
                builder.Append('-');
                anteriorFueGuion = true;
            }
        }

        var slug = builder
            .ToString()
            .Normalize(NormalizationForm.FormC)
            .Trim('-');

        return string.IsNullOrWhiteSpace(slug)
            ? "cliente"
            : slug;
    }
}

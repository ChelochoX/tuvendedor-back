using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using System.Net;
using tuvendedorback.Common;
using tuvendedorback.Configurations;
using tuvendedorback.DTOs;
using tuvendedorback.Exceptions;
using tuvendedorback.Services.Interfaces;

namespace tuvendedorback.Services.Storage;

public class R2ImageStorageService : IImageStorageService
{
    private readonly AmazonS3Client _s3;
    private readonly R2Options _options;
    private readonly ILogger<R2ImageStorageService> _logger;
    private readonly long _maxFileSize;
    private readonly string _rootFolder;
    private readonly string _environmentFolder;
    private readonly int _imageQuality;
    private readonly int _thumbnailQuality;

    public R2ImageStorageService(
     IOptions<R2Options> options,
     IConfiguration config,
     IHostEnvironment environment,
     ILogger<R2ImageStorageService> logger)
    {
        _options = options.Value ?? new R2Options();
        _options.Validar();

        _logger = logger;

        _maxFileSize = config.GetValue<long>(
            "Upload:MaxFileSize",
            15L * 1024 * 1024);

        _rootFolder = NormalizarCarpeta(
            string.IsNullOrWhiteSpace(_options.RootFolder)
                ? "tuvendedor"
                : _options.RootFolder);

        _environmentFolder = NormalizarCarpeta(
            string.IsNullOrWhiteSpace(_options.EnvironmentFolder)
                ? (environment.IsProduction() ? "prod" : "dev")
                : _options.EnvironmentFolder);

        _imageQuality = Math.Clamp(_options.ImageQuality, 70, 95);
        _thumbnailQuality = Math.Clamp(_options.ThumbnailQuality, 65, 90);

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

    public async Task<UploadResultDto> SubirArchivo(
        IFormFile archivo,
        string carpetaDestino = "publicaciones",
        bool generarMiniatura = false)
    {
        ValidarArchivoBasico(archivo);

        carpetaDestino = NormalizarCarpeta(carpetaDestino);

        var extension = Path
            .GetExtension(archivo.FileName)
            .ToLowerInvariant();

        var resourceType = ObtenerResourceType(extension, archivo.ContentType);

        try
        {
            _logger.LogInformation(
                "Iniciando subida de archivo a R2. Nombre={Nombre}, ContentType={ContentType}, Tipo={Tipo}, Carpeta={Carpeta}, GenerarMiniatura={GenerarMiniatura}",
                archivo.FileName,
                archivo.ContentType,
                resourceType,
                carpetaDestino,
                generarMiniatura);

            return resourceType switch
            {
                StorageResourceType.Image =>
                    await SubirImagenOptimizadaAsync(
                        archivo,
                        carpetaDestino,
                        generarMiniatura),

                StorageResourceType.Video =>
                    await SubirOriginalAsync(
                        archivo,
                        carpetaDestino,
                        extension,
                        usarComoThumb: true),

                _ =>
                    await SubirOriginalAsync(
                        archivo,
                        carpetaDestino,
                        extension,
                        usarComoThumb: false)
            };
        }
        catch (ReglasdeNegocioException)
        {
            throw;
        }
        catch (RepositoryException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al subir archivo a R2. Nombre={Nombre}, Carpeta={Carpeta}",
                archivo.FileName,
                carpetaDestino);

            throw new RepositoryException("Error al subir archivo a R2.", ex);
        }
    }

    public async Task<UploadResultDto> SubirImagenOptimizada(
        IFormFile archivo,
        string carpetaDestino,
        int width,
        int height,
        int calidad = 90)
    {
        ValidarArchivoBasico(archivo);

        if (!EsImagen(archivo.ContentType))
        {
            throw new ReglasdeNegocioException(
                "El archivo enviado debe ser una imagen.");
        }

        if (width <= 0 || height <= 0)
        {
            throw new ReglasdeNegocioException(
                "Las dimensiones deben ser mayores a cero.");
        }

        carpetaDestino = NormalizarCarpeta(carpetaDestino);

        return await SubirImagenOptimizadaAsync(
            archivo,
            carpetaDestino,
            generarMiniatura: false,
            width: width,
            height: height,
            calidad: Math.Clamp(calidad, 70, 95));
    }

    public async Task EliminarArchivo(string archivoUrl)
    {
        if (string.IsNullOrWhiteSpace(archivoUrl))
        {
            _logger.LogWarning(
                "Se intentó eliminar un archivo con URL vacía o nula.");
            return;
        }

        var objectKey = ExtraerObjectKeyDesdeUrl(archivoUrl);

        if (string.IsNullOrWhiteSpace(objectKey))
        {
            _logger.LogInformation(
                "La URL no pertenece al dominio público de R2. Se omite eliminación remota. Url={Url}",
                archivoUrl);
            return;
        }

        try
        {
            await _s3.DeleteObjectAsync(
                new DeleteObjectRequest
                {
                    BucketName = _options.BucketName,
                    Key = objectKey
                });

            _logger.LogInformation(
                "Archivo eliminado de R2. Key={ObjectKey}",
                objectKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al eliminar archivo de R2. Url={Url}, Key={ObjectKey}",
                archivoUrl,
                objectKey);

            throw new RepositoryException(
                $"Error al eliminar archivo de R2. Url={archivoUrl}",
                ex);
        }
    }

    private async Task<UploadResultDto> SubirImagenOptimizadaAsync(
        IFormFile archivo,
        string carpetaDestino,
        bool generarMiniatura,
        int width = 1080,
        int height = 1080,
        int? calidad = null)
    {
        if (!EsImagen(archivo.ContentType))
        {
            throw new ReglasdeNegocioException(
                "El archivo enviado debe ser una imagen.");
        }

        var mainBytes = await ImagenHelper.GenerarWebPAsync(
            archivo,
            width: width,
            height: height,
            calidad: calidad ?? _imageQuality,
            crop: false);

        var mainKey = ConstruirObjectKey(
            carpetaDestino,
            $"{Guid.NewGuid():N}.webp");

        await SubirBytesAsync(
            mainKey,
            mainBytes,
            "image/webp");

        var mainUrl = ConstruirUrlPublica(mainKey);

        if (!generarMiniatura)
        {
            return new UploadResultDto
            {
                MainUrl = mainUrl,
                ThumbUrl = mainUrl
            };
        }

        var thumbBytes = await ImagenHelper.GenerarWebPAsync(
            archivo,
            width: 400,
            height: 300,
            calidad: _thumbnailQuality,
            crop: true);

        var thumbKey = ConstruirObjectKey(
            carpetaDestino,
            $"{Guid.NewGuid():N}_thumb.webp");

        await SubirBytesAsync(
            thumbKey,
            thumbBytes,
            "image/webp");

        return new UploadResultDto
        {
            MainUrl = mainUrl,
            ThumbUrl = ConstruirUrlPublica(thumbKey)
        };
    }

    private async Task<UploadResultDto> SubirOriginalAsync(
        IFormFile archivo,
        string carpetaDestino,
        string extension,
        bool usarComoThumb)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".bin";
        }

        var objectKey = ConstruirObjectKey(
            carpetaDestino,
            $"{Guid.NewGuid():N}{extension}");

        await using var memoryStream = new MemoryStream();
        await archivo.CopyToAsync(memoryStream);

        var bytes = memoryStream.ToArray();

        await SubirBytesAsync(
            objectKey,
            bytes,
            string.IsNullOrWhiteSpace(archivo.ContentType)
                ? "application/octet-stream"
                : archivo.ContentType);

        var url = ConstruirUrlPublica(objectKey);

        return new UploadResultDto
        {
            MainUrl = url,
            ThumbUrl = usarComoThumb ? url : string.Empty
        };
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
                $"R2 no confirmó la subida del archivo. Key={objectKey}, StatusCode={response.HttpStatusCode}");
        }

        return response;
    }

    private string ConstruirObjectKey(
        string carpetaDestino,
        string fileName)
    {
        var partes = new[]
        {
            _rootFolder,
            _environmentFolder,
            NormalizarCarpeta(carpetaDestino),
            fileName.Trim().Trim('/')
        };

        return string.Join(
            '/',
            partes.Where(parte => !string.IsNullOrWhiteSpace(parte)));
    }

    private string ConstruirUrlPublica(string objectKey)
    {
        var segmentos = objectKey
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.EscapeDataString);

        return $"{_options.PublicBaseUrl.TrimEnd('/')}/{string.Join('/', segmentos)}";
    }

    private string? ExtraerObjectKeyDesdeUrl(string archivoUrl)
    {
        if (!Uri.TryCreate(archivoUrl, UriKind.Absolute, out var archivoUri))
        {
            return null;
        }

        if (!Uri.TryCreate(_options.PublicBaseUrl, UriKind.Absolute, out var baseUri))
        {
            return null;
        }

        if (!string.Equals(
                archivoUri.Host,
                baseUri.Host,
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var basePath = baseUri.AbsolutePath.TrimEnd('/');
        var path = archivoUri.AbsolutePath;

        if (!string.IsNullOrWhiteSpace(basePath)
            && path.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
        {
            path = path[basePath.Length..];
        }

        var key = path.TrimStart('/');

        return string.IsNullOrWhiteSpace(key)
            ? null
            : Uri.UnescapeDataString(key);
    }

    private void ValidarArchivoBasico(IFormFile archivo)
    {
        if (archivo == null || archivo.Length <= 0)
        {
            throw new ReglasdeNegocioException(
                "El archivo no puede estar vacío.");
        }

        if (archivo.Length > _maxFileSize)
        {
            throw new ReglasdeNegocioException(
                $"El archivo supera el tamaño máximo permitido de {_maxFileSize / 1024 / 1024} MB.");
        }
    }

    private static bool EsImagen(string? contentType)
    {
        return !string.IsNullOrWhiteSpace(contentType)
            && contentType.StartsWith(
                "image/",
                StringComparison.OrdinalIgnoreCase);
    }

    private static StorageResourceType ObtenerResourceType(
        string extension,
        string? contentType)
    {
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                return StorageResourceType.Image;
            }

            if (contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
            {
                return StorageResourceType.Video;
            }
        }

        return extension switch
        {
            ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif" =>
                StorageResourceType.Image,

            ".mp4" or ".mov" or ".avi" or ".mkv" or ".webm" =>
                StorageResourceType.Video,

            _ =>
                StorageResourceType.Raw
        };
    }

    private static string NormalizarCarpeta(string carpeta)
    {
        if (string.IsNullOrWhiteSpace(carpeta))
        {
            return string.Empty;
        }

        return carpeta
            .Trim()
            .Replace("\\", "/")
            .Trim('/');
    }

    private enum StorageResourceType
    {
        Image,
        Video,
        Raw
    }
}

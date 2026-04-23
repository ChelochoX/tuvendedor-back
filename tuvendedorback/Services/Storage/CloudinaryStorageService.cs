using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using System.Net;
using tuvendedorback.Common;
using tuvendedorback.DTOs;
using tuvendedorback.Exceptions;
using tuvendedorback.Services.Interfaces;

namespace tuvendedorback.Services.Storage;

public class CloudinaryStorageService : IImageStorageService
{
    private readonly Cloudinary _cloudinary;
    private readonly ILogger<CloudinaryStorageService> _logger;

    public CloudinaryStorageService(IConfiguration config, ILogger<CloudinaryStorageService> logger)
    {
        var account = new Account(
            config["Cloudinary:CloudName"],
            config["Cloudinary:ApiKey"],
            config["Cloudinary:ApiSecret"]
        );

        _cloudinary = new Cloudinary(account)
        {
            Api = { Secure = true }
        };

        _logger = logger;
    }

    public async Task<UploadResultDto> SubirArchivo(
        IFormFile archivo,
        string carpetaDestino = "publicaciones",
        bool generarMiniatura = false)
    {
        if (archivo == null || archivo.Length == 0)
            throw new ReglasdeNegocioException("El archivo no puede estar vacío.");

        carpetaDestino = NormalizarCarpeta(carpetaDestino);

        var extension = Path.GetExtension(archivo.FileName).ToLowerInvariant();
        var resourceType = ObtenerResourceType(extension, archivo.ContentType);

        try
        {
            _logger.LogInformation(
                "Iniciando subida de archivo a Cloudinary. Nombre={Nombre}, ContentType={ContentType}, Tipo={Tipo}, Carpeta={Carpeta}, GenerarMiniatura={GenerarMiniatura}",
                archivo.FileName,
                archivo.ContentType,
                resourceType,
                carpetaDestino,
                generarMiniatura);

            return resourceType switch
            {
                ResourceType.Image => await SubirImagenOptimizadaAsync(archivo, carpetaDestino, generarMiniatura),
                ResourceType.Video => await SubirVideoOriginalAsync(archivo, carpetaDestino, extension),
                _ => await SubirRawOriginalAsync(archivo, carpetaDestino, extension)
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
                "Error al subir archivo a Cloudinary. Nombre={Nombre}, Carpeta={Carpeta}",
                archivo.FileName,
                carpetaDestino);

            throw new RepositoryException("Error al subir archivo a Cloudinary.", ex);
        }
    }

    private async Task<UploadResultDto> SubirImagenOptimizadaAsync(
        IFormFile archivo,
        string carpetaDestino,
        bool generarMiniatura)
    {
        var mainBytes = await ImagenHelper.GenerarWebPAsync(
            archivo,
            width: 1080,
            height: 1080,
            calidad: 90,
            crop: false
        );

        var mainPublicId = Guid.NewGuid().ToString("N");

        await using var mainStream = new MemoryStream(mainBytes);

        var mainParams = new ImageUploadParams
        {
            File = new FileDescription($"{mainPublicId}.webp", mainStream),
            Folder = carpetaDestino,
            PublicId = mainPublicId,
            UseFilename = false,
            UniqueFilename = false,
            Overwrite = false,
            Type = "upload"
        };

        var mainResult = await _cloudinary.UploadAsync(mainParams);

        if (mainResult.StatusCode != HttpStatusCode.OK)
        {
            _logger.LogWarning(
                "Cloudinary no devolvió OK al subir imagen principal. StatusCode={StatusCode}, Error={Error}",
                mainResult.StatusCode,
                mainResult.Error?.Message);

            throw new RepositoryException("No se pudo subir la imagen principal a Cloudinary.");
        }

        var mainUrl = mainResult.SecureUrl?.ToString() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(mainUrl))
            throw new RepositoryException("Cloudinary no devolvió URL para la imagen principal.");

        // Modo optimizado: no subimos otro archivo.
        // La misma imagen principal se usa como miniatura.
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
            calidad: 85,
            crop: true
        );

        var thumbPublicId = $"{Guid.NewGuid():N}_thumb";

        await using var thumbStream = new MemoryStream(thumbBytes);

        var thumbParams = new ImageUploadParams
        {
            File = new FileDescription($"{thumbPublicId}.webp", thumbStream),
            Folder = carpetaDestino,
            PublicId = thumbPublicId,
            UseFilename = false,
            UniqueFilename = false,
            Overwrite = false,
            Type = "upload"
        };

        var thumbResult = await _cloudinary.UploadAsync(thumbParams);

        if (thumbResult.StatusCode != HttpStatusCode.OK)
        {
            _logger.LogWarning(
                "No se pudo subir miniatura. Se usará imagen principal como ThumbUrl. StatusCode={StatusCode}, Error={Error}",
                thumbResult.StatusCode,
                thumbResult.Error?.Message);

            return new UploadResultDto
            {
                MainUrl = mainUrl,
                ThumbUrl = mainUrl
            };
        }

        return new UploadResultDto
        {
            MainUrl = mainUrl,
            ThumbUrl = thumbResult.SecureUrl?.ToString() ?? mainUrl
        };
    }

    private async Task<UploadResultDto> SubirVideoOriginalAsync(
        IFormFile archivo,
        string carpetaDestino,
        string extension)
    {
        var publicId = Guid.NewGuid().ToString("N");

        await using var stream = new MemoryStream();
        await archivo.CopyToAsync(stream);
        stream.Position = 0;

        var uploadParams = new VideoUploadParams
        {
            File = new FileDescription($"{publicId}{extension}", stream),
            Folder = carpetaDestino,
            PublicId = publicId,
            UseFilename = false,
            UniqueFilename = false,
            Overwrite = false,
            Type = "upload"
        };

        var result = await _cloudinary.UploadAsync(uploadParams);

        if (result.StatusCode != HttpStatusCode.OK)
        {
            _logger.LogWarning(
                "Cloudinary no devolvió OK al subir video. StatusCode={StatusCode}, Error={Error}",
                result.StatusCode,
                result.Error?.Message);

            throw new RepositoryException("No se pudo subir el video a Cloudinary.");
        }

        var url = result.SecureUrl?.ToString() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(url))
            throw new RepositoryException("Cloudinary no devolvió URL para el video.");

        return new UploadResultDto
        {
            MainUrl = url,
            ThumbUrl = url
        };
    }

    private async Task<UploadResultDto> SubirRawOriginalAsync(
        IFormFile archivo,
        string carpetaDestino,
        string extension)
    {
        var publicId = Guid.NewGuid().ToString("N");

        await using var stream = new MemoryStream();
        await archivo.CopyToAsync(stream);
        stream.Position = 0;

        var rawParams = new RawUploadParams
        {
            File = new FileDescription($"{publicId}{extension}", stream),
            Folder = carpetaDestino,
            PublicId = publicId,
            UseFilename = false,
            UniqueFilename = false,
            Overwrite = false
        };

        var result = await _cloudinary.UploadAsync(rawParams);

        if (result.StatusCode != HttpStatusCode.OK)
        {
            _logger.LogWarning(
                "Cloudinary no devolvió OK al subir archivo RAW. StatusCode={StatusCode}, Error={Error}",
                result.StatusCode,
                result.Error?.Message);

            throw new RepositoryException("No se pudo subir el archivo a Cloudinary.");
        }

        var url = result.SecureUrl?.ToString() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(url))
            throw new RepositoryException("Cloudinary no devolvió URL para el archivo.");

        return new UploadResultDto
        {
            MainUrl = url,
            ThumbUrl = string.Empty
        };
    }

    public async Task EliminarArchivo(string archivoUrl)
    {
        if (string.IsNullOrWhiteSpace(archivoUrl))
        {
            _logger.LogWarning("Se intentó eliminar un archivo con URL vacía o nula.");
            return;
        }

        try
        {
            if (!Uri.TryCreate(archivoUrl, UriKind.Absolute, out var uri))
            {
                _logger.LogWarning(
                    "La URL del archivo no es válida. Se omite eliminación remota. Url={ArchivoUrl}",
                    archivoUrl);
                return;
            }

            if (!EsUrlCloudinary(uri))
            {
                _logger.LogInformation(
                    "La URL no pertenece a Cloudinary. Se omite eliminación remota. Url={ArchivoUrl}",
                    archivoUrl);
                return;
            }

            var segmentos = uri.AbsolutePath
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .ToList();

            var uploadIndex = segmentos.FindIndex(s =>
                s.Equals("upload", StringComparison.OrdinalIgnoreCase));

            if (uploadIndex == -1)
            {
                _logger.LogWarning(
                    "No se pudo identificar el segmento 'upload' en la URL. Se omite eliminación. Url={ArchivoUrl}",
                    archivoUrl);
                return;
            }

            var partesPublicId = segmentos
                .Skip(uploadIndex + 1)
                .Where(s => !EsVersionCloudinary(s))
                .ToList();

            if (!partesPublicId.Any())
            {
                _logger.LogWarning(
                    "No se pudo construir PublicId desde la URL. Se omite eliminación. Url={ArchivoUrl}",
                    archivoUrl);
                return;
            }

            var ultimo = partesPublicId[^1];
            partesPublicId[^1] = Path.GetFileNameWithoutExtension(ultimo);

            var publicId = string.Join("/", partesPublicId);

            if (string.IsNullOrWhiteSpace(publicId))
            {
                _logger.LogWarning(
                    "PublicId vacío al intentar eliminar archivo. Se omite eliminación. Url={ArchivoUrl}",
                    archivoUrl);
                return;
            }

            var extension = Path.GetExtension(segmentos[^1]).ToLowerInvariant();
            var resourceType = ObtenerResourceType(extension, string.Empty);

            _logger.LogInformation(
                "Intentando eliminar archivo de Cloudinary. PublicId={PublicId}, Tipo={Tipo}",
                publicId,
                resourceType);

            var deletionParams = new DeletionParams(publicId)
            {
                ResourceType = resourceType
            };

            var result = await _cloudinary.DestroyAsync(deletionParams);

            if (result.Result == "ok" || result.Result == "not found")
            {
                _logger.LogInformation(
                    "Resultado de eliminación en Cloudinary. PublicId={PublicId}, Resultado={Resultado}",
                    publicId,
                    result.Result);

                return;
            }

            _logger.LogWarning(
                "Cloudinary no confirmó eliminación. PublicId={PublicId}, Resultado={Resultado}, Error={Error}",
                publicId,
                result.Result,
                result.Error?.Message);

            throw new RepositoryException(
                $"Cloudinary no confirmó la eliminación del archivo. PublicId={publicId}, Resultado={result.Result}, Error={result.Error?.Message}");
        }
        catch (RepositoryException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al intentar eliminar archivo de Cloudinary. Url={ArchivoUrl}",
                archivoUrl);

            throw new RepositoryException(
                $"Error al eliminar archivo de Cloudinary. Url={archivoUrl}",
                ex);
        }
    }

    private static bool EsUrlCloudinary(Uri uri)
    {
        return uri.Host.Contains("cloudinary.com", StringComparison.OrdinalIgnoreCase);
    }

    private static ResourceType ObtenerResourceType(string extension, string? contentType)
    {
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                return ResourceType.Image;

            if (contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
                return ResourceType.Video;
        }

        return extension switch
        {
            ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif" => ResourceType.Image,
            ".mp4" or ".mov" or ".avi" or ".mkv" or ".webm" => ResourceType.Video,
            _ => ResourceType.Raw
        };
    }

    private static string NormalizarCarpeta(string carpeta)
    {
        if (string.IsNullOrWhiteSpace(carpeta))
            return "publicaciones";

        return carpeta
            .Trim()
            .Replace("\\", "/")
            .Trim('/');
    }

    private static bool EsVersionCloudinary(string segmento)
    {
        if (string.IsNullOrWhiteSpace(segmento))
            return false;

        if (!segmento.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            return false;

        return segmento
            .Skip(1)
            .All(char.IsDigit);
    }
}

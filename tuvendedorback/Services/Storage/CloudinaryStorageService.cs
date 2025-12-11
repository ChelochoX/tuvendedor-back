using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using System.Net;
using tuvendedorback.Common;
using tuvendedorback.DTOs;
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

        _cloudinary = new Cloudinary(account) { Api = { Secure = true } };
        _logger = logger;
    }

    public async Task<UploadResultDto> SubirArchivo(IFormFile archivo, string carpetaDestino = "publicaciones")
    {
        var extension = Path.GetExtension(archivo.FileName).ToLower();

        var resourceType = extension switch
        {
            ".jpg" or ".jpeg" or ".png" or ".webp" => ResourceType.Image,
            ".mp4" or ".mov" or ".avi" => ResourceType.Video,
            _ => ResourceType.Raw
        };

        if (resourceType == ResourceType.Image)
        {
            // 1️⃣ Generamos nosotros la versión principal 1080x1080
            var mainBytes = await ImagenHelper.GenerarWebPAsync(
                archivo,
                width: 1080,
                height: 1080,
                calidad: 90,
                crop: false
            );

            // 2️⃣ Generamos thumbnail 400x300 (crop)
            var thumbBytes = await ImagenHelper.GenerarWebPAsync(
                archivo,
                width: 400,
                height: 300,
                calidad: 85,
                crop: true
            );

            // 3️⃣ Subimos ambas sin transformación
            var mainStream = new MemoryStream(mainBytes);
            var mainParams = new ImageUploadParams
            {
                File = new FileDescription($"{Guid.NewGuid()}.webp", mainStream),
                Folder = carpetaDestino,
                UseFilename = true,
                UniqueFilename = false,
                Type = "upload"
            };

            var mainResult = await _cloudinary.UploadAsync(mainParams);

            var thumbStream = new MemoryStream(thumbBytes);
            var thumbParams = new ImageUploadParams
            {
                File = new FileDescription($"{Guid.NewGuid()}_thumb.webp", thumbStream),
                Folder = carpetaDestino,
                UseFilename = true,
                UniqueFilename = false,
                Type = "upload"
            };

            var thumbResult = await _cloudinary.UploadAsync(thumbParams);

            if (mainResult.StatusCode == HttpStatusCode.OK && thumbResult.StatusCode == HttpStatusCode.OK)
            {
                return new UploadResultDto
                {
                    MainUrl = mainResult.SecureUrl.ToString(),
                    ThumbUrl = thumbResult.SecureUrl.ToString()
                };
            }
        }

        // 4️⃣ Videos (igual que antes, pero sin transformaciones innecesarias)
        else if (resourceType == ResourceType.Video)
        {
            using var stream = new MemoryStream();
            await archivo.CopyToAsync(stream);
            stream.Position = 0;

            var uploadParams = new VideoUploadParams
            {
                File = new FileDescription(archivo.FileName, stream),
                Folder = carpetaDestino,
                UseFilename = true,
                UniqueFilename = false,
                Type = "upload",
                Format = "mp4",
            };

            var result = await _cloudinary.UploadAsync(uploadParams);

            if (result.StatusCode == HttpStatusCode.OK)
            {
                return new UploadResultDto
                {
                    MainUrl = result.SecureUrl?.ToString() ?? "",
                    ThumbUrl = result.SecureUrl?.ToString() ?? ""
                };
            }
        }

        // 5️⃣ Archivos RAW
        else
        {
            using var stream = new MemoryStream();
            await archivo.CopyToAsync(stream);
            stream.Position = 0;

            var rawParams = new RawUploadParams
            {
                File = new FileDescription(archivo.FileName, stream),
                Folder = carpetaDestino,
                UseFilename = true,
                UniqueFilename = false
            };

            var result = await _cloudinary.UploadAsync(rawParams);

            if (result.StatusCode == HttpStatusCode.OK)
            {
                return new UploadResultDto
                {
                    MainUrl = result.SecureUrl?.ToString() ?? "",
                    ThumbUrl = ""
                };
            }
        }

        throw new Exception("Error al subir archivo a Cloudinary");
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
            var uri = new Uri(archivoUrl);
            var segmentos = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

            // 🔹 Obtener carpeta y nombre base
            var carpeta = segmentos.Length > 1 ? segmentos[^2] : string.Empty;
            var nombreArchivo = Path.GetFileNameWithoutExtension(segmentos[^1]);

            // 🔹 Construir publicId (lo que Cloudinary usa para eliminar)
            var publicId = string.IsNullOrEmpty(carpeta)
                ? nombreArchivo
                : $"{carpeta}/{nombreArchivo}";

            // 🔹 Detectar tipo de recurso según la extensión
            var extension = Path.GetExtension(segmentos[^1]).ToLowerInvariant();
            var resourceType = extension switch
            {
                ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif" => ResourceType.Image,
                ".mp4" or ".mov" or ".avi" or ".mkv" => ResourceType.Video,
                _ => ResourceType.Raw // Por ejemplo: PDF, ZIP, DOCX, etc.
            };

            _logger.LogInformation("Intentando eliminar archivo {PublicId} (tipo: {Tipo})", publicId, resourceType);

            // 🔹 Ejecutar eliminación en Cloudinary
            var deletionParams = new DeletionParams(publicId)
            {
                ResourceType = resourceType
            };

            var result = await _cloudinary.DestroyAsync(deletionParams);

            if (result.Result == "ok")
            {
                _logger.LogInformation("✅ Archivo eliminado correctamente de Cloudinary: {PublicId}", publicId);
            }
            else
            {
                _logger.LogWarning("⚠️ No se pudo eliminar el archivo {PublicId} de Cloudinary. Resultado: {Resultado} | Error: {Error}",
                    publicId, result.Result, result.Error?.Message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error al intentar eliminar archivo de Cloudinary con URL {ArchivoUrl}", archivoUrl);
        }
    }

}

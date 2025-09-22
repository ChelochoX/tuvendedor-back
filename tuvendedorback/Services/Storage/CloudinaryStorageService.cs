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

    public CloudinaryStorageService(IConfiguration config)
    {
        var account = new Account(
            config["Cloudinary:CloudName"],
            config["Cloudinary:ApiKey"],
            config["Cloudinary:ApiSecret"]
        );

        _cloudinary = new Cloudinary(account) { Api = { Secure = true } };
    }

    public async Task<UploadResultDto> SubirArchivo(IFormFile archivo, string carpetaDestino = "publicaciones")
    {
        var extension = Path.GetExtension(archivo.FileName).ToLower();

        // Detectamos el tipo de archivo
        var resourceType = extension switch
        {
            ".jpg" or ".jpeg" or ".png" or ".webp" => ResourceType.Image,
            ".mp4" or ".mov" or ".avi" => ResourceType.Video,
            _ => ResourceType.Raw
        };

        if (resourceType == ResourceType.Image)
        {
            var webpBytes = await ImagenHelper.ConvertirAWebPAsync(archivo);

            using var mainStream = new MemoryStream(webpBytes);

            // 📌 Imagen principal 1080x1080
            var mainParams = new ImageUploadParams
            {
                File = new FileDescription($"{Guid.NewGuid()}.webp", mainStream),
                Folder = carpetaDestino,
                UseFilename = true,
                UniqueFilename = false,
                Format = "webp",
                Transformation = new Transformation()
                 .Width(1080).Height(1080).Crop("pad").Background("white").FetchFormat("webp")
            };

            var mainResult = await _cloudinary.UploadAsync(mainParams);

            using var thumbStream = new MemoryStream(webpBytes);

            // 📌 Miniatura 300x300
            var thumbParams = new ImageUploadParams
            {
                File = new FileDescription($"{Guid.NewGuid()}_thumb.webp", thumbStream),
                Folder = carpetaDestino,
                UseFilename = true,
                UniqueFilename = false,
                Format = "webp",
                Transformation = new Transformation()
                .Width(400).Height(300)
                .Crop("fill").Gravity("auto")
                .FetchFormat("webp")
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
            };

            var result = await _cloudinary.UploadAsync(uploadParams);
            if (result.StatusCode == HttpStatusCode.OK)
            {
                return new UploadResultDto
                {
                    MainUrl = result.SecureUrl?.ToString() ?? string.Empty,
                    ThumbUrl = string.Empty // no hay miniatura generada aquí
                };
            }
        }
        else // ResourceType.Raw
        {
            using var stream = new MemoryStream();

            await archivo.CopyToAsync(stream);
            stream.Position = 0;

            var uploadParams = new RawUploadParams
            {
                File = new FileDescription(archivo.FileName, stream),
                Folder = carpetaDestino,
                UseFilename = true,
                UniqueFilename = false,
            };

            var result = await _cloudinary.UploadAsync(uploadParams);
            if (result.StatusCode == HttpStatusCode.OK)
            {
                return new UploadResultDto
                {
                    MainUrl = result.SecureUrl?.ToString() ?? string.Empty,
                    ThumbUrl = string.Empty
                };
            }
        }

        throw new Exception("Error al subir archivo a Cloudinary");
    }


}

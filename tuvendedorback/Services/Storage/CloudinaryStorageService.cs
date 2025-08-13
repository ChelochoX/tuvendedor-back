using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using System.Net;
using tuvendedorback.Common;
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

    public async Task<string> SubirArchivo(IFormFile archivo, string carpetaDestino = "publicaciones")
    {
        var extension = Path.GetExtension(archivo.FileName).ToLower();

        // Detectamos el tipo de archivo
        var resourceType = extension switch
        {
            ".jpg" or ".jpeg" or ".png" or ".webp" => ResourceType.Image,
            ".mp4" or ".mov" or ".avi" => ResourceType.Video,
            _ => ResourceType.Raw
        };

        using var stream = new MemoryStream();

        if (resourceType == ResourceType.Image)
        {
            var webpBytes = await ImagenHelper.ConvertirAWebPAsync(archivo);
            stream.Write(webpBytes, 0, webpBytes.Length);
            stream.Position = 0;

            var uploadParams = new ImageUploadParams
            {
                File = new FileDescription($"{Guid.NewGuid()}.webp", stream),
                Folder = carpetaDestino,
                UseFilename = true,
                UniqueFilename = true,
            };

            var result = await _cloudinary.UploadAsync(uploadParams);
            if (result.StatusCode == HttpStatusCode.OK)
                return result.SecureUrl.ToString();
        }
        else if (resourceType == ResourceType.Video)
        {
            await archivo.CopyToAsync(stream);
            stream.Position = 0;

            var uploadParams = new VideoUploadParams
            {
                File = new FileDescription(archivo.FileName, stream),
                Folder = carpetaDestino,
                UseFilename = true,
                UniqueFilename = true,
            };

            var result = await _cloudinary.UploadAsync(uploadParams);
            if (result.StatusCode == HttpStatusCode.OK)
                return result.SecureUrl.ToString();
        }
        else // ResourceType.Raw
        {
            await archivo.CopyToAsync(stream);
            stream.Position = 0;

            var uploadParams = new RawUploadParams
            {
                File = new FileDescription(archivo.FileName, stream),
                Folder = carpetaDestino,
                UseFilename = true,
                UniqueFilename = true,
            };

            var result = await _cloudinary.UploadAsync(uploadParams);
            if (result.StatusCode == HttpStatusCode.OK)
                return result.SecureUrl.ToString();
        }

        throw new Exception("Error al subir archivo a Cloudinary");
    }


}

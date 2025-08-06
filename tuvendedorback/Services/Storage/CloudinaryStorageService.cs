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

    public async Task<string> SubirImagenWebP(IFormFile imagenFile, string carpetaDestino = "publicaciones")
    {
        var webpBytes = await ImagenHelper.ConvertirAWebPAsync(imagenFile);

        using var ms = new MemoryStream(webpBytes);

        var uploadParams = new RawUploadParams
        {
            File = new FileDescription($"{Guid.NewGuid()}.webp", ms),
            Folder = carpetaDestino,
            ResourceType = ResourceType.Image,
            Format = "webp"
        };

        var result = await _cloudinary.UploadAsync(uploadParams);

        if (result.StatusCode == System.Net.HttpStatusCode.OK)
            return result.SecureUrl.ToString();

        throw new Exception("Error al subir imagen a Cloudinary");
    }
}

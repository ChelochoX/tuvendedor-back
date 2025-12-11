using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace tuvendedorback.Common;

public static class ImagenHelper
{
    // Genera una versión WebP redimensionada
    public static async Task<byte[]> GenerarWebPAsync(
        IFormFile file,
        int width,
        int height,
        int calidad = 85,
        bool crop = false)
    {
        using var image = await Image.LoadAsync(file.OpenReadStream());

        var opciones = new ResizeOptions
        {
            Size = new Size(width, height),
            Mode = crop ? ResizeMode.Crop : ResizeMode.Max
        };

        image.Mutate(x => x.Resize(opciones));

        using var ms = new MemoryStream();
        await image.SaveAsync(ms, new WebpEncoder { Quality = calidad });
        return ms.ToArray();
    }
}

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace tuvendedorback.Common;

public static class ImagenHelper
{
    public static async Task<byte[]> ConvertirAWebPAsync(IFormFile file, int maxLado = 1280, int calidad = 85)
    {
        using var image = await Image.LoadAsync(file.OpenReadStream());

        // Redimensionar si la imagen es más grande que el maxLado
        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Mode = ResizeMode.Max,
            Size = new Size(maxLado, maxLado) // ejemplo: máximo 1280px de ancho/alto
        }));

        using var ms = new MemoryStream();

        await image.SaveAsync(ms, new WebpEncoder
        {
            Quality = calidad // control de calidad aquí (0-100)
        });

        return ms.ToArray();
    }

}

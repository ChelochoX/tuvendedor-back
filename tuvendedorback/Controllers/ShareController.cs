using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Text;
using System.Text.Json;
using tuvendedorback.Services.Interfaces;

namespace tuvendedorback.Controllers;

[ApiController]
[Route("share")]
public class ShareController : ControllerBase
{
    private readonly IPublicacionService _publicacionService;
    private readonly IConfiguration _configuration;

    public ShareController(
        IPublicacionService publicacionService,
        IConfiguration configuration)
    {
        _publicacionService = publicacionService;
        _configuration = configuration;
    }

    [HttpGet("producto/{id:int}")]
    public async Task<IActionResult> Producto(int id)
    {
        var producto = await _publicacionService.ObtenerProductoSharePreview(id);

        if (producto == null)
            return NotFound("Publicación no encontrada.");

        var baseUrl = ObtenerBaseUrl();
        var logoUrl = ObtenerLogoUrl(baseUrl);

        var titulo = Limitar(
            string.IsNullOrWhiteSpace(producto.Titulo)
                ? "TuVendedor Marketplace"
                : producto.Titulo,
            90);

        var descripcionBase = !string.IsNullOrWhiteSpace(producto.Descripcion)
            ? producto.Descripcion
            : $"{producto.Categoria} en {producto.Ubicacion}";

        var descripcion = Limitar(LimpiarTexto(descripcionBase), 180);

        var imagen = !string.IsNullOrWhiteSpace(producto.ImagenUrl)
            ? producto.ImagenUrl.Trim()
            : logoUrl;

        var slug = string.IsNullOrWhiteSpace(producto.SlugVendedor)
            ? ""
            : producto.SlugVendedor.Trim();

        var destino = !string.IsNullOrWhiteSpace(slug)
            ? $"{baseUrl}/vendedor/{WebUtility.UrlEncode(slug)}?producto={producto.Id}"
            : $"{baseUrl}/producto/{producto.Id}";

        var html = ConstruirHtmlPreview(
            titulo,
            descripcion,
            imagen,
            destino
        );

        return Content(html, "text/html; charset=utf-8", Encoding.UTF8);
    }

    private string ObtenerBaseUrl()
    {
        var baseUrl = _configuration["PublicApp:BaseUrl"];

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            var request = HttpContext.Request;
            baseUrl = $"{request.Scheme}://{request.Host}";
        }

        return baseUrl.Trim().TrimEnd('/');
    }

    private string ObtenerLogoUrl(string baseUrl)
    {
        var logoUrl = _configuration["PublicApp:LogoUrl"];

        if (!string.IsNullOrWhiteSpace(logoUrl))
            return logoUrl.Trim();

        return $"{baseUrl}/logoTuVendedor.png";
    }

    private static string ConstruirHtmlPreview(
        string titulo,
        string descripcion,
        string imagen,
        string destino)
    {
        var safeTitle = WebUtility.HtmlEncode(titulo);
        var safeDescription = WebUtility.HtmlEncode(descripcion);
        var safeImage = WebUtility.HtmlEncode(imagen);
        var safeDestino = WebUtility.HtmlEncode(destino);
        var destinoJs = JsonSerializer.Serialize(destino);

                    return $@"<!DOCTYPE html>
            <html lang=""es"">
            <head>
              <meta charset=""utf-8"" />
              <meta name=""viewport"" content=""width=device-width, initial-scale=1"" />

              <title>{safeTitle}</title>
              <meta name=""description"" content=""{safeDescription}"" />

              <meta property=""og:type"" content=""product"" />
              <meta property=""og:site_name"" content=""TuVendedor Marketplace"" />
              <meta property=""og:title"" content=""{safeTitle}"" />
              <meta property=""og:description"" content=""{safeDescription}"" />
              <meta property=""og:image"" content=""{safeImage}"" />
              <meta property=""og:image:secure_url"" content=""{safeImage}"" />
              <meta property=""og:url"" content=""{safeDestino}"" />

              <meta name=""twitter:card"" content=""summary_large_image"" />
              <meta name=""twitter:title"" content=""{safeTitle}"" />
              <meta name=""twitter:description"" content=""{safeDescription}"" />
              <meta name=""twitter:image"" content=""{safeImage}"" />

              <meta http-equiv=""refresh"" content=""0;url={safeDestino}"" />

              <script>
                window.location.replace({destinoJs});
              </script>
            </head>
            <body>
              <p>Redirigiendo a la publicación...</p>
              <p><a href=""{safeDestino}"">Ver publicación</a></p>
            </body>
            </html>";
    }

    private static string LimpiarTexto(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return "Publicación disponible en TuVendedor Marketplace.";

        return texto
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Replace("\t", " ")
            .Trim();
    }

    private static string Limitar(string texto, int max)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return string.Empty;

        texto = texto.Trim();

        return texto.Length <= max
            ? texto
            : texto.Substring(0, max).Trim() + "...";
    }
}

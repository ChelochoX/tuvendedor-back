using System.Globalization;
using System.Net;
using tuvendedorback.Repositories.Interfaces;
using tuvendedorback.Services.Interfaces;

namespace tuvendedorback.Services;

public class CompartirService : ICompartirService
{
    private readonly ICompartirRepository _repository;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CompartirService> _logger;

    public CompartirService(
        ICompartirRepository repository,
        IConfiguration configuration,
        ILogger<CompartirService> logger)
    {
        _repository = repository;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<string> GenerarHtmlProductoCompartir(int idProducto)
    {
        var producto = await _repository.ObtenerProductoParaCompartir(idProducto);

        var frontendUrl = ObtenerFrontendUrl();
        var productoUrl = $"{frontendUrl}/producto/{idProducto}";
        var logoUrl = $"{frontendUrl}/logoTuVendedor.png";

        if (producto == null)
        {
            return GenerarHtml(
                titulo: "Producto no encontrado | Tu Vendedor",
                descripcion: "La publicación solicitada no se encuentra disponible.",
                imagenUrl: logoUrl,
                destinoUrl: frontendUrl
            );
        }

        var titulo = LimpiarTexto(producto.Titulo);
        var precio = FormatearPrecio(producto.Precio);
        var ubicacion = LimpiarTexto(producto.Ubicacion ?? "");
        var categoria = LimpiarTexto(producto.Categoria ?? "");

        var descripcionBase = !string.IsNullOrWhiteSpace(producto.Descripcion)
            ? producto.Descripcion
            : "Producto disponible en Tu Vendedor.";

        var descripcion = LimpiarTexto(
            $"{descripcionBase} {(string.IsNullOrWhiteSpace(ubicacion) ? "" : $"Ubicación: {ubicacion}.")} Precio: {precio}."
        );

        var imagenUrl = !string.IsNullOrWhiteSpace(producto.ImagenUrl)
            ? producto.ImagenUrl
            : logoUrl;

        _logger.LogInformation(
            "HTML Open Graph generado para producto. IdProducto={IdProducto}, Titulo={Titulo}",
            idProducto,
            titulo
        );

        return GenerarHtml(
            titulo: $"{titulo} | {precio}",
            descripcion: descripcion,
            imagenUrl: imagenUrl,
            destinoUrl: productoUrl,
            categoria: categoria,
            ubicacion: ubicacion,
            precio: precio
        );
    }

    private string ObtenerFrontendUrl()
    {
        var url = _configuration["Frontend:PublicUrl"];

        if (string.IsNullOrWhiteSpace(url))
        {
            url = "https://www.tuvendedor.com.py";
        }

        return url.TrimEnd('/');
    }

    private static string FormatearPrecio(decimal precio)
    {
        if (precio <= 0)
        {
            return "Consultar precio";
        }

        var culture = new CultureInfo("es-PY");

        return $"Gs. {precio.ToString("N0", culture)}";
    }

    private static string LimpiarTexto(string valor)
    {
        return WebUtility.HtmlEncode(valor?.Trim() ?? string.Empty);
    }

    private static string GenerarHtml(
        string titulo,
        string descripcion,
        string imagenUrl,
        string destinoUrl,
        string? categoria = null,
        string? ubicacion = null,
        string? precio = null)
    {
        var tituloSeguro = LimpiarTexto(titulo);
        var descripcionSeguro = LimpiarTexto(descripcion);
        var imagenSeguro = LimpiarTexto(imagenUrl);
        var destinoSeguro = LimpiarTexto(destinoUrl);
        var categoriaSeguro = LimpiarTexto(categoria ?? "");
        var ubicacionSeguro = LimpiarTexto(ubicacion ?? "");
        var precioSeguro = LimpiarTexto(precio ?? "");

        return $@"
<!doctype html>
<html lang=""es"">
<head>
  <meta charset=""utf-8"" />
  <meta name=""viewport"" content=""width=device-width, initial-scale=1"" />

  <title>{tituloSeguro}</title>
  <meta name=""description"" content=""{descripcionSeguro}"" />

  <meta property=""og:type"" content=""product"" />
  <meta property=""og:site_name"" content=""Tu Vendedor"" />
  <meta property=""og:title"" content=""{tituloSeguro}"" />
  <meta property=""og:description"" content=""{descripcionSeguro}"" />
  <meta property=""og:image"" content=""{imagenSeguro}"" />
  <meta property=""og:image:secure_url"" content=""{imagenSeguro}"" />
  <meta property=""og:url"" content=""{destinoSeguro}"" />
  <meta property=""og:locale"" content=""es_PY"" />

  <meta name=""twitter:card"" content=""summary_large_image"" />
  <meta name=""twitter:title"" content=""{tituloSeguro}"" />
  <meta name=""twitter:description"" content=""{descripcionSeguro}"" />
  <meta name=""twitter:image"" content=""{imagenSeguro}"" />

  <link rel=""canonical"" href=""{destinoSeguro}"" />

  <script>
    setTimeout(function () {{
      window.location.href = ""{destinoSeguro}"";
    }}, 900);
  </script>

  <style>
    body {{
      margin: 0;
      min-height: 100vh;
      background: #050914;
      color: white;
      font-family: Arial, sans-serif;
      display: flex;
      align-items: center;
      justify-content: center;
      padding: 24px;
    }}

    .card {{
      width: 100%;
      max-width: 520px;
      border: 1px solid rgba(250, 204, 21, 0.25);
      border-radius: 28px;
      background: #101722;
      overflow: hidden;
      box-shadow: 0 24px 80px rgba(0, 0, 0, 0.45);
    }}

    img {{
      width: 100%;
      height: 280px;
      object-fit: cover;
      display: block;
      background: #111827;
    }}

    .content {{
      padding: 22px;
    }}

    .badge {{
      display: inline-block;
      background: #facc15;
      color: #000;
      font-weight: 800;
      font-size: 12px;
      border-radius: 999px;
      padding: 7px 12px;
      margin-bottom: 12px;
    }}

    h1 {{
      font-size: 24px;
      line-height: 1.15;
      margin: 0 0 10px;
    }}

    p {{
      color: #cbd5e1;
      line-height: 1.5;
      margin: 0 0 14px;
    }}

    .price {{
      color: #facc15;
      font-size: 22px;
      font-weight: 900;
      margin-bottom: 16px;
    }}

    a {{
      display: inline-block;
      background: #facc15;
      color: #000;
      text-decoration: none;
      font-weight: 900;
      border-radius: 999px;
      padding: 13px 18px;
    }}
  </style>
</head>
<body>
  <main class=""card"">
    <img src=""{imagenSeguro}"" alt=""{tituloSeguro}"" />

    <section class=""content"">
      <span class=""badge"">Tu Vendedor</span>
      <h1>{tituloSeguro}</h1>

      {(string.IsNullOrWhiteSpace(categoriaSeguro) ? "" : $"<p>{categoriaSeguro}</p>")}
      {(string.IsNullOrWhiteSpace(ubicacionSeguro) ? "" : $"<p>{ubicacionSeguro}</p>")}
      {(string.IsNullOrWhiteSpace(precioSeguro) ? "" : $"<div class=\"price\">{precioSeguro}</div>")}

      <p>{descripcionSeguro}</p>

      <a href=""{destinoSeguro}"">Ver publicación</a>
    </section>
  </main>
</body>
</html>";
    }
}

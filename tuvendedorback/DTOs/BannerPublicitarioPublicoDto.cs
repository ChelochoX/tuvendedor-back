using System.Text.Json.Serialization;

namespace tuvendedorback.DTOs;

public class BannerPublicitarioPublicoDto
{
    public int Id { get; set; }

    public string NombreCliente { get; set; } = string.Empty;
    public string Ubicacion { get; set; } = string.Empty;

    public string Titulo { get; set; } = string.Empty;
    public string? Subtitulo { get; set; }
    public string? Descripcion { get; set; }

    public string Etiqueta { get; set; } = "Publicidad";
    public string? TextoBoton { get; set; }

    public string ImagenDesktopUrl { get; set; } = string.Empty;
    public string? ImagenMobileUrl { get; set; }

    public string? UrlDestino { get; set; }
    public string? WhatsappUrl { get; set; }

    public bool AbrirNuevaPestana { get; set; }

    [JsonIgnore]
    public int Orden { get; set; }

    [JsonIgnore]
    public int Prioridad { get; set; }

    [JsonIgnore]
    public bool EsExclusivo { get; set; }
}

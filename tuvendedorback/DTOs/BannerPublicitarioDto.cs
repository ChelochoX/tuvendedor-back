namespace tuvendedorback.DTOs;

public class BannerPublicitarioDto
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

    public DateTime FechaInicio { get; set; }
    public DateTime FechaFin { get; set; }

    public string Estado { get; set; } = string.Empty;
    public string EstadoVigencia { get; set; } = string.Empty;

    public int Orden { get; set; }
    public int Prioridad { get; set; }

    public bool EsExclusivo { get; set; }
    public bool AbrirNuevaPestana { get; set; }

    public DateTime FechaCreacion { get; set; }
    public DateTime? FechaModificacion { get; set; }

    public long CantidadImpresiones { get; set; }
    public long CantidadClicks { get; set; }
    public long CantidadWhatsApp { get; set; }
}



namespace tuvendedorback.DTOs;

public class ComercialDashboardDto
{
    public DateTime FechaDesde { get; set; }

    public DateTime FechaHasta { get; set; }

    public ComercialDashboardResumenDto Resumen { get; set; } = new();

    public List<ComercialDashboardSerieDiariaDto> SerieDiaria { get; set; } = new();

    public List<ComercialDashboardTopPublicacionDto> TopPublicaciones { get; set; } = new();

    public List<ComercialDashboardTopBannerDto> TopBanners { get; set; } = new();

    public List<ComercialDashboardRubroDto> Rubros { get; set; } = new();
}

public class ComercialDashboardResumenDto
{
    public int TotalUsuariosRegistrados { get; set; }

    public int TotalVendedores { get; set; }

    public int PublicacionesTotales { get; set; }

    public int PublicacionesActivas { get; set; }

    public int PublicacionesDestacadasActivas { get; set; }

    public long VistasPublicaciones { get; set; }

    public long ClicksWhatsappPublicaciones { get; set; }

    public long FavoritosActivos { get; set; }

    public long SolicitudesVisita { get; set; }

    public int BannersActivos { get; set; }

    public long ImpresionesBanners { get; set; }

    public long ClicksBanners { get; set; }

    public long WhatsAppBanners { get; set; }

    public decimal CtrBanners { get; set; }

    public decimal TasaWhatsappPublicaciones { get; set; }
}

public class ComercialDashboardSerieDiariaDto
{
    public DateTime Fecha { get; set; }

    public long VistasPublicaciones { get; set; }

    public long ClicksWhatsappPublicaciones { get; set; }

    public long ImpresionesBanners { get; set; }

    public long ClicksBanners { get; set; }

    public long WhatsAppBanners { get; set; }

    public long SolicitudesVisita { get; set; }
}

public class ComercialDashboardTopPublicacionDto
{
    public int IdPublicacion { get; set; }

    public string Titulo { get; set; } = string.Empty;

    public string Categoria { get; set; } = string.Empty;

    public string Vendedor { get; set; } = string.Empty;

    public long Vistas { get; set; }

    public long ClicksWhatsapp { get; set; }

    public long Favoritos { get; set; }
}

public class ComercialDashboardTopBannerDto
{
    public int IdBanner { get; set; }

    public string NombreCliente { get; set; } = string.Empty;

    public string Titulo { get; set; } = string.Empty;

    public string Ubicacion { get; set; } = string.Empty;

    public long Impresiones { get; set; }

    public long Clicks { get; set; }

    public long WhatsApp { get; set; }

    public decimal Ctr { get; set; }
}

public class ComercialDashboardRubroDto
{
    public string Rubro { get; set; } = string.Empty;

    public int PublicacionesActivas { get; set; }

    public long Vistas { get; set; }

    public long ClicksWhatsapp { get; set; }
}

namespace tuvendedorback.DTOs;

public class ResumenBannersPublicitariosDto
{
    public int TotalBanners { get; set; }

    public int BannersActivos { get; set; }

    public int BannersProgramados { get; set; }

    public int BannersVencidos { get; set; }

    public long TotalImpresiones { get; set; }

    public long TotalClicks { get; set; }

    public long TotalWhatsApp { get; set; }

    public decimal Ctr { get; set; }
}

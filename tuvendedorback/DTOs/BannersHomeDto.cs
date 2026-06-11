namespace tuvendedorback.DTOs;

public class BannersHomeDto
{
    public List<BannerPublicitarioPublicoDto> HomeTop { get; set; } = new();

    public List<BannerPublicitarioPublicoDto> HomeInline { get; set; } = new();
}

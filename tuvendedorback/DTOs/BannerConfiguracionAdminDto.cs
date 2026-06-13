namespace tuvendedorback.DTOs;

public class BannerConfiguracionAdminDto
{
    public List<string> Ubicaciones { get; set; } = new();

    public List<string> TiposDestino { get; set; } = new();

    public List<string> EstadosEditables { get; set; } = new();

    public Dictionary<string, BannerDimensionDto> Medidas
    {
        get;
        set;
    } = new();

    public List<string> FormatosPermitidos { get; set; } = new();
}

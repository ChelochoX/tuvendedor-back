namespace tuvendedorback.DTOs;

public class BannerDimensionDto
{
    public int Width { get; set; }

    public int Height { get; set; }

    public string Descripcion =>
        $"{Width} × {Height} px";
}

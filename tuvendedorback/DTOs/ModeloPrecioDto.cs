namespace tuvendedorback.DTOs;

public class ModeloPrecioDto
{
    public int IdModeloProducto { get; set; }
    public string Marca { get; set; } = null!;
    public string NombreModelo { get; set; } = null!;
    public string CodigoReferencia { get; set; } = null!;

    public List<ListaPrecioDto> ListasPrecios { get; set; } = new();
}

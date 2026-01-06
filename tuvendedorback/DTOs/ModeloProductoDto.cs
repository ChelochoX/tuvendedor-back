namespace tuvendedorback.DTOs;

public class ModeloProductoDto
{
    public int Id { get; set; }
    public int IdMarca { get; set; }
    public string Marca { get; set; } = null!;
    public string Rubro { get; set; } = null!;
    public string CodigoReferencia { get; set; } = null!;
    public string NombreModelo { get; set; } = null!;
    public string Estado { get; set; } = null!;
}

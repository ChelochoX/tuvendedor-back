namespace tuvendedorback.DTOs;

public class MotoModeloCandidatoDto
{
    public int IdModeloProducto { get; set; }

    public int IdMarca { get; set; }

    public string Marca { get; set; } = string.Empty;

    public string Modelo { get; set; } = string.Empty;

    public string CodigoReferencia { get; set; } = string.Empty;

    public int? Cilindrada { get; set; }

    public int? IdPublicacion { get; set; }
}

namespace tuvendedorback.DTOs;

public class PublicacionParaVisitaDto
{
    public int IdPublicacion { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public string? Ubicacion { get; set; }

    public int IdUsuarioVendedor { get; set; }
    public string? NombreVendedor { get; set; }
    public string? WhatsappVendedor { get; set; }
}

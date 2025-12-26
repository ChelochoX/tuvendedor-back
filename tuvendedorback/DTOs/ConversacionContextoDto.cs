namespace tuvendedorback.DTOs;

public class ConversacionContextoDto
{
    public string PasoActual { get; set; } = "INICIO";
    public string? Intencion { get; set; }
    public int? IdPublicacion { get; set; }
    public string? CodigoPrompt { get; set; }
}

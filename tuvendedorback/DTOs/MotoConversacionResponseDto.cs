namespace tuvendedorback.DTOs;

public class MotoConversacionResponseDto
{
    public int IdConversacion { get; set; }

    public int? IdPublicacion { get; set; }

    public string? Marca { get; set; }

    public string? Modelo { get; set; }

    public string Respuesta { get; set; } = string.Empty;

    public bool RequierePublicacion { get; set; }
}


public class MensajeConversacionHistorialDto
{
    public string Emisor { get; set; } = string.Empty;

    public string Mensaje { get; set; } = string.Empty;

    public DateTime Fecha { get; set; }
}

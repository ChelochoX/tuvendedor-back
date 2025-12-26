namespace tuvendedorback.DTOs;

public class MensajeConversacionDto
{
    public string Emisor { get; set; } = null!;
    public string Mensaje { get; set; } = null!;
    public DateTime Fecha { get; set; }
}

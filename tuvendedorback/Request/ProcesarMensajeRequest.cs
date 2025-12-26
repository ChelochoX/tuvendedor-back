namespace tuvendedorback.Request;

public class ProcesarMensajeRequest
{
    public string Canal { get; set; } = default!;
    public string IdentificadorExterno { get; set; } = default!;
    public string Mensaje { get; set; } = default!;
}

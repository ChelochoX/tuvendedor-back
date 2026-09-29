namespace tuvendedorback.Request;

public class CambiarEstadoContadoMotoRequest
{
    public string Estado { get; set; }
        = string.Empty;

    public string? Observacion { get; set; }
}
namespace tuvendedorback.Request;

public class CreditoMotoCambiarEstadoRequest
{
    public string Estado { get; set; } = string.Empty;

    public string? Observacion { get; set; }
}

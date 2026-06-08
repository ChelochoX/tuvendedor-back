namespace tuvendedorback.Request;

public class CrearSolicitudServicioPremiumRequest
{
    public string TipoServicio { get; set; } = string.Empty;

    public int? IdPublicacion { get; set; }

    public string? Observacion { get; set; }
}

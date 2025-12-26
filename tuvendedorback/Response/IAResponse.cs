namespace tuvendedorback.Response;

public class IAResponse
{
    public string Texto { get; set; } = default!;
    public string NuevoPaso { get; set; } = default!;
    public string? Intencion { get; set; }
    public int? IdPublicacion { get; set; }
}

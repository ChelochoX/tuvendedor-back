namespace tuvendedorback.ERP.ERPRequest;

public class AgregarDireccionClienteRequest
{
    public int ClienteId { get; set; }
    public string TipoDireccion { get; set; } = null!;
    public string Direccion { get; set; } = null!;
    public string? Ciudad { get; set; }
    public decimal? Latitud { get; set; }
    public decimal? Longitud { get; set; }
    public bool EsPrincipal { get; set; }
}

namespace tuvendedorback.ERP.ERPRequest;

public class CrearERPClienteRequest
{
    public string TipoDocumento { get; set; } = null!;
    public string NumeroDocumento { get; set; } = null!;
    public string RazonSocial { get; set; } = null!;
    public string? NombreFantasia { get; set; }
    public string? Telefono { get; set; }
    public string? Email { get; set; }
    public int? CRMInteresadoId { get; set; }
}

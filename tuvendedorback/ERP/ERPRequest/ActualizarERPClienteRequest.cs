namespace tuvendedorback.ERP.ERPRequest;

public class ActualizarERPClienteRequest
{
    public int ClienteId { get; set; }

    public string RazonSocial { get; set; } = null!;
    public string? NombreFantasia { get; set; }
    public string? Telefono { get; set; }
    public string? Email { get; set; }
}

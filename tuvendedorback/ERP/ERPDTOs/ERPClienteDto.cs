namespace tuvendedorback.ERP.ERPDTOs;

public class ERPClienteDto
{
    public int ClienteId { get; set; }
    public string RazonSocial { get; set; } = null!;
    public string TipoDocumento { get; set; } = null!;
    public string NumeroDocumento { get; set; } = null!;
    public string? Telefono { get; set; }
    public string? Email { get; set; }
    public bool Activo { get; set; }
}

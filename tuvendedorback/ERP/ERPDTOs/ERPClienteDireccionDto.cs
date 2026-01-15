namespace tuvendedorback.ERP.ERPDTOs;

public class ERPClienteDireccionDto
{
    public int DireccionId { get; set; }
    public string TipoDireccion { get; set; } = null!;
    public string Direccion { get; set; } = null!;
    public string? Ciudad { get; set; }
    public bool EsPrincipal { get; set; }
}

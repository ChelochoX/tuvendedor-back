namespace tuvendedorback.ERP.ERPModels;

public class ERPClienteDireccion
{
    public int DireccionId { get; set; }
    public int ClienteId { get; set; }

    public string TipoDireccion { get; set; } = null!;
    public string Direccion { get; set; } = null!;
    public string? Ciudad { get; set; }

    public decimal? Latitud { get; set; }
    public decimal? Longitud { get; set; }

    public bool EsPrincipal { get; set; }

    public DateTime FechaCreacion { get; set; }
}

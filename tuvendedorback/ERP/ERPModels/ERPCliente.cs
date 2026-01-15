namespace tuvendedorback.ERP.ERPModels;

public class ERPCliente
{
    public int ClienteId { get; set; }

    public string TipoDocumento { get; set; } = null!;
    public string NumeroDocumento { get; set; } = null!;

    public string RazonSocial { get; set; } = null!;
    public string? NombreFantasia { get; set; }

    public string? Telefono { get; set; }
    public string? Email { get; set; }

    public bool Activo { get; set; }

    public int? CRMInteresadoId { get; set; }

    public DateTime FechaCreacion { get; set; }
    public int? UsuarioCreacion { get; set; }
    public DateTime? FechaModificacion { get; set; }
    public int? UsuarioModificacion { get; set; }
}

namespace tuvendedorback.DTOs;

public class ActualizarMiPerfilVendedorRequest
{
    public string? NombreNegocio { get; set; }
    public string? Slug { get; set; }
    public string? Rubro { get; set; }
    public string? Descripcion { get; set; }

    public string? Whatsapp { get; set; }
    public string? InstagramUrl { get; set; }
    public string? FacebookUrl { get; set; }
    public string? CiudadVisible { get; set; }

    public bool? EsPerfilPublico { get; set; }
    public bool? MostrarTelefono { get; set; }

    public IFormFile? FotoPerfil { get; set; }
    public IFormFile? Banner { get; set; }
}

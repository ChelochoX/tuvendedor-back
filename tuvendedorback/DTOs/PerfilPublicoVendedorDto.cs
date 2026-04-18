namespace tuvendedorback.DTOs;

public class PerfilPublicoVendedorDto
{
    public int IdVendedor { get; set; }
    public int IdUsuario { get; set; }

    public string? Slug { get; set; }

    public string? NombreNegocio { get; set; }
    public string? NombreUsuario { get; set; }
    public string? Descripcion { get; set; }

    public string? BannerUrl { get; set; }
    public string? BannerTipo { get; set; }
    public string? FotoPerfil { get; set; }

    public string? Rubro { get; set; }
    public string? CiudadVisible { get; set; }

    public string? Telefono { get; set; }
    public string? Whatsapp { get; set; }
    public string? InstagramUrl { get; set; }
    public string? FacebookUrl { get; set; }

    public bool EsPerfilPublico { get; set; }
    public bool EsPremium { get; set; }
    public bool MostrarTelefono { get; set; }

    public int CantidadPublicaciones { get; set; }
    public List<PerfilPublicoPublicacionDto> Publicaciones { get; set; } = new();


}

using tuvendedorback.DTOs;

namespace tuvendedorback.Repositories.Interfaces;

public interface IPerfilVendedorRepository
{
    Task<PerfilPublicoVendedorDto?> ObtenerPerfilPublicoPorSlug(string slug);
    Task<List<PerfilPublicoPublicacionDto>> ObtenerPublicacionesActivasPorSlug(string slug);
    Task<PerfilPublicoVendedorDto?> ObtenerMiPerfilVendedor(int idUsuario);

    Task<bool> ExisteSlugEnOtroVendedor(string slug, int idUsuario);

    Task ActualizarMiPerfilVendedor(
        ActualizarMiPerfilVendedorRequest request,
        int idUsuario,
        string? fotoPerfilUrl,
        string? bannerUrl,
        string? bannerTipo);
}

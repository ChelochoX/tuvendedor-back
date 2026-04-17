using tuvendedorback.DTOs;

namespace tuvendedorback.Repositories.Interfaces;

public interface IPerfilVendedorRepository
{
    Task<PerfilPublicoVendedorDto?> ObtenerPerfilPublicoPorSlug(string slug);
    Task<List<PerfilPublicoPublicacionDto>> ObtenerPublicacionesActivasPorSlug(string slug);
}

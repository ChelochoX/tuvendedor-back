using tuvendedorback.DTOs;

namespace tuvendedorback.Services.Interfaces;

public interface IPerfilVendedorService
{
    Task<PerfilPublicoVendedorDto> ObtenerPerfilPublicoPorSlug(string slug);
}

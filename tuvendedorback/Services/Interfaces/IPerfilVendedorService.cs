using tuvendedorback.DTOs;

namespace tuvendedorback.Services.Interfaces;

public interface IPerfilVendedorService
{
    Task<PerfilPublicoVendedorDto> ObtenerPerfilPublicoPorSlug(string slug);
    Task<PerfilPublicoVendedorDto> ObtenerMiPerfilVendedor(int idUsuario);

    Task<PerfilPublicoVendedorDto> ActualizarMiPerfilVendedor(
        ActualizarMiPerfilVendedorRequest request,
        int idUsuario);
}

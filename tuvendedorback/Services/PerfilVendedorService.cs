using tuvendedorback.DTOs;
using tuvendedorback.Exceptions;
using tuvendedorback.Repositories.Interfaces;
using tuvendedorback.Services.Interfaces;

namespace tuvendedorback.Services;

public class PerfilVendedorService : IPerfilVendedorService
{
    private readonly IPerfilVendedorRepository _repository;
    private readonly ILogger<PerfilVendedorService> _logger;

    public PerfilVendedorService(
        IPerfilVendedorRepository repository,
        ILogger<PerfilVendedorService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<PerfilPublicoVendedorDto> ObtenerPerfilPublicoPorSlug(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
            throw new ReglasdeNegocioException("El slug del perfil es obligatorio.");

        var perfil = await _repository.ObtenerPerfilPublicoPorSlug(slug);

        if (perfil == null)
            throw new NoDataFoundException("No se encontró el perfil del vendedor.");

        if (!perfil.EsPerfilPublico)
            throw new NoDataFoundException("El perfil del vendedor no está disponible públicamente.");

        var publicaciones = await _repository.ObtenerPublicacionesActivasPorSlug(slug);

        perfil.Publicaciones = publicaciones;
        perfil.CantidadPublicaciones = publicaciones.Count;

        return perfil;
    }
}

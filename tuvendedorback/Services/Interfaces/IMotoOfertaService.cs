using tuvendedorback.DTOs;

namespace tuvendedorback.Services.Interfaces;

public interface IMotoOfertaService
{
    Task<MotoOfertaDto> ObtenerOfertaPorPublicacion(
        int idPublicacion);
}

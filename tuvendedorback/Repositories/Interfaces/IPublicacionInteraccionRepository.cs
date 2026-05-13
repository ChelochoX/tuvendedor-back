using tuvendedorback.DTOs;

namespace tuvendedorback.Repositories.Interfaces;

public interface IPublicacionInteraccionRepository
{
    Task<bool> ExistePublicacionActiva(int idPublicacion);

    Task<PublicacionFavoritoResponseDto> ToggleFavorito(
        int idPublicacion,
        int? idUsuario,
        string? visitorId);

    Task RegistrarEvento(
        int idPublicacion,
        int? idUsuario,
        string? visitorId,
        string tipoEvento);
}

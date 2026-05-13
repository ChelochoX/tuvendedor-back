using tuvendedorback.DTOs;
using tuvendedorback.Request;

namespace tuvendedorback.Services.Interfaces;

public interface IPublicacionInteraccionService
{
    Task<PublicacionFavoritoResponseDto> ToggleFavorito(PublicacionInteraccionRequest request);
    Task RegistrarVista(PublicacionInteraccionRequest request);
    Task RegistrarClickWhatsapp(PublicacionInteraccionRequest request);
}

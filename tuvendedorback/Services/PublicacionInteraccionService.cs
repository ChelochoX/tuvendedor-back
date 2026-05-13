using tuvendedorback.Common;
using tuvendedorback.DTOs;
using tuvendedorback.Exceptions;
using tuvendedorback.Repositories.Interfaces;
using tuvendedorback.Request;
using tuvendedorback.Services.Interfaces;

namespace tuvendedorback.Services;

public class PublicacionInteraccionService : IPublicacionInteraccionService
{
    private const string EventoVistaDetalle = "VIEW_DETAIL";
    private const string EventoClickWhatsapp = "CLICK_WHATSAPP";

    private readonly IPublicacionInteraccionRepository _repository;
    private readonly UserContext _userContext;

    public PublicacionInteraccionService(
        IPublicacionInteraccionRepository repository,
        UserContext userContext)
    {
        _repository = repository;
        _userContext = userContext;
    }

    public async Task<PublicacionFavoritoResponseDto> ToggleFavorito(PublicacionInteraccionRequest request)
    {
        ValidarRequest(request);

        var existePublicacion = await _repository.ExistePublicacionActiva(request.IdPublicacion);
        if (!existePublicacion)
            throw new ReglasdeNegocioException("La publicación no existe o no se encuentra activa.");

        var idUsuario = ObtenerIdUsuario();
        var visitorId = NormalizarVisitorId(request.VisitorId, idUsuario);

        ValidarIdentidad(idUsuario, visitorId);

        return await _repository.ToggleFavorito(
            request.IdPublicacion,
            idUsuario,
            visitorId);
    }

    public async Task RegistrarVista(PublicacionInteraccionRequest request)
    {
        await RegistrarEvento(request, EventoVistaDetalle);
    }

    public async Task RegistrarClickWhatsapp(PublicacionInteraccionRequest request)
    {
        await RegistrarEvento(request, EventoClickWhatsapp);
    }

    private async Task RegistrarEvento(PublicacionInteraccionRequest request, string tipoEvento)
    {
        ValidarRequest(request);

        var existePublicacion = await _repository.ExistePublicacionActiva(request.IdPublicacion);
        if (!existePublicacion)
            return;

        var idUsuario = ObtenerIdUsuario();
        var visitorId = NormalizarVisitorId(request.VisitorId, idUsuario);

        ValidarIdentidad(idUsuario, visitorId);

        await _repository.RegistrarEvento(
            request.IdPublicacion,
            idUsuario,
            visitorId,
            tipoEvento);
    }

    private int? ObtenerIdUsuario()
    {
        return _userContext.IdUsuario != null && _userContext.IdUsuario > 0
            ? _userContext.IdUsuario
            : null;
    }

    private static string? NormalizarVisitorId(string? visitorId, int? idUsuario)
    {
        if (idUsuario.HasValue)
            return null;

        return string.IsNullOrWhiteSpace(visitorId)
            ? null
            : visitorId.Trim();
    }

    private static void ValidarIdentidad(int? idUsuario, string? visitorId)
    {
        if (!idUsuario.HasValue && string.IsNullOrWhiteSpace(visitorId))
            throw new ReglasdeNegocioException("No se pudo identificar al visitante.");
    }

    private static void ValidarRequest(PublicacionInteraccionRequest request)
    {
        if (request == null)
            throw new ReglasdeNegocioException("La solicitud no puede estar vacía.");

        if (request.IdPublicacion <= 0)
            throw new ReglasdeNegocioException("El identificador de la publicación no es válido.");
    }
}

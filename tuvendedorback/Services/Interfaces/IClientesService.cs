using tuvendedorback.DTOs;
using tuvendedorback.Request;

namespace tuvendedorback.Services.Interfaces;

public interface IClientesService
{
    Task<int> RegistrarInteresado(
        InteresadoRequest request,
        int idUsuario);

    Task<int> AgregarSeguimiento(
        SeguimientoRequest request,
        int idUsuario);

    Task<(List<InteresadoDto> Items, int Total)> ObtenerInteresados(
        FiltroInteresadosRequest filtro);

    Task<List<SeguimientoDto>> ObtenerSeguimientosPorInteresado(
        int idInteresado);

    Task<InteresadoDetalleDto> ObtenerDetalleInteresado(
        int idInteresado);

    Task<InteresadosResumenDto> ObtenerResumenInteresados(
        DateTime? fecha);

    Task ActualizarInteresado(
        int id,
        InteresadoRequest request,
        int idUsuario);

    Task<int> RegistrarInteraccionWhatsApp(
        InteresadoWhatsAppEventoRequest request);

    Task<SincronizacionWhatsAppResultadoDto> SincronizarWhatsAppDia(
        DateTime? fecha,
        int idUsuario);

    Task ActualizarSeguimientoInteresado(
        int idInteresado,
        ActualizarSeguimientoInteresadoRequest request,
        int idUsuario);
}

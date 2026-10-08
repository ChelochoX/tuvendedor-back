using tuvendedorback.DTOs;
using tuvendedorback.Request;

namespace tuvendedorback.Repositories.Interfaces;

public interface IClientesRepository
{
    Task<int> InsertarInteresado(InteresadoDto interesado);

    Task<int> InsertarSeguimiento(SeguimientoDto seguimiento);

    Task<(List<InteresadoDto> Items, int TotalRegistros)> ObtenerInteresados(
        FiltroInteresadosRequest filtro);

    Task<List<SeguimientoDto>> ObtenerSeguimientosPorInteresado(
        int idInteresado);

    Task<InteresadoDto?> ObtenerInteresadoPorId(
        int id);

    Task<InteresadoDetalleDto?> ObtenerDetalleInteresado(
        int id);

    Task<InteresadosResumenDto> ObtenerResumenInteresados(
        DateTime? fecha);

    Task ActualizarInteresado(
        InteresadoDto dto);

    Task<int> RegistrarInteraccionWhatsApp(
        InteresadoWhatsAppEventoRequest request);

    Task<SincronizarContactoWhatsAppResultadoDto> SincronizarContactoWhatsApp(
        WhatsAppContactoSincronizacionRequest request);

    Task ActualizarSeguimientoInteresado(
        int idInteresado,
        ActualizarSeguimientoInteresadoRequest request);
}

using tuvendedorback.DTOs;
using tuvendedorback.Request;
using tuvendedorback.Wrappers;

namespace tuvendedorback.Repositories.Interfaces;

public interface IServicioPremiumRepository
{
    Task<int?> ObtenerIdVendedorPorUsuario(
          int idUsuario);

    Task<bool> EsAdministrador(
        int idUsuario);

    Task<bool> EsPublicacionDelVendedor(
        int idPublicacion,
        int idVendedor);

    Task<bool> ExisteSolicitudPendiente(
        int idVendedor,
        string tipoServicio,
        int? idPublicacion);

    Task<int> CrearSolicitud(
        int idVendedor,
        CrearSolicitudServicioPremiumRequest request);

    Task<Datos<List<ServicioPremiumDto>>> ObtenerServicios(
        FiltrosServiciosPremiumRequest filtros);

    Task<ServicioPremiumDto?> ObtenerServicioPorId(
        int idServicio);

    Task<ResumenServiciosPremiumDto> ObtenerResumen();

    Task ActivarServicio(
        int idServicio,
        ActivarServicioPremiumRequest request,
        int idUsuarioAdmin);

    Task CancelarServicio(
        int idServicio,
        string? observacion,
        int idUsuarioAdmin);

    Task SincronizarVencimientos();
}

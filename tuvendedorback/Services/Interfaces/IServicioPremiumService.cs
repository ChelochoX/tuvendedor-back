using tuvendedorback.DTOs;
using tuvendedorback.Request;
using tuvendedorback.Wrappers;

namespace tuvendedorback.Services.Interfaces;

public interface IServicioPremiumService
{
    Task<int> CrearSolicitud(
       CrearSolicitudServicioPremiumRequest request,
       int idUsuario);

    Task<Datos<List<ServicioPremiumDto>>>
        ObtenerServiciosParaAdministrador(
            FiltrosServiciosPremiumRequest filtros,
            int idUsuarioAdmin);

    Task<ServicioPremiumDto>
        ObtenerServicioPorIdParaAdministrador(
            int idServicio,
            int idUsuarioAdmin);

    Task<ResumenServiciosPremiumDto>
        ObtenerResumenParaAdministrador(
            int idUsuarioAdmin);

    Task ActivarServicio(
        int idServicio,
        ActivarServicioPremiumRequest request,
        int idUsuarioAdmin);

    Task CancelarServicio(
        int idServicio,
        CancelarServicioPremiumRequest request,
        int idUsuarioAdmin);
}

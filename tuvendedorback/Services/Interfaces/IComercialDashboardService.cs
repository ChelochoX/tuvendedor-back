using tuvendedorback.DTOs;
using tuvendedorback.Request;

namespace tuvendedorback.Services.Interfaces;

public interface IComercialDashboardService
{
    Task<ComercialDashboardDto> ObtenerDashboard(
        FiltroDashboardComercialRequest filtro,
        int idUsuario);
}

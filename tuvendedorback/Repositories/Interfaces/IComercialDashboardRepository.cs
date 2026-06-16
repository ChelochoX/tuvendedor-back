using tuvendedorback.DTOs;

namespace tuvendedorback.Repositories.Interfaces;

public interface IComercialDashboardRepository
{
    Task<ComercialDashboardDto> ObtenerDashboard(
        DateTime fechaDesde,
        DateTime fechaHasta);

    Task<bool> EsAdministrador(
        int idUsuario);
}

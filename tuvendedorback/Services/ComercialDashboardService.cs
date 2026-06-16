using tuvendedorback.DTOs;
using tuvendedorback.Exceptions;
using tuvendedorback.Repositories.Interfaces;
using tuvendedorback.Request;
using tuvendedorback.Services.Interfaces;

namespace tuvendedorback.Services;

public class ComercialDashboardService
    : IComercialDashboardService
{
    private readonly IComercialDashboardRepository _repository;

    public ComercialDashboardService(
        IComercialDashboardRepository repository)
    {
        _repository = repository;
    }

    public async Task<ComercialDashboardDto> ObtenerDashboard(
        FiltroDashboardComercialRequest filtro,
        int idUsuario)
    {
        await ValidarAdministrador(idUsuario);

        var fechaHasta =
            filtro.FechaHasta?.Date
            ?? DateTime.Today;

        var fechaDesde =
            filtro.FechaDesde?.Date
            ?? fechaHasta.AddDays(-27);

        if (fechaDesde > fechaHasta)
        {
            throw new ReglasdeNegocioException(
                "La fecha desde no puede ser mayor a la fecha hasta.");
        }

        var cantidadDias =
            (fechaHasta - fechaDesde).TotalDays;

        if (cantidadDias > 180)
        {
            throw new ReglasdeNegocioException(
                "El período máximo permitido para el dashboard es de 180 días.");
        }

        return await _repository.ObtenerDashboard(
            fechaDesde,
            fechaHasta);
    }

    private async Task ValidarAdministrador(
        int idUsuario)
    {
        if (idUsuario <= 0)
        {
            throw new UnauthorizedAccessException();
        }

        var esAdministrador =
            await _repository.EsAdministrador(idUsuario);

        if (!esAdministrador)
        {
            throw new UnauthorizedAccessException();
        }
    }
}

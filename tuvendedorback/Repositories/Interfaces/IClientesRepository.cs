using tuvendedorback.DTOs;
using tuvendedorback.Request;

namespace tuvendedorback.Repositories.Interfaces;

public interface IClientesRepository
{
    Task<int> InsertarInteresado(InteresadoDto interesado);
    Task<int> InsertarSeguimiento(SeguimientoDto seguimiento);
    Task<(List<InteresadoDto> Items, int TotalRegistros)> ObtenerInteresados(FiltroInteresadosRequest filtro);
    Task<List<SeguimientoDto>> ObtenerSeguimientosPorInteresado(int idInteresado);
}

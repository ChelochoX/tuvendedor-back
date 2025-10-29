using tuvendedorback.DTOs;
using tuvendedorback.Request;

namespace tuvendedorback.Services.Interfaces;

public interface IClientesService
{
    Task<int> RegistrarInteresado(InteresadoRequest request, int idUsuario);
    Task<int> AgregarSeguimiento(SeguimientoRequest request, int idUsuario);
    Task<(List<InteresadoDto> Items, int Total)> ObtenerInteresados(FiltroInteresadosRequest filtro);
    Task<List<SeguimientoDto>> ObtenerSeguimientosPorInteresado(int idInteresado);
    Task ActualizarInteresado(int id, InteresadoRequest request, int idUsuario);
}

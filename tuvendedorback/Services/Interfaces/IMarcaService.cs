using tuvendedorback.DTOs;
using tuvendedorback.Request;

namespace tuvendedorback.Services.Interfaces;

public interface IMarcaService
{

    Task<int> CrearMarca(CrearMarcaRequest request);
    Task EditarMarca(EditarMarcaRequest request);
    Task ActivarMarca(int id);
    Task DesactivarMarca(int id);
    Task<List<MarcaDto>> ObtenerMarcas(bool soloActivas = true);
}

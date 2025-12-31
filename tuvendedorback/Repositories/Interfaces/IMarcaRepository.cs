using tuvendedorback.DTOs;

namespace tuvendedorback.Repositories.Interfaces;

public interface IMarcaRepository
{
    Task<int> CrearMarca(string nombre);
    Task EditarMarca(int id, string nombre);
    Task DesactivarMarca(int id);
    Task ActivarMarca(int id);
    Task<bool> ExisteMarcaConNombre(string nombre, int? excluirId = null);
    Task<List<MarcaDto>> ObtenerMarcas(bool soloActivas);
}

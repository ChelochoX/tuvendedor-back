using tuvendedorback.DTOs;
using tuvendedorback.Request;

namespace tuvendedorback.Services.Interfaces;

public interface IDescuentoContadoMotoService
{
    Task<IReadOnlyList<DescuentoContadoMarcaDto>>
        ListarMarcas();


    Task<DescuentoContadoConfiguracionDto>
        ObtenerConfiguracion(
            int idMarca,
            int anio,
            int mes);


    Task<DescuentoContadoConfiguracionDto>
        GuardarConfiguracion(
            GuardarDescuentoContadoMesRequest request);


    Task<DescuentoContadoConfiguracionDto>
        CrearModeloConExcepcion(
            CrearModeloConExcepcionDescuentoRequest request);
}
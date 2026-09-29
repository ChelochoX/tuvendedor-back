using tuvendedorback.DTOs;

namespace tuvendedorback.Repositories.Interfaces;

public interface IDescuentoContadoMotoRepository
{
    Task<IReadOnlyList<DescuentoContadoMarcaDto>>
        ListarMarcas();


    Task<DescuentoContadoConfiguracionDto?>
        ObtenerConfiguracion(
            int idMarca,
            int anio,
            int mes,
            DateTime fechaDesde,
            DateTime fechaHasta);


    Task GuardarConfiguracionMes(
        int idMarca,
        DateTime fechaDesde,
        DateTime fechaHasta,
        decimal porcentajeGeneral,
        IReadOnlyList<DescuentoContadoExcepcionGuardarDto> excepciones);


    Task<int> CrearModeloConExcepcion(
        int idMarca,
        string codigoReferencia,
        string nombreModelo,
        int? cilindrada,
        string? categoria,
        DateTime fechaDesde,
        DateTime fechaHasta,
        decimal porcentajeDescuento);
}
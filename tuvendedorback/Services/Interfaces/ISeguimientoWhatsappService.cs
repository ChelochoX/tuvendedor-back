using tuvendedorback.DTOs;
using tuvendedorback.Request;

namespace tuvendedorback.Services.Interfaces;

public interface ISeguimientoWhatsappService
{
    Task<SeguimientoWhatsappConfiguracionDto> ObtenerConfiguracion();

    Task<SeguimientoWhatsappConfiguracionDto> ActualizarConfiguracion(
        ActualizarSeguimientoWhatsappConfiguracionRequest request,
        int? idUsuario);

    Task ProcesarCiclo(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SeguimientoWhatsappEnvioDto>> ListarEnvios(
        string? estado,
        int limite = 200);
}

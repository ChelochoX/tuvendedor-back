using tuvendedorback.DTOs;

namespace tuvendedorback.Services.Interfaces;

public interface ISeguimientoWhatsappSender
{
    Task<SeguimientoWhatsappEnvioResultadoDto> Enviar(
        long idEnvio,
        string telefono,
        string mensaje,
        CancellationToken cancellationToken = default);
}

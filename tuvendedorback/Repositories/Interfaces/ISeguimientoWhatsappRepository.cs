using tuvendedorback.DTOs;
using tuvendedorback.Request;

namespace tuvendedorback.Repositories.Interfaces;

public interface ISeguimientoWhatsappRepository
{
    Task<SeguimientoWhatsappConfiguracionDto> ObtenerConfiguracion();

    Task GuardarConfiguracion(
        ActualizarSeguimientoWhatsappConfiguracionRequest request,
        int? idUsuario);

    Task<IReadOnlyList<SeguimientoWhatsappCandidatoDto>> ObtenerCandidatos(
        DateTime fechaDesdeElegibilidad,
        int limite);

    Task<bool> CrearPendienteSiNoExiste(
        SeguimientoWhatsappCandidatoDto candidato,
        SeguimientoWhatsappReglaDto regla,
        DateTime programadoPara,
        string mensaje);

    Task AplicarBajasAutomaticas();

    Task RecuperarProcesandoVencidos(int minutos);

    Task<SeguimientoWhatsappEnvioPendienteDto?> TomarSiguientePendiente(
        int separacionEnviosMinutos);

    Task<SeguimientoWhatsappEstadoVigenciaDto> ValidarVigencia(long idEnvio);

    // Si el operador apaga el motor durante un envío, devolverlo a la cola.
    Task LiberarPendiente(long idEnvio);

    Task MarcarCancelado(long idEnvio, string motivo);

    Task MarcarEnviado(
        long idEnvio,
        string? messageIdProveedor);

    Task MarcarError(
        long idEnvio,
        string error,
        int minutosReintento,
        int maximoReintentos);

    Task<IReadOnlyList<SeguimientoWhatsappEnvioDto>> ListarEnvios(
        string? estado,
        int limite);
}

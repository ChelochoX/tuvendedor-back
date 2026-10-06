using tuvendedorback.DTOs;

namespace tuvendedorback.Repositories.Interfaces;

public interface ISolicitudMotoRepository
{
    Task<SolicitudMotoProcesoDto?> ObtenerActivaPorConversacion(
        int idConversacion);

    Task<ReglaCreditoMotoDto> ObtenerReglaCreditoActiva();

    Task<int> ObtenerOCrearContacto(
        string telefono);

    Task<int> CrearCredito(
        int idConversacion,
        int idModeloProducto,
        int? idPublicacion,
        int idContacto);

    Task<int> CrearContado(
        int idConversacion,
        int idModeloProducto,
        int? idPublicacion,
        int idContacto);

    Task ActualizarPaso(
        string tipoOperacion,
        int idSolicitud,
        string pasoActual);

    Task GuardarFechaNacimiento(
        int idContacto,
        DateTime fechaNacimiento);

    Task GuardarDatosContactoParciales(
        int idContacto,
        string? nombreCompleto = null,
        string? numeroCedula = null,
        string? ciudad = null,
        string? barrio = null,
        string? direccion = null);

    Task GuardarDatosLaboralesParciales(
        int idSolicitudCredito,
        string? empresa = null,
        int? antiguedadMeses = null,
        bool? aportaIps = null,
        int? cantidadAportesIps = null,
        string? direccionEmpresa = null,
        string? telefonoEmpresa = null,
        bool? telefonoEsMovil = null,
        string? nombreJefeEncargado = null);

    Task GuardarIdentidadContacto(
        int idContacto,
        string nombreCompleto,
        string numeroCedula);

    Task GuardarDomicilioContacto(
        int idContacto,
        string ciudad,
        string barrio,
        string direccion);

    Task GuardarPrecalificacionLaboral(
        int idSolicitudCredito,
        string empresa,
        int antiguedadMeses,
        bool aportaIps,
        int cantidadAportesIps);

    Task GuardarDatosLaboralesCompletos(
        int idSolicitudCredito,
        string direccionEmpresa,
        string telefonoEmpresa,
        bool telefonoEsMovil,
        string? nombreJefeEncargado);

    Task GuardarResultadoPreEvaluacion(
        int idSolicitudCredito,
        string resultado,
        string? motivo,
        string? viaEvaluacion,
        string? siguientePaso);

    Task AgregarReferencia(
        int idSolicitudCredito,
        string tipo,
        string nombre,
        string telefono,
        string? parentesco,
        string? observacion);

    Task<IReadOnlyList<SolicitudMotoReferenciaDto>> ObtenerReferencias(
        int idSolicitudCredito);

    Task GuardarEstadoCedula(
        string tipoOperacion,
        int idSolicitud,
        string estadoCedula);

    Task GuardarDocumento(
        string tipoOperacion,
        int idSolicitud,
        int idConversacion,
        int? idModeloProducto,
        string tipoDocumento,
        string nombreArchivo,
        string mimeType,
        string rutaPrivada,
        string hashSha256);

    Task<bool> TieneDocumento(
        string tipoOperacion,
        int idSolicitud,
        string tipoDocumento);


    Task<string?> ObtenerPromptFlujo(
        string codigo);

    Task<decimal?> ObtenerCuotaReferenciaModelo(
        int idModeloProducto);

    Task GuardarReferenciaComercialPrecalificacion(
        int idSolicitudCredito,
        bool esGarante,
        string? nombre = null,
        int? antiguedadMeses = null,
        decimal? montoCuota = null);

    Task GuardarDatosGarantePrecalificacion(
        int idSolicitudCredito,
        bool? requiereGarante = null,
        bool? aportaIps = null,
        int? cantidadAportesIps = null,
        string? estado = null);

    Task<string?> ObtenerTextoAutorizacion();

    Task GuardarAutorizacion(
        int idSolicitudCredito,
        string version,
        string textoAutorizacion,
        string mensajeOriginal,
        string nombreCompleto,
        string numeroCedula);

    Task<bool> TieneAutorizacion(
        int idSolicitudCredito);

    Task MarcarListaRevision(
        string tipoOperacion,
        int idSolicitud);

    Task Cancelar(
        string tipoOperacion,
        int idSolicitud,
        string motivo);
}

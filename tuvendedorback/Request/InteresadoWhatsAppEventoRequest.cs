namespace tuvendedorback.Request;

/// <summary>
/// Evento interno generado por Panambí para mantener actualizado
/// el CRM de interesados a partir de la conversación de WhatsApp.
/// </summary>
public class InteresadoWhatsAppEventoRequest
{
    public int IdConversacion { get; set; }

    /// <summary>
    /// Identificador usado por el bridge/WhatsApp para la conversación.
    /// Puede ser número o un identificador LID.
    /// </summary>
    public string? IdentificadorExterno { get; set; }

    /// <summary>
    /// Número real de WhatsApp cuando el bridge lo pueda resolver.
    /// Es opcional para mantener compatibilidad con el bridge actual.
    /// </summary>
    public string? NumeroWhatsapp { get; set; }

    /// <summary>
    /// Nombre/pushName de WhatsApp cuando el bridge lo envíe.
    /// </summary>
    public string? NombreContacto { get; set; }

    public int? IdModeloProducto { get; set; }

    public int? IdPublicacion { get; set; }

    public string? TipoConsulta { get; set; }

    public string? EstadoConsulta { get; set; }

    public string? MensajeCliente { get; set; }

    public string? Respuesta { get; set; }

    public string? TipoOperacion { get; set; }

    public int? IdSolicitudOperacion { get; set; }

    public string? PasoOperacion { get; set; }

    public string? MotivoSeguimiento { get; set; }

    /// <summary>
    /// True solamente cuando el evento corresponde a un nuevo mensaje
    /// que acaba de enviar el cliente.
    /// </summary>
    public bool EsEntradaCliente { get; set; }
}


public class ActualizarSeguimientoInteresadoRequest
{
    public DateTime? FechaProximoContacto { get; set; }

    public bool? RequiereSeguimiento { get; set; }

    public string? MotivoSeguimiento { get; set; }

    public string? EstadoConsulta { get; set; }

    public string? Comentario { get; set; }
}


/// <summary>
/// Contacto/chat recuperado desde la sesión activa de WhatsApp para
/// sincronización comercial manual desde el CRM.
/// </summary>
public class WhatsAppContactoSincronizacionRequest
{
    public string IdentificadorExterno { get; set; } = string.Empty;

    public string? NumeroWhatsapp { get; set; }

    public string? NombreContacto { get; set; }

    public string? UltimoMensajeCliente { get; set; }

    public string? UltimaRespuesta { get; set; }

    public DateTime? FechaUltimoMensajeCliente { get; set; }

    public DateTime? FechaUltimaRespuesta { get; set; }

    public DateTime FechaUltimaInteraccion { get; set; }

    public int CantidadMensajesDia { get; set; }
}

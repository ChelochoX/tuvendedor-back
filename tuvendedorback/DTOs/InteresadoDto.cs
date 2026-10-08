namespace tuvendedorback.DTOs;

public class InteresadoDto
{
    public int Id { get; set; }

    public string Nombre { get; set; } = null!;

    public string? Telefono { get; set; }

    public string? Email { get; set; }

    public string? Ciudad { get; set; }

    public string? ProductoInteres { get; set; }

    public bool AportaIPS { get; set; }

    public int CantidadAportes { get; set; }

    public string? Estado { get; set; }

    public DateTime FechaRegistro { get; set; }

    public DateTime? FechaProximoContacto { get; set; }

    public string? Descripcion { get; set; }

    public string? ArchivoUrl { get; set; }

    public string? UsuarioResponsable { get; set; }


    // =========================================================
    // DATOS COMERCIALES / WHATSAPP
    // =========================================================

    public string? Origen { get; set; }

    public string? IdentificadorExterno { get; set; }

    public int? IdConversacion { get; set; }

    public int? IdModeloProducto { get; set; }

    public int? IdPublicacion { get; set; }

    public string? MarcaInteres { get; set; }

    public string? ModeloInteres { get; set; }

    public string? CodigoReferencia { get; set; }

    public string? EstadoConsulta { get; set; }

    public string? EstadoGestion { get; set; }

    public string? TipoOperacion { get; set; }

    public int? IdSolicitudOperacion { get; set; }

    public string? PasoOperacion { get; set; }

    public bool RequiereSeguimiento { get; set; }

    public string? MotivoSeguimiento { get; set; }

    public DateTime? FechaUltimoMensajeCliente { get; set; }

    public DateTime? FechaUltimaRespuesta { get; set; }

    public DateTime? FechaUltimaInteraccion { get; set; }

    public string? UltimoMensajeCliente { get; set; }

    public string? UltimaRespuesta { get; set; }

    public int CantidadInteracciones { get; set; }

    public bool SinRespuesta { get; set; }

    public bool SeguimientoVencido { get; set; }
}


public class InteresadoConsultaMotoDto
{
    public int Id { get; set; }

    public int IdInteresado { get; set; }

    public int IdConversacion { get; set; }

    public int? IdModeloProducto { get; set; }

    public int? IdPublicacion { get; set; }

    public string? Marca { get; set; }

    public string? Modelo { get; set; }

    public string? CodigoReferencia { get; set; }

    public string? TipoConsulta { get; set; }

    public string? EstadoConsulta { get; set; }

    public string? TipoOperacion { get; set; }

    public int? IdSolicitudOperacion { get; set; }

    public string? PasoOperacion { get; set; }

    public string? UltimoMensajeCliente { get; set; }

    public string? UltimaRespuesta { get; set; }

    public DateTime FechaPrimeraConsulta { get; set; }

    public DateTime FechaUltimaConsulta { get; set; }

    public int CantidadInteracciones { get; set; }
}


public class InteresadoDetalleDto
{
    public InteresadoDto Interesado { get; set; } = new();

    public List<InteresadoConsultaMotoDto> ConsultasMoto { get; set; } = new();

    public List<MensajeConversacionHistorialDto> UltimosMensajes { get; set; } = new();

    public List<SeguimientoDto> Seguimientos { get; set; } = new();
}


public class InteresadosResumenDto
{
    public int TotalActivos { get; set; }

    public int NuevosDelDia { get; set; }

    /// <summary>
    /// Cantidad de interesados con interacción en la fecha consultada.
    /// Es un conteo de contactos únicos, no de mensajes.
    /// </summary>
    public int InteraccionesDelDia { get; set; }

    public int PendientesSeguimiento { get; set; }

    public int SeguimientosVencidos { get; set; }

    public int SinRespuesta { get; set; }

    public int Consultando { get; set; }

    public int Cotizados { get; set; }

    public int CreditoEnProceso { get; set; }

    public int ContadoEnProceso { get; set; }

    public int DerivadosHumano { get; set; }
}


public class WhatsAppConversacionSincronizacionDto
{
    public string IdentificadorExterno { get; set; } =
        string.Empty;

    public string? UltimoMensajeCliente { get; set; }

    public string? UltimaRespuesta { get; set; }

    public DateTime? FechaUltimoMensajeCliente { get; set; }

    public DateTime? FechaUltimaRespuesta { get; set; }

    public DateTime FechaUltimaInteraccion { get; set; }

    public int CantidadMensajesDia { get; set; }
}


public class SincronizarContactoWhatsAppResultadoDto
{
    public int IdInteresado { get; set; }

    public bool EsNuevo { get; set; }

    public bool TieneTelefonoReal { get; set; }
}


public class SincronizacionWhatsAppResultadoDto
{
    public DateTime Fecha { get; set; }

    public int ChatsEncontrados { get; set; }

    public int Procesados { get; set; }

    public int Nuevos { get; set; }

    public int Actualizados { get; set; }

    public int ConTelefonoReal { get; set; }

    public int SinTelefonoReal { get; set; }

    public int Errores { get; set; }
}

namespace tuvendedorback.DTOs;

public sealed class SeguimientoWhatsappConfiguracionDto
{
    public bool Activo { get; set; }
    public string ModoEnvio { get; set; } = "SIMULACION";
    public int SeparacionEnviosMinutos { get; set; }
    public TimeSpan HoraInicio { get; set; }
    public TimeSpan HoraFin { get; set; }
    public DateTime? FechaDesdeElegibilidad { get; set; }
    public int MinutosReintentoTecnico { get; set; }
    public int MaximoReintentosTecnicos { get; set; }
    public DateTime FechaActualizacion { get; set; }
    public IReadOnlyList<SeguimientoWhatsappReglaDto> Reglas { get; set; } = new List<SeguimientoWhatsappReglaDto>();
}

public sealed class SeguimientoWhatsappReglaDto
{
    public int Id { get; set; }
    public int Orden { get; set; }
    public int DemoraValor { get; set; }
    public string DemoraUnidad { get; set; } = string.Empty;
    public string Mensaje { get; set; } = string.Empty;
    public bool Activo { get; set; }
}

public sealed class SeguimientoWhatsappCandidatoDto
{
    public int IdInteresado { get; set; }
    public int IdConversacion { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public string? ProductoInteres { get; set; }
    public DateTime CicloInicio { get; set; }
    public DateTime FechaUltimoMensajeCliente { get; set; }
    public string? UltimoMensajeCliente { get; set; }
    public int CantidadSeguimientosEnviados { get; set; }
    public int? UltimoNumeroSeguimientoEnviado { get; set; }
    public DateTime? FechaUltimoSeguimientoEnviado { get; set; }
    public DateTime? FechaPausaHasta { get; set; }
}

public sealed class SeguimientoWhatsappEnvioPendienteDto
{
    public long Id { get; set; }
    public int IdInteresado { get; set; }
    public int? IdConversacion { get; set; }
    public int IdRegla { get; set; }
    public int NumeroSeguimiento { get; set; }
    public DateTime CicloInicio { get; set; }
    public string Telefono { get; set; } = string.Empty;
    public string Mensaje { get; set; } = string.Empty;
    public DateTime ProgramadoPara { get; set; }
    public int IntentosTecnicos { get; set; }
}

public sealed class SeguimientoWhatsappEnvioDto
{
    public long Id { get; set; }
    public int IdInteresado { get; set; }
    public string Cliente { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public string? ProductoInteres { get; set; }
    public int NumeroSeguimiento { get; set; }
    public DateTime CicloInicio { get; set; }
    public DateTime ProgramadoPara { get; set; }
    public string Estado { get; set; } = string.Empty;
    public int IntentosTecnicos { get; set; }
    public DateTime FechaCreacion { get; set; }
    public DateTime? FechaEnvio { get; set; }
    public string? UltimoError { get; set; }
    public string? MotivoCancelacion { get; set; }
    public string Mensaje { get; set; } = string.Empty;
}

public sealed class SeguimientoWhatsappEnvioResultadoDto
{
    public bool Enviado { get; set; }
    public string? MessageId { get; set; }
    public string? Error { get; set; }
}

public sealed class SeguimientoWhatsappEstadoVigenciaDto
{
    public bool Vigente { get; set; }
    public string? MotivoCancelacion { get; set; }
    public string? UltimoMensajeCliente { get; set; }
    public DateTime? FechaUltimoMensajeCliente { get; set; }
}

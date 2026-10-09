namespace tuvendedorback.Request;

public sealed class ActualizarSeguimientoWhatsappConfiguracionRequest
{
    public bool Activo { get; set; }
    public string ModoEnvio { get; set; } = "SIMULACION";
    public int SeparacionEnviosMinutos { get; set; } = 5;
    public TimeSpan HoraInicio { get; set; } = new(8, 0, 0);
    public TimeSpan HoraFin { get; set; } = new(19, 0, 0);
    public DateTime? FechaDesdeElegibilidad { get; set; }
    public int MinutosReintentoTecnico { get; set; } = 30;
    public int MaximoReintentosTecnicos { get; set; } = 3;
    public List<ActualizarSeguimientoWhatsappReglaRequest> Reglas { get; set; } = new();
}

public sealed class ActualizarSeguimientoWhatsappReglaRequest
{
    public int Orden { get; set; }
    public int DemoraValor { get; set; }
    public string DemoraUnidad { get; set; } = "DIA";
    public string Mensaje { get; set; } = string.Empty;
    public bool Activo { get; set; } = true;
}

namespace tuvendedorback.Prospeccion;

public sealed class ProspectoDto
{
    public long Id { get; set; }
    public string Rubro { get; set; } = "";
    public string SolicitudCompra { get; set; } = "";
    public string? Distrito { get; set; }
    public string? Departamento { get; set; }
    public string? PresupuestoConfirmadoGs { get; set; }
    public string? ImporteMostradoGs { get; set; }
    public string? FechaDeteccionOriginal { get; set; }
    public string? FechaPublicacionOriginal { get; set; }
    public string? Fuente { get; set; }
    public string? UrlOriginal { get; set; }
    public string? Confianza { get; set; }
    public string? MotivoConfianza { get; set; }
    public string? Prioridad { get; set; }
    public string? EstadoFuente { get; set; }
    public string? Observaciones { get; set; }
    public string? ArchivoOrigen { get; set; }
    public string? UrlPlanilla { get; set; }
    public string EstadoGestion { get; set; } = "NUEVO";
    public string? NotasGestion { get; set; }
    public DateTime FechaPrimerRegistroUtc { get; set; }
    public DateTime FechaUltimaDeteccionUtc { get; set; }
    public DateTime? FechaGestionUtc { get; set; }
}

public sealed class ProspectoListadoDto
{
    public List<ProspectoDto> Items { get; set; } = new();
    public int Total { get; set; }
    public int Nuevos { get; set; }
    public int EnRevision { get; set; }
    public int Contactados { get; set; }
    public int TotalUltimas24Horas { get; set; }
    public DateTime? UltimaSincronizacionUtc { get; set; }
    public bool UltimaSincronizacionExitosa { get; set; }
    public string? UltimoError { get; set; }
    public int Pagina { get; set; }
    public int PorPagina { get; set; }
}

public sealed class CambiarGestionRequest
{
    public string EstadoGestion { get; set; } = "";
    public string? NotasGestion { get; set; }
}

public sealed class ResultadoSincronizacion
{
    public int Planillas { get; set; }
    public int FilasLeidas { get; set; }
    public int FilasNuevas { get; set; }
    public int FilasRepetidas { get; set; }
    public int FilasOmitidas { get; set; }
}

internal sealed class ProspectoImportado
{
    public string Rubro { get; set; } = "";
    public string ClaveFuente { get; set; } = "";
    public string? IdFuente { get; set; }
    public string SolicitudCompra { get; set; } = "";
    public string? Distrito { get; set; }
    public string? Departamento { get; set; }
    public string? PresupuestoConfirmadoGs { get; set; }
    public string? ImporteMostradoGs { get; set; }
    public string? FechaDeteccionOriginal { get; set; }
    public string? FechaPublicacionOriginal { get; set; }
    public string? Fuente { get; set; }
    public string? UrlOriginal { get; set; }
    public string? Confianza { get; set; }
    public string? MotivoConfianza { get; set; }
    public string? Prioridad { get; set; }
    public string? EstadoFuente { get; set; }
    public string? Observaciones { get; set; }
    public string? ArchivoOrigen { get; set; }
    public string? UrlPlanilla { get; set; }
}

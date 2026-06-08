namespace tuvendedorback.DTOs;

public class ResumenServiciosPremiumDto
{
    public int SolicitudesPendientes { get; set; }

    public int ServiciosActivos { get; set; }

    public int ProximosAVencer { get; set; }

    public decimal MontoCobrado { get; set; }
}

namespace tuvendedorback.DTOs;

public class SolicitudesdeCreditoDTO
{
    public int Id { get; set; }
    public string Cedula { get; set; }
    public string ModeloSolicitado { get; set; }
    public decimal EntregaInicial { get; set; }
    public int Cuotas { get; set; }
    public decimal MontoPorCuota { get; set; }
    public string Telefono { get; set; }
    public DateTime FechaCreacion { get; set; }
   
}

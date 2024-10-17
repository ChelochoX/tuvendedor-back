namespace tuvendedorback.Request;

public class CalculoCuotaRequest
{
    public string ModeloSolicitado { get; set; }
    public decimal EntregaInicial { get; set; }
    public int CantidadCuotas { get; set; }    
}

namespace tuvendedorback.Request;

public class EditarPlanFinanciacionProductoRequest
{
    public int Id { get; set; }
    public decimal EntregaInicial { get; set; }
    public int CantidadCuotas { get; set; }
    public decimal ImporteCuota { get; set; }
    public decimal? Interes { get; set; }
    public string? CodigoPlan { get; set; }
}

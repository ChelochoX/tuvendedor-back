namespace tuvendedorback.Models;

public class PlanCredito
{
    public int Id { get; set; }
    public int PublicacionId { get; set; }
    public int Cuotas { get; set; }
    public decimal ValorCuota { get; set; }
}

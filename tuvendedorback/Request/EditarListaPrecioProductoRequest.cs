namespace tuvendedorback.Request;

public class EditarListaPrecioProductoRequest
{
    public int Id { get; set; }
    public decimal PrecioPublico { get; set; }
    public decimal PrecioDistribuidor { get; set; }
    public decimal PrecioBase { get; set; }
    public DateTime FechaDesde { get; set; }
    public DateTime? FechaHasta { get; set; }
}

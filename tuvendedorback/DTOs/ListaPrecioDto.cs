namespace tuvendedorback.DTOs;

public class ListaPrecioDto
{
    public int IdListaPrecio { get; set; }
    public int IdModeloProducto { get; set; }

    public decimal PrecioPublico { get; set; }
    public decimal PrecioDistribuidor { get; set; }
    public decimal PrecioBase { get; set; }

    public DateTime FechaDesde { get; set; }
    public DateTime? FechaHasta { get; set; }
    public bool EsPromo { get; set; }

    public List<PlanFinanciacionDto> Planes { get; set; } = new();
}

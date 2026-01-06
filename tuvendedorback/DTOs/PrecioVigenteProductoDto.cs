namespace tuvendedorback.DTOs;

public class PrecioVigenteProductoDto
{
    public int IdModeloProducto { get; set; }
    public string Rubro { get; set; } = "";
    public string CodigoReferencia { get; set; } = "";
    public string NombreModelo { get; set; } = "";
    public string Marca { get; set; } = "";

    public int IdListaPrecio { get; set; }
    public decimal PrecioPublico { get; set; }
    public decimal PrecioDistribuidor { get; set; }
    public decimal PrecioBase { get; set; }
    public DateTime FechaDesde { get; set; }
    public DateTime? FechaHasta { get; set; }

    public List<PlanFinanciacionDto> Planes { get; set; } = new();
}
public class PlanFinanciacionDto
{
    public int Id { get; set; }
    public int IdListaPrecio { get; set; }
    public decimal EntregaInicial { get; set; }
    public int CantidadCuotas { get; set; }
    public decimal ImporteCuota { get; set; }
    public decimal? Interes { get; set; }
    public string? CodigoPlan { get; set; }
}
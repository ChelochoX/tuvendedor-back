using System.Numerics;

namespace tuvendedorback.DTOs;

public class ProductoDTO
{
    public int IdProducto { get; set; }
    public string Articulo { get; set; }
    public string Modelo { get; set; }
    public decimal PrecioPublico { get; set; }
    public decimal PrecioMayorista { get; set; }
    public decimal PrecioBase { get; set; }

    public List<Plan> Planes { get; set; }

    public ProductoDTO()
    {
        Planes = new List<Plan>(); 
    }
}
public class Plan
{
    public int IdPrecioPlan { get; set; }
    public int IdPlan { get; set; }
    public decimal Entrega { get; set; }
    public int Cuotas { get; set; }
    public decimal Importe { get; set; }
    public string NombrePlan { get; set; }
}
using System.Numerics;

namespace tuvendedorback.DTOs;

public class ProductosDTOPromo
{
    public int IdProducto { get; set; }
    public string Articulo { get; set; }
    public string Modelo { get; set; }
    public decimal PrecioPublicoPromo { get; set; }
    public decimal PrecioMayoristaPromo { get; set; }
    public decimal PrecioBasePromo { get; set; }

    public List<PlanesPromo> Planes { get; set; }

    public ProductosDTOPromo()
    {
        Planes = new List<PlanesPromo>(); 
    }
}
public class PlanesPromo
{
    public int IdPrecioPlan { get; set; }
    public int IdPlan { get; set; }
    public decimal EntregaPromo { get; set; }
    public int CuotasPromo { get; set; }
    public decimal ImportePromo { get; set; }
    public string NombrePlan { get; set; }
}
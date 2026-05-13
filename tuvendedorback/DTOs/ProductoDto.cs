namespace tuvendedorback.DTOs;

public class ProductoDto
{
    public int Id { get; set; }
    public string Nombre { get; set; }
    public decimal Precio { get; set; }
    public string? Moneda { get; set; }
    public string Categoria { get; set; }
    public string Ubicacion { get; set; }
    public string Estado { get; set; }

    public string Imagen { get; set; }
    public string Miniatura { get; set; }

    public List<string> Imagenes { get; set; } = new();
    public string Descripcion { get; set; }
    public bool MostrarBotonesCompra { get; set; }

    // ⭐ DESTACADO
    public bool EsDestacada { get; set; }
    public DateTime? FechaFinDestacado { get; set; }

    // 👑 TEMPORADA
    public bool EsTemporada { get; set; }
    public DateTime? FechaFinTemporada { get; set; }
    public string? BadgeTexto { get; set; }
    public string? BadgeColor { get; set; }

    //GPS
    public decimal? Latitud { get; set; }
    public decimal? Longitud { get; set; }
    public string? GoogleMapsUrl { get; set; }

    // 👤 Vendedor
    public VendedorDto Vendedor { get; set; }

    // 💳 Planes crédito
    public PlanCreditoDto? PlanCredito { get; set; }

    //Me Gusta/Vistas/WA
    public int CantidadFavoritos { get; set; }
    public int CantidadVistas { get; set; }
    public int CantidadClicksWhatsapp { get; set; }
    public bool EsFavorito { get; set; }
}

public class VendedorDto
{
    public string? Nombre { get; set; }
    public string? Avatar { get; set; }
    public string Telefono { get; set; }
}

public class PlanCreditoDto
{
    public List<PlanOpcionDto> Opciones { get; set; } = new();
}

public class PlanOpcionDto
{
    public int Cuotas { get; set; }
    public decimal ValorCuota { get; set; }
}


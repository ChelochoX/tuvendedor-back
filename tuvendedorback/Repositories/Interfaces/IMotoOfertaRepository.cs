using tuvendedorback.DTOs;

namespace tuvendedorback.Repositories.Interfaces;

public interface IMotoOfertaRepository
{
    Task<MotoOfertaDataDto?> ObtenerOfertaPorPublicacion(
        int idPublicacion,
        DateTime fechaActual);

    Task<MotoOfertaDataDto?> ObtenerOfertaPorModelo(
        int idModeloProducto,
        DateTime fechaActual);

    Task<List<MotoPlanOfertaDataDto>> ObtenerPlanesPorListaPrecio(
        int idListaPrecio);
}


public class MotoOfertaData
{
    public MotoOfertaCabeceraData Cabecera { get; set; } = new();

    public List<MotoPlanData> Planes { get; set; } = new();
}


public class MotoOfertaCabeceraData
{
    public int? PublicacionId { get; set; }

    public int ModeloId { get; set; }

    public string Marca { get; set; } = string.Empty;

    public string NombreModelo { get; set; } = string.Empty;

    public string CodigoReferencia { get; set; } = string.Empty;

    public int? Cilindrada { get; set; }

    public decimal PrecioPublico { get; set; }

    public decimal? PorcentajeDescuento { get; set; }

    public decimal? PrecioContadoFinal { get; set; }

    public bool TienePromoCredito { get; set; }

    public string TipoCredito { get; set; } = string.Empty;

    public DateTime? FechaDesde { get; set; }

    public DateTime? FechaHasta { get; set; }
}


public class MotoPlanData
{
    public decimal EntregaInicial { get; set; }

    public int CantidadCuotas { get; set; }

    public decimal ImporteCuota { get; set; }

    public decimal? Interes { get; set; }

    public string? CodigoPlan { get; set; }
}

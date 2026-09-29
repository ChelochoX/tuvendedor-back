namespace tuvendedorback.DTOs;

public class DescuentoContadoMarcaDto
{
    public int IdMarca { get; set; }

    public string Marca { get; set; } = string.Empty;
}


public class DescuentoContadoConfiguracionDto
{
    public int IdMarca { get; set; }

    public string Marca { get; set; } = string.Empty;

    public int Anio { get; set; }

    public int Mes { get; set; }

    public DateTime FechaDesde { get; set; }

    public DateTime FechaHasta { get; set; }

    public int? IdReglaGeneral { get; set; }

    public decimal? PorcentajeGeneral { get; set; }

    public DateTime? FechaDesdeReglaGeneral { get; set; }

    public DateTime? FechaHastaReglaGeneral { get; set; }

    public bool ReglaGeneralEsDelMes { get; set; }

    public List<DescuentoContadoModeloDto> Modelos { get; set; }
        = new();
}


public class DescuentoContadoModeloDto
{
    public int IdModeloProducto { get; set; }

    public string CodigoReferencia { get; set; } = string.Empty;

    public string NombreModelo { get; set; } = string.Empty;

    public int? Cilindrada { get; set; }

    public int? IdReglaEspecifica { get; set; }

    public decimal? PorcentajeExcepcion { get; set; }

    public decimal? PorcentajeEfectivo { get; set; }

    public bool TieneExcepcion { get; set; }

    public DateTime? FechaDesdeReglaEspecifica { get; set; }

    public DateTime? FechaHastaReglaEspecifica { get; set; }

    public bool ReglaEspecificaEsDelMes { get; set; }
}


public class DescuentoContadoReglaGeneralDto
{
    public int IdReglaGeneral { get; set; }

    public decimal PorcentajeGeneral { get; set; }

    public DateTime FechaDesdeReglaGeneral { get; set; }

    public DateTime? FechaHastaReglaGeneral { get; set; }
}


public class DescuentoContadoExcepcionGuardarDto
{
    public int IdModeloProducto { get; set; }

    public decimal PorcentajeDescuento { get; set; }
}
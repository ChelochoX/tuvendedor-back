namespace tuvendedorback.DTOs;

public class MotoOfertaDto
{
    public int? PublicacionId { get; set; }

    public MotoModeloOfertaDto Modelo { get; set; } = new();

    public MotoContadoOfertaDto Contado { get; set; } = new();

    public MotoCreditoOfertaDto Credito { get; set; } = new();
}

public class MotoModeloOfertaDto
{
    public int Id { get; set; }

    public string Marca { get; set; } = string.Empty;

    public string Nombre { get; set; } = string.Empty;

    public string CodigoReferencia { get; set; } = string.Empty;

    public int? Cilindrada { get; set; }
}

public class MotoContadoOfertaDto
{
    public decimal PrecioLista { get; set; }

    public string PrecioListaFormateado { get; set; } = string.Empty;

    public decimal? PorcentajeDescuento { get; set; }

    public string? PorcentajeDescuentoFormateado { get; set; }

    public decimal? PrecioFinal { get; set; }

    public string? PrecioFinalFormateado { get; set; }
}

public class MotoCreditoOfertaDto
{
    public bool TienePromo { get; set; }

    public string Tipo { get; set; } = string.Empty;

    /*
     * Fechas listas para mostrar.
     * Formato: dd/MM/yyyy
     */
    public string? FechaDesde { get; set; }

    public string? FechaHasta { get; set; }

    public List<MotoPlanOfertaDto> Planes { get; set; } = new();
}

public class MotoPlanOfertaDto
{
    public decimal EntregaInicial { get; set; }

    public string EntregaInicialFormateada { get; set; } = string.Empty;

    public int CantidadCuotas { get; set; }

    public decimal ImporteCuota { get; set; }

    public string ImporteCuotaFormateado { get; set; } = string.Empty;

    public decimal? Interes { get; set; }

    public string? CodigoPlan { get; set; }

    public string Resumen { get; set; } = string.Empty;
}


/*
 * ============================================================
 * DTOs internos Repository -> Service
 *
 * IMPORTANTE:
 * Acá mantenemos DateTime porque son datos internos.
 * El formato se aplica solamente en la respuesta pública.
 * ============================================================
 */

public class MotoOfertaDataDto
{
    public int? PublicacionId { get; set; }

    public int ModeloId { get; set; }

    public string Marca { get; set; } = string.Empty;

    public string NombreModelo { get; set; } = string.Empty;

    public string CodigoReferencia { get; set; } = string.Empty;

    public int? Cilindrada { get; set; }

    public int IdListaCredito { get; set; }

    public decimal PrecioPublico { get; set; }

    public decimal? PorcentajeDescuento { get; set; }

    public decimal? PrecioContadoFinal { get; set; }

    public bool TienePromoCredito { get; set; }

    public string TipoCredito { get; set; } = string.Empty;

    public DateTime? FechaDesde { get; set; }

    public DateTime? FechaHasta { get; set; }
}

public class MotoPlanOfertaDataDto
{
    public decimal EntregaInicial { get; set; }

    public int CantidadCuotas { get; set; }

    public decimal ImporteCuota { get; set; }

    public decimal? Interes { get; set; }

    public string? CodigoPlan { get; set; }
}
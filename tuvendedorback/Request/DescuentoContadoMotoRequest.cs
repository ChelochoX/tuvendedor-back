namespace tuvendedorback.Request;

public class GuardarDescuentoContadoMesRequest
{
    public int IdMarca { get; set; }

    public int Anio { get; set; }

    public int Mes { get; set; }

    public decimal PorcentajeGeneral { get; set; }

    public List<GuardarDescuentoContadoModeloRequest> Excepciones { get; set; }
        = new();
}


public class GuardarDescuentoContadoModeloRequest
{
    public int IdModeloProducto { get; set; }

    public decimal PorcentajeDescuento { get; set; }
}


// =========================================================
// NUEVO MODELO + EXCEPCION
// =========================================================

public class CrearModeloConExcepcionDescuentoRequest
{
    public int IdMarca { get; set; }

    public int Anio { get; set; }

    public int Mes { get; set; }

    public string CodigoReferencia { get; set; }
        = string.Empty;

    public string NombreModelo { get; set; }
        = string.Empty;

    public int? Cilindrada { get; set; }

    public string? Categoria { get; set; }

    public decimal PorcentajeDescuento { get; set; }
}
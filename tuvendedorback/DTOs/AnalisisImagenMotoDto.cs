namespace tuvendedorback.DTOs;

public class AnalisisImagenMotoDto
{
    public bool EsMoto { get; set; }

    /// <summary>
    /// MOTO, OTRO_PRODUCTO, OTRO_CONTENIDO o INCIERTO.
    /// </summary>
    public string TipoContenido { get; set; } = "INCIERTO";

    public string? Marca { get; set; }

    public string? Modelo { get; set; }

    public string? TextoVisible { get; set; }

    public string? DescripcionBreve { get; set; }

    public double Confianza { get; set; }

    public string? Motivo { get; set; }
}

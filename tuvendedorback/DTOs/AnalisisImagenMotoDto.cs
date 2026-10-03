namespace tuvendedorback.DTOs;

public class AnalisisImagenMotoDto
{
    public bool EsMoto { get; set; }

    public string? Marca { get; set; }

    public string? Modelo { get; set; }

    public string? TextoVisible { get; set; }

    public double Confianza { get; set; }

    public string? Motivo { get; set; }
}

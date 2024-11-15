namespace tuvendedorback.DTOs;

public class CreditoEstadisticasDto
{
    public int TotalCreditosGenerales { get; set; }
    public int TotalCreditos { get; set; }
    public string ModeloSolicitado { get; set; }
    public int CreditosPorModelo { get; set; }
    public string Mes { get; set; } // En formato "yyyy-MM"
    public int CreditosPorMes { get; set; }
    public int CreditosPorModeloPorMes { get; set; }
}

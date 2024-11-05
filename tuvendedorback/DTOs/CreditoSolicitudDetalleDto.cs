namespace tuvendedorback.DTOs;

public class CreditoSolicitudDetalleDto
{
    public int Id { get; set; }
    public string CedulaIdentidad { get; set; }
    public string ModeloSolicitado { get; set; }
    public decimal EntregaInicial { get; set; }
    public int CantidadCuotas { get; set; }
    public decimal MontoPorCuota { get; set; }
    public string TelefonoMovil { get; set; }
    public DateTime FechaNacimiento { get; set; }
    public string Barrio { get; set; }
    public string Ciudad { get; set; }
    public string DireccionParticular { get; set; }
    public DateTime FechaCreacion { get; set; }
    public string NombresApellidos { get; set; }

    // Datos Laborales
    public DatosLaboralesDto DatosLaborales { get; set; }

    // Referencias Comerciales
    public List<ReferenciaComercialDto> ReferenciasComerciales { get; set; }

    // Referencias Personales
    public List<ReferenciaPersonalDto> ReferenciasPersonales { get; set; }
}

public class DatosLaboralesDto
{
    public string Empresa { get; set; }
    public string DireccionLaboral { get; set; }
    public string TelefonoLaboral { get; set; }
    public int? AntiguedadAnios { get; set; }
    public bool AportaIPS { get; set; }
    public int? CantidadAportes { get; set; }
    public decimal Salario { get; set; }
}

public class ReferenciaComercialDto
{
    public string NombreLocal { get; set; }
    public string Telefono { get; set; }
}

public class ReferenciaPersonalDto
{
    public string Nombre { get; set; }
    public string Telefono { get; set; }
}

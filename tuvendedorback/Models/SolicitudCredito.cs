namespace tuvendedorback.Models;

public class SolicitudCredito
{
    public string ModeloSolicitado { get; set; }
    public decimal EntregaInicial { get; set; }
    public int CantidadCuotas { get; set; }
    public decimal MontoPorCuota { get; set; }
    public string NombresApellidos { get; set; }
    public string CedulaIdentidad { get; set; }
    public string TelefonoMovil { get; set; }
    public DateTime FechaNacimiento { get; set; }
    public string Barrio { get; set; }
    public string Ciudad { get; set; }
    public string DireccionParticular { get; set; }

    // Datos Laborales
    public string Empresa { get; set; }
    public string DireccionLaboral { get; set; }
    public string TelefonoLaboral { get; set; }
    public int AntiguedadAnios { get; set; }
    public bool AportaIPS { get; set; }
    public int CantidadAportes { get; set; }
    public decimal Salario { get; set; }

    // Referencias Comerciales y Personales
    public List<ReferenciaComercial> ReferenciasComerciales { get; set; }
    public List<ReferenciaPersonal> ReferenciasPersonales { get; set; }
}

public class ReferenciaComercial
{
    public string NombreLocal { get; set; }
    public string Telefono { get; set; }
}

public class ReferenciaPersonal
{
    public string Nombre { get; set; }
    public string Telefono { get; set; }
}


namespace tuvendedorback.DTOs;

public class SolicitudMotoProcesoDto
{
    public string TipoOperacion { get; set; } = string.Empty; // CREDITO / CONTADO
    public int IdSolicitud { get; set; }
    public int IdConversacion { get; set; }
    public int? IdModeloProducto { get; set; }
    public int? IdPublicacion { get; set; }
    public int IdContacto { get; set; }
    public string Estado { get; set; } = string.Empty;
    public string PasoActual { get; set; } = string.Empty;
    public string? ResultadoPreEvaluacion { get; set; }
    public string? MotivoPreEvaluacion { get; set; }
    public string? ViaEvaluacion { get; set; }
    public string? EstadoCedula { get; set; }

    public string? Nombre { get; set; }
    public string? Apellido { get; set; }
    public string? Cedula { get; set; }
    public DateTime? FechaNacimiento { get; set; }
    public string? Telefono { get; set; }
    public string? Direccion { get; set; }
    public string? Barrio { get; set; }
    public string? Ciudad { get; set; }

    public string? Empresa { get; set; }
    public int? AntiguedadMeses { get; set; }
    public bool? AportaIPS { get; set; }
    public int? CantidadAportesIPS { get; set; }
    public string? DireccionEmpresa { get; set; }
    public string? TelefonoEmpresa { get; set; }
    public bool? TelefonoEmpresaEsMovil { get; set; }
    public string? NombreJefeEncargado { get; set; }
}

public class ReglaCreditoMotoDto
{
    public int Id { get; set; }
    public int EdadMinima { get; set; }
    public int AntiguedadLaboralMinMeses { get; set; }
    public int AportesIPSMinimos { get; set; }
    public int ReferenciasFamiliaresMinimas { get; set; }
    public int ReferenciasAmigosMinimas { get; set; }
    public int ReferenciasComercialesMinimasSinIps { get; set; }
}

public class SolicitudMotoReferenciaDto
{
    public int Id { get; set; }
    public int IdSolicitud { get; set; }
    public string Tipo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public string? Parentesco { get; set; }
    public string? Observacion { get; set; }
}

public class SolicitudMotoProcesoResultadoDto
{
    public bool Manejado { get; set; }
    public int? IdSolicitud { get; set; }
    public string? TipoOperacion { get; set; }
    public string Respuesta { get; set; } = string.Empty;
    public string? Estado { get; set; }
    public string? PasoActual { get; set; }
}

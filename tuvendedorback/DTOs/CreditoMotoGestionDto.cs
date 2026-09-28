namespace tuvendedorback.DTOs;

public class CreditoMotoGestionListaDto
{
    public int IdGestion { get; set; }
    public int IdSolicitudCredito { get; set; }

    public int IdConversacion { get; set; }
    public int IdContacto { get; set; }
    public int IdModeloProducto { get; set; }

    public string? Marca { get; set; }
    public string? Modelo { get; set; }
    public string? CodigoReferencia { get; set; }

    public string? NombreCompleto { get; set; }
    public string? Cedula { get; set; }
    public string? Telefono { get; set; }

    public string EstadoSolicitud { get; set; } = string.Empty;
    public string? ResultadoPreEvaluacion { get; set; }

    public string EstadoControl { get; set; } = string.Empty;

    public int? IdUsuarioAsignado { get; set; }
    public string? UsuarioAsignado { get; set; }

    public DateTime FechaRecepcion { get; set; }
    public DateTime? FechaUltimaGestion { get; set; }
    public DateTime? FechaCierreControl { get; set; }
}


public class CreditoMotoGestionDetalleDto
{
    public int IdGestion { get; set; }
    public int IdSolicitudCredito { get; set; }

    public int IdConversacion { get; set; }
    public int IdContacto { get; set; }
    public int IdModeloProducto { get; set; }
    public int? IdPublicacion { get; set; }

    public string? Marca { get; set; }
    public string? Modelo { get; set; }
    public string? CodigoReferencia { get; set; }
    public int? Cilindrada { get; set; }

    public string? NombreCompleto { get; set; }
    public string? Cedula { get; set; }
    public DateTime? FechaNacimiento { get; set; }
    public string? Telefono { get; set; }

    public string? Ciudad { get; set; }
    public string? Barrio { get; set; }
    public string? Direccion { get; set; }

    public string EstadoSolicitud { get; set; } = string.Empty;
    public string PasoActual { get; set; } = string.Empty;

    public string? ResultadoPreEvaluacion { get; set; }
    public string? MotivoPreEvaluacion { get; set; }
    public string? ViaEvaluacion { get; set; }
    public string? EstadoCedula { get; set; }

    public string? ObservacionSolicitud { get; set; }

    public DateTime? FechaCreacion { get; set; }
    public DateTime? FechaActualizacion { get; set; }
    public DateTime? FechaCierre { get; set; }

    public string EstadoControl { get; set; } = string.Empty;

    public int? IdUsuarioAsignado { get; set; }
    public string? UsuarioAsignado { get; set; }

    public string? ObservacionInterna { get; set; }

    public DateTime FechaRecepcion { get; set; }
    public DateTime? FechaUltimaGestion { get; set; }
    public DateTime? FechaCierreControl { get; set; }

    public CreditoMotoLaboralDto? Laboral { get; set; }

    public List<CreditoMotoReferenciaDto> Referencias { get; set; }
        = new();

    public List<CreditoMotoDocumentoDto> Documentos { get; set; }
        = new();

    public CreditoMotoAutorizacionDto? Autorizacion { get; set; }

    public List<CreditoMotoHistorialDto> Historial { get; set; }
        = new();
}


public class CreditoMotoLaboralDto
{
    public string? Empresa { get; set; }
    public int AntiguedadMeses { get; set; }
    public bool AportaIPS { get; set; }
    public int CantidadAportesIPS { get; set; }

    public string? Cargo { get; set; }
    public decimal? Salario { get; set; }
    public string? TipoPago { get; set; }

    public string? DireccionEmpresa { get; set; }
    public string? TelefonoEmpresa { get; set; }
    public bool? TelefonoEmpresaEsMovil { get; set; }
    public string? NombreJefeEncargado { get; set; }
}


public class CreditoMotoReferenciaDto
{
    public int Id { get; set; }

    public string Tipo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;

    public string? Parentesco { get; set; }
    public string? Observacion { get; set; }
}


public class CreditoMotoDocumentoDto
{
    public int Id { get; set; }

    public string TipoDocumento { get; set; } = string.Empty;
    public string NombreArchivo { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public string EstadoRevision { get; set; } = string.Empty;

    public DateTime FechaRecepcion { get; set; }
}


public class CreditoMotoAutorizacionDto
{
    public int Id { get; set; }

    public string VersionAutorizacion { get; set; } = string.Empty;
    public string TextoAutorizacion { get; set; } = string.Empty;
    public string MensajeOriginal { get; set; } = string.Empty;

    public string NombreCompleto { get; set; } = string.Empty;
    public string NumeroCedula { get; set; } = string.Empty;

    public string Canal { get; set; } = string.Empty;

    public DateTime FechaAutorizacion { get; set; }
}


public class CreditoMotoHistorialDto
{
    public int Id { get; set; }

    public string Accion { get; set; } = string.Empty;

    public string? EstadoAnterior { get; set; }
    public string? EstadoNuevo { get; set; }

    public string? Observacion { get; set; }

    public int? IdUsuario { get; set; }
    public string? Usuario { get; set; }

    public DateTime Fecha { get; set; }
}


public class CreditoMotoDocumentoArchivoDataDto
{
    public string NombreArchivo { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public string RutaPrivada { get; set; } = string.Empty;
}


public class CreditoMotoArchivoDto
{
    public string NombreArchivo { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;

    public byte[] Contenido { get; set; }
        = Array.Empty<byte>();
}
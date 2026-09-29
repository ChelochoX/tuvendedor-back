namespace tuvendedorback.DTOs;

public class ContadoMotoGestionListaDto
{
    public int IdGestion { get; set; }

    public int IdSolicitudContado { get; set; }

    public int IdConversacion { get; set; }

    public int IdContacto { get; set; }

    public int IdModeloProducto { get; set; }


    public string? Marca { get; set; }

    public string? Modelo { get; set; }

    public string? CodigoReferencia { get; set; }


    public string? NombreCompleto { get; set; }

    public string? Cedula { get; set; }

    public string? Telefono { get; set; }


    public string? EstadoSolicitud { get; set; }

    public string? PasoActual { get; set; }

    public string? EstadoCedula { get; set; }


    public string EstadoControl { get; set; }
        = string.Empty;

    public string Prioridad { get; set; }
        = string.Empty;


    public int? IdUsuarioAsignado { get; set; }

    public string? ObservacionInterna { get; set; }


    public DateTime FechaRecepcion { get; set; }

    public DateTime? FechaUltimaGestion { get; set; }

    public DateTime? FechaCierreControl { get; set; }
}


public class ContadoMotoGestionDetalleDto
    : ContadoMotoGestionListaDto
{
    public string? Direccion { get; set; }

    public string? Barrio { get; set; }

    public string? Ciudad { get; set; }


    public List<ContadoMotoDocumentoDto> Documentos
    {
        get;
        set;
    } = new();


    public List<ContadoMotoGestionHistorialDto> Historial
    {
        get;
        set;
    } = new();
}


public class ContadoMotoDocumentoDto
{
    public int Id { get; set; }

    public string TipoDocumento { get; set; }
        = string.Empty;

    public string NombreArchivo { get; set; }
        = string.Empty;

    public string MimeType { get; set; }
        = "application/octet-stream";

    public string? EstadoRevision { get; set; }

    public DateTime? FechaRecepcion { get; set; }
}


public class ContadoMotoGestionHistorialDto
{
    public int Id { get; set; }

    public int IdSolicitudContado { get; set; }

    public string Accion { get; set; }
        = string.Empty;

    public string? EstadoAnterior { get; set; }

    public string? EstadoNuevo { get; set; }

    public string? Observacion { get; set; }

    public int? IdUsuario { get; set; }

    public DateTime Fecha { get; set; }
}


public class ContadoMotoDocumentoArchivoDto
{
    public string NombreArchivo { get; set; }
        = string.Empty;

    public string MimeType { get; set; }
        = "application/octet-stream";

    public string RutaPrivada { get; set; }
        = string.Empty;
}


public class ContadoMotoArchivoDto
{
    public byte[] Contenido { get; set; }
        = Array.Empty<byte>();

    public string NombreArchivo { get; set; }
        = string.Empty;

    public string MimeType { get; set; }
        = "application/octet-stream";
}
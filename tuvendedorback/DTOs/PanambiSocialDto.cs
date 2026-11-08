namespace tuvendedorback.DTOs;

// Recibido exclusivamente desde el bridge autenticado, nunca desde el público.
public sealed class PanambiSocialBorradorRequest
{
    public string NumeroWhatsapp { get; set; } = string.Empty;
    public string Comando { get; set; } = "#PUBLICARMOTOS";
    public List<PanambiSocialModeloEntrada> Modelos { get; set; } = new();
}
public sealed class PanambiSocialModeloEntrada
{
    public string NombreModelo { get; set; } = string.Empty;
    // Identificadores de medios enviados por el bridge; NO se descargan ni publican todavía.
    public List<string> ReferenciasFotos { get; set; } = new();
}
public sealed class PanambiSocialResultado
{
    public string Estado { get; set; } = "PENDIENTE_APROBACION";
    public List<PanambiSocialAnuncio> Borradores { get; set; } = new();
    public List<PanambiSocialPendiente> Pendientes { get; set; } = new();
    public string ResumenWhatsapp { get; set; } = string.Empty;
}
public sealed class PanambiSocialAnuncio
{
    public int IdModeloProducto { get; set; }
    public string Marca { get; set; } = string.Empty;
    public string Modelo { get; set; } = string.Empty;
    public string Texto { get; set; } = string.Empty;
    public List<string> ReferenciasFotos { get; set; } = new();
}
public sealed class PanambiSocialPendiente
{
    public string ModeloSolicitado { get; set; } = string.Empty;
    public string Motivo { get; set; } = string.Empty;
}

public sealed class PanambiSocialFotoRequest
{
    public string NumeroWhatsapp { get; set; } = string.Empty;
    public string MediaBase64 { get; set; } = string.Empty;
    public string MediaMimeType { get; set; } = string.Empty;
}

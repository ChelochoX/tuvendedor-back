namespace tuvendedorback.Request;

public interface IBannerPublicitarioRequestBase
{
    string NombreCliente { get; set; }

    string Ubicacion { get; set; }

    string Titulo { get; set; }

    string? Subtitulo { get; set; }

    string? Descripcion { get; set; }

    string Etiqueta { get; set; }

    string? TextoBoton { get; set; }

    string TipoDestino { get; set; }

    string? UrlDestino { get; set; }

    bool MostrarBotonWhatsapp { get; set; }

    string? WhatsappUrl { get; set; }

    string TextoBotonWhatsapp { get; set; }

    DateTime FechaInicio { get; set; }

    DateTime FechaFin { get; set; }

    string Estado { get; set; }

    int Orden { get; set; }

    int Prioridad { get; set; }

    bool EsExclusivo { get; set; }

    bool AbrirNuevaPestana { get; set; }
}

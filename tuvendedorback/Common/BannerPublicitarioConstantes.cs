namespace tuvendedorback.Common;

public static class BannerPublicitarioConstantes
{
    public const string HomeTop = "HOME_TOP";
    public const string HomeInline = "HOME_INLINE";

    public const string EstadoBorrador = "BORRADOR";
    public const string EstadoActivo = "ACTIVO";
    public const string EstadoPausado = "PAUSADO";

    public const string EstadoProgramado = "PROGRAMADO";
    public const string EstadoVencido = "VENCIDO";

    public const string EventoImpresion = "IMPRESION";
    public const string EventoClick = "CLICK";
    public const string EventoWhatsapp = "WHATSAPP";

    public const string DispositivoDesktop = "DESKTOP";
    public const string DispositivoMobile = "MOBILE";
    public const string DispositivoTablet = "TABLET";

    public static readonly string[] UbicacionesPermitidas =
    {
        HomeTop,
        HomeInline
    };

    public static readonly string[] EstadosEditables =
    {
        EstadoBorrador,
        EstadoActivo,
        EstadoPausado
    };

    public static readonly string[] EstadosFiltro =
    {
        EstadoBorrador,
        EstadoActivo,
        EstadoPausado,
        EstadoProgramado,
        EstadoVencido
    };

    public static readonly string[] TiposEventosPermitidos =
    {
        EventoImpresion,
        EventoClick,
        EventoWhatsapp
    };

    public static readonly string[] DispositivosPermitidos =
    {
        DispositivoDesktop,
        DispositivoMobile,
        DispositivoTablet
    };
}

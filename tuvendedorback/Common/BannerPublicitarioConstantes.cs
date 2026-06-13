using tuvendedorback.DTOs;

namespace tuvendedorback.Common;

public static class BannerPublicitarioConstantes
{
    public const string HomeTop = "HOME_TOP";
    public const string HomeInline = "HOME_INLINE";

    public const string EstadoBorrador = "BORRADOR";
    public const string EstadoActivo = "ACTIVO";
    public const string EstadoPausado = "PAUSADO";
    public const string EstadoFinalizado = "FINALIZADO";

    public const string EstadoProgramado = "PROGRAMADO";
    public const string EstadoVencido = "VENCIDO";

    public const string TipoDestinoWeb = "WEB";
    public const string TipoDestinoFacebook = "FACEBOOK";
    public const string TipoDestinoInstagram = "INSTAGRAM";
    public const string TipoDestinoWhatsapp = "WHATSAPP";
    public const string TipoDestinoVitrinaInterna = "VITRINA_INTERNA";
    public const string TipoDestinoOtro = "OTRO";

    public const string EventoImpresion = "IMPRESION";
    public const string EventoClick = "CLICK";
    public const string EventoWhatsapp = "WHATSAPP";

    public const string DispositivoDesktop = "DESKTOP";
    public const string DispositivoMobile = "MOBILE";
    public const string DispositivoTablet = "TABLET";

    public const string EstadoArchivoActivo = "ACTIVO";

    public const string EstadoArchivoPendienteEliminacion =
        "PENDIENTE_ELIMINACION";

    public const string EstadoArchivoEliminado = "ELIMINADO";

    public const string EstadoArchivoErrorEliminacion =
        "ERROR_ELIMINACION";

    public const int DiasRetencionArchivosDefault = 7;

    public static readonly string[] UbicacionesPermitidas =
    {
        HomeTop,
        HomeInline
    };

    public static readonly string[] EstadosEditables =
    {
        EstadoBorrador,
        EstadoActivo,
        EstadoPausado,
        EstadoFinalizado
    };

    public static readonly string[] EstadosFiltro =
    {
        EstadoBorrador,
        EstadoActivo,
        EstadoPausado,
        EstadoFinalizado,
        EstadoProgramado,
        EstadoVencido
    };

    public static readonly string[] TiposDestinoPermitidos =
    {
        TipoDestinoWeb,
        TipoDestinoFacebook,
        TipoDestinoInstagram,
        TipoDestinoWhatsapp,
        TipoDestinoVitrinaInterna,
        TipoDestinoOtro
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

    public static BannerDimensionDto ObtenerDimensionEsperada(
        string ubicacion,
        string tipoDispositivo)
    {
        var ubicacionNormalizada =
            ubicacion.Trim().ToUpperInvariant();

        var dispositivoNormalizado =
            tipoDispositivo.Trim().ToUpperInvariant();

        return (ubicacionNormalizada, dispositivoNormalizado) switch
        {
            (HomeTop, DispositivoDesktop) =>
                new BannerDimensionDto
                {
                    Width = 1600,
                    Height = 420
                },

            (HomeTop, DispositivoMobile) =>
                new BannerDimensionDto
                {
                    Width = 1080,
                    Height = 720
                },

            (HomeInline, DispositivoDesktop) =>
                new BannerDimensionDto
                {
                    Width = 1600,
                    Height = 240
                },

            (HomeInline, DispositivoMobile) =>
                new BannerDimensionDto
                {
                    Width = 1080,
                    Height = 480
                },

            _ => throw new ArgumentOutOfRangeException(
                nameof(tipoDispositivo),
                "No existe una medida configurada para la ubicación " +
                "y el dispositivo informados.")
        };
    }

    public static BannerConfiguracionAdminDto
        ObtenerConfiguracionAdmin()
    {
        return new BannerConfiguracionAdminDto
        {
            Ubicaciones =
                UbicacionesPermitidas.ToList(),

            TiposDestino =
                TiposDestinoPermitidos.ToList(),

            EstadosEditables =
                EstadosEditables.ToList(),

            FormatosPermitidos =
                new List<string>
                {
                    ".webp",
                    ".jpg",
                    ".jpeg",
                    ".png"
                },

            Medidas =
                new Dictionary<string, BannerDimensionDto>
                {
                    [$"{HomeTop}_{DispositivoDesktop}"] =
                        ObtenerDimensionEsperada(
                            HomeTop,
                            DispositivoDesktop),

                    [$"{HomeTop}_{DispositivoMobile}"] =
                        ObtenerDimensionEsperada(
                            HomeTop,
                            DispositivoMobile),

                    [$"{HomeInline}_{DispositivoDesktop}"] =
                        ObtenerDimensionEsperada(
                            HomeInline,
                            DispositivoDesktop),

                    [$"{HomeInline}_{DispositivoMobile}"] =
                        ObtenerDimensionEsperada(
                            HomeInline,
                            DispositivoMobile)
                }
        };
    }
}
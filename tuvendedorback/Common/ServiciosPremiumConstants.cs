namespace tuvendedorback.Common;

public class ServiciosPremiumConstants
{
    public static class TiposServicioPremium
    {
        public const string VitrinaProfesional =
            "VITRINA_PROFESIONAL";

        public const string PublicacionDestacada =
            "PUBLICACION_DESTACADA";

        public const string PublicacionEspecial =
            "PUBLICACION_ESPECIAL";

        public const string BannerMarketplace =
            "BANNER_MARKETPLACE";

        public static readonly HashSet<string> Permitidos =
            new(StringComparer.OrdinalIgnoreCase)
            {
            VitrinaProfesional,
            PublicacionDestacada,
            PublicacionEspecial,
            BannerMarketplace
            };

        public static readonly HashSet<string> PermitidosParaSolicitud =
            new(StringComparer.OrdinalIgnoreCase)
            {
            VitrinaProfesional,
            PublicacionDestacada,
            PublicacionEspecial
            };
    }

    public static class EstadosServicioPremium
    {
        public const string Solicitado =
            "SOLICITADO";

        public const string PendientePago =
            "PENDIENTE_PAGO";

        public const string Activo =
            "ACTIVO";

        public const string Vencido =
            "VENCIDO";

        public const string Cancelado =
            "CANCELADO";

        public static readonly HashSet<string> Permitidos =
            new(StringComparer.OrdinalIgnoreCase)
            {
            Solicitado,
            PendientePago,
            Activo,
            Vencido,
            Cancelado
            };
    }
}

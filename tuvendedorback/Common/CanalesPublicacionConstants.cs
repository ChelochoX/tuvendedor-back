namespace tuvendedorback.Common;

public static class CanalesPublicacionConstants
{
    public const string Marketplace = "MARKETPLACE";

    public const string Vitrina = "VITRINA";

    public static readonly HashSet<string> Permitidos =
        new(StringComparer.OrdinalIgnoreCase)
        {
            Marketplace,
            Vitrina
        };

    public static string Normalizar(string? canal)
    {
        /*
         * Compatibilidad:
         * si todavía algún consumidor no manda CanalPublicacion,
         * se considera MARKETPLACE.
         */
        if (string.IsNullOrWhiteSpace(canal))
            return Marketplace;

        var valor =
            canal.Trim().ToUpperInvariant();

        if (!Permitidos.Contains(valor))
        {
            throw new ArgumentException(
                "El canal de publicación no es válido.",
                nameof(canal));
        }

        return valor;
    }
}

namespace tuvendedorback.Common;

public static class FechaHelper
{
    public static string Formatear(DateTime fecha)
    {
        return fecha.ToString("dd/MM/yyyy");
    }

    public static string? Formatear(DateTime? fecha)
    {
        return fecha.HasValue
            ? fecha.Value.ToString("dd/MM/yyyy")
            : null;
    }
}

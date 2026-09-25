using System.Globalization;

namespace tuvendedorback.Common;

public static class GuaraniFormatter
{
    private static readonly NumberFormatInfo FormatoNumero = new()
    {
        NumberGroupSeparator = ".",
        NumberDecimalSeparator = ",",
        NumberGroupSizes = new[] { 3 }
    };


    public static string Formatear(decimal valor)
    {
        var valorRedondeado =
            decimal.Round(
                valor,
                0,
                MidpointRounding.AwayFromZero
            );

        return $"Gs. {valorRedondeado.ToString("#,0", FormatoNumero)}";
    }


    public static string FormatearPorcentaje(decimal valor)
    {
        if (valor == decimal.Truncate(valor))
        {
            return $"{decimal.Truncate(valor):0}%";
        }

        return $"{valor:0.##}%";
    }
}

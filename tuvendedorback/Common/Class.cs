using System.Globalization;

namespace tuvendedorback.Common
{
    public static class DecimalHelper
    {
        public static decimal? ParseInteres(string? interes)
        {
            if (string.IsNullOrWhiteSpace(interes))
                return null;

            return decimal.Parse(
                interes.Replace(",", "."),
                CultureInfo.InvariantCulture
            );
        }
    }
}

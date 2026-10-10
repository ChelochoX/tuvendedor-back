using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace tuvendedorback.Helpers;

/// <summary>
/// Interpreta solamente decisiones explícitas del cliente sobre el contacto comercial.
/// No utiliza la IA para decidir un opt-out y no interpreta silencios como rechazo.
/// Las duraciones se calculan en SQL con GETDATE(), para mantener la misma referencia
/// horaria que la agenda del motor de seguimiento.
/// </summary>
public static class PoliticaSeguimientoCliente
{
    public sealed record Decision(
        bool NoContactar,
        bool Desiste,
        bool PausaIndefinida,
        int? PausaValor,
        string? PausaUnidad,
        bool RetomaConsulta);

    public static Decision Evaluar(string? mensaje)
    {
        if (string.IsNullOrWhiteSpace(mensaje))
            return new(false, false, false, null, null, false);

        var texto = Normalizar(mensaje);

        // Una negativa breve y aislada es ambigua. No se seguirá insistiendo
        // automáticamente, pero tampoco se marca como baja global del canal.
        if (Regex.IsMatch(texto, @"^(?:no\s*,?\s*gracias|gracias\s*,?\s*no)\b"))
            return new(false, false, true, null, null, false);

        // Un rechazo definitivo prevalece incluso si el mensaje contiene
        // simultáneamente una referencia temporal.
        if (ContieneAlguna(texto,
                "no me escribas mas", "no me escriban mas", "no me escribas nunca",
                "no me escriban nunca", "nunca me escribas", "nunca me contacten"))
            return new(true, false, false, null, null, false);

        // La frase «no me escribas por dos meses» es una pausa temporal,
        // no un bloqueo definitivo. Solo aceptamos duraciones explícitas.
        var optOutTemporal = Regex.Match(texto,
            @"\b(?:no me escribas|no me escriban|no me contactes|no me contacten|no me mandes mensajes|no me manden mensajes|no me envies mensajes|no me envien mensajes)\s+(?:por|durante|hasta dentro de)\s+(?:unos?\s+)?(?<n>\d{1,3}|un|una|dos|tres|cuatro|cinco|seis|siete|ocho|nueve|diez|quince|treinta)\s+(?<u>dias?|semanas?|meses?|anos?|horas?)\b",
            RegexOptions.CultureInvariant);
        var pausaTemporal = ExtraerDuracion(optOutTemporal);
        if (pausaTemporal.HasValue)
            return new(false, false, false, pausaTemporal.Value.Valor,
                pausaTemporal.Value.Unidad, false);

        // Pedido explícito e indefinido de no recibir mensajes.
        var noContactar = ContieneAlguna(texto,
            "no me escrib", "no escribas", "no me contact", "no me mandes",
            "no me envies", "no quiero recibir mensajes", "dejen de escrib",
            "no quiero que me escrib", "no quiero que me contact", "no me vuelvan a escribir",
            "no quiero mas mensajes", "dejen de mandarme mensajes",
            "nunca me escribas", "no me escriban nunca");
        if (noContactar)
        {
            // Si indicó una espera pero no puede calcularse la fecha, se
            // suspende indefinidamente hasta nueva indicación del cliente.
            if (Regex.IsMatch(texto,
                    @"\b(?:por un tiempo|por ahora|de momento|hasta que|hasta el|hasta la)\b"))
                return new(false, false, true, null, null, false);
            return new(true, false, false, null, null, false);
        }

        // Rechazo concreto. No confundir con «no quiero credito», «no quiero roja», etc.
        var desiste = Regex.IsMatch(texto, @"^no me interesa(?:[.!?]|$|\s+(?:la moto|esa moto|el modelo))")
            || ContieneAlguna(texto,
            "no quiero la moto", "no quiero esa moto", "no quiero mas la moto",
            "ya no quiero la moto", "no me interesa la moto", "no me interesa esa moto",
            "ya no me interesa", "no estoy interesado", "no estoy interesada",
            "no voy a comprar la moto", "desisto de la moto", "cancelo la moto",
            "no quiero comprar la moto", "ya compre otra moto", "ya tengo moto");
        if (desiste)
            return new(false, true, false, null, null, false);

        // Fecha expresada por el cliente: «escribime en 15 días», «quiero esperar 2 meses».
        var pideEsperar = Regex.IsMatch(texto,
                @"\b(?:quiero|prefiero|necesito|voy a|mejor|podemos|podria|tengo que|debo)\s+(?:esperar|aguardar)\b")
            || ContieneAlguna(texto,
            "esperame", "espera ", "esperare ", "mas adelante", "por ahora no", "dentro de ",
            "en unos ", "en un mes", "en una semana", "escribime en ",
            "contactame en ", "el mes que viene", "la semana que viene",
            "proximo mes", "proxima semana", "cuando cobre");

        if (pideEsperar)
        {
            // Se limita el rango para evitar desbordamiento en DATEADD.
            var duracion = Regex.Match(texto,
                @"\b(?:en|dentro de|esperar|esperare|esperame|espera|pasado|hasta dentro de|contactame en|escribime en)\s+(?:unos?\s+)?(?<n>\d{1,3}|un|una|dos|tres|cuatro|cinco|seis|siete|ocho|nueve|diez|quince|treinta)\s+(?<u>dias?|semanas?|meses?|anos?|horas?)\b",
                RegexOptions.CultureInvariant);
            var pausa = ExtraerDuracion(duracion);
            if (pausa.HasValue)
                return new(false, false, false, pausa.Value.Valor, pausa.Value.Unidad, false);
            if (ContieneAlguna(texto, "el mes que viene", "proximo mes"))
                return new(false, false, false, 1, "MES", false);
            if (ContieneAlguna(texto, "la semana que viene", "proxima semana"))
                return new(false, false, false, 1, "SEMANA", false);

            // Si no hay una duración verificable, pausar indefinidamente: no inventar fecha.
            return new(false, false, true, null, null, false);
        }

        // Reanudación SOLO por interés explícito; «hola» no reactiva rechazos o pausas.
        var retoma = !ContieneAlguna(texto, "no me interesa", "no quiero") &&
            ContieneAlguna(texto,
            "[tv_producto:", "me interesa", "quiero comprar", "quiero la moto",
            "quiero otra moto", "quiero cotizar", "cotizame", "precio de la moto",
            "quiero ver las motos", "quiero saber el precio de la moto");
        return new(false, false, false, null, null, retoma);
    }

    /// <summary>
    /// Defensa adicional al cambiar de rubro sin un vínculo de publicación explícito.
    /// Excluye el seguimiento de motos ante consultas claramente inmobiliarias.
    /// </summary>
    public static bool EsConsultaInmueble(string? mensaje) =>
        !string.IsNullOrWhiteSpace(mensaje) &&
        Regex.IsMatch(Normalizar(mensaje),
            @"\b(?:terrenos?|lotes?|casas?|departamentos?|duplex|inmuebles?)\b",
            RegexOptions.CultureInvariant);

    private static (int Valor, string Unidad)? ExtraerDuracion(Match match)
    {
        if (!match.Success)
            return null;

        var numero = match.Groups["n"].Value;
        var palabras = new Dictionary<string, int>
        {
            ["un"] = 1,
            ["una"] = 1,
            ["dos"] = 2,
            ["tres"] = 3,
            ["cuatro"] = 4,
            ["cinco"] = 5,
            ["seis"] = 6,
            ["siete"] = 7,
            ["ocho"] = 8,
            ["nueve"] = 9,
            ["diez"] = 10,
            ["quince"] = 15,
            ["treinta"] = 30
        };
        var valor = palabras.TryGetValue(numero, out var literal)
            ? literal : int.Parse(numero, CultureInfo.InvariantCulture);
        if (valor is < 1 or > 365)
            return null;

        var unidad = match.Groups["u"].Value;
        var codigo = unidad.StartsWith("dia") ? "DIA"
            : unidad.StartsWith("semana") ? "SEMANA"
            : unidad.StartsWith("mes") ? "MES"
            : unidad.StartsWith("ano") ? "ANIO" : "HORA";
        return (valor, codigo);
    }

    private static bool ContieneAlguna(string texto, params string[] opciones)
        => opciones.Any(texto.Contains);

    private static string Normalizar(string texto)
    {
        var descompuesto = texto.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(descompuesto.Length);
        foreach (var c in descompuesto)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return Regex.Replace(sb.ToString().Normalize(NormalizationForm.FormC), @"\s+", " ").Trim();
    }
}

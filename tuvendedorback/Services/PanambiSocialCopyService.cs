using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using tuvendedorback.DTOs;

namespace tuvendedorback.Services;

// Solo construye borradores: no llama a Meta, no modifica precios ni publica.
public static class PanambiSocialCopyService
{
    public sealed class Borrador
    {
        public string Titulo { get; set; } = string.Empty;
        public string Mensaje { get; set; } = string.Empty;
        public string CuotaTexto { get; set; } = string.Empty;
        public string LlamadoAccion { get; set; } = string.Empty;
        public List<string> Hashtags { get; set; } = new();
        public string TextoCompleto { get; set; } = string.Empty;
        public string Estado { get; set; } = "PENDIENTE_APROBACION";
        public bool PuedePublicar { get; set; } = false;
    }

    public static Borrador? Generar(int idModelo, MotoOfertaDto? oferta)
    {
        if (oferta is null) return null;
        var planes = oferta.Credito?.Planes?
            .Where(p => p.CantidadCuotas > 0 && p.ImporteCuota > 0 && p.EntregaInicial >= 0)
            .OrderBy(p => p.ImporteCuota)
            .ThenBy(p => p.EntregaInicial)
            .ToList();
        if (planes is null || planes.Count == 0) return null;

        var elegido = planes[0];
        var nombre = string.Join(" ", new[] { oferta.Modelo?.Marca, oferta.Modelo?.Nombre }
            .Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
        if (string.IsNullOrWhiteSpace(nombre)) return null;

        // Rotación estable dentro del día. Cambia entre modelos y días, nunca números ni condiciones.
        var semilla = (long)idModelo * 31L + (long)DateTime.UtcNow.Date.Ticks / TimeSpan.TicksPerDay;
        var indice = (int)(Math.Abs(semilla) % 5);
        string[] titulos = {
            $"🏍️ ¡Tu próxima moto puede ser la {nombre}!",
            $"✨ ¡Dale un nuevo rumbo a tus días con la {nombre}!",
            $"🔥 ¡Conocé la {nombre} y empezá a planificar tu próxima moto!",
            $"🏍️ ¡La {nombre} puede ser tu próximo gran paso!",
            $"🚀 ¡Hacé espacio para una {nombre} en tus planes!"
        };
        string[] mensajes = {
            "Consultá las opciones de financiación disponibles y elegí la que mejor se adapte a vos.",
            "Conocé sus condiciones de financiación y encontrá una opción para dar el siguiente paso.",
            "Tu próxima moto merece una buena decisión. Mirá las cuotas disponibles y consultá los detalles.",
            "Una moto propia puede estar más cerca de lo que pensás. Descubrí las opciones de pago.",
            "Tomá impulso para tu próximo recorrido. Consultá las condiciones y elegí con información clara."
        };
        string[] llamados = {
            "📲 Escribinos por WhatsApp y te asesoramos.",
            "📲 Consultanos ahora y conocé los requisitos.",
            "📲 Mandanos un mensaje para recibir más información.",
            "📲 Hablá con nuestro equipo y consultá disponibilidad.",
            "📲 Pedinos detalles por WhatsApp, sin compromiso."
        };
        var moneda = elegido.ImporteCuota.ToString("N0", CultureInfo.GetCultureInfo("es-PY"));
        var cuota = $"💳 {elegido.CantidadCuotas} cuotas de Gs. {moneda}";
        var entrega = elegido.EntregaInicial > 0
            ? $"Entrega inicial: Gs. {elegido.EntregaInicial.ToString("N0", CultureInfo.GetCultureInfo("es-PY"))}. "
            : "";
        var condiciones = $"{entrega}Financiación sujeta a condiciones, aprobación y disponibilidad. Importes por confirmar antes de publicar.";
        var hashtags = new List<string> { Hashtag(oferta.Modelo?.Marca), Hashtag(oferta.Modelo?.Nombre), "#MotoPropia", "#TuVendedor" }
            .Where(h => h.Length > 1).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return new Borrador
        {
            Titulo = titulos[indice],
            Mensaje = mensajes[(indice + 2) % mensajes.Length],
            CuotaTexto = cuota,
            LlamadoAccion = llamados[(indice + 3) % llamados.Length],
            Hashtags = hashtags,
            TextoCompleto = string.Join("\n\n", new[] {
                titulos[indice], mensajes[(indice + 2) % mensajes.Length], cuota,
                condiciones, llamados[(indice + 3) % llamados.Length], string.Join(" ", hashtags)
            }),
            Estado = "PENDIENTE_VALIDACION_IMAGEN_Y_APROBACION",
            PuedePublicar = false
        };
    }

    private static string Hashtag(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return "";
        var limpio = new string(texto.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(c)).ToArray());
        return string.IsNullOrEmpty(limpio) ? "" : "#" + limpio;
    }
}

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
        // WhatsApp acepta *negrita*; las redes reciben TextoCompleto sin asteriscos.
        public string TextoWhatsApp { get; set; } = string.Empty;
        public string? UrlWhatsApp { get; set; }
        public string? UrlProducto { get; set; }
        public string Estado { get; set; } = "PENDIENTE_APROBACION";
        public bool PuedePublicar { get; set; } = false;
    }

    // Datos comerciales: SIEMPRE del catálogo, no del texto impreso en el flyer.
    // Esta clase únicamente redacta. No autoriza ni ejecuta publicaciones.
    public static Borrador? Generar(int idModelo, MotoOfertaDto? oferta, string? numeroWhatsapp, string? urlSitio)
    {
        if (oferta is null) return null;
        var nombre = string.Join(" ", new[] { oferta.Modelo?.Marca, oferta.Modelo?.Nombre }
            .Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
        if (string.IsNullOrWhiteSpace(nombre)) return null;

        var planes = oferta.Credito?.Planes?
            .Where(p => p.CantidadCuotas > 0 && p.ImporteCuota > 0 && p.EntregaInicial >= 0)
            .OrderBy(p => p.ImporteCuota)
            .ThenBy(p => p.EntregaInicial)
            .ToList() ?? new List<MotoPlanOfertaDto>();
        var plan = planes.FirstOrDefault();
        var contado = oferta.Contado?.PrecioFinal;
        var tieneContado = contado.HasValue && contado.Value > 0;
        if (plan is null && !tieneContado) return null;

        // Variación por modelo y día, sin alterar las condiciones comerciales.
        var dia = DateTime.UtcNow.Date.Ticks / TimeSpan.TicksPerDay;
        var semilla = Math.Abs((long)idModelo * 37L + dia);
        var indice = (int)(semilla % 6);
        var aventura = nombre.Contains("CANYON", StringComparison.OrdinalIgnoreCase)
            || nombre.Contains("HUNTER", StringComparison.OrdinalIgnoreCase);
        var estilo = nombre.Contains("BLACK", StringComparison.OrdinalIgnoreCase)
            || nombre.Contains("LTD", StringComparison.OrdinalIgnoreCase);
        string[] titulos = aventura ? new[] {
            $"🏍️ ¡Tu próxima aventura comienza con la {nombre}!",
            $"🔥 ¡Descubrí nuevos caminos con la {nombre}!",
            $"🌍 ¡Dale un nuevo rumbo a tus viajes con la {nombre}!",
            $"🏍️ ¡La {nombre} te espera para tu próximo recorrido!",
            $"✨ ¡Animate a descubrir la {nombre}!",
            $"🚀 ¡Elegí tu próximo camino con la {nombre}!"
        } : estilo ? new[] {
            $"🏍️🔥 ¡Hacé que cada camino tenga su propio estilo con la {nombre}!",
            $"✨ ¡Destacate con tu nueva {nombre}!",
            $"🔥 ¡Conocé el carácter de la {nombre}!",
            $"🏍️ ¡Tu próxima moto puede ser la {nombre}!",
            $"🚀 ¡Dale un giro a tu día con la {nombre}!",
            $"✨ ¡Subite al estilo de la {nombre}!"
        } : new[] {
            $"🏍️ ¡Tu próxima moto te espera: {nombre}!",
            $"🔥 ¡Este puede ser el momento de tener tu {nombre}!",
            $"✨ ¡Movete a tu manera con la {nombre}!",
            $"🏍️ ¡Descubrí la {nombre} y prepará tu próximo recorrido!",
            $"🚀 ¡Animate a dar el paso con la {nombre}!",
            $"🔥 ¡La {nombre} puede acompañar tus próximos planes!"
        };
        string[] mensajes = {
            "¡Esta es una gran oportunidad para dar el paso hacia tu propia moto! Conocé las opciones disponibles.",
            "Dale un nuevo ritmo a tus días. Descubrí cómo podés financiar tu próxima moto.",
            "¿Pensando en cambiar de moto? Conocé esta opción y sus condiciones de financiación.",
            "Tu próximo recorrido puede comenzar hoy. Consultá las opciones de compra disponibles.",
            "Un nuevo camino empieza con una buena elección. ¡Conocé esta moto!",
            "Hacé lugar para nuevos recorridos: te contamos cómo podés llevarte esta moto."
        };
        string[] llamados = {
            "📲 ¡Escribinos por WhatsApp y consultá cómo llevarte la tuya!",
            "📲 ¡Hablá con nosotros y conocé todos los detalles!",
            "📲 ¡Mandanos un mensaje y recibí asesoramiento!",
            "📲 ¡Consultanos ahora por WhatsApp!",
            "📲 ¡Escribinos y conocé las condiciones!",
            "📲 ¡Pedinos información para dar el siguiente paso!"
        };

        var cuota = plan is null ? string.Empty :
            $"💳 {plan.CantidadCuotas} cuotas de Gs. {plan.ImporteCuota.ToString("N0", CultureInfo.GetCultureInfo("es-PY"))}";
        var entrega = plan?.EntregaInicial > 0 ?
            $"Entrega inicial: Gs. {plan.EntregaInicial.ToString("N0", CultureInfo.GetCultureInfo("es-PY"))}" : string.Empty;
        var contadoTexto = tieneContado ?
            $"💰 Precio contado: Gs. {contado!.Value.ToString("N0", CultureInfo.GetCultureInfo("es-PY"))}" : string.Empty;
        var etiquetas = new List<string> {
            Hashtag(oferta.Modelo?.Marca), Hashtag(oferta.Modelo?.Nombre),
            aventura ? "#AventuraEnMoto" : "#MotoPropia", "#TuVendedor"
        }.Where(h => h.Length > 1).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        // La ruta /producto/:id del frontend corresponde a PublicacionId,
        // NO a Modelo.Id. Si no hay publicación asociada, no inventamos enlace.
        var idPublicacion = oferta.PublicacionId;
        var urlProducto = idPublicacion is > 0 && Uri.TryCreate(urlSitio, UriKind.Absolute, out var sitio)
            && (sitio.Scheme == Uri.UriSchemeHttps || sitio.Scheme == Uri.UriSchemeHttp)
            ? $"{sitio.GetLeftPart(UriPartial.Authority).TrimEnd('/')}/producto/{idPublicacion.Value}"
            : null;

        // wa.me necesita número internacional sin '+' y un mensaje precompletado.
        var telefono = new string((numeroWhatsapp ?? string.Empty).Where(char.IsDigit).ToArray());
        var urlWhatsApp = telefono.Length >= 10 && telefono.Length <= 15
            ? $"https://wa.me/{telefono}?text={Uri.EscapeDataString($"Hola, quiero consultar por la {nombre}.")}"
            : null;

        var titulo = titulos[indice];
        var mensaje = mensajes[(indice + 2) % mensajes.Length];
        var llamado = llamados[(indice + 3) % llamados.Length];
        // Llamado y enlaces de consulta siempre separados del contenido técnico.
        var lineas = new[] {
            titulo, mensaje, cuota, entrega, contadoTexto,
            "Financiación sujeta a evaluación y condiciones vigentes.",
            llamado,
            urlWhatsApp is null ? null : $"📲 Consultá directamente: {urlWhatsApp}",
            urlProducto is null ? null : $"🔗 Mirá la moto en TuVendedor: {urlProducto}",
            string.Join(" ", etiquetas)
        }.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();

        // En el chat destacamos título y montos; en el post de Meta no enviamos
        // marcadores de formato propios de WhatsApp.
        var lineasWhatsApp = lineas.Select(linea =>
            linea == titulo || linea == cuota || linea == contadoTexto
                ? $"*{linea}*"
                : linea);

        return new Borrador {
            Titulo = titulo,
            Mensaje = mensaje,
            CuotaTexto = cuota,
            LlamadoAccion = llamado,
            Hashtags = etiquetas,
            TextoCompleto = string.Join("\n\n", lineas),
            TextoWhatsApp = string.Join("\n\n", lineasWhatsApp),
            UrlWhatsApp = urlWhatsApp,
            UrlProducto = urlProducto,
            Estado = "PENDIENTE_APROBACION",
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

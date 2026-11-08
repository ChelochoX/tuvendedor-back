using System.Globalization;
using System.Text;
using tuvendedorback.DTOs;
using tuvendedorback.Services.Interfaces;

namespace tuvendedorback.Services;

/// <summary>
/// Primera fase: catálogo real + oferta real -> borradores.
/// No hay llamadas a Meta, envío WhatsApp ni publicación automática.
/// </summary>
public sealed class PanambiSocialBorradorService
{
    private readonly IModeloProductoService _modelos;
    private readonly IMotoOfertaService _ofertas;

    public PanambiSocialBorradorService(IModeloProductoService modelos, IMotoOfertaService ofertas)
    {
        _modelos = modelos;
        _ofertas = ofertas;
    }

    public async Task<PanambiSocialResultado> PrepararAsync(PanambiSocialBorradorRequest entrada)
    {
        var resultado = new PanambiSocialResultado();
        var activos = (await _modelos.ObtenerModelos(null, true))
            .Where(m => Normalizar(m.Rubro).Contains("MOTO", StringComparison.Ordinal))
            .ToList();

        foreach (var solicitado in entrada.Modelos)
        {
            var busqueda = (solicitado.NombreModelo ?? string.Empty).Trim();
            var normal = Normalizar(busqueda);
            if (normal.Length < 3)
            {
                Pendiente("Modelo vacío o demasiado corto.");
                continue;
            }
            // Solo coincidencias exactas (modelo, marca+modelo o código). Sin adivinar modelos.
            var encontrados = activos.Where(m =>
                Normalizar(m.NombreModelo) == normal ||
                Normalizar($"{m.Marca} {m.NombreModelo}") == normal ||
                (!string.IsNullOrWhiteSpace(m.CodigoReferencia) && Normalizar(m.CodigoReferencia) == normal))
                .GroupBy(m => m.Id).Select(g => g.First()).ToList();
            if (encontrados.Count != 1)
            {
                Pendiente(encontrados.Count == 0 ? "No existe un modelo de moto activo con coincidencia exacta en la base." : "Coincidencia ambigua: hay más de un modelo; indicar código de referencia.");
                continue;
            }
            var m = encontrados[0];
            if (solicitado.ReferenciasFotos is null || solicitado.ReferenciasFotos.Count == 0 || solicitado.ReferenciasFotos.Any(string.IsNullOrWhiteSpace))
            {
                Pendiente("Faltan fotos asociadas a este modelo.");
                continue;
            }
            MotoOfertaDto oferta;
            try { oferta = await _ofertas.ObtenerOfertaPorModelo(m.Id); }
            catch (Exception) // No se revela información técnica ni se inventan precios.
            {
                Pendiente("No se pudo verificar una oferta vigente para este modelo.");
                continue;
            }
            var planes = oferta.Credito?.Planes?
                .Where(p => p.CantidadCuotas > 0 && p.ImporteCuota > 0 && p.EntregaInicial >= 0)
                .ToList() ?? new();
            if (planes.Count == 0)
            {
                Pendiente("Modelo encontrado, pero sin financiación válida registrada.");
                continue;
            }
            var elegido = planes.OrderBy(p => p.ImporteCuota).ThenBy(p => p.CantidadCuotas).First();
            var texto = $"🏍️ {oferta.Modelo.Marca} {oferta.Modelo.Nombre}\n" +
                "¡Consultá por tu próxima moto en TuVendedor!\n\n" +
                $"💰 Entrega inicial: {elegido.EntregaInicialFormateada}\n" +
                $"📆 {elegido.CantidadCuotas} cuotas de {elegido.ImporteCuotaFormateado}\n" +
                "📲 Escribinos para consultar condiciones, requisitos y disponibilidad.\n" +
                "Precios sujetos a confirmación al momento de la compra.";
            resultado.Borradores.Add(new PanambiSocialAnuncio
            {
                IdModeloProducto = m.Id, Marca = oferta.Modelo.Marca,
                Modelo = oferta.Modelo.Nombre, Texto = texto,
                ReferenciasFotos = solicitado.ReferenciasFotos.Distinct().Take(10).ToList()
            });
            void Pendiente(string motivo) => resultado.Pendientes.Add(new PanambiSocialPendiente { ModeloSolicitado = busqueda, Motivo = motivo });
        }
        resultado.ResumenWhatsapp = $"Panambi: {resultado.Borradores.Count} borrador(es) para revisar y {resultado.Pendientes.Count} pendiente(s). " +
            (resultado.Pendientes.Count > 0 ? "Faltantes: " + string.Join("; ", resultado.Pendientes.Select(p => $"{p.ModeloSolicitado} ({p.Motivo})")) : "") +
            " No se publicó nada: módulo en modo borrador.";
        return resultado;
    }

    private static string Normalizar(string? s)
    {
        var d = (s ?? string.Empty).Trim().Normalize(NormalizationForm.FormD);
        var b = new StringBuilder();
        foreach (var c in d)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c)) b.Append(char.ToUpperInvariant(c));
        }
        return b.ToString();
    }
}

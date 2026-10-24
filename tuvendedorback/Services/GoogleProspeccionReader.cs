using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace tuvendedorback.Prospeccion;

/// <summary>
/// Lee exclusivamente hojas "Prospectos" de Google Sheets en una carpeta configurada.
/// La cuenta de servicio usa Drive y Sheets de solo lectura. Nunca recibe claves desde el browser.
/// </summary>
public sealed class GoogleProspeccionReader
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _token;
    private DateTime _tokenExpiresUtc;

    private sealed class ServiceAccount
    {
        [JsonPropertyName("client_email")] public string Email { get; set; } = "";
        [JsonPropertyName("private_key")] public string PrivateKey { get; set; } = "";
        [JsonPropertyName("token_uri")] public string TokenUri { get; set; } = "https://oauth2.googleapis.com/token";
    }

    public GoogleProspeccionReader(HttpClient http, IConfiguration config)
    {
        _http = http;
        _http.Timeout = TimeSpan.FromSeconds(35);
        _config = config;
    }

    private ServiceAccount LeerCredenciales()
    {
        var ruta = _config["GoogleDriveProspectos:ServiceAccountFile"];
        // Para Docker: usar preferentemente secreto montado como archivo.
        var json = !string.IsNullOrWhiteSpace(ruta)
            ? File.ReadAllText(ruta)
            : Environment.GetEnvironmentVariable("GOOGLE_SERVICE_ACCOUNT_JSON");
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException("Configurar GoogleDriveProspectos__ServiceAccountFile o GOOGLE_SERVICE_ACCOUNT_JSON.");
        var cuenta = JsonSerializer.Deserialize<ServiceAccount>(json)
            ?? throw new InvalidOperationException("JSON de cuenta de servicio inválido.");
        if (string.IsNullOrWhiteSpace(cuenta.Email) || string.IsNullOrWhiteSpace(cuenta.PrivateKey))
            throw new InvalidOperationException("Credenciales de Google Drive incompletas.");
        if (!string.Equals(cuenta.TokenUri, "https://oauth2.googleapis.com/token", StringComparison.Ordinal))
            throw new InvalidOperationException("El token_uri no es el de Google OAuth.");
        return cuenta;
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes)
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private async Task<string> TokenAsync(CancellationToken ct)
    {
        if (_token != null && _tokenExpiresUtc > DateTime.UtcNow.AddMinutes(2)) return _token;
        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_token != null && _tokenExpiresUtc > DateTime.UtcNow.AddMinutes(2)) return _token;
            var cuenta = LeerCredenciales();
            var ahora = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var head = Base64Url(Encoding.UTF8.GetBytes("{\"alg\":\"RS256\",\"typ\":\"JWT\"}"));
            var payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
            {
                iss = cuenta.Email,
                scope = "https://www.googleapis.com/auth/drive.readonly https://www.googleapis.com/auth/spreadsheets.readonly",
                aud = cuenta.TokenUri,
                iat = ahora,
                exp = ahora + 3600
            }));
            var contenido = $"{head}.{payload}";
            using var rsa = RSA.Create();
            rsa.ImportFromPem(cuenta.PrivateKey);
            var firma = Base64Url(rsa.SignData(Encoding.ASCII.GetBytes(contenido), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            using var body = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                ["assertion"] = $"{contenido}.{firma}"
            });
            using var response = await _http.PostAsync(cuenta.TokenUri, body, ct);
            response.EnsureSuccessStatusCode();
            using var tokenJson = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            _token = tokenJson.RootElement.GetProperty("access_token").GetString()
                ?? throw new InvalidOperationException("Google no devolvió access_token.");
            var segundos = tokenJson.RootElement.TryGetProperty("expires_in", out var expira) ? expira.GetInt32() : 3600;
            _tokenExpiresUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, segundos - 120));
            return _token;
        }
        finally { _tokenLock.Release(); }
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync(ct));
        using var response = await _http.SendAsync(req, ct);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    }

    private sealed record GoogleSheetFile(string Id, string Name, string Url);

    private async Task<List<GoogleSheetFile>> ListarPlanillasAsync(string folderId, CancellationToken ct)
    {
        var archivos = new List<GoogleSheetFile>();
        string? pageToken = null;
        do
        {
            var q = $"'{folderId.Replace("'", "")}' in parents and trashed = false and mimeType = 'application/vnd.google-apps.spreadsheet'";
            var url = "https://www.googleapis.com/drive/v3/files?q=" + Uri.EscapeDataString(q)
                + "&fields=" + Uri.EscapeDataString("nextPageToken,files(id,name,webViewLink)")
                + "&pageSize=100" + (pageToken == null ? "" : "&pageToken=" + Uri.EscapeDataString(pageToken));
            using var doc = await GetJsonAsync(url, ct);
            foreach (var file in doc.RootElement.GetProperty("files").EnumerateArray())
            {
                var name = file.GetProperty("name").GetString() ?? "";
                if (!name.StartsWith("prospeccion_", StringComparison.OrdinalIgnoreCase)) continue;
                var id = file.GetProperty("id").GetString() ?? "";
                if (id.Length == 0) continue;
                archivos.Add(new GoogleSheetFile(id, name,
                    $"https://docs.google.com/spreadsheets/d/{id}/edit"));
                if (archivos.Count >= 250) return archivos;
            }
            pageToken = doc.RootElement.TryGetProperty("nextPageToken", out var token)
                ? token.GetString() : null;
        } while (!string.IsNullOrEmpty(pageToken));
        return archivos;
    }

    private static string? Valor(Dictionary<string, string> fila, params string[] alias)
    {
        foreach (var key in alias)
            if (fila.TryGetValue(key, out var valor) && !string.IsNullOrWhiteSpace(valor))
                return valor.Trim();
        return null;
    }

    private static string ClaveCabecera(string s) => Regex.Replace(
        s.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD), @"[^a-z0-9_]", "");

    private static string Rubro(string? s)
    {
        var texto = (s ?? "").ToLowerInvariant();
        if (texto.Contains("inmueble") || texto.Contains("terreno") || texto.Contains("lote") || texto.Contains("departamento") || texto.Contains("casa") || texto.Contains("rural")) return "INMUEBLES";
        if (texto.Contains("motocic") || texto.Contains("moto")) return "MOTOS";
        return "OTROS";
    }

    private static string? UrlSegura(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)) return null;
        return uri.AbsoluteUri;
    }

    private static string? Limitar(string? value, int max) => value is { Length: > 0 }
        ? value[..Math.Min(value.Length, max)] : null;

    private static ProspectoImportado? Mapear(Dictionary<string, string> fila, GoogleSheetFile archivo)
    {
        var descripcion = Valor(fila, "solicitud_compra", "tipo_solicitud");
        if (string.IsNullOrWhiteSpace(descripcion)) return null;
        var idFuente = Valor(fila, "id_fuente");
        var url = UrlSegura(Valor(fila, "url_original"));
        // Nunca inventar ni agregar un prospecto sin un identificador de anuncio rastreable.
        if (idFuente == null && url == null) return null;
        var rubro = Rubro(Valor(fila, "rubro"));
        // El id_fuente es estable entre ejecuciones aunque la URL cambie de parámetros.
        // Cuando no existe, usamos host+ruta sin query/fragments como clave de respaldo.
        string clave;
        if (idFuente != null)
            clave = "id:" + Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(idFuente.Trim().ToLowerInvariant())));
        else
        {
            var uri = new Uri(url!);
            var canonical = uri.Host.ToLowerInvariant() + uri.AbsolutePath.TrimEnd('/').ToLowerInvariant();
            clave = "url:" + Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        }

        return new ProspectoImportado
        {
            Rubro = rubro,
            ClaveFuente = clave,
            IdFuente = Limitar(idFuente, 250),
            SolicitudCompra = Limitar(descripcion, 2000)!,
            Distrito = Limitar(Valor(fila, "distrito_objetivo", "distrito"), 250),
            Departamento = Limitar(Valor(fila, "departamento_objetivo", "departamento"), 250),
            PresupuestoConfirmadoGs = Limitar(Valor(fila, "presupuesto_confirmado_gs", "presupuesto_gs"), 120),
            ImporteMostradoGs = Limitar(Valor(fila, "importe_mostrado_gs"), 120),
            FechaDeteccionOriginal = Limitar(Valor(fila, "fecha_deteccion_py", "deteccion_py"), 70),
            FechaPublicacionOriginal = Limitar(Valor(fila, "fecha_publicacion"), 70),
            Fuente = Limitar(Valor(fila, "fuente"), 120),
            UrlOriginal = Limitar(url, 1600),
            Confianza = Limitar(Valor(fila, "confianza"), 100),
            MotivoConfianza = Limitar(Valor(fila, "motivo_confianza", "evidencia"), 1500),
            Prioridad = Limitar(Valor(fila, "prioridad"), 80),
            EstadoFuente = Limitar(Valor(fila, "estado", "estado_validacion"), 300),
            Observaciones = Limitar(Valor(fila, "observaciones"), 2000),
            ArchivoOrigen = Limitar(archivo.Name, 280),
            UrlPlanilla = archivo.Url
        };
    }

    internal async Task<(List<ProspectoImportado> items, int archivos, int leidas, int omitidas)> LeerAsync(CancellationToken ct)
    {
        var folderId = _config["GoogleDriveProspectos:FolderId"];
        if (string.IsNullOrWhiteSpace(folderId) || !Regex.IsMatch(folderId, @"^[a-zA-Z0-9_-]{10,100}$"))
            throw new InvalidOperationException("GoogleDriveProspectos:FolderId no está configurado correctamente.");
        var archivos = await ListarPlanillasAsync(folderId, ct);
        // Preferir última ejecución al colapsar duplicados en un mismo lote.
        archivos.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        var items = new Dictionary<string, ProspectoImportado>(StringComparer.Ordinal);
        var leidas = 0; var omitidas = 0;
        foreach (var archivo in archivos)
        {
            var range = Uri.EscapeDataString("'Prospectos'!A1:Z1000");
            using var doc = await GetJsonAsync($"https://sheets.googleapis.com/v4/spreadsheets/{archivo.Id}/values/{range}?valueRenderOption=FORMATTED_VALUE", ct);
            if (!doc.RootElement.TryGetProperty("values", out var values)) continue;
            var rows = values.EnumerateArray().ToList();
            if (rows.Count < 2) continue;
            var headers = rows[0].EnumerateArray().Select(x => ClaveCabecera(x.ToString())).ToArray();
            foreach (var row in rows.Skip(1))
            {
                leidas++;
                var campos = row.EnumerateArray().Select(x => x.ToString()).ToArray();
                var d = new Dictionary<string, string>(StringComparer.Ordinal);
                for (var j = 0; j < Math.Min(headers.Length, campos.Length); j++)
                    if (!string.IsNullOrWhiteSpace(headers[j])) d[headers[j]] = campos[j];
                var item = Mapear(d, archivo);
                if (item == null) { omitidas++; continue; }
                items[item.Rubro + ":" + item.ClaveFuente] = item;
            }
        }
        // Los duplicados internos al lote también se computan en repetidas.
        return (items.Values.ToList(), archivos.Count, leidas, omitidas);
    }
}

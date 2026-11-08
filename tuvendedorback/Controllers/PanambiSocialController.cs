
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using tuvendedorback.DTOs;
using tuvendedorback.Services;
using tuvendedorback.Services.Interfaces;
using tuvendedorback.Wrappers;

namespace tuvendedorback.Controllers;

/// <summary>
/// Controlador interno para la identificación de motocicletas
/// y la preparación de borradores de publicaciones de Panambi.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/internal/panambi-social")]
public sealed class PanambiSocialController : ControllerBase
{
    private readonly PanambiSocialBorradorService _borradores;
    private readonly IConfiguration _configuration;
    private readonly IOllamaService _ollamaService;
    private readonly IModeloProductoService _modeloProductoService;

    public PanambiSocialController(
        PanambiSocialBorradorService borradores,
        IConfiguration configuration,
        IOllamaService ollamaService,
        IModeloProductoService modeloProductoService)
    {
        _borradores = borradores;
        _configuration = configuration;
        _ollamaService = ollamaService;
        _modeloProductoService = modeloProductoService;
    }

    /// <summary>
    /// Prepara los borradores de publicaciones de motocicletas.
    /// No realiza publicaciones en redes sociales.
    /// </summary>
    [HttpPost("borradores")]
    public async Task<IActionResult> Borradores(
        [FromBody] PanambiSocialBorradorRequest request)
    {
        if (!ClaveInternaValida())
        {
            return Unauthorized(new
            {
                success = false,
                message = "No autorizado"
            });
        }

        if (!NumeroAutorizado(request.NumeroWhatsapp))
        {
            return StatusCode(403, new
            {
                success = false,
                message = "Número no autorizado para publicaciones"
            });
        }

        if (!string.Equals(
                request.Comando?.Trim(),
                "#PUBLICARMOTOS",
                StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new
            {
                success = false,
                message = "Comando no reconocido"
            });
        }

        if (!ModelosValidos(request.Modelos))
        {
            return BadRequest(new
            {
                success = false,
                message = "Cantidad de modelos/fotos inválida"
            });
        }

        var resultado = await _borradores.PrepararAsync(request);

        return Ok(new Response<PanambiSocialResultado>
        {
            Success = true,
            StatusCode = 200,
            Message = "Borradores preparados, ninguna red social fue modificada.",
            Data = resultado
        });
    }

    /// <summary>
    /// Identifica una motocicleta a partir de una imagen
    /// y comprueba si existe un modelo activo en el catálogo.
    /// </summary>
    [HttpPost("identificar-foto")]
    [RequestSizeLimit(12_000_000)]
    public async Task<IActionResult> IdentificarFoto(
        [FromBody] PanambiSocialFotoRequest request,
        CancellationToken cancellationToken)
    {
        if (!ClaveInternaValida())
        {
            return Unauthorized(new
            {
                success = false,
                message = "No autorizado"
            });
        }

        if (!NumeroAutorizado(request.NumeroWhatsapp))
        {
            return StatusCode(403, new
            {
                success = false,
                message = "Número no autorizado"
            });
        }

        if (!ImagenValida(request))
        {
            return BadRequest(new
            {
                success = false,
                message = "Imagen inválida o demasiado grande"
            });
        }

        var analisis = await _ollamaService.AnalizarImagenMoto(
            request.MediaBase64,
            request.MediaMimeType,
            "#PANAMBI_SOCIAL_PUBLICIDAD",
            cancellationToken);

        if (analisis is null ||
            !analisis.EsMoto ||
            !string.Equals(
                analisis.TipoContenido,
                "MOTO",
                StringComparison.OrdinalIgnoreCase))
        {
            return Ok(new
            {
                success = true,
                data = new
                {
                    encontrada = false,
                    motivo = "No se pudo reconocer con seguridad una motocicleta"
                }
            });
        }

        var nombreDetectado = $"{analisis.Marca} {analisis.Modelo}".Trim();

        if (analisis.Confianza < 0.78)
        {
            return Ok(new
            {
                success = true,
                data = new
                {
                    encontrada = false,
                    posibleModelo = nombreDetectado,
                    motivo = "Reconocimiento con confianza insuficiente"
                }
            });
        }

        var modelos = await _modeloProductoService.ObtenerModelos(
            null,
            true);

        var modelosActivos = modelos
            .Where(modelo =>
                Normalizar(modelo.Rubro)
                    .Contains("MOTO", StringComparison.Ordinal))
            .ToList();

        var nombreNormalizado = Normalizar(nombreDetectado);
        var modeloNormalizado = Normalizar(analisis.Modelo);

        var coincidencias = modelosActivos
            .Where(modelo =>
                CoincideModelo(
                    modelo.Marca,
                    modelo.NombreModelo,
                    analisis.Marca,
                    nombreNormalizado,
                    modeloNormalizado))
            .GroupBy(modelo => modelo.Id)
            .Select(grupo => grupo.First())
            .ToList();

        if (coincidencias.Count != 1)
        {
            return Ok(new
            {
                success = true,
                data = new
                {
                    encontrada = false,
                    posibleModelo = nombreDetectado,
                    motivo = coincidencias.Count == 0
                        ? "Modelo no registrado en catálogo de motos activas"
                        : "Más de un modelo coincidente; requiere revisión"
                }
            });
        }

        var modeloEncontrado = coincidencias.Single();

        return Ok(new
        {
            success = true,
            data = new
            {
                encontrada = true,
                idModelo = modeloEncontrado.Id,
                marca = modeloEncontrado.Marca,
                modelo = modeloEncontrado.NombreModelo,
                confianza = analisis.Confianza
            }
        });
    }

    #region Validaciones

    private bool ClaveInternaValida()
    {
        var claveEsperada = _configuration["IA:InternalKey"];

        var claveRecibida = Request.Headers[
            "X-TuVendedor-Internal-Key"].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(claveEsperada) ||
            string.IsNullOrWhiteSpace(claveRecibida))
        {
            return false;
        }

        var hashEsperado = SHA256.HashData(
            Encoding.UTF8.GetBytes(claveEsperada));

        var hashRecibido = SHA256.HashData(
            Encoding.UTF8.GetBytes(claveRecibida));

        return CryptographicOperations.FixedTimeEquals(
            hashEsperado,
            hashRecibido);
    }

    private bool NumeroAutorizado(string? numero)
    {
        var numerosPermitidos = (
            _configuration["PanambiSocial:AllowedPhones"] ?? string.Empty)
            .Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Select(SoloNumeros)
            .Where(numeroPermitido => numeroPermitido.Length >= 9)
            .ToHashSet();

        return numerosPermitidos.Count > 0 &&
               numerosPermitidos.Contains(SoloNumeros(numero));
    }

    private static bool ModelosValidos(
        List<PanambiSocialModeloEntrada>? modelos)
    {
        if (modelos is null ||
            modelos.Count < 1 ||
            modelos.Count > 20)
        {
            return false;
        }

        return modelos.All(modelo =>
            !string.IsNullOrWhiteSpace(modelo.NombreModelo) &&
            modelo.NombreModelo.Length <= 120 &&
            modelo.ReferenciasFotos is not null &&
            modelo.ReferenciasFotos.Count <= 10);
    }


    private static bool ImagenValida(
        PanambiSocialFotoRequest request)
    {
        var tiposPermitidos = new[]
        {
            "image/jpeg",
            "image/png",
            "image/webp"
        };

        return tiposPermitidos.Contains(request.MediaMimeType) &&
               !string.IsNullOrWhiteSpace(request.MediaBase64) &&
               request.MediaBase64.Length <= 10_000_000;
    }

    #endregion

    #region Utilidades

    private static bool CoincideModelo(
        string? marcaCatalogo,
        string? nombreCatalogo,
        string? marcaDetectada,
        string nombreNormalizado,
        string modeloNormalizado)
    {
        if (!string.IsNullOrWhiteSpace(marcaDetectada))
        {
            var nombreCatalogoCompleto = Normalizar(
                $"{marcaCatalogo} {nombreCatalogo}");

            return nombreCatalogoCompleto == nombreNormalizado;
        }

        return modeloNormalizado.Length >= 4 &&
               Normalizar(nombreCatalogo) == modeloNormalizado;
    }

    private static string Normalizar(string? texto)
    {
        var textoNormalizado = (texto ?? string.Empty)
            .Normalize(NormalizationForm.FormD);

        var caracteres = textoNormalizado
            .Where(caracter =>
                CharUnicodeInfo.GetUnicodeCategory(caracter) !=
                UnicodeCategory.NonSpacingMark)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray();

        return new string(caracteres);
    }

    private static string SoloNumeros(string? valor)
    {
        var numeros = (valor ?? string.Empty)
            .Where(char.IsDigit)
            .ToArray();

        return new string(numeros);
    }

    #endregion
}

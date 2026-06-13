using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using SixLabors.ImageSharp;
using System.Globalization;
using System.Net;
using System.Text;
using tuvendedorback.Common;
using tuvendedorback.DTOs;
using tuvendedorback.Exceptions;
using tuvendedorback.Services.Interfaces;

namespace tuvendedorback.Services.Storage;

public class CloudinaryBannerImageStorageService
    : IBannerImageStorageService
{
    private readonly Cloudinary _cloudinary;

    private readonly ILogger<
        CloudinaryBannerImageStorageService
    > _logger;

    private readonly string _rootFolder;

    private readonly string _environmentFolder;

    private readonly int _quality;

    public CloudinaryBannerImageStorageService(
        IConfiguration config,
        IHostEnvironment environment,
        ILogger<
            CloudinaryBannerImageStorageService
        > logger)
    {
        var cloudName =
            config["Cloudinary:CloudName"];

        var apiKey =
            config["Cloudinary:ApiKey"];

        var apiSecret =
            config["Cloudinary:ApiSecret"];

        if (
            string.IsNullOrWhiteSpace(cloudName)
            || string.IsNullOrWhiteSpace(apiKey)
            || string.IsNullOrWhiteSpace(apiSecret)
        )
        {
            throw new InvalidOperationException(
                "Falta configurar Cloudinary:CloudName, " +
                "Cloudinary:ApiKey o Cloudinary:ApiSecret.");
        }

        _cloudinary =
            new Cloudinary(
                new Account(
                    cloudName,
                    apiKey,
                    apiSecret))
            {
                Api =
                {
                    Secure = true
                }
            };

        _logger = logger;

        _rootFolder =
            NormalizarCarpeta(
                config["Cloudinary:BannerRootFolder"]
                ?? "tuvendedor/banners");

        _environmentFolder =
            NormalizarSegmento(
                config[
                    "Cloudinary:BannerEnvironmentFolder"
                ]
                ?? (
                    environment.IsProduction()
                        ? "prod"
                        : "dev"
                ));

        _quality =
            Math.Clamp(
                config.GetValue<int?>(
                    "Cloudinary:BannerWebpQuality")
                ?? 84,
                70,
                95);
    }

    /*
      Ejemplo generado automáticamente:

      tuvendedor/
      └── banners/
          └── dev/
              └── ferremas/
                  └── home-inline/
                      └── storage-key/
    */
    public string ConstruirAssetFolder(
        Guid storageKey,
        string nombreCliente,
        string ubicacion)
    {
        var clienteSlug =
            CrearSlug(nombreCliente);

        var ubicacionSlug =
            CrearSlug(
                ubicacion.Replace('_', '-'));

        return
            $"{_rootFolder}/" +
            $"{_environmentFolder}/" +
            $"{clienteSlug}/" +
            $"{ubicacionSlug}/" +
            $"{storageKey:N}";
    }

    /*
      La imagen debe ingresar con la medida exacta.
      Posteriormente se almacena como WebP optimizado.
    */
    public async Task<BannerArchivoUploadResultDto>
        SubirImagen(
            IFormFile archivo,
            string assetFolder,
            Guid storageKey,
            string tipoDispositivo,
            int revision,
            BannerDimensionDto dimensionEsperada)
    {
        ValidarArchivo(archivo);

        if (revision <= 0)
        {
            throw new ReglasdeNegocioException(
                "La revisión del archivo debe ser mayor a cero.");
        }

        var dispositivo =
            tipoDispositivo
                .Trim()
                .ToUpperInvariant();

        await ValidarDimensionExacta(
            archivo,
            dimensionEsperada,
            dispositivo);

        var webpBytes =
            await ImagenHelper.GenerarWebPAsync(
                archivo,
                width: dimensionEsperada.Width,
                height: dimensionEsperada.Height,
                calidad: _quality,
                crop: false);

        var publicId =
            $"tv-banner-{storageKey:N}-" +
            $"{dispositivo.ToLowerInvariant()}-" +
            $"r{revision:000}";

        var displayName =
            $"{dispositivo.ToLowerInvariant()}-" +
            $"r{revision:000}";

        await using var stream =
            new MemoryStream(webpBytes);

        var uploadParams =
            new ImageUploadParams
            {
                File =
                    new FileDescription(
                        $"{publicId}.webp",
                        stream),

                /*
                  En carpetas dinámicas, AssetFolder organiza
                  visualmente el recurso en Cloudinary.
                */
                AssetFolder =
                    NormalizarCarpeta(assetFolder),

                DisplayName =
                    displayName,

                PublicId =
                    publicId,

                UseFilename =
                    false,

                UniqueFilename =
                    false,

                /*
                  Nunca sobrescribimos una versión anterior.
                  Cada modificación crea r002, r003, etc.
                */
                Overwrite =
                    false,

                Type =
                    "upload"
            };

        try
        {
            var result =
                await _cloudinary.UploadAsync(
                    uploadParams);

            if (
                result.StatusCode
                    != HttpStatusCode.OK
                || result.Error != null
            )
            {
                throw new RepositoryException(
                    "Cloudinary no pudo subir " +
                    "la imagen del banner. " +
                    $"Error={result.Error?.Message}");
            }

            var secureUrl =
                result.SecureUrl?.ToString();

            if (
                string.IsNullOrWhiteSpace(
                    secureUrl)
                || string.IsNullOrWhiteSpace(
                    result.PublicId)
                || string.IsNullOrWhiteSpace(
                    result.AssetId)
            )
            {
                throw new RepositoryException(
                    "Cloudinary no devolvió los " +
                    "identificadores requeridos para " +
                    "administrar la imagen.");
            }

            return new BannerArchivoUploadResultDto
            {
                TipoDispositivo =
                    dispositivo,

                Revision =
                    revision,

                AssetFolder =
                    result.AssetFolder
                    ?? assetFolder,

                PublicId =
                    result.PublicId,

                AssetId =
                    result.AssetId,

                SecureUrl =
                    secureUrl,

                Width =
                    result.Width,

                Height =
                    result.Height,

                Bytes =
                    result.Bytes,

                Format =
                    result.Format
                    ?? "webp"
            };
        }
        catch (RepositoryException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al subir imagen de banner. " +
                "Dispositivo={Dispositivo}, " +
                "Carpeta={Carpeta}",
                dispositivo,
                assetFolder);

            throw new RepositoryException(
                "Error al subir imagen de banner " +
                "a Cloudinary.",
                ex);
        }
    }

    /*
      La eliminación física se ejecutará después del
      período de seguridad. No se borra inmediatamente
      al actualizar una campaña.
    */
    public async Task EliminarImagen(
        string? publicId)
    {
        if (
            string.IsNullOrWhiteSpace(
                publicId)
        )
        {
            return;
        }

        try
        {
            var result =
                await _cloudinary.DestroyAsync(
                    new DeletionParams(
                        publicId)
                    {
                        ResourceType =
                            ResourceType.Image,

                        Invalidate =
                            true
                    });

            if (
                result.Result is "ok"
                or "not found"
            )
            {
                return;
            }

            throw new RepositoryException(
                "Cloudinary no confirmó la eliminación. " +
                $"PublicId={publicId}, " +
                $"Resultado={result.Result}, " +
                $"Error={result.Error?.Message}");
        }
        catch (RepositoryException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al eliminar imagen de banner. " +
                "PublicId={PublicId}",
                publicId);

            throw new RepositoryException(
                "Error al eliminar imagen de banner " +
                "en Cloudinary.",
                ex);
        }
    }

    private static void ValidarArchivo(
        IFormFile archivo)
    {
        if (
            archivo == null
            || archivo.Length <= 0
        )
        {
            throw new ReglasdeNegocioException(
                "La imagen del banner no puede estar vacía.");
        }

        var permitidos =
            new[]
            {
                "image/jpeg",
                "image/png",
                "image/webp"
            };

        if (
            !permitidos.Contains(
                archivo.ContentType,
                StringComparer.OrdinalIgnoreCase)
        )
        {
            throw new ReglasdeNegocioException(
                "La imagen del banner debe ser JPG, PNG o WEBP.");
        }
    }

    private static async Task ValidarDimensionExacta(
        IFormFile archivo,
        BannerDimensionDto dimensionEsperada,
        string dispositivo)
    {
        await using var stream =
            archivo.OpenReadStream();

        var info =
            await Image
                .IdentifyAsync(stream);

        if (info == null)
        {
            throw new ReglasdeNegocioException(
                "No fue posible leer la imagen del banner.");
        }

        if (
            info.Width != dimensionEsperada.Width
            || info.Height != dimensionEsperada.Height
        )
        {
            throw new ReglasdeNegocioException(
                $"La imagen {dispositivo} debe medir " +
                $"exactamente {dimensionEsperada.Width} × " +
                $"{dimensionEsperada.Height} px. " +
                $"El archivo enviado mide " +
                $"{info.Width} × {info.Height} px.");
        }
    }

    private static string NormalizarCarpeta(
        string valor)
    {
        return valor
            .Trim()
            .Replace("\\", "/")
            .Trim('/');
    }

    private static string NormalizarSegmento(
        string valor)
    {
        return CrearSlug(valor);
    }

    private static string CrearSlug(
        string valor)
    {
        var texto =
            valor
                .Trim()
                .ToLowerInvariant()
                .Normalize(
                    NormalizationForm.FormD);

        var builder =
            new StringBuilder();

        var anteriorFueGuion =
            false;

        foreach (var caracter in texto)
        {
            if (
                CharUnicodeInfo
                    .GetUnicodeCategory(
                        caracter)
                ==
                UnicodeCategory
                    .NonSpacingMark
            )
            {
                continue;
            }

            if (
                char.IsLetterOrDigit(
                    caracter)
            )
            {
                builder.Append(
                    caracter);

                anteriorFueGuion =
                    false;
            }
            else if (!anteriorFueGuion)
            {
                builder.Append('-');

                anteriorFueGuion =
                    true;
            }
        }

        var slug =
            builder
                .ToString()
                .Normalize(
                    NormalizationForm.FormC)
                .Trim('-');

        return
            string.IsNullOrWhiteSpace(
                slug)
                ? "cliente"
                : slug;
    }
}

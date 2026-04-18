using System.Text;
using System.Text.RegularExpressions;
using tuvendedorback.DTOs;
using tuvendedorback.Exceptions;
using tuvendedorback.Repositories.Interfaces;
using tuvendedorback.Services.Interfaces;

namespace tuvendedorback.Services;

public class PerfilVendedorService : IPerfilVendedorService
{
    private readonly IPerfilVendedorRepository _repository;
    private readonly ILogger<PerfilVendedorService> _logger;
    private readonly IImageStorageService _imageStorage;

    public PerfilVendedorService(
        IPerfilVendedorRepository repository,
        ILogger<PerfilVendedorService> logger,
        IImageStorageService imageStorage)
    {
        _repository = repository;
        _logger = logger;
        _imageStorage = imageStorage;
    }

    public async Task<PerfilPublicoVendedorDto> ObtenerPerfilPublicoPorSlug(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
            throw new ReglasdeNegocioException("El slug del perfil es obligatorio.");

        var perfil = await _repository.ObtenerPerfilPublicoPorSlug(slug);

        if (perfil == null)
            throw new NoDataFoundException("No se encontró el perfil del vendedor.");

        if (!perfil.EsPerfilPublico)
            throw new NoDataFoundException("El perfil del vendedor no está disponible públicamente.");

        var publicaciones = await _repository.ObtenerPublicacionesActivasPorSlug(slug);

        perfil.Publicaciones = publicaciones;
        perfil.CantidadPublicaciones = publicaciones.Count;

        return perfil;
    }

    public async Task<PerfilPublicoVendedorDto> ObtenerMiPerfilVendedor(int idUsuario)
    {
        if (idUsuario <= 0)
            throw new UnauthorizedAccessException();

        var perfil = await _repository.ObtenerMiPerfilVendedor(idUsuario);

        if (perfil == null)
            throw new NoDataFoundException("No se encontró el perfil vendedor del usuario.");

        var publicaciones = await _repository.ObtenerPublicacionesActivasPorSlug(perfil.Slug ?? string.Empty);

        perfil.Publicaciones = publicaciones;
        perfil.CantidadPublicaciones = publicaciones.Count;

        return perfil;
    }

    public async Task<PerfilPublicoVendedorDto> ActualizarMiPerfilVendedor(
        ActualizarMiPerfilVendedorRequest request,
        int idUsuario)
    {
        if (idUsuario <= 0)
            throw new UnauthorizedAccessException();

        var perfilActual = await _repository.ObtenerMiPerfilVendedor(idUsuario);

        if (perfilActual == null)
            throw new NoDataFoundException("No se encontró el perfil vendedor del usuario.");

        if (!string.IsNullOrWhiteSpace(request.Slug))
        {
            request.Slug = NormalizarSlug(request.Slug);

            var existeSlug = await _repository.ExisteSlugEnOtroVendedor(request.Slug, idUsuario);

            if (existeSlug)
                throw new ReglasdeNegocioException("El slug ingresado ya está siendo utilizado por otro vendedor.");
        }
        else if (!string.IsNullOrWhiteSpace(request.NombreNegocio) && string.IsNullOrWhiteSpace(perfilActual.Slug))
        {
            request.Slug = NormalizarSlug(request.NombreNegocio);
        }

        string? fotoPerfilUrl = null;
        string? bannerUrl = null;
        string? bannerTipo = null;

        if (request.FotoPerfil != null && request.FotoPerfil.Length > 0)
        {
            var carpetaPerfil = $"vendedores/{idUsuario}/perfil";

            var resultadoFoto = await _imageStorage.SubirArchivo(
                request.FotoPerfil,
                carpetaPerfil,
                generarMiniatura: false
            );

            fotoPerfilUrl = resultadoFoto.MainUrl;
        }

        if (request.Banner != null && request.Banner.Length > 0)
        {
            var carpetaBanner = $"vendedores/{idUsuario}/banner";

            var resultadoBanner = await _imageStorage.SubirArchivo(
                request.Banner,
                carpetaBanner,
                generarMiniatura: false
            );

            bannerUrl = resultadoBanner.MainUrl;
            bannerTipo = ObtenerTipoBanner(request.Banner.ContentType);
        }

        await _repository.ActualizarMiPerfilVendedor(
            request,
            idUsuario,
            fotoPerfilUrl,
            bannerUrl,
            bannerTipo);

        return await ObtenerMiPerfilVendedor(idUsuario);
    }

    private static string NormalizarSlug(string valor)
    {
        var slug = valor.Trim().ToLowerInvariant();

        slug = RemoverAcentos(slug);
        slug = Regex.Replace(slug, @"[^a-z0-9\s-]", "");
        slug = Regex.Replace(slug, @"\s+", "-");
        slug = Regex.Replace(slug, @"-+", "-");

        return slug.Trim('-');
    }

    private static string RemoverAcentos(string texto)
    {
        var normalized = texto.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();

        foreach (var c in normalized)
        {
            var unicodeCategory = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);

            if (unicodeCategory != System.Globalization.UnicodeCategory.NonSpacingMark)
                builder.Append(c);
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string ObtenerTipoBanner(string? contentType)
    {
        if (!string.IsNullOrWhiteSpace(contentType) &&
            contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
            return "VIDEO";

        return "IMAGEN";
    }
}

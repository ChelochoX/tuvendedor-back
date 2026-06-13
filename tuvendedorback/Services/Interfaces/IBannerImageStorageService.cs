using tuvendedorback.DTOs;

namespace tuvendedorback.Services.Interfaces;

public interface IBannerImageStorageService
{
    string ConstruirAssetFolder(
        Guid storageKey,
        string nombreCliente,
        string ubicacion);

    Task<BannerArchivoUploadResultDto> SubirImagen(
        IFormFile archivo,
        string assetFolder,
        Guid storageKey,
        string tipoDispositivo,
        int revision,
        BannerDimensionDto dimensionEsperada);

    Task EliminarImagen(string? publicId);
}

using tuvendedorback.DTOs;

namespace tuvendedorback.Services.Interfaces;

public interface IImageStorageService
{
    Task<UploadResultDto> SubirArchivo(
        IFormFile archivo,
        string carpetaDestino = "publicaciones",
        bool generarMiniatura = false);

    Task EliminarArchivo(string archivoUrl);
}

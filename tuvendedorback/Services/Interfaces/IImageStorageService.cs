using tuvendedorback.DTOs;

namespace tuvendedorback.Services.Interfaces;

public interface IImageStorageService
{
    Task<UploadResultDto> SubirArchivo(
        IFormFile archivo,
        string carpetaDestino = "publicaciones",
        bool generarMiniatura = false);

    Task<UploadResultDto> SubirImagenOptimizada(
     IFormFile archivo,
     string carpetaDestino,
     int width,
     int height,
     int calidad = 90);

    Task EliminarArchivo(string archivoUrl);
}

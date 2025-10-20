using tuvendedorback.DTOs;

namespace tuvendedorback.Services.Interfaces;

public interface IImageStorageService
{
    Task<UploadResultDto> SubirArchivo(IFormFile imagenFile, string carpetaDestino = "publicaciones");
    Task EliminarArchivo(string archivoUrl);
}

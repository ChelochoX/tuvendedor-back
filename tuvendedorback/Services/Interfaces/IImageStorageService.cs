namespace tuvendedorback.Services.Interfaces;

public interface IImageStorageService
{
    Task<string> SubirArchivo(IFormFile imagenFile, string carpetaDestino = "publicaciones");
}

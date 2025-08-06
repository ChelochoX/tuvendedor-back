namespace tuvendedorback.Services.Interfaces;

public interface IImageStorageService
{
    Task<string> SubirImagenWebP(IFormFile imagenFile, string carpetaDestino = "publicaciones");
}

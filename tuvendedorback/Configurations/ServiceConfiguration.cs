using Microsoft.Extensions.FileProviders;
using tuvendedorback.Data;

namespace tuvendedorback.Configurations;

public static class ServiceConfiguration
{
    public static void AddConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<DbConnections>();

        services.AddCors(options =>
        {
            options.AddPolicy("AllowFrontend",
                policy =>
                {
                    policy.AllowAnyOrigin()
                        .AllowAnyHeader()
                        .AllowAnyMethod();
                });
        });
    }
    public static void UseStaticFilesConfiguration(this WebApplication app, IConfiguration configuration)
    {
        // Obtener la ruta base desde la configuración
        string externalImagesPath = configuration["ImagenesMotosPath"];

        if (app.Environment.IsProduction())
        {
            // Valor predeterminado para producción
            externalImagesPath ??= "/app/ImagenesMotos";
        }
        else
        {
            // Valor predeterminado para desarrollo
            externalImagesPath ??= "C:/ImagenesMotos";
        }

        // Configuración para la carpeta general de ImagenesMotos
        if (!string.IsNullOrEmpty(externalImagesPath) && Directory.Exists(externalImagesPath))
        {
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(externalImagesPath),
                RequestPath = "/uploads"
            });

            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(externalImagesPath),
                RequestPath = "/imagenes_motos"
            });
        }
        else
        {
            throw new DirectoryNotFoundException($"La ruta de imágenes {externalImagesPath} no existe.");
        }
    }
}

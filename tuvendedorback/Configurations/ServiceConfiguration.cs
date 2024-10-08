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
        var externalImagesPath = configuration["ImagenesMotosPath"];

        if (!string.IsNullOrEmpty(externalImagesPath) && Directory.Exists(externalImagesPath))
        {
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(externalImagesPath),
                RequestPath = "/uploads"
            });
        }
        else
        {
            throw new DirectoryNotFoundException($"La ruta de imágenes {externalImagesPath} no existe.");
        }
    }
}

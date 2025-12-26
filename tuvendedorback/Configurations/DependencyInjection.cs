using Microsoft.AspNetCore.Identity;
using tuvendedorback.Repositories;
using tuvendedorback.Repositories.Interfaces;
using tuvendedorback.Services;
using tuvendedorback.Services.IA;
using tuvendedorback.Services.Interfaces;
using tuvendedorback.Services.Storage;

namespace tuvendedorback.Configurations;

public static class DependencyInjection
{
    public static IServiceCollection AddRepositories(this IServiceCollection services)
    {
        services.AddScoped<IUsuariosRepository, UsuariosRepository>();
        services.AddScoped<IPermisosRepository, PermisosRepository>();
        services.AddScoped<IPublicacionRepository, PublicacionRepository>();
        services.AddScoped<IClientesRepository, ClientesRepository>();
        services.AddScoped<IConversacionRepository, ConversacionRepository>();
        services.AddScoped<IPromptIARepository, PromptIARepository>();
        return services;
    }

    public static IServiceCollection AddServices(this IServiceCollection services)
    {
        services.AddScoped<IUsuarioService, UsuarioService>();
        services.AddScoped<IPermisosService, PermisosService>();
        services.AddScoped<IPasswordHasher<string>, PasswordHasher<string>>();
        services.AddScoped<IPublicacionService, PublicacionService>();
        services.AddScoped<IImageStorageService, CloudinaryStorageService>();
        services.AddScoped<IClientesService, ClientesService>();
        services.AddScoped<IConversacionService, ConversacionService>();
        services.AddScoped<IAgenteIAService, AgenteIAService>();
        services.AddScoped<PromptBuilder>();
        return services;
    }
}

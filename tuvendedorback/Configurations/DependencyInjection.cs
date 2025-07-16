using Microsoft.AspNetCore.Identity;
using tuvendedorback.Repositories;
using tuvendedorback.Repositories.Interfaces;
using tuvendedorback.Services;
using tuvendedorback.Services.Interfaces;

namespace tuvendedorback.Configurations;

public static class DependencyInjection
{
    public static IServiceCollection AddRepositories(this IServiceCollection services)
    {
        services.AddScoped<IUsuariosRepository, UsuariosRepository>();
        services.AddScoped<IPermisosRepository, PermisosRepository>();
        return services;
    }

    public static IServiceCollection AddServices(this IServiceCollection services)
    {
        services.AddScoped<IUsuarioService, UsuarioService>();
        services.AddScoped<IPermisosService, PermisosService>();
        services.AddScoped<IPasswordHasher<string>, PasswordHasher<string>>();
        return services;
    }
}

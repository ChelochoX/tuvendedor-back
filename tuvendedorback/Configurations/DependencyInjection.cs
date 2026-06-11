using Microsoft.AspNetCore.Identity;
using tuvendedorback.ERP.ERPRepositories;
using tuvendedorback.ERP.ERPRepositories.Interfaces;
using tuvendedorback.ERP.ERPServices;
using tuvendedorback.ERP.ERPServices.Interfaces;
using tuvendedorback.Repositories;
using tuvendedorback.Repositories.Interfaces;
using tuvendedorback.Services;
using tuvendedorback.Services.Interfaces;
using tuvendedorback.Services.Storage;

namespace tuvendedorback.Configurations;

public static class DependencyInjection
{
    public static IServiceCollection AddRepositories(this IServiceCollection services)
    {
        // Marketplace
        services.AddScoped<IUsuariosRepository, UsuariosRepository>();
        services.AddScoped<IPermisosRepository, PermisosRepository>();
        services.AddScoped<IPublicacionRepository, PublicacionRepository>();
        services.AddScoped<IClientesRepository, ClientesRepository>();
        services.AddScoped<IPrecioProductoRepository, PrecioProductoRepository>();
        services.AddScoped<IMarcaRepository, MarcaRepository>();
        services.AddScoped<IModeloProductoRepository, ModeloProductoRepository>();
        services.AddScoped<IPerfilVendedorRepository, PerfilVendedorRepository>();
        services.AddScoped<ICompartirRepository, CompartirRepository>();
        services.AddScoped<ISolicitudVisitaRepository, SolicitudVisitaRepository>();
        services.AddScoped<IPublicacionInteraccionRepository, PublicacionInteraccionRepository>();
        services.AddScoped<IServicioPremiumRepository,ServicioPremiumRepository>();
        services.AddScoped<IPublicacionInteraccionRepository,PublicacionInteraccionRepository>();
        services.AddScoped<IBannerPublicitarioRepository,BannerPublicitarioRepository>();

        // ERP
        services.AddScoped<IERPClienteRepository, ERPClienteRepository>();

        return services;
    }

    public static IServiceCollection AddServices(this IServiceCollection services)
    {
        // Marketplace
        services.AddScoped<IUsuarioService, UsuarioService>();
        services.AddScoped<IPermisosService, PermisosService>();
        services.AddScoped<IPasswordHasher<string>, PasswordHasher<string>>();
        services.AddScoped<IPublicacionService, PublicacionService>();
        services.AddScoped<IImageStorageService, CloudinaryStorageService>();
        services.AddScoped<IClientesService, ClientesService>();
        services.AddScoped<IPrecioProductoService, PrecioProductoService>();
        services.AddScoped<IMarcaService, MarcaService>();
        services.AddScoped<IModeloProductoService, ModeloProductoService>();
        services.AddScoped<IPerfilVendedorService, PerfilVendedorService>();
        services.AddScoped<ICompartirService, CompartirService>();
        services.AddScoped<ISolicitudVisitaService, SolicitudVisitaService>();
        services.AddScoped<IWhatsAppNotificationService, WhatsAppNotificationService>();
        services.AddScoped<IPublicacionInteraccionService, PublicacionInteraccionService>();
        services.AddScoped<IServicioPremiumService,ServicioPremiumService>();
        services.AddScoped<IBannerPublicitarioService,BannerPublicitarioService>();

        // ERP
        services.AddScoped<IERPClienteService, ERPClienteService>();

        return services;
    }
}

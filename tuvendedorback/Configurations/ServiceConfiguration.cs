using FluentValidation;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.IdentityModel.Tokens;
using System.Reflection;
using System.Text;
using tuvendedorback.Common;
using tuvendedorback.Data;
using tuvendedorback.Services;

namespace tuvendedorback.Configurations;

public static class ServiceConfiguration
{
    public static void AddConfiguration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        _ = services.AddSingleton<DbConnections>();

        _ = services.AddCors(options =>
        {
            options.AddPolicy(
                "AllowFrontend",
                policy =>
                {
                    _ = policy
                        .AllowAnyOrigin()
                        .AllowAnyHeader()
                        .AllowAnyMethod();
                });
        });

        // ---------------------------------------
        // Configuración de Cloudflare R2
        // ---------------------------------------
        services.Configure<R2Options>(
            configuration.GetSection("R2"));

        // ---------------------------------------
        // Configuración de archivos
        // ---------------------------------------
        var uploadSection =
            configuration.GetRequiredSection(
                UploadOptions.SectionName);

        services.Configure<UploadOptions>(
            uploadSection);

        var uploadOptions =
            uploadSection.Get<UploadOptions>()
            ?? throw new InvalidOperationException(
                "No se pudo cargar la configuración Upload.");

        if (uploadOptions.MaxRequestBodySize <= 0)
        {
            throw new InvalidOperationException(
                "Upload:MaxRequestBodySize debe ser mayor a cero.");
        }

        if (uploadOptions.MaxFileSize <= 0)
        {
            throw new InvalidOperationException(
                "Upload:MaxFileSize debe ser mayor a cero.");
        }

        if (uploadOptions.MaxFiles <= 0)
        {
            throw new InvalidOperationException(
                "Upload:MaxFiles debe ser mayor a cero.");
        }

        // ---------------------------------------
        // Límite para multipart/form-data
        // ---------------------------------------
        services.Configure<FormOptions>(options =>
        {
            options.MultipartBodyLengthLimit =
                uploadOptions.MaxRequestBodySize;
        });

        // JWT Service
        services.AddSingleton<JwtService>();

        services.AddScoped<UserContext>();

        // Autenticación JWT
        services
            .AddAuthentication("Bearer")
            .AddJwtBearer(
                "Bearer",
                options =>
                {
                    var key =
                        configuration["Jwt:Key"];

                    var issuer =
                        configuration["Jwt:Issuer"];

                    var audience =
                        configuration["Jwt:Audience"];

                    options.TokenValidationParameters =
                        new TokenValidationParameters
                        {
                            ValidateIssuerSigningKey = true,

                            IssuerSigningKey =
                                new SymmetricSecurityKey(
                                    Encoding.UTF8.GetBytes(
                                        key!)),

                            ValidateIssuer = true,
                            ValidateAudience = true,
                            ValidateLifetime = true,

                            ValidIssuer = issuer,
                            ValidAudience = audience
                        };
                });

        // AutoMapper
        services.AddAutoMapper(
            cfg => { },
            Assembly.GetExecutingAssembly());

        // FluentValidation
        services.AddValidatorsFromAssembly(
            Assembly.GetExecutingAssembly());
    }
}
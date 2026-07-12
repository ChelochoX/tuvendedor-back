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
    public static WebApplicationBuilder AddConfiguration(
        this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;

        ConfigurarLogging(builder);

        ConfigurarUpload(builder);

        services.AddSingleton<DbConnections>();

        services.AddCors(options =>
        {
            options.AddPolicy(
                "AllowFrontend",
                policy =>
                {
                    policy
                        .AllowAnyOrigin()
                        .AllowAnyHeader()
                        .AllowAnyMethod();
                });
        });

        // ---------------------------------------
        // Cloudflare R2
        // ---------------------------------------
        services.Configure<R2Options>(
            configuration.GetRequiredSection("R2"));

        // ---------------------------------------
        // JWT
        // ---------------------------------------
        services.AddSingleton<JwtService>();

        services.AddScoped<UserContext>();

        services
            .AddAuthentication("Bearer")
            .AddJwtBearer(
                "Bearer",
                options =>
                {
                    var key = ObtenerValorRequerido(
                        configuration,
                        "Jwt:Key");

                    var issuer = ObtenerValorRequerido(
                        configuration,
                        "Jwt:Issuer");

                    var audience = ObtenerValorRequerido(
                        configuration,
                        "Jwt:Audience");

                    options.TokenValidationParameters =
                        new TokenValidationParameters
                        {
                            ValidateIssuerSigningKey = true,

                            IssuerSigningKey =
                                new SymmetricSecurityKey(
                                    Encoding.UTF8.GetBytes(key)),

                            ValidateIssuer = true,
                            ValidateAudience = true,
                            ValidateLifetime = true,

                            ValidIssuer = issuer,
                            ValidAudience = audience
                        };
                });

        // ---------------------------------------
        // Controllers
        // ---------------------------------------
        services.AddControllers();

        // ---------------------------------------
        // AutoMapper
        // ---------------------------------------
        services.AddAutoMapper(
            config => { },
            Assembly.GetExecutingAssembly());

        // ---------------------------------------
        // FluentValidation
        // ---------------------------------------
        services.AddValidatorsFromAssembly(
            Assembly.GetExecutingAssembly());

        // ---------------------------------------
        // Swagger
        // ---------------------------------------
        services.AddEndpointsApiExplorer();

        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc(
                "v1",
                new Microsoft.OpenApi.Models.OpenApiInfo
                {
                    Title =
                        "Documentacion API Market Place de Tu Vendedor",

                    Version = "v1",

                    Description =
                        "REST API Market Place de Tu Vendedor"
                });

            options.EnableAnnotations();

            var xmlFile =
                $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";

            var xmlPath =
                Path.Combine(
                    AppContext.BaseDirectory,
                    xmlFile);

            options.IncludeXmlComments(xmlPath);
        });

        return builder;
    }

    private static void ConfigurarUpload(
        WebApplicationBuilder builder)
    {
        var uploadSection =
            builder.Configuration.GetRequiredSection(
                UploadOptions.SectionName);

        var uploadOptions =
            uploadSection.Get<UploadOptions>()
            ?? throw new InvalidOperationException(
                $"No se pudo cargar la sección " +
                $"'{UploadOptions.SectionName}'.");

        ValidarUploadOptions(uploadOptions);

        /*
         * Configuración tipada para que los servicios,
         * validadores y controladores consuman IOptions<UploadOptions>.
         */
        builder.Services
            .AddOptions<UploadOptions>()
            .Bind(uploadSection)
            .Validate(
                EsUploadOptionsValido,
                "La configuración de Upload no es válida.")
            .ValidateOnStart();

        /*
         * Límite total aceptado directamente por Kestrel.
         */
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize =
                uploadOptions.MaxRequestBodySize;
        });

        /*
         * Límite para solicitudes multipart/form-data.
         */
        builder.Services.Configure<FormOptions>(options =>
        {
            options.MultipartBodyLengthLimit =
                uploadOptions.MaxRequestBodySize;
        });
    }

    private static void ConfigurarLogging(
        WebApplicationBuilder builder)
    {
        builder.Logging.ClearProviders();

        builder.Logging.AddConsole();

        builder.Logging.SetMinimumLevel(
            LogLevel.Information);
    }

    private static void ValidarUploadOptions(
        UploadOptions options)
    {
        if (options.MaxRequestBodySize <= 0)
        {
            throw new InvalidOperationException(
                $"{UploadOptions.SectionName}:" +
                $"{nameof(UploadOptions.MaxRequestBodySize)} " +
                "debe ser mayor a cero.");
        }

        if (options.MaxFileSize <= 0)
        {
            throw new InvalidOperationException(
                $"{UploadOptions.SectionName}:" +
                $"{nameof(UploadOptions.MaxFileSize)} " +
                "debe ser mayor a cero.");
        }

        if (options.MaxFiles <= 0)
        {
            throw new InvalidOperationException(
                $"{UploadOptions.SectionName}:" +
                $"{nameof(UploadOptions.MaxFiles)} " +
                "debe ser mayor a cero.");
        }

        if (
            options.MaxRequestBodySize <
            options.MaxFileSize
        )
        {
            throw new InvalidOperationException(
                $"{UploadOptions.SectionName}:" +
                $"{nameof(UploadOptions.MaxRequestBodySize)} " +
                "debe ser mayor o igual a " +
                $"{UploadOptions.SectionName}:" +
                $"{nameof(UploadOptions.MaxFileSize)}.");
        }
    }

    private static bool EsUploadOptionsValido(
        UploadOptions options)
    {
        return
            options.MaxRequestBodySize > 0 &&
            options.MaxFileSize > 0 &&
            options.MaxFiles > 0 &&
            options.MaxRequestBodySize >=
            options.MaxFileSize;
    }

    private static string ObtenerValorRequerido(
        IConfiguration configuration,
        string clave)
    {
        var valor = configuration[clave];

        if (string.IsNullOrWhiteSpace(valor))
        {
            throw new InvalidOperationException(
                $"Falta configurar '{clave}'.");
        }

        return valor;
    }
}
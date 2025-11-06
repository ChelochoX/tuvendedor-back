using tuvendedorback.Configurations;
using tuvendedorback.Middlewares;

var environmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";

var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory()) // Asegúrate de establecer la ruta base correctamente
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true) // Cargar el archivo base
    .AddJsonFile($"appsettings.{environmentName}.json", optional: true, reloadOnChange: true) // Cargar archivo específico según el entorno
    .AddEnvironmentVariables() // Cargar variables de entorno si es necesario
    .Build();

var builder = WebApplication.CreateBuilder(args);

// Configuración de Logs
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);

// Add services to the container.
builder.Services.AddRepositories();
builder.Services.AddServices();
builder.Services.AddControllers();
builder.Services.AddConfiguration(configuration);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Documentacion API Market Place de Tu Vendedor",
        Version = "v1",
        Description = "REST API Market Place de Tu Vendedor"
    });
    c.EnableAnnotations();


    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    c.IncludeXmlComments(xmlPath);
});

var app = builder.Build();

//app.UseStaticFilesConfiguration(configuration);

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
ILogger<Program> logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Aplicacion Iniciada Correctamente");

app.UseHandlingMiddleware();

app.UseHttpsRedirection();
app.UseCors("AllowFrontend");

app.UseAuthentication();
app.UseAuthorization();

app.UseMiddleware<UserContextMiddleware>();

app.MapControllers();

app.Run();

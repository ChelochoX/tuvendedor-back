using tuvendedorback.Middlewares;

namespace tuvendedorback.Configurations;

public static class AppExtensions
{
    public static void UseHandlingMiddleware(this IApplicationBuilder app)
    {
        app.UseMiddleware<ErrorHandlingMiddleware>();
    }
}

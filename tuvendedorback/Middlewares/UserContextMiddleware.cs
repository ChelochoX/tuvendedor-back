using tuvendedorback.Common;
using tuvendedorback.Repositories.Interfaces;

namespace tuvendedorback.Middlewares;

public class UserContextMiddleware
{
    private readonly RequestDelegate _next;

    public UserContextMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task Invoke(HttpContext context, UserContext userContext, IUsuariosRepository usuarioRepo)
    {
        var user = context.User;
        if (user.Identity?.IsAuthenticated == true)
        {
            var idUsuarioStr = user.FindFirst("id_usuario")?.Value;
            if (!int.TryParse(idUsuarioStr, out var idUsuario))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Token inválido.");
                return;
            }

            var usuarioDb = await usuarioRepo.ObtenerUsuarioActivoPorId(idUsuario);
            if (usuarioDb == null)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Usuario no autorizado o deshabilitado.");
                return;
            }

            userContext.IdUsuario = usuarioDb.IdUsuario;
            userContext.NombreUsuario = usuarioDb.NombreUsuario;
            // Aquí podrías agregar también IdRol si lo necesitás más adelante
        }

        await _next(context);
    }

}


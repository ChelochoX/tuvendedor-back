using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace tuvendedorback.Middlewares.Filters;

public class PermisoRequeridoAttribute : Attribute, IAsyncActionFilter
{
    private readonly string _entidad;
    private readonly string _recurso;

    public PermisoRequeridoAttribute(string recurso, string entidad)
    {
        _recurso = recurso;
        _entidad = entidad;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var httpContext = context.HttpContext;
        var logger = httpContext.RequestServices.GetRequiredService<ILogger<PermisoRequeridoAttribute>>();
        var user = httpContext.User;

        try
        {
            logger.LogInformation("🔎 Header Authorization recibido: {AuthHeader}",
                httpContext.Request.Headers["Authorization"].ToString());

            if (!user.Identity?.IsAuthenticated ?? true)
            {
                logger.LogWarning("Acceso no autorizado: el usuario no está autenticado.");
                context.Result = new UnauthorizedObjectResult(new
                {
                    Message = "No estás autenticado para acceder a este recurso."
                });
                return;
            }

            var idUsuarioClaim = user.FindFirst("id_usuario")?.Value;
            if (string.IsNullOrEmpty(idUsuarioClaim) || !int.TryParse(idUsuarioClaim, out int idUsuario))
            {
                logger.LogWarning("Acceso no autorizado: el token no contiene un ID de usuario válido.");
                context.Result = new UnauthorizedObjectResult(new
                {
                    Message = "No se pudo identificar al usuario en el token."
                });
                return;
            }

            var permisoService = httpContext.RequestServices.GetRequiredService<IPermisosService>();
            var tienePermiso = await permisoService.TienePermiso(idUsuario, _entidad, _recurso);

            if (!tienePermiso)
            {
                logger.LogWarning("Permiso denegado: Usuario {IdUsuario} intentó realizar '{Recurso}' sobre '{Entidad}' sin permiso.",
                    idUsuario, _recurso, _entidad);

                context.Result = new ObjectResult(new
                {
                    Message = $"No tenés permisos para realizar la acción '{_recurso}' sobre '{_entidad}'."
                })
                {
                    StatusCode = StatusCodes.Status403Forbidden
                };

                return;
            }

            // Permiso concedido
            logger.LogInformation("Permiso concedido: Usuario {IdUsuario} puede realizar '{Recurso}' sobre '{Entidad}'.",
                idUsuario, _recurso, _entidad);

            await next();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error inesperado al validar permiso para la acción '{Recurso}' sobre '{Entidad}'.", _recurso, _entidad);

            context.Result = new ObjectResult(new
            {
                Message = "Ocurrió un error inesperado al verificar permisos.",
                Error = ex.Message // ⚠️ Podés omitir esto en producción si querés ocultar detalles
            })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }
}

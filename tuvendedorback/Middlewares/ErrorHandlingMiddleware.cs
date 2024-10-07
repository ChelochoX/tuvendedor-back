using FluentValidation;
using System.Net;
using System.Text.Json;
using tuvendedorback.Exceptions;
using tuvendedorback.Wrappers;

namespace tuvendedorback.Middlewares;

public class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IHostEnvironment _env;

    public ErrorHandlingMiddleware(RequestDelegate next, IHostEnvironment env)
    {
        _next = next;
        _env = env;
    }

    public async Task Invoke(HttpContext context)
    {
        try
        {
            await _next(context);

            if (context.Response.StatusCode == 401 || context.Response.StatusCode == 403)
            {
                throw new UnauthorizedAccessException();
            }
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        var response = new Response<object>
        {
            Success = false,
            StatusCode = (int)HttpStatusCode.InternalServerError,
            Errors = new List<string>()
        };

        // Definir el mensaje en función del ambiente
        var showStackTrace = _env.IsDevelopment();

        switch (exception)
        {
            case ValidationException validationException:
                response.StatusCode = (int)HttpStatusCode.BadRequest;
                response.Errors.AddRange(validationException.Errors.Select(e => e.ErrorMessage));
                break;

            case RepositoryException repositoryException:
                response.StatusCode = (int)HttpStatusCode.InternalServerError;
                response.Errors.Add(repositoryException.Message);
                if (showStackTrace)
                {
                    response.Errors.Add(repositoryException.InnerException.Message);
                }
                break;

            case NoDataFoundException noDataFoundException:
                response.StatusCode = (int)HttpStatusCode.NoContent;
                break;

            case ReglasdeNegocioException reglasdeNegocioException:
                response.StatusCode = (int)HttpStatusCode.BadRequest;
                response.Errors.Add(reglasdeNegocioException.Message);
                break;

            //case ErrorAutorizacionUsuarioException errorAutorizacionException:
            //    response.StatusCode = (int)HttpStatusCode.Forbidden;
            //    response.Errors.Add(errorAutorizacionException.Message);
            //    break;

            //case UnauthorizedAccessException unauthorizedException:
            //    response.StatusCode = (int)HttpStatusCode.Forbidden;
            //    response.Errors.Add(unauthorizedException.Message);
            //    break;

            case ParametroFaltanteCadenaConexionException parametrosConexionFaltanteException:
                response.StatusCode = (int)HttpStatusCode.BadRequest;
                response.Errors.Add(parametrosConexionFaltanteException.Message);
                break;

            default:
                response.StatusCode = (int)HttpStatusCode.InternalServerError;
                response.Errors.Add("Ocurrió un error inesperado en el servidor.");
                break;
        }

        var jsonResponse = JsonSerializer.Serialize(response);
        context.Response.StatusCode = response.StatusCode;
        return context.Response.WriteAsync(jsonResponse);
    }

}

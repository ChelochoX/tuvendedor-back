using FluentValidation;
using FluentValidation.Results;

namespace tuvendedorback.Common;

public static class ValidationHelper
{
    public static async Task ValidarAsync<T>(T request, IServiceProvider serviceProvider)
    {
        IValidator<T>? validator = serviceProvider.GetService<IValidator<T>>();

        if (validator == null)
        {
            throw new InvalidOperationException($"No se encontró un validador para el tipo {typeof(T).Name}");
        }

        ValidationResult validationResult = await validator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            throw new ValidationException(validationResult.Errors);
        }
    }
}

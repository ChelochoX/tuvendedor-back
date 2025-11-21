using FluentValidation;

namespace tuvendedorback.Request;

public class DestacarPublicacionRequest
{
    public int IdPublicacion { get; set; }

    /// <summary>
    /// Cantidad de días que estará destacada la publicación.
    /// </summary>
    public int DuracionDias { get; set; } = 7;
}
public class DestacarPublicacionRequestValidator : AbstractValidator<DestacarPublicacionRequest>
{
    public DestacarPublicacionRequestValidator()
    {
        RuleFor(x => x.IdPublicacion)
            .GreaterThan(0)
            .WithMessage("El Id de la publicación es obligatorio.");

        RuleFor(x => x.DuracionDias)
            .GreaterThan(0)
            .LessThanOrEqualTo(30) // puedes ajustar este límite
            .WithMessage("La duración del destacado debe ser entre 1 y 30 días.");
    }
}
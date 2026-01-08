using FluentValidation;

namespace tuvendedorback.Request;

public class EditarListaPrecioProductoRequest
{
    public int Id { get; set; }
    public decimal PrecioPublico { get; set; }
    public decimal PrecioDistribuidor { get; set; }
    public decimal PrecioBase { get; set; }
    public DateTime FechaDesde { get; set; }
    public DateTime? FechaHasta { get; set; }
}

public class EditarListaPrecioProductoRequestValidator
    : AbstractValidator<EditarListaPrecioProductoRequest>
{
    public EditarListaPrecioProductoRequestValidator()
    {
        RuleFor(x => x.Id)
            .GreaterThan(0)
            .WithMessage("El Id de la lista de precios es obligatorio.");

        RuleFor(x => x.PrecioPublico)
            .GreaterThan(0)
            .WithMessage("El precio público debe ser mayor a cero.");

        RuleFor(x => x.PrecioDistribuidor)
            .GreaterThan(0)
            .WithMessage("El precio distribuidor debe ser mayor a cero.");

        RuleFor(x => x.PrecioBase)
            .GreaterThan(0)
            .WithMessage("El precio base debe ser mayor a cero.");
    }
}
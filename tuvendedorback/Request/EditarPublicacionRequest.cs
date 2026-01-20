using FluentValidation;

namespace tuvendedorback.Request;

public class EditarPublicacionRequest
{
    public int IdPublicacion { get; set; }

    public string Titulo { get; set; } = default!;
    public string Descripcion { get; set; } = default!;
    public decimal Precio { get; set; }
    public string Categoria { get; set; } = default!;
    public string Ubicacion { get; set; } = default!;
    public bool MostrarBotonesCompra { get; set; }

    // 🖼️ Nuevas imágenes
    public List<IFormFile>? NuevasImagenes { get; set; }

    // ❌ Imágenes a eliminar (URLs)
    public List<string>? ImagenesAEliminar { get; set; }

    // 💳 Planes de crédito
    public List<EditarPlanFinanciacionProductoRequest>? PlanCredito { get; set; }
}
public class EditarPublicacionRequestValidator
    : AbstractValidator<EditarPublicacionRequest>
{
    public EditarPublicacionRequestValidator()
    {
        RuleFor(x => x.IdPublicacion)
            .GreaterThan(0);

        RuleFor(x => x.Titulo)
            .NotEmpty()
            .MaximumLength(150);

        RuleFor(x => x.Descripcion)
            .NotEmpty()
            .MaximumLength(2000);

        RuleFor(x => x.Precio)
            .GreaterThan(0);

        RuleFor(x => x.Categoria)
            .NotEmpty();

        RuleFor(x => x.Ubicacion)
            .NotEmpty();

        RuleForEach(x => x.PlanCredito)
            .SetValidator(new EditarPlanFinanciacionProductoRequestValidator()!)
            .When(x => x.MostrarBotonesCompra && x.PlanCredito != null);
    }
}
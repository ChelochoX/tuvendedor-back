using FluentValidation;

namespace tuvendedorback.Request;

public class CrearPublicacionRequest
{
    public string Titulo { get; set; }
    public string Descripcion { get; set; }
    public decimal Precio { get; set; }
    public string Categoria { get; set; }
    public string? Ubicacion { get; set; } = "";
    public bool MostrarBotonesCompra { get; set; }
    public List<IFormFile> Imagenes { get; set; }
    public List<PlanCreditoDto>? PlanCredito { get; set; }
}

public class PlanCreditoDto
{
    public int Cuotas { get; set; }
    public decimal ValorCuota { get; set; }
}

public class CrearPublicacionRequestValidator : AbstractValidator<CrearPublicacionRequest>
{
    public CrearPublicacionRequestValidator()
    {
        RuleFor(x => x.Titulo).NotEmpty().MaximumLength(10000);
        RuleFor(x => x.Precio).GreaterThan(0);
        RuleFor(x => x.Categoria).NotEmpty();
        RuleFor(x => x.Imagenes).NotEmpty().WithMessage("Debe adjuntar al menos una imagen.")
                                 .Must(i => i.Count <= 10).WithMessage("Máximo 10 imágenes permitidas.");

        RuleForEach(x => x.PlanCredito)
            .SetValidator(new PlanCreditoDtoValidator())
            .When(x => x.MostrarBotonesCompra);
    }
}

public class PlanCreditoDtoValidator : AbstractValidator<PlanCreditoDto>
{
    public PlanCreditoDtoValidator()
    {
        RuleFor(x => x.Cuotas).GreaterThan(0);
        RuleFor(x => x.ValorCuota).GreaterThan(0);
    }
}
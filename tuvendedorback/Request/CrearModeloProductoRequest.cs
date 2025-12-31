using FluentValidation;

namespace tuvendedorback.Request;

public class CrearModeloProductoRequest
{
    public int IdMarca { get; set; }
    public string Rubro { get; set; } = "";           // MOTO, VEHICULO, etc.
    public string CodigoReferencia { get; set; } = ""; // Ej: 59012167
    public string NombreModelo { get; set; } = "";

    public int? Cilindrada { get; set; }
    public string? Categoria { get; set; }
}

public class CrearModeloProductoRequestValidator : AbstractValidator<CrearModeloProductoRequest>
{
    public CrearModeloProductoRequestValidator()
    {
        RuleFor(x => x.IdMarca).GreaterThan(0);
        RuleFor(x => x.Rubro).NotEmpty().MaximumLength(50);
        RuleFor(x => x.CodigoReferencia).NotEmpty().MaximumLength(50);
        RuleFor(x => x.NombreModelo).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Categoria).MaximumLength(50);
    }
}
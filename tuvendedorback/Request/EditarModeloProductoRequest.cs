using FluentValidation;

namespace tuvendedorback.Request;

public class EditarModeloProductoRequest
{
    public int Id { get; set; }
    public int IdMarca { get; set; }
    public string NombreModelo { get; set; }
    public string? CodigoReferencia { get; set; }
}
public class EditarModeloProductoRequestValidator
    : AbstractValidator<EditarModeloProductoRequest>
{
    public EditarModeloProductoRequestValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.NombreModelo).NotEmpty();
    }
}
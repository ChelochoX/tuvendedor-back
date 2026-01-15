using FluentValidation;

namespace tuvendedorback.ERP.ERPRequest;

public class CrearERPClienteRequest
{
    public string TipoDocumento { get; set; } = null!;
    public string NumeroDocumento { get; set; } = null!;
    public string RazonSocial { get; set; } = null!;
    public string? NombreFantasia { get; set; }
    public string? Telefono { get; set; }
    public string? Email { get; set; }
    public int? CRMInteresadoId { get; set; }
}
public class CrearERPClienteRequestValidator : AbstractValidator<CrearERPClienteRequest>
{
    public CrearERPClienteRequestValidator()
    {
        RuleFor(x => x.TipoDocumento)
            .NotEmpty()
            .MaximumLength(20);

        RuleFor(x => x.NumeroDocumento)
            .NotEmpty()
            .MaximumLength(50);

        RuleFor(x => x.Telefono)
            .MaximumLength(50)
            .When(x => !string.IsNullOrWhiteSpace(x.Telefono));
    }
}
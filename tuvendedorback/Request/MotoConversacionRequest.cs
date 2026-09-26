using FluentValidation;

namespace tuvendedorback.Request;

public class MotoConversacionRequest
{
    public string Telefono { get; set; } = string.Empty;

    public string Mensaje { get; set; } = string.Empty;

    public int? IdPublicacion { get; set; }

    /// <summary>
    /// TEXTO, AUDIO, IMAGEN o DOCUMENTO.
    /// Para AUDIO el bridge de WhatsApp envía en Mensaje
    /// la transcripción obtenida localmente con Whisper.
    /// </summary>
    public string TipoMensaje { get; set; } = "TEXTO";

    /// <summary>
    /// Contenido base64 para IMAGEN o DOCUMENTO.
    /// </summary>
    public string? MediaBase64 { get; set; }

    public string? MediaMimeType { get; set; }

    public string? MediaNombre { get; set; }
}

public class MotoConversacionRequestValidator
    : AbstractValidator<MotoConversacionRequest>
{
    public MotoConversacionRequestValidator()
    {
        RuleFor(x => x.Telefono)
            .NotEmpty()
            .WithMessage("El identificador del contacto es obligatorio.")
            .MaximumLength(100)
            .WithMessage("El identificador del contacto no es válido.");

        RuleFor(x => x.TipoMensaje)
            .NotEmpty()
            .Must(tipo =>
                new[] { "TEXTO", "AUDIO", "IMAGEN", "DOCUMENTO" }
                    .Contains((tipo ?? string.Empty).Trim().ToUpperInvariant()))
            .WithMessage("El tipo de mensaje no es válido.");

        When(
            x =>
            {
                var tipo = (x.TipoMensaje ?? "TEXTO").Trim().ToUpperInvariant();
                return tipo == "TEXTO" || tipo == "AUDIO";
            },
            () =>
            {
                RuleFor(x => x.Mensaje)
                    .NotEmpty()
                    .WithMessage("El mensaje es obligatorio.")
                    .MaximumLength(6000)
                    .WithMessage("El mensaje supera la longitud permitida.");
            });

        When(
            x =>
            {
                var tipo = (x.TipoMensaje ?? string.Empty).Trim().ToUpperInvariant();
                return tipo == "IMAGEN" || tipo == "DOCUMENTO";
            },
            () =>
            {
                RuleFor(x => x.MediaBase64)
                    .NotEmpty()
                    .WithMessage("El archivo es obligatorio para este tipo de mensaje.");

                RuleFor(x => x.MediaMimeType)
                    .NotEmpty()
                    .WithMessage("El tipo MIME del archivo es obligatorio.");
            });

        RuleFor(x => x.Mensaje)
            .MaximumLength(6000)
            .WithMessage("El mensaje supera la longitud permitida.");

        When(
            x => x.IdPublicacion.HasValue,
            () =>
            {
                RuleFor(x => x.IdPublicacion!.Value)
                    .GreaterThan(0)
                    .WithMessage("El identificador de publicación no es válido.");
            });
    }
}

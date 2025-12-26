namespace tuvendedorback.Repositories.Interfaces;

public interface IPromptIARepository
{
    Task<string> ObtenerPromptActivo(string codigoPrompt);
}

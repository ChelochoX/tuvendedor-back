namespace tuvendedorback.Exceptions;

public class ReglasdeNegocioException : ApiException
{
    public ReglasdeNegocioException(string message) : base(message)
    {
    }

    public ReglasdeNegocioException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

namespace tuvendedorback.Exceptions;

public class RepositoryException : ApiException
{
    public RepositoryException(string message) : base(message)
    {

    }

    public RepositoryException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

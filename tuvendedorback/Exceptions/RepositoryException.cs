namespace tuvendedorback.Exceptions;

public class RepositoryException : ApiException
{
    public string? Marca { get; }
    public RepositoryException(string message) : base(message)
    {

    }

    public RepositoryException(string message, Exception? innerException) : base(message, innerException)
    {
    }

    public RepositoryException(string marca, string message, Exception? innerException = null)
      : base(message, innerException)
    {
        Marca = marca;
    }
}

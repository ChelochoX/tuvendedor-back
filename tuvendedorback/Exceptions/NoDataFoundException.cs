namespace tuvendedorback.Exceptions;

public class NoDataFoundException : ApiException
{
    public NoDataFoundException(string message) : base(message)
    {
    }
    public NoDataFoundException(string message, Exception innerException) : base(message, innerException)
    {
    }
}


namespace tuvendedorback.Exceptions;

public class CredencialesInvalidasException : ApiException
{
    public CredencialesInvalidasException(string message)
       : base(message) { }
}

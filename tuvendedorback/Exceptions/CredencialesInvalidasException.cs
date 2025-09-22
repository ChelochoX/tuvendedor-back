namespace tuvendedorback.Exceptions;

public class CredencialesInvalidasException : ApiException
{
    public CredencialesInvalidasException()
       : base("Credenciales inválidas.") { }
}

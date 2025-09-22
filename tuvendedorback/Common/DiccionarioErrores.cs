namespace tuvendedorback.Common;

public class DiccionarioErrores
{
    public static readonly Dictionary<string, string> ErroresPorModulo = new()
    {
        { "Error_Usuario_EmailDuplicado", "Ya existe un usuario con ese correo." },
        { "Error_Registro_Usuario_SQL", "No se pudo registrar el usuario por un problema en la base de datos." },
    };
}

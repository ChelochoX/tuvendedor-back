namespace tuvendedorback.Wrappers;

public class Datos<T>
{
    public T Items { get; set; }
    public int TotalRegistros { get; set; }

    public static implicit operator Datos<T>(string v)
    {
        throw new NotImplementedException();
    }
}

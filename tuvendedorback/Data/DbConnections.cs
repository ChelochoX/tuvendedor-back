using Microsoft.Data.SqlClient;
using System.Data;
using tuvendedorback.Exceptions;

namespace tuvendedorback.Data;

public class DbConnections
{
    private readonly IConfiguration _configuration;

    public DbConnections(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public IDbConnection CreateSqlConnection()
    {
        string sqlConnectionString = BuildConnectionString(_configuration, "ConexionDB");
        return new SqlConnection(sqlConnectionString);
    }

    private string BuildConnectionString(IConfiguration configuration, string name)
    {
        var connectionSettings = configuration.GetSection($"ConnectionStrings:{name}");

        if (connectionSettings == null || !connectionSettings.Exists())
        {
            throw new ParametroFaltanteCadenaConexionException($"Detalles de conexión para '{name}' no encontrados.");
        }

        var server = connectionSettings["Server"];
        var initialCatalog = connectionSettings["InitialCatalog"];
        var userId = connectionSettings["UserId"];
        var password = connectionSettings["Pwd"];
        var multipleActiveResultSets = connectionSettings.GetValue<bool?>("MultipleActiveResultSets");
        var pooling = connectionSettings.GetValue<bool?>("Pooling");
        var maxPoolSize = connectionSettings.GetValue<int?>("MaxPoolSize");
        var minPoolSize = connectionSettings.GetValue<int?>("MinPoolSize");
        var encrypt = connectionSettings.GetValue<bool?>("Encrypt");
        var trustServerCertificate = connectionSettings.GetValue<bool?>("TrustServerCertificate");

        if (string.IsNullOrEmpty(server) || string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(password) || string.IsNullOrEmpty(initialCatalog))
        {
            throw new ParametroFaltanteCadenaConexionException("Uno o más parámetros requeridos de la cadena de conexión son nulos o están vacíos.");
        }

        string dbUser = string.Empty;
        string db = initialCatalog;   

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = server,
            InitialCatalog = db,
            UserID = userId,
            Password = password
        };

        if (multipleActiveResultSets.HasValue) builder.MultipleActiveResultSets = multipleActiveResultSets.Value;
        if (pooling.HasValue) builder.Pooling = pooling.Value;
        if (maxPoolSize.HasValue) builder.MaxPoolSize = maxPoolSize.Value;
        if (minPoolSize.HasValue) builder.MinPoolSize = minPoolSize.Value;
        if (encrypt.HasValue) builder.Encrypt = encrypt.Value;
        if (trustServerCertificate.HasValue) builder.TrustServerCertificate = trustServerCertificate.Value;

        return builder.ConnectionString;
    }
}

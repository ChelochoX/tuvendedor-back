using Dapper;
using tuvendedorback.Data;
using tuvendedorback.Repositories.Interfaces;

namespace tuvendedorback.Repositories;

public class PromptIARepository : IPromptIARepository
{
    private readonly DbConnections _conexion;
    private readonly ILogger<PromptIARepository> _logger;

    public PromptIARepository(DbConnections conexion, ILogger<PromptIARepository> logger)
    {
        _conexion = conexion;
        _logger = logger;
    }

    public async Task<string> ObtenerPromptActivo(string codigoPrompt)
    {
        using var conn = _conexion.CreateSqlConnection();
        try
        {
            return await conn.ExecuteScalarAsync<string>(@"
                SELECT PromptBase
                FROM PromptsIA
                WHERE Codigo = @Codigo AND Activo = 1",
                new { Codigo = codigoPrompt });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error obteniendo prompt IA {Codigo}", codigoPrompt);
            throw;
        }
    }
}

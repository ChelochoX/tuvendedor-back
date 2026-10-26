using Dapper;
using tuvendedorback.Data;
using System.Data;

namespace tuvendedorback.Repositories;

/// <summary>Estado técnico de aclaración por URL: independiente del texto editable del prompt.</summary>
public sealed class PanambiEnlacePendienteRepository
{
    private readonly DbConnections _conexion;
    public PanambiEnlacePendienteRepository(DbConnections conexion) => _conexion = conexion;

    public async Task MarcarPendiente(int idConversacion)
    {
        using var conn = _conexion.CreateSqlConnection();
        conn.Open();
        using var tx = conn.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            await conn.ExecuteAsync(@"
UPDATE dbo.PanambiEnlacePendiente WITH (UPDLOCK, HOLDLOCK)
SET ExpiraUtc = DATEADD(HOUR, 24, SYSUTCDATETIME()), ActualizadoUtc = SYSUTCDATETIME()
WHERE IdConversacion = @id;
IF @@ROWCOUNT = 0
 INSERT INTO dbo.PanambiEnlacePendiente (IdConversacion, ExpiraUtc, ActualizadoUtc)
 VALUES (@id, DATEADD(HOUR, 24, SYSUTCDATETIME()), SYSUTCDATETIME());", new { id = idConversacion }, tx);
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    public async Task<bool> EstaPendiente(int idConversacion)
    {
        using var conn = _conexion.CreateSqlConnection();
        return await conn.ExecuteScalarAsync<int>(@"
SELECT COUNT(1) FROM dbo.PanambiEnlacePendiente
WHERE IdConversacion = @id AND ExpiraUtc > SYSUTCDATETIME();",
            new { id = idConversacion }) > 0;
    }

    public async Task LimpiarPendiente(int idConversacion)
    {
        using var conn = _conexion.CreateSqlConnection();
        await conn.ExecuteAsync("DELETE FROM dbo.PanambiEnlacePendiente WHERE IdConversacion=@id;",
            new { id = idConversacion });
    }
}

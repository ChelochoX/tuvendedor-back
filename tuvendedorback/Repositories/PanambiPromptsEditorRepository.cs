using Dapper;
using tuvendedorback.Data;
using System.Data;

namespace tuvendedorback.Repositories;

/// <summary>Versión publicada de los prompts editoriales. No modifica la conexión SQL existente.</summary>
public sealed class PanambiPromptsEditorRepository
{
    private readonly DbConnections _conexion;
    public PanambiPromptsEditorRepository(DbConnections conexion) => _conexion = conexion;

    public sealed class VersionPrompt
    {
        public long Id { get; set; }
        public string Codigo { get; set; } = "";
        public int Version { get; set; }
        public string Contenido { get; set; } = "";
        public string Estado { get; set; } = "";
        public DateTime FechaCreacionUtc { get; set; }
        public int? IdUsuario { get; set; }
    }

    public async Task<string?> ObtenerPublicado(string codigo)
    {
        using var conn = _conexion.CreateSqlConnection();
        return await conn.QueryFirstOrDefaultAsync<string?>(@"
SELECT TOP (1) Contenido FROM dbo.PanambiPromptsEditor
WHERE Codigo = @codigo AND Estado = 'PUBLICADO'
ORDER BY Version DESC;", new { codigo });
    }

    /// <summary>Plantilla publicada obligatoria. Nunca emplea textos de respuesta incrustados en C#.</summary>
    public async Task<string> ObtenerObligatorio(string codigo)
    {
        var contenido = await ObtenerPublicado(codigo);
        if (string.IsNullOrWhiteSpace(contenido))
            throw new InvalidOperationException($"No existe una versión publicada de la plantilla {codigo}. Ejecutá la migración SQL y publicala desde Prompts de Panambí.");
        return contenido;
    }

    public sealed class PlantillaCatalogo
    {
        public string Codigo { get; set; } = "";
        public string Titulo { get; set; } = "";
        public string Descripcion { get; set; } = "";
        public string Categoria { get; set; } = "";
    }

    public async Task<IReadOnlyList<PlantillaCatalogo>> ListarCatalogo()
    {
        using var conn = _conexion.CreateSqlConnection();
        var items = await conn.QueryAsync<PlantillaCatalogo>(@"
SELECT Codigo, Titulo, Descripcion, Categoria
FROM dbo.PanambiPlantillasCatalogo
WHERE Activo = 1
ORDER BY Categoria, Titulo, Codigo;");
        return items.ToList();
    }

    public async Task<IReadOnlyList<VersionPrompt>> ListarPublicados()
    {
        using var conn = _conexion.CreateSqlConnection();
        var items = await conn.QueryAsync<VersionPrompt>(@"
SELECT Id, Codigo, Version, Contenido, Estado, FechaCreacionUtc, IdUsuario
FROM dbo.PanambiPromptsEditor WHERE Estado = 'PUBLICADO' ORDER BY Codigo;");
        return items.ToList();
    }

    public async Task<IReadOnlyList<VersionPrompt>> Historial(string codigo)
    {
        using var conn = _conexion.CreateSqlConnection();
        var items = await conn.QueryAsync<VersionPrompt>(@"
SELECT TOP (30) Id, Codigo, Version, Contenido, Estado, FechaCreacionUtc, IdUsuario
FROM dbo.PanambiPromptsEditor WHERE Codigo = @codigo ORDER BY Version DESC;", new { codigo });
        return items.ToList();
    }

    public async Task<IReadOnlyList<VersionPrompt>> ListarLegados()
    {
        using var conn = _conexion.CreateSqlConnection();
        // Tabla y campos ya utilizados en el sistema de ofertas actual.
        var items = await conn.QueryAsync<VersionPrompt>(@"
SELECT CAST(Id AS bigint) AS Id, Codigo, CAST(0 AS int) AS Version,
       PromptBase AS Contenido, CAST('LEGADO' AS varchar(20)) AS Estado,
       CAST('20000101' AS datetime2) AS FechaCreacionUtc,
       CAST(NULL AS int) AS IdUsuario
FROM dbo.PromptsIA
WHERE Activo = 1 AND Id IN
 (SELECT MAX(Id) FROM dbo.PromptsIA WHERE Activo = 1 GROUP BY Codigo)
ORDER BY Codigo;");
        return items.ToList();
    }

    public async Task<VersionPrompt> Publicar(string codigo, string contenido, int idUsuario, int? versionEsperada)
    {
        using var conn = _conexion.CreateSqlConnection();
        conn.Open();
        using var tx = conn.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            var versionActual = await conn.ExecuteScalarAsync<int>(@"
SELECT ISNULL(MAX(Version),0) FROM dbo.PanambiPromptsEditor WITH (UPDLOCK,HOLDLOCK)
WHERE Codigo = @codigo;", new { codigo }, tx);
            if (versionEsperada.HasValue && versionActual != versionEsperada.Value)
                throw new InvalidOperationException("Otro administrador modificó este prompt. Actualizá la pantalla antes de publicar.");
            await conn.ExecuteAsync(@"
UPDATE dbo.PanambiPromptsEditor SET Estado = 'ARCHIVADO'
WHERE Codigo = @codigo AND Estado = 'PUBLICADO';", new { codigo }, tx);
            var nuevo = await conn.QuerySingleAsync<VersionPrompt>(@"
INSERT INTO dbo.PanambiPromptsEditor
 (Codigo, Version, Contenido, Estado, FechaCreacionUtc, IdUsuario)
OUTPUT INSERTED.Id, INSERTED.Codigo, INSERTED.Version, INSERTED.Contenido,
 INSERTED.Estado, INSERTED.FechaCreacionUtc, INSERTED.IdUsuario
VALUES (@codigo, @version, @contenido, 'PUBLICADO', SYSUTCDATETIME(), @idUsuario);",
                new { codigo, version = versionActual + 1, contenido, idUsuario }, tx);
            tx.Commit();
            return nuevo;
        }
        catch { tx.Rollback(); throw; }
    }

    public async Task<VersionPrompt?> ObtenerVersion(string codigo, int version)
    {
        using var conn = _conexion.CreateSqlConnection();
        return await conn.QueryFirstOrDefaultAsync<VersionPrompt>(@"
SELECT TOP (1) Id, Codigo, Version, Contenido, Estado, FechaCreacionUtc, IdUsuario
FROM dbo.PanambiPromptsEditor WHERE Codigo=@codigo AND Version=@version;",
            new { codigo, version });
    }
}

using Dapper;
using tuvendedorback.Data;

namespace tuvendedorback.Prospeccion;

public sealed class ProspeccionRepository
{
    public sealed class Conteos
    {
        public int Nuevos { get; set; }
        public int EnRevision { get; set; }
        public int Contactados { get; set; }
        public int TotalUltimas24Horas { get; set; }
    }
    public sealed class UltimaSync
    {
        public DateTime? FechaFinUtc { get; set; }
        public bool Exito { get; set; }
        public string? ErrorResumen { get; set; }
    }

    private readonly DbConnections _db;
    public ProspeccionRepository(DbConnections db) => _db = db;

    public async Task<ProspectoListadoDto> ListarAsync(string rubro, string? estado, string? q, int pagina, int porPagina)
    {
        using var conn = _db.CreateSqlConnection();
        const string filtro = @"
            WHERE Rubro = @Rubro
              AND (@Estado IS NULL OR EstadoGestion = @Estado)
              AND (@Busqueda IS NULL OR SolicitudCompra LIKE @Busqueda OR Distrito LIKE @Busqueda OR Departamento LIKE @Busqueda OR Fuente LIKE @Busqueda)";
        var p = new { Rubro = rubro, Estado = estado, Busqueda = string.IsNullOrWhiteSpace(q) ? null : "%" + q.Trim()[..Math.Min(120, q.Trim().Length)] + "%", Offset = (pagina - 1) * porPagina, PorPagina = porPagina };
        var items = await conn.QueryAsync<ProspectoDto>(@"
            SELECT Id,Rubro,SolicitudCompra,Distrito,Departamento,PresupuestoConfirmadoGs,ImporteMostradoGs,
                   FechaDeteccionOriginal,FechaPublicacionOriginal,Fuente,UrlOriginal,Confianza,MotivoConfianza,
                   Prioridad,EstadoFuente,Observaciones,ArchivoOrigen,UrlPlanilla,EstadoGestion,NotasGestion,
                   FechaPrimerRegistroUtc,FechaUltimaDeteccionUtc,FechaGestionUtc
            FROM dbo.ProspectosInvestigados " + filtro + @"
            ORDER BY CASE WHEN Prioridad = N'Alta' THEN 0 WHEN Prioridad = N'Media' THEN 1 ELSE 2 END,
                     FechaPrimerRegistroUtc DESC, Id DESC
            OFFSET @Offset ROWS FETCH NEXT @PorPagina ROWS ONLY", p);
        var total = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM dbo.ProspectosInvestigados " + filtro, p);
        var counts = await conn.QuerySingleAsync<Conteos>(@"
            SELECT
              ISNULL(SUM(CASE WHEN EstadoGestion='NUEVO' THEN 1 ELSE 0 END),0) AS Nuevos,
              ISNULL(SUM(CASE WHEN EstadoGestion='EN_REVISION' THEN 1 ELSE 0 END),0) AS EnRevision,
              ISNULL(SUM(CASE WHEN EstadoGestion='CONTACTADO' THEN 1 ELSE 0 END),0) AS Contactados,
              ISNULL(SUM(CASE WHEN FechaPrimerRegistroUtc >= DATEADD(HOUR,-24,SYSUTCDATETIME()) THEN 1 ELSE 0 END),0) AS TotalUltimas24Horas
            FROM dbo.ProspectosInvestigados WHERE Rubro=@Rubro", new { Rubro = rubro });
        var sync = await conn.QueryFirstOrDefaultAsync<UltimaSync>(@"
            SELECT TOP (1) FechaFinUtc,Exito,ErrorResumen
            FROM dbo.ProspeccionSincronizaciones ORDER BY Id DESC");
        return new ProspectoListadoDto
        {
            Items = items.ToList(),
            Total = total,
            Nuevos = counts.Nuevos,
            EnRevision = counts.EnRevision,
            Contactados = counts.Contactados,
            TotalUltimas24Horas = counts.TotalUltimas24Horas,
            UltimaSincronizacionUtc = sync?.FechaFinUtc,
            UltimaSincronizacionExitosa = sync?.Exito ?? false,
            UltimoError = sync?.Exito == true ? null : sync?.ErrorResumen,
            Pagina = pagina,
            PorPagina = porPagina
        };
    }

    public async Task<bool> CambiarGestionAsync(long id, string rubro, string estado, string? notas)
    {
        using var conn = _db.CreateSqlConnection();
        return await conn.ExecuteAsync(@"
            UPDATE dbo.ProspectosInvestigados SET EstadoGestion=@Estado,
                NotasGestion=@Notas, FechaGestionUtc=SYSUTCDATETIME()
            WHERE Id=@Id AND Rubro=@Rubro", new { Id = id, Rubro = rubro, Estado = estado, Notas = notas }) > 0;
    }

    public async Task<long> IniciarSincronizacionAsync()
    {
        using var conn = _db.CreateSqlConnection();
        return await conn.ExecuteScalarAsync<long>(@"
            INSERT dbo.ProspeccionSincronizaciones(FechaInicioUtc) OUTPUT INSERTED.Id
            VALUES(SYSUTCDATETIME())");
    }

    public async Task FinalizarSincronizacionAsync(long id, ResultadoSincronizacion? r, string? error)
    {
        using var conn = _db.CreateSqlConnection();
        await conn.ExecuteAsync(@"
            UPDATE dbo.ProspeccionSincronizaciones SET
                FechaFinUtc=SYSUTCDATETIME(),Exito=@Exito,Planillas=@Planillas,
                FilasLeidas=@FilasLeidas,FilasNuevas=@FilasNuevas,
                FilasRepetidas=@FilasRepetidas,FilasOmitidas=@FilasOmitidas,ErrorResumen=@Error
            WHERE Id=@Id", new
        {
            Id = id,
            Exito = error == null,
            Planillas = r?.Planillas ?? 0,
            FilasLeidas = r?.FilasLeidas ?? 0,
            FilasNuevas = r?.FilasNuevas ?? 0,
            FilasRepetidas = r?.FilasRepetidas ?? 0,
            FilasOmitidas = r?.FilasOmitidas ?? 0,
            Error = error == null ? null : error[..Math.Min(500, error.Length)]
        });
    }

    internal async Task<(int nuevos, int repetidos)> GuardarLoteAsync(IReadOnlyList<ProspectoImportado> prospectos, CancellationToken ct)
    {
        using var conn = _db.CreateSqlConnection();
        conn.Open();
        using var tx = conn.BeginTransaction();
        var nuevos = 0;
        try
        {
            foreach (var prospecto in prospectos)
            {
                ct.ThrowIfCancellationRequested();
                // MERGE serializado por clave compuesta; actualiza datos encontrados SIN borrar estado o notas comerciales.
                var insertado = await conn.ExecuteScalarAsync<int>(new CommandDefinition(@"
                    MERGE dbo.ProspectosInvestigados WITH (HOLDLOCK) AS t
                    USING (SELECT @Rubro AS Rubro, @ClaveFuente AS ClaveFuente) AS s
                    ON t.Rubro=s.Rubro AND t.ClaveFuente=s.ClaveFuente
                    WHEN MATCHED THEN UPDATE SET
                      IdFuente=@IdFuente,SolicitudCompra=@SolicitudCompra,Distrito=@Distrito,
                      Departamento=@Departamento,PresupuestoConfirmadoGs=@PresupuestoConfirmadoGs,
                      ImporteMostradoGs=@ImporteMostradoGs,FechaDeteccionOriginal=@FechaDeteccionOriginal,
                      FechaPublicacionOriginal=@FechaPublicacionOriginal,Fuente=@Fuente,UrlOriginal=@UrlOriginal,
                      Confianza=@Confianza,MotivoConfianza=@MotivoConfianza,Prioridad=@Prioridad,
                      EstadoFuente=@EstadoFuente,Observaciones=@Observaciones,
                      ArchivoOrigen=@ArchivoOrigen,UrlPlanilla=@UrlPlanilla,
                      FechaUltimaDeteccionUtc=SYSUTCDATETIME()
                    WHEN NOT MATCHED BY TARGET THEN INSERT
                      (Rubro,ClaveFuente,IdFuente,SolicitudCompra,Distrito,Departamento,PresupuestoConfirmadoGs,
                      ImporteMostradoGs,FechaDeteccionOriginal,FechaPublicacionOriginal,Fuente,UrlOriginal,
                      Confianza,MotivoConfianza,Prioridad,EstadoFuente,Observaciones,ArchivoOrigen,UrlPlanilla)
                    VALUES
                      (@Rubro,@ClaveFuente,@IdFuente,@SolicitudCompra,@Distrito,@Departamento,@PresupuestoConfirmadoGs,
                      @ImporteMostradoGs,@FechaDeteccionOriginal,@FechaPublicacionOriginal,@Fuente,@UrlOriginal,
                      @Confianza,@MotivoConfianza,@Prioridad,@EstadoFuente,@Observaciones,@ArchivoOrigen,@UrlPlanilla)
                    OUTPUT CASE WHEN $action='INSERT' THEN 1 ELSE 0 END;
                ", prospecto, tx, cancellationToken: ct, commandTimeout: 60));
                nuevos += insertado;
            }
            tx.Commit();
            return (nuevos, prospectos.Count - nuevos);
        }
        catch { tx.Rollback(); throw; }
    }
}

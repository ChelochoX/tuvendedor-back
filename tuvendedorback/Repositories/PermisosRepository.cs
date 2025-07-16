using Dapper;
using System.Data;
using tuvendedorback.Exceptions;
using tuvendedorback.Repositories.Interfaces;

namespace tuvendedorback.Repositories;

public class PermisosRepository : IPermisosRepository
{
    private readonly IDbConnection _conexion;
    private readonly ILogger<PermisosRepository> _logger;

    public PermisosRepository(ILogger<PermisosRepository> logger, IDbConnection conexion)
    {
        _logger = logger;
        _conexion = conexion;
    }

    public async Task<bool> TienePermiso(int idUsuario, string entidad, string recurso)
    {
        _logger.LogInformation("Verificando si el usuario {IdUsuario} tiene permiso '{Recurso}' sobre '{Entidad}'", idUsuario, recurso, entidad);

        const string query = @"
        SELECT 1
        FROM Permisos p
        INNER JOIN Roles r ON p.IdRol = r.Id
        INNER JOIN UsuarioRoles ur ON ur.IdRol = r.Id
        INNER JOIN Entidades e ON e.Id = p.IdEntidad
        INNER JOIN Recursos re ON re.Id = p.IdRecurso
        WHERE ur.IdUsuario = @IdUsuario
          AND e.NombreEntidad = @Entidad
          AND re.NombreRecurso = @Recurso;";

        try
        {
            var result = await _conexion.ExecuteScalarAsync<int?>(query, new
            {
                IdUsuario = idUsuario,
                Entidad = entidad,
                Recurso = recurso
            });

            var tienePermiso = result.HasValue;

            if (tienePermiso)
                _logger.LogInformation("Permiso concedido al usuario {IdUsuario} para '{Recurso}' sobre '{Entidad}'", idUsuario, recurso, entidad);
            else
                _logger.LogWarning("Permiso denegado al usuario {IdUsuario} para '{Recurso}' sobre '{Entidad}'", idUsuario, recurso, entidad);

            return tienePermiso;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al verificar permisos para usuario {IdUsuario}", idUsuario);
            throw new RepositoryException("Error al verificar permisos del usuario", ex);
        }
    }

}

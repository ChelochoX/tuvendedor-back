using Dapper;
using Microsoft.AspNetCore.Identity;
using System.Data;
using tuvendedorback.Exceptions;
using tuvendedorback.Models;
using tuvendedorback.Repositories.Interfaces;
using tuvendedorback.Request;

namespace tuvendedorback.Repositories;

public class UsuariosRepository : IUsuariosRepository
{
    private readonly IDbConnection _conexion;
    private readonly ILogger<UsuariosRepository> _logger;
    public readonly PasswordHasher<string> _hasher;

    public UsuariosRepository(IDbConnection conexion, ILogger<UsuariosRepository> logger)
    {
        _conexion = conexion;
        _logger = logger;
        _hasher = new PasswordHasher<string>();
    }

    public async Task<Usuario?> ValidarCredenciales(LoginRequest request)
    {
        try
        {
            _logger.LogInformation("Validando credenciales para el email: {Email}", request.Email);

            const string query = @"SELECT Id AS IdUsuario, NombreUsuario, Email, ClaveHash, Estado, FechaRegistro 
                               FROM Usuarios 
                               WHERE Email = @Email AND Estado = 'Activo'";

            var usuarioDb = await _conexion.QueryFirstOrDefaultAsync<Usuario>(query, new { Email = request.Email });

            if (usuarioDb == null)
            {
                _logger.LogWarning("No se encontró usuario con email: {Email}", request.Email);
                return null;
            }

            var resultado = _hasher.VerifyHashedPassword(null, usuarioDb.ClaveHash, request.Clave);
            if (resultado == PasswordVerificationResult.Success)
            {
                _logger.LogInformation("Autenticación exitosa para el usuario con email: {Email}", request.Email);
                return usuarioDb;
            }

            _logger.LogWarning("Contraseña incorrecta para el usuario con email: {Email}", request.Email);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al validar credenciales para el email: {Email}", request.Email);
            throw new RepositoryException("Error al validar credenciales", ex);
        }
    }


    public async Task<List<string>> ObtenerRolesPorUsuario(int idUsuario)
    {
        const string query = @"
        SELECT R.NombreRol
        FROM UsuarioRoles UR
        INNER JOIN Roles R ON UR.IdRol = R.Id
        WHERE UR.IdUsuario = @IdUsuario
        ";

        _logger.LogInformation("Iniciando consulta de roles para el usuario con ID: {IdUsuario}", idUsuario);

        try
        {
            var roles = (await _conexion.QueryAsync<string>(query, new { IdUsuario = idUsuario })).ToList();

            if (!roles.Any())
            {
                _logger.LogWarning("No se encontraron roles asignados al usuario con ID: {IdUsuario}", idUsuario);
            }
            else
            {
                _logger.LogInformation("Se encontraron {Cantidad} roles para el usuario con ID: {IdUsuario}: {Roles}",
                    roles.Count, idUsuario, string.Join(", ", roles));
            }

            return roles;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener los roles del usuario con ID: {IdUsuario}", idUsuario);
            throw new RepositoryException("No se pudieron obtener los roles del usuario.", ex);
        }
    }

    public async Task<int> InsertarUsuarioConRol(RegistroRequest request)
    {
        _logger.LogInformation("Iniciando registro de nuevo usuario: {NombreUsuario}, Email: {Email}", request.NombreUsuario, request.Email);

        using var transaction = _conexion.BeginTransaction();

        try
        {
            var claveHash = _hasher.HashPassword(null, request.Clave);

            _logger.LogDebug("Clave hasheada correctamente para el usuario: {NombreUsuario}", request.NombreUsuario);

            const string insertUsuario = @"
            INSERT INTO Usuarios (NombreUsuario, Email, ClaveHash, Estado, FechaRegistro)
            VALUES (@NombreUsuario, @Email, @ClaveHash, 'Activo', GETDATE());
            SELECT SCOPE_IDENTITY();
            ";

            var idUsuario = await _conexion.ExecuteScalarAsync<int>(insertUsuario, new
            {
                request.NombreUsuario,
                request.Email,
                ClaveHash = claveHash
            }, transaction);

            _logger.LogInformation("Usuario insertado correctamente en la base de datos con ID: {IdUsuario}", idUsuario);

            const string insertRol = @"
            INSERT INTO UsuarioRoles (IdUsuario, IdRol)
            VALUES (@IdUsuario, @IdRol);
            ";

            await _conexion.ExecuteAsync(insertRol, new
            {
                IdUsuario = idUsuario,
                IdRol = request.IdRol
            }, transaction);

            _logger.LogInformation("Rol (ID: {IdRol}) asignado al usuario (ID: {IdUsuario})", request.IdRol, idUsuario);

            transaction.Commit();

            _logger.LogInformation("Transacción completada con éxito para el usuario {NombreUsuario}", request.NombreUsuario);

            return idUsuario;
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            _logger.LogError(ex, "Error al registrar el usuario {NombreUsuario}, se realizó rollback", request.NombreUsuario);
            throw new RepositoryException("Error al registrar el usuario", ex);
        }
    }

    public async Task<bool> ActualizarClaveUsuario(int idUsuario, string nuevaClaveHash)
    {
        _logger.LogInformation("Intentando actualizar la clave para el usuario ID: {IdUsuario}", idUsuario);

        const string query = @"UPDATE Usuarios SET ClaveHash = @ClaveHash WHERE Id = @IdUsuario";

        try
        {
            var filas = await _conexion.ExecuteAsync(query, new { ClaveHash = nuevaClaveHash, IdUsuario = idUsuario });

            if (filas > 0)
            {
                _logger.LogInformation("Clave actualizada correctamente para el usuario ID: {IdUsuario}", idUsuario);
                return true;
            }
            else
            {
                _logger.LogWarning("No se actualizó ninguna fila al intentar cambiar la clave del usuario ID: {IdUsuario}", idUsuario);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al intentar actualizar la clave del usuario ID: {IdUsuario}", idUsuario);
            throw new RepositoryException("Error al actualizar la clave del usuario", ex);
        }
    }

    public async Task<Usuario?> ObtenerUsuarioPorEmail(string email)
    {
        _logger.LogInformation("Buscando usuario por email: {Email}", email);

        const string query = @"SELECT Id AS IdUsuario, NombreUsuario, Email, ClaveHash, Estado, FechaRegistro
                           FROM Usuarios WHERE Email = @Email";

        try
        {
            var usuario = await _conexion.QueryFirstOrDefaultAsync<Usuario>(query, new { Email = email });

            if (usuario == null)
                _logger.LogWarning("No se encontró ningún usuario con email: {Email}", email);
            else
                _logger.LogInformation("Usuario encontrado con ID: {IdUsuario}", usuario.Id);

            return usuario;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al buscar usuario con email: {Email}", email);
            throw new RepositoryException("Error al obtener usuario por email", ex);
        }
    }

    public async Task<Usuario?> ObtenerUsuarioActivoPorId(int id)
    {
        _logger.LogInformation("Buscando usuario activo por ID: {Id}", id);

        const string query = @"
        SELECT Id AS IdUsuario, NombreUsuario, Email, Estado
        FROM Usuarios
        WHERE Id = @Id AND Estado = 'Activo'";

        try
        {
            var usuario = await _conexion.QueryFirstOrDefaultAsync<Usuario>(query, new { Id = id });

            if (usuario == null)
                _logger.LogWarning("No se encontró usuario activo con ID: {Id}", id);
            else
                _logger.LogInformation("Usuario activo encontrado: {IdUsuario} - {NombreUsuario}", usuario.Id, usuario.NombreUsuario);

            return usuario;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener usuario activo con ID: {Id}", id);
            throw new RepositoryException("Error al obtener usuario activo por ID", ex);
        }
    }


}


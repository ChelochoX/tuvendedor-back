using Dapper;
using Microsoft.AspNetCore.Identity;
using tuvendedorback.Data;
using tuvendedorback.Exceptions;
using tuvendedorback.Models;
using tuvendedorback.Repositories.Interfaces;
using tuvendedorback.Request;

namespace tuvendedorback.Repositories;

public class UsuariosRepository : IUsuariosRepository
{
    private readonly DbConnections _conexion;
    private readonly ILogger<UsuariosRepository> _logger;
    public readonly PasswordHasher<string> _hasher;

    public UsuariosRepository(DbConnections conexion, ILogger<UsuariosRepository> logger)
    {
        _conexion = conexion;
        _logger = logger;
        _hasher = new PasswordHasher<string>();
    }

    public async Task<Usuario?> ValidarCredencialesPorEmailYClave(string email, string clave)
    {
        try
        {
            _logger.LogInformation("Validando credenciales para el email: {Email}", email);

            const string query = @"SELECT Id AS IdUsuario, NombreUsuario, Email, ClaveHash, Estado, FechaRegistro 
                               FROM Usuarios 
                               WHERE Email = @Email AND Estado = 'Activo'";

            using var connection = _conexion.CreateSqlConnection();
            var usuarioDb = await connection.QueryFirstOrDefaultAsync<Usuario>(query, new { Email = email });

            if (usuarioDb == null)
            {
                _logger.LogWarning("No se encontró usuario con email: {Email}", email);
                return null;
            }

            var resultado = _hasher.VerifyHashedPassword(null, usuarioDb.ClaveHash, clave);
            if (resultado == PasswordVerificationResult.Success)
            {
                _logger.LogInformation("Autenticación exitosa para el usuario con email: {Email}", email);
                return usuarioDb;
            }

            _logger.LogWarning("Contraseña incorrecta para el usuario con email: {Email}", email);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al validar credenciales para el email: {Email}", email);
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
            using var connection = _conexion.CreateSqlConnection();
            var roles = (await connection.QueryAsync<string>(query, new { IdUsuario = idUsuario })).ToList();

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
        using var connection = _conexion.CreateSqlConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        try
        {
            var esLoginExterno = !string.IsNullOrEmpty(request.Proveedor);

            var claveHash = esLoginExterno
                ? null
                : _hasher.HashPassword(null, request.Clave);

            const string insertUsuario = @"
                INSERT INTO Usuarios 
                (NombreUsuario, Email, ClaveHash, Estado, FechaRegistro, Proveedor, ProveedorId, Telefono, Ciudad, Direccion, FotoPerfil)
                VALUES 
                (@NombreUsuario, @Email, @ClaveHash, 'Activo', GETDATE(), @Proveedor, @ProveedorId, @Telefono, @Ciudad, @Direccion, @FotoPerfil);
                SELECT SCOPE_IDENTITY();";

            var idUsuario = await connection.ExecuteScalarAsync<int>(insertUsuario, new
            {
                request.NombreUsuario,
                request.Email,
                ClaveHash = claveHash,
                request.Proveedor,
                request.ProveedorId,
                request.Telefono,
                request.Ciudad,
                request.Direccion,
                request.FotoPerfil
            }, transaction);

            // Rol comprador (siempre)
            await connection.ExecuteAsync("INSERT INTO UsuarioRoles (IdUsuario, IdRol) VALUES (@IdUsuario, 2);", new
            {
                IdUsuario = idUsuario
            }, transaction);

            // Rol vendedor (opcional)
            if (request.EsVendedor)
            {
                await connection.ExecuteAsync("INSERT INTO UsuarioRoles (IdUsuario, IdRol) VALUES (@IdUsuario, 3);", new
                {
                    IdUsuario = idUsuario
                }, transaction);

                // Registrar detalles del vendedor si tenés una tabla aparte como `Vendedores`
                await connection.ExecuteAsync(@"
                INSERT INTO Vendedores (IdUsuario, NombreNegocio, Ruc, Rubro)
                VALUES (@IdUsuario, @NombreNegocio, @Ruc, @Rubro);", new
                {
                    IdUsuario = idUsuario,
                    request.NombreNegocio,
                    request.Ruc,
                    request.Rubro
                }, transaction);
            }

            transaction.Commit();
            return idUsuario;
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            _logger.LogError(ex, "Error al registrar el usuario");
            throw new RepositoryException("Error al registrar el usuario", ex);
        }
    }

    public async Task<bool> ActualizarClaveUsuario(int idUsuario, string nuevaClaveHash)
    {
        _logger.LogInformation("Intentando actualizar la clave para el usuario ID: {IdUsuario}", idUsuario);

        const string query = @"UPDATE Usuarios SET ClaveHash = @ClaveHash WHERE Id = @IdUsuario";

        try
        {
            using var connection = _conexion.CreateSqlConnection();
            var filas = await connection.ExecuteAsync(query, new { ClaveHash = nuevaClaveHash, IdUsuario = idUsuario });

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

    public async Task<Usuario?> ObtenerUsuarioPorProveedor(LoginRequest request)
    {
        _logger.LogInformation("Buscando usuario externo: {Email} - Proveedor: {Proveedor} - ID: {ProveedorId}",
                                request.Email, request.TipoLogin, request.ProveedorId);

        const string query = @"
        SELECT Id AS IdUsuario, NombreUsuario, Email, ClaveHash, Estado, FechaRegistro, Proveedor, ProveedorId, FotoPerfil
        FROM Usuarios
        WHERE Email = @Email AND Proveedor = @Proveedor AND ProveedorId = @ProveedorId";

        try
        {
            using var connection = _conexion.CreateSqlConnection();
            var usuario = await connection.QueryFirstOrDefaultAsync<Usuario>(query, new
            {
                Email = request.Email,
                Proveedor = request.TipoLogin,
                request.ProveedorId
            });

            if (usuario == null)
                _logger.LogWarning("No se encontró usuario externo con email: {Email}", request.Email);
            else
                _logger.LogInformation("Usuario encontrado con ID: {IdUsuario}", usuario.Id);

            return usuario;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al buscar usuario por proveedor");
            throw new RepositoryException("Error al obtener usuario por proveedor", ex);
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
            using var connection = _conexion.CreateSqlConnection();
            var usuario = await connection.QueryFirstOrDefaultAsync<Usuario>(query, new { Id = id });

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


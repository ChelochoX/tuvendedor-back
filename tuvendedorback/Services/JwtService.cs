using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace tuvendedorback.Services;

public class JwtService
{
    private readonly SymmetricSecurityKey _key;
    private readonly string _issuer;
    private readonly string _audience;

    public JwtService(IConfiguration config)
    {
        var secretKey = config["Jwt:Key"];
        _issuer = config["Jwt:Issuer"];
        _audience = config["Jwt:Audience"];

        if (string.IsNullOrEmpty(secretKey)) throw new Exception("Falta clave JWT");
        _key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
    }

    public string GenerarToken(int idUsuario, string nombreUsuario, IEnumerable<string> roles)
    {
        var claims = new List<Claim>
            {
                new Claim("id_usuario", idUsuario.ToString()),
                new Claim("nombre_usuario", nombreUsuario)
            };

        foreach (var rol in roles)
            claims.Add(new Claim("rol", rol));

        var creds = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(12),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
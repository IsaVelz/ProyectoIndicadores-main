using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace PIndicadores.Services
{
    // Servicio para generar tokens JWT de autenticación.
    public class TokenService
    {
        private readonly string _key;
        private readonly string _issuer;
        private readonly string _audience;

        // Constructor que recibe configuración de claves JWT.
        public TokenService(IConfiguration configuration)
        {
            _key = configuration["Jwt:Key"] ?? throw new ArgumentNullException(nameof(_key));
            _issuer = configuration["Jwt:Issuer"] ?? throw new ArgumentNullException(nameof(_issuer));
            _audience = configuration["Jwt:Audience"] ?? throw new ArgumentNullException(nameof(_audience));
        }

        // Genera un token JWT para un usuario con múltiples roles.
        // Parametro "email">Correo electrónico del usuario
        // Parametro "roles">Lista de roles asignados al usuario
        // Retorna un Token JWT en formato string
        public string GenerateToken(string email, List<string> roles)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var keyBytes = Encoding.ASCII.GetBytes(_key);

            // Crear lista de claims
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, email) // Claim del email
            };

            // Agregar un claim por cada rol
            foreach (var rol in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, rol)); // Claim del rol
            }

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddHours(1), // Duración del token
                Issuer = _issuer,
                Audience = _audience,
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(keyBytes),
                    SecurityAlgorithms.HmacSha256Signature
                )
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }
    }
}
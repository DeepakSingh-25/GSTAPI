using Gst.Application.Interfaces.IService;
using Gst.Domain.Configurations;
using Gst.Domain.Constants;
using Gst.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;

namespace Gst.Persistence.Service
{
    public class JwtTokenService(IOptions<JwtConfig> jwtOptions) : IJwtTokenService
    {
        private readonly JwtConfig _jwt = jwtOptions.Value;

        public (string Token, DateTime ExpiresOn) CreateToken(User user)
        {
            var expiresOn = DateTime.UtcNow.AddMinutes(_jwt.Expiry);
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.Key));

            var descriptor = new SecurityTokenDescriptor
            {
                Issuer = _jwt.Issuer,
                Audience = _jwt.Audience,
                Expires = expiresOn,
                SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
                Subject = new ClaimsIdentity(
                [
                    new Claim(AppClaims.UserId, user.Id.ToString()),
                    new Claim(AppClaims.Email, user.Email),
                    new Claim(AppClaims.Name, user.FullName),
                    new Claim(AppClaims.Role, user.Role)
                ])
            };

            return (new JsonWebTokenHandler().CreateToken(descriptor), expiresOn);
        }
    }
}

using Gst.Application.Common;
using Gst.Application.Interfaces.IRepository;
using Gst.Application.Interfaces.IService;
using Gst.Domain.Configurations;
using Gst.Domain.Constants;
using Gst.Domain.Entities;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;

namespace Gst.Api.Services
{
    // Session = JWT access + refresh token. Cookie likhna ASP.NET ka kaam hai, isliye Api layer mein.
    public class AuthSessionService(
        IHttpContextAccessor httpContextAccessor,
        IJwtTokenService tokens,
        IRefreshTokenRepository refreshTokens,
        IOptions<AuthConfig> authOptions,
        TimeProvider clock) : IAuthSessionService
    {
        private const string AccessPath = "/api";
        private const string RefreshPath = "/api/auth";

        public async Task<AuthSession> CreateAsync(
            User user, bool rememberMe, CancellationToken cancellationToken = default, DateTime? refreshExpiresOn = null)
        {
            var config = authOptions.Value;
            var now = clock.GetUtcNow().UtcDateTime;

            var (jwt, jwtExpiresOn) = tokens.CreateToken(user);

            // 64 random bytes, base64url — DB mein sirf SHA256 hex hash
            var refresh = Base64Url(RandomNumberGenerator.GetBytes(64));

            // Rotation mein purana absolute expiry hi chalta hai
            var expiresOn = refreshExpiresOn ?? (rememberMe ? now.AddDays(config.RememberMeDays) : now.AddHours(config.SessionHours));

            var row = new RefreshToken
            {
                UserId = user.Id,
                TokenHash = TokenHasher.Hash(refresh),
                ExpiresOn = expiresOn,
                RememberMe = rememberMe,
                CreatedByIp = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString()
            };
            await refreshTokens.AddAsync(row, cancellationToken);

            return new AuthSession(jwt, jwtExpiresOn, refresh, expiresOn, rememberMe, row.Id);
        }

        public void WriteCookies(AuthSession session)
        {
            var response = httpContextAccessor.HttpContext?.Response
                ?? throw new InvalidOperationException("No active HTTP context.");

            var access = CookieOptions(AccessPath);
            access.Expires = new DateTimeOffset(DateTime.SpecifyKind(session.AccessExpiresOn, DateTimeKind.Utc));
            response.Cookies.Append(AuthCookies.Access, session.AccessToken, access);

            // rememberMe nahi to Expires nahi — session cookie
            var refresh = CookieOptions(RefreshPath);
            if (session.RememberMe)
                refresh.Expires = new DateTimeOffset(DateTime.SpecifyKind(session.RefreshExpiresOn, DateTimeKind.Utc));
            response.Cookies.Append(AuthCookies.Refresh, session.RefreshToken, refresh);
        }

        public string? GetRefreshToken() =>
            httpContextAccessor.HttpContext?.Request.Cookies[AuthCookies.Refresh];

        public void ClearCookies()
        {
            var response = httpContextAccessor.HttpContext?.Response
                ?? throw new InvalidOperationException("No active HTTP context.");

            // Path / Secure / SameSite same na ho to browser cookie delete nahi karta
            response.Cookies.Delete(AuthCookies.Access, CookieOptions(AccessPath));
            response.Cookies.Delete(AuthCookies.Refresh, CookieOptions(RefreshPath));
        }

        private static CookieOptions CookieOptions(string path) => new()
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = path
        };

        private static string Base64Url(byte[] bytes) =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}

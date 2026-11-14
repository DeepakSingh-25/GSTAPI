using Gst.Domain.Entities;

namespace Gst.Application.Interfaces.IService
{
    // RefreshTokenId = naye refresh row ki Id (rotation mein purane row ka ReplacedByTokenId banta hai)
    public record AuthSession(
        string AccessToken, DateTime AccessExpiresOn, string RefreshToken, DateTime RefreshExpiresOn, bool RememberMe,
        Guid RefreshTokenId = default);

    public interface IAuthSessionService
    {
        // Refresh row add karta hai (save nahi). refreshExpiresOn diya to wahi absolute expiry (rotation extend nahi karta).
        Task<AuthSession> CreateAsync(User user, bool rememberMe, CancellationToken cancellationToken = default, DateTime? refreshExpiresOn = null);

        // Save ke BAAD cookies likho
        void WriteCookies(AuthSession session);

        // Request ki gst_refresh cookie (nahi to null)
        string? GetRefreshToken();

        // Dono cookies delete — likhte waqt wale hi Path / Secure / HttpOnly / SameSite ke saath
        void ClearCookies();
    }
}

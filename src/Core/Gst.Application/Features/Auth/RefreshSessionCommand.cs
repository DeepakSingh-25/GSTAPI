using Gst.Application.Common;
using Gst.Application.Interfaces.IContext;
using Gst.Application.Interfaces.IRepository;
using Gst.Application.Interfaces.IService;
using Gst.Domain.Common;
using Gst.Domain.Configurations;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;

namespace Gst.Application.Features.Auth
{
    // Body nahi — gst_refresh cookie se token aata hai
    public record RefreshSessionCommand : IRequest<ApiResponse<MeDto>>;

    public class RefreshSessionCommandHandler(
        IRefreshTokenRepository refreshTokens,
        IAuthSessionService sessions,
        IUnitOfWork unitOfWork,
        IOptions<AuthConfig> authOptions,
        TimeProvider clock,
        ILogger<RefreshSessionCommandHandler> logger)
        : IRequestHandler<RefreshSessionCommand, ApiResponse<MeDto>>
    {
        // Har failure pe same message — kuch leak nahi
        private const string Expired = "Session expired. Please log in again.";

        public async Task<ApiResponse<MeDto>> Handle(RefreshSessionCommand request, CancellationToken cancellationToken)
        {
            var config = authOptions.Value;
            var now = clock.GetUtcNow().UtcDateTime;

            // 1. Cookie
            var raw = sessions.GetRefreshToken();
            if (string.IsNullOrEmpty(raw))
                return Unauthorized(clearCookies: true);

            // 2. Row
            var row = await refreshTokens.GetByHashAsync(TokenHasher.Hash(raw), cancellationToken);
            if (row is null)
                return Unauthorized(clearCookies: true);

            // 3. Pehle se revoked
            if (row.RevokedOn is { } revokedOn)
            {
                // Parallel tab ne abhi rotate kiya — naya cookie browser ke paas hai, clear mat karo
                if ((now - revokedOn).TotalSeconds <= config.RefreshReuseGraceSeconds && row.ReplacedByTokenId is not null)
                    return Unauthorized(clearCookies: false);

                // Replay / chori — user ke saare active tokens band
                foreach (var active in await refreshTokens.GetActiveByUserAsync(row.UserId, cancellationToken))
                    active.RevokedOn = now;
                await unitOfWork.SaveChangesAsync(cancellationToken);
                logger.LogWarning("Refresh token reuse detected. UserId {UserId}", row.UserId);
                return Unauthorized(clearCookies: true);
            }

            // 4. Expired
            if (row.ExpiresOn <= now)
                return Unauthorized(clearCookies: true);

            // 5. User gaya ya inactive
            var user = row.User;
            if (user is null || !user.IsActive)
            {
                row.RevokedOn = now;
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return Unauthorized(clearCookies: true);
            }

            // 6. Rotate — expiry absolute (extend nahi), LastLoginOn untouched
            row.RevokedOn = now;
            var session = await sessions.CreateAsync(user, row.RememberMe, cancellationToken, refreshExpiresOn: row.ExpiresOn);
            row.ReplacedByTokenId = session.RefreshTokenId;
            await unitOfWork.SaveChangesAsync(cancellationToken);
            sessions.WriteCookies(session);

            return ApiResponse<MeDto>.Success(MeDto.From(user));
        }

        private ApiResponse<MeDto> Unauthorized(bool clearCookies)
        {
            if (clearCookies)
                sessions.ClearCookies();
            return ApiResponse<MeDto>.Fail(Expired, HttpStatusCode.Unauthorized);
        }
    }
}

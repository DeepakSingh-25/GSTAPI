using Gst.Application.Common;
using Gst.Application.Interfaces.IContext;
using Gst.Application.Interfaces.IRepository;
using Gst.Application.Interfaces.IService;
using Gst.Domain.Common;
using MediatR;

namespace Gst.Application.Features.Auth
{
    // Body nahi — cookie se refresh token. Access token expire ho chuka ho tab bhi chalta hai.
    public record LogoutCommand : IRequest<ApiResponse<string>>;

    public class LogoutCommandHandler(
        IRefreshTokenRepository refreshTokens,
        IAuthSessionService sessions,
        IUnitOfWork unitOfWork,
        TimeProvider clock)
        : IRequestHandler<LogoutCommand, ApiResponse<string>>
    {
        public async Task<ApiResponse<string>> Handle(LogoutCommand request, CancellationToken cancellationToken)
        {
            var raw = sessions.GetRefreshToken();
            if (!string.IsNullOrEmpty(raw))
            {
                var row = await refreshTokens.GetByHashAsync(TokenHasher.Hash(raw), cancellationToken);
                if (row is not null && row.RevokedOn is null)
                {
                    row.RevokedOn = clock.GetUtcNow().UtcDateTime;
                    await unitOfWork.SaveChangesAsync(cancellationToken);
                }
            }

            // Hamesha clear + 200 — token tha ya nahi, reveal nahi
            sessions.ClearCookies();
            return ApiResponse<string>.Success("Logged out.");
        }
    }
}

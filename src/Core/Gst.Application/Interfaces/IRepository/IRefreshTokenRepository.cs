using Gst.Domain.Entities;

namespace Gst.Application.Interfaces.IRepository
{
    public interface IRefreshTokenRepository : IRepository<RefreshToken>
    {
        // Hash se, TRACKED, User + User.Company include (refresh rotate karta hai)
        Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default);
        // User ke un-revoked tokens, TRACKED (reuse detect hone par revoke-all)
        Task<List<RefreshToken>> GetActiveByUserAsync(Guid userId, CancellationToken cancellationToken = default);
    }
}

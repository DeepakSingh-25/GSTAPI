using Gst.Application.Interfaces.IRepository;
using Gst.Domain.Entities;
using Gst.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace Gst.Persistence.Repository
{
    public class RefreshTokenRepository(ApplicationDbContext context) : Repository<RefreshToken>(context), IRefreshTokenRepository
    {
        public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default) =>
            Set.Include(x => x.User).ThenInclude(u => u!.Company)
                .FirstOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);

        public Task<List<RefreshToken>> GetActiveByUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Set.Where(x => x.UserId == userId && x.RevokedOn == null).ToListAsync(cancellationToken);
    }
}

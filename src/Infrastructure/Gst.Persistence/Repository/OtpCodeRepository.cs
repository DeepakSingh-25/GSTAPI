using Gst.Application.Interfaces.IRepository;
using Gst.Domain.Constants;
using Gst.Domain.Entities;
using Gst.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace Gst.Persistence.Repository
{
    public class OtpCodeRepository(ApplicationDbContext context) : Repository<OtpCode>(context), IOtpCodeRepository
    {
        public Task<OtpCode?> GetActiveAsync(Guid userId, string purpose, DateTime now, CancellationToken cancellationToken = default) =>
            Set.AsNoTracking()
                .Where(x => x.UserId == userId && x.Purpose == purpose
                            && x.ExpiresOn > now && x.ConsumedOn == null && x.Attempts < OtpRules.MaxAttempts)
                .OrderByDescending(x => x.CreatedOn)
                .FirstOrDefaultAsync(cancellationToken);

        public Task<OtpCode?> GetLatestAsync(Guid userId, string purpose, CancellationToken cancellationToken = default) =>
            Set.AsNoTracking()
                .Where(x => x.UserId == userId && x.Purpose == purpose)
                .OrderByDescending(x => x.CreatedOn)
                .FirstOrDefaultAsync(cancellationToken);

        public Task<OtpCode?> GetByIdAndPurposeAsync(Guid id, string purpose, CancellationToken cancellationToken = default) =>
            Set.FirstOrDefaultAsync(x => x.Id == id && x.Purpose == purpose, cancellationToken);

        public Task<List<OtpCode>> GetUnconsumedAsync(Guid userId, string purpose, CancellationToken cancellationToken = default) =>
            Set.Where(x => x.UserId == userId && x.Purpose == purpose && x.ConsumedOn == null)
                .ToListAsync(cancellationToken);

        public async Task<DateTime?> GetLatestCreatedOnAsync(string email, string purpose, CancellationToken cancellationToken = default) =>
            await Set.AsNoTracking()
                .Where(x => x.Email == email && x.Purpose == purpose)
                .OrderByDescending(x => x.CreatedOn)
                .Select(x => (DateTime?)x.CreatedOn)
                .FirstOrDefaultAsync(cancellationToken);

        public Task<int> CountCreatedSinceAsync(string email, string purpose, DateTime since, CancellationToken cancellationToken = default) =>
            Set.AsNoTracking().CountAsync(x => x.Email == email && x.Purpose == purpose && x.CreatedOn >= since, cancellationToken);
    }
}

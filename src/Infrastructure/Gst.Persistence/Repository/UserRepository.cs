using Gst.Application.Interfaces.IRepository;
using Gst.Domain.Entities;
using Gst.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace Gst.Persistence.Repository
{
    public class UserRepository(ApplicationDbContext context) : Repository<User>(context), IUserRepository
    {
        public Task<User?> GetByLoginIdAsync(string loginId, CancellationToken cancellationToken = default)
        {
            var query = Set.Include(x => x.Company);
            return loginId.Contains('@')
                ? query.FirstOrDefaultAsync(x => x.Email == loginId.ToLower(), cancellationToken)
                : query.FirstOrDefaultAsync(x => x.MobileNo == loginId, cancellationToken);
        }

        public Task<User?> GetWithCompanyAsync(Guid id, CancellationToken cancellationToken = default) =>
            Set.AsNoTracking().Include(x => x.Company).FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        public Task<User?> GetForUpdateWithCompanyAsync(Guid id, CancellationToken cancellationToken = default) =>
            Set.Include(x => x.Company).FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default) =>
            Set.AsNoTracking().AnyAsync(x => x.Email == email.ToLower(), cancellationToken);

        public Task<bool> MobileExistsAsync(string mobileNo, CancellationToken cancellationToken = default) =>
            Set.AsNoTracking().AnyAsync(x => x.MobileNo == mobileNo, cancellationToken);
    }
}

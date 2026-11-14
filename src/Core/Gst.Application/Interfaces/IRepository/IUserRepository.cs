using Gst.Domain.Entities;

namespace Gst.Application.Interfaces.IRepository
{
    public interface IUserRepository : IRepository<User>
    {
        // Email ('@' ho to) ya mobile se, Company include, TRACKED (login counters badalte hain)
        Task<User?> GetByLoginIdAsync(string loginId, CancellationToken cancellationToken = default);
        // Me ke liye — AsNoTracking + Company
        Task<User?> GetWithCompanyAsync(Guid id, CancellationToken cancellationToken = default);
        // Id se + Company, TRACKED (verify-otp user ko badalta hai)
        Task<User?> GetForUpdateWithCompanyAsync(Guid id, CancellationToken cancellationToken = default);
        Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default);
        Task<bool> MobileExistsAsync(string mobileNo, CancellationToken cancellationToken = default);
    }
}

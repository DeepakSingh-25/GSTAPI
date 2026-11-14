using Gst.Domain.Entities;

namespace Gst.Application.Interfaces.IRepository
{
    public interface IOtpCodeRepository : IRepository<OtpCode>
    {
        // Chalu OTP: expire nahi, consume nahi, attempts bache hain — sabse naya pehle
        Task<OtpCode?> GetActiveAsync(Guid userId, string purpose, DateTime now, CancellationToken cancellationToken = default);
        // Sabse naya OTP (kisi bhi state mein) — login pe limit lagi ho to yahi otpId lautta hai
        Task<OtpCode?> GetLatestAsync(Guid userId, string purpose, CancellationToken cancellationToken = default);
        // Id + purpose se, TRACKED (attempts / consume badalte hain)
        Task<OtpCode?> GetByIdAndPurposeAsync(Guid id, string purpose, CancellationToken cancellationToken = default);
        // User ke saare unconsumed OTP (purpose ke), TRACKED — ek SaveChanges mein consume
        Task<List<OtpCode>> GetUnconsumedAsync(Guid userId, string purpose, CancellationToken cancellationToken = default);
        // Resend cooldown: is email+purpose ka sabse naya CreatedOn (nahi to null)
        Task<DateTime?> GetLatestCreatedOnAsync(string email, string purpose, CancellationToken cancellationToken = default);
        // Resend hourly cap: since ke baad bane OTP ki ginti
        Task<int> CountCreatedSinceAsync(string email, string purpose, DateTime since, CancellationToken cancellationToken = default);
    }
}

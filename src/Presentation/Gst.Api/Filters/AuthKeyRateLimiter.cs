using Gst.Domain.Configurations;
using Microsoft.Extensions.Options;
using System.Threading.RateLimiting;

namespace Gst.Api.Filters
{
    // Dusra limiter (IP ke alawa) — key = otpId / normalized login userId. Singleton, state process memory mein.
    // Partitions idle hone par limiter khud saaf karta hai; nayi keys ki raftaar per-IP "auth" limiter se bandhi hai.
    public sealed class AuthKeyRateLimiter : IDisposable
    {
        private readonly PartitionedRateLimiter<string> limiter;

        public AuthKeyRateLimiter(IOptions<AuthConfig> authOptions)
        {
            var config = authOptions.Value;
            limiter = PartitionedRateLimiter.Create<string, string>(key =>
                RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = config.KeyRateLimitPermits,
                    Window = TimeSpan.FromMinutes(config.KeyRateLimitWindowMinutes),
                    QueueLimit = 0
                }));
        }

        // true = request chal sakti hai
        public bool TryAcquire(string key)
        {
            using var lease = limiter.AttemptAcquire(key);
            return lease.IsAcquired;
        }

        public void Dispose() => limiter.Dispose();
    }
}

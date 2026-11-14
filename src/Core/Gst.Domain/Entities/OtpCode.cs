using Gst.Domain.Common;

namespace Gst.Domain.Entities
{
    // Email OTP — plain code kabhi store nahi hota, sirf hash (CodeHash)
    public class OtpCode : BaseEntity
    {
        public Guid UserId { get; set; }
        public User? User { get; set; }
        public string Email { get; set; } = string.Empty;
        public string Purpose { get; set; } = string.Empty;
        public string CodeHash { get; set; } = string.Empty;
        public DateTime ExpiresOn { get; set; }
        public int Attempts { get; set; }
        public DateTime? VerifiedOn { get; set; }
        public DateTime? ConsumedOn { get; set; }
    }
}

using Gst.Domain.Common;

namespace Gst.Domain.Entities
{
    // Login session ka refresh token — DB mein sirf SHA256 hash
    public class RefreshToken : BaseEntity
    {
        public Guid UserId { get; set; }
        public User? User { get; set; }
        public string TokenHash { get; set; } = string.Empty;
        public DateTime ExpiresOn { get; set; }
        public bool RememberMe { get; set; }
        public DateTime? RevokedOn { get; set; }
        public Guid? ReplacedByTokenId { get; set; }
        public string? CreatedByIp { get; set; }
    }
}

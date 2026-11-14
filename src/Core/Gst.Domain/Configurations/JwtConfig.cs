using System.Text;

namespace Gst.Domain.Configurations
{
    public class JwtConfig
    {
        public const int MinKeyBytes = 32; // HMAC-SHA256 ke liye kam se kam 256 bit

        public string Issuer { get; set; } = string.Empty;
        public string Audience { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
        public int Expiry { get; set; } = 15; // minutes

        // Placeholder ya chhoti key = koi bhi admin token forge kar sakta hai, startup par hi reject
        public bool HasStrongKey() =>
            !string.IsNullOrWhiteSpace(Key)
            && !Key.StartsWith("CHANGE_ME", StringComparison.OrdinalIgnoreCase)
            && Encoding.UTF8.GetByteCount(Key) >= MinKeyBytes;
    }
}

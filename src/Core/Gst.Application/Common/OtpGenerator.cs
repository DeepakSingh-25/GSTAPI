using System.Security.Cryptography;
using System.Text;

namespace Gst.Application.Common
{
    // OTP banata hai aur hash karta hai — DB me sirf hash jaata hai
    public static class OtpGenerator
    {
        public static string Generate() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

        // SHA256(otpId + code) hex
        public static string Hash(Guid otpId, string code) =>
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(otpId.ToString("D") + code)));
    }
}

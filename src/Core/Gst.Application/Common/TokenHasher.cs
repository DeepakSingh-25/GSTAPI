using System.Security.Cryptography;
using System.Text;

namespace Gst.Application.Common
{
    // Refresh token ka hash — DB mein sirf yahi jaata hai (SHA256 lowercase hex)
    public static class TokenHasher
    {
        public static string Hash(string rawToken) =>
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
    }
}

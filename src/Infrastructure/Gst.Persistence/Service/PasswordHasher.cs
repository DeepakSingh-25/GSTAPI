using Gst.Application.Interfaces.IService;

namespace Gst.Persistence.Service
{
    public class PasswordHasher : IPasswordHasher
    {
        public string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, workFactor: 11);

        // Kharab / khali legacy hash pe 500 nahi — galat password jaisa (401), warna account ka hona pata chalta
        public bool Verify(string password, string hash)
        {
            try
            {
                return BCrypt.Net.BCrypt.Verify(password, hash);
            }
            catch (BCrypt.Net.SaltParseException)
            {
                return false;
            }
        }
    }
}

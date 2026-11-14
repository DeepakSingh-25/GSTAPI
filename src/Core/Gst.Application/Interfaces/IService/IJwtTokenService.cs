using Gst.Domain.Entities;

namespace Gst.Application.Interfaces.IService
{
    public interface IJwtTokenService
    {
        (string Token, DateTime ExpiresOn) CreateToken(User user);
    }
}

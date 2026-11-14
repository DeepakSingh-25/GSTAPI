namespace Gst.Application.Interfaces.IService
{
    // JWT token se logged-in user ki info
    public interface ICurrentUserService
    {
        Guid? UserId { get; }
        string? Email { get; }
        string? Role { get; }
    }
}

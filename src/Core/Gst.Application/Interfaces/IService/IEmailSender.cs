namespace Gst.Application.Interfaces.IService
{
    public interface IEmailSender
    {
        Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default);
    }
}

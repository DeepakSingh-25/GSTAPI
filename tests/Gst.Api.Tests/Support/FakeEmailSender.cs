using Gst.Application.Interfaces.IService;

namespace Gst.Api.Tests.Support;

internal sealed class FakeEmailSender : IEmailSender
{
    public List<(string To, string Subject, string Body)> Sent { get; } = [];

    public Task SendAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        lock (Sent) Sent.Add((to, subject, body));
        return Task.CompletedTask;
    }
}

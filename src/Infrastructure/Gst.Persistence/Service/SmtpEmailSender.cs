using Gst.Application.Interfaces.IService;
using Gst.Domain.Configurations;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Mail;

namespace Gst.Persistence.Service
{
    // BCL SmtpClient, har send par naya client.
    // Order: PickupDirectory set -> .eml file; Host khali -> email nahi jaata; warna SMTP send.
    public class SmtpEmailSender(IOptions<SmtpConfig> options, ILogger<SmtpEmailSender> logger, IHostEnvironment env) : IEmailSender
    {
        // Pickup mode me FromEmail khali ho to ye use hota hai
        private const string PickupFallbackFrom = "no-reply@localhost";

        private readonly SmtpConfig _config = options.Value;

        public async Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrWhiteSpace(_config.PickupDirectory))
            {
                await WriteToPickupDirectoryAsync(to, subject, body, cancellationToken);
                return;
            }

            if (string.IsNullOrWhiteSpace(_config.Host))
            {
                // Dev mode: OTP body sirf Development me Debug level par dikhta hai
                if (env.IsDevelopment())
                    logger.LogDebug("Email (SMTP off) to {To} | {Subject} | {Body}", to, subject, body);
                logger.LogWarning("SMTP not configured; email not sent");
                return;
            }

            using var client = new SmtpClient(_config.Host, _config.Port)
            {
                EnableSsl = _config.EnableSsl,
                Timeout = 10_000,
                Credentials = string.IsNullOrWhiteSpace(_config.UserName)
                    ? null
                    : new NetworkCredential(_config.UserName, _config.Password)
            };

            using var message = BuildMessage(_config.FromEmail, to, subject, body);
            await client.SendMailAsync(message, cancellationToken);
        }

        // Dev: mail network par nahi jaati, .eml file folder me likhi jaati hai
        private async Task WriteToPickupDirectoryAsync(string to, string subject, string body, CancellationToken cancellationToken)
        {
            // SmtpClient ko absolute path chahiye
            var directory = Path.GetFullPath(_config.PickupDirectory, env.ContentRootPath);
            Directory.CreateDirectory(directory);

            using var client = new SmtpClient
            {
                DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory,
                PickupDirectoryLocation = directory
            };

            var from = string.IsNullOrWhiteSpace(_config.FromEmail) ? PickupFallbackFrom : _config.FromEmail;
            using var message = BuildMessage(from, to, subject, body);
            await client.SendMailAsync(message, cancellationToken);

            logger.LogInformation("Email written to pickup directory {Directory}", directory);
        }

        private MailMessage BuildMessage(string fromEmail, string to, string subject, string body)
        {
            var message = new MailMessage
            {
                From = new MailAddress(fromEmail, _config.FromName),
                Subject = subject,
                Body = body
            };
            message.To.Add(new MailAddress(to));
            return message;
        }
    }
}

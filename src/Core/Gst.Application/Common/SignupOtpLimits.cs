using Gst.Application.Interfaces.IRepository;
using Gst.Domain.Configurations;
using Gst.Domain.Constants;

namespace Gst.Application.Common
{
    // Naya Signup OTP banane se pehle limits — login aur resend dono yahi use karte hain
    // (warna login se baar-baar naya OTP lekar 5-attempt limit ghoom jaati)
    public static class SignupOtpLimits
    {
        public static string CooldownMessage(int seconds) => $"Please wait {seconds} seconds before requesting a new OTP.";

        // null = naya OTP ban sakta hai; warna 429 ka message
        public static async Task<string?> CheckAsync(
            IOtpCodeRepository otpCodes, string email, DateTime now, AuthConfig config, CancellationToken cancellationToken)
        {
            // Note: CreatedOn DbContext DateTime.UtcNow se set karta hai, compare TimeProvider ke now se
            var latest = await otpCodes.GetLatestCreatedOnAsync(email, OtpPurposes.Signup, cancellationToken);
            if (latest is { } lastCreated && (now - lastCreated).TotalSeconds < config.OtpResendCooldownSeconds)
            {
                return CooldownMessage((int)Math.Ceiling(config.OtpResendCooldownSeconds - (now - lastCreated).TotalSeconds));
            }

            var sentLastHour = await otpCodes.CountCreatedSinceAsync(email, OtpPurposes.Signup, now.AddHours(-1), cancellationToken);
            return sentLastHour >= config.OtpMaxPerHour ? "Too many OTP requests. Please try again later." : null;
        }
    }
}
